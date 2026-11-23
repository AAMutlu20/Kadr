using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Gameplay
{
    /// <summary>Everything the player can rebind in Settings > Controls.</summary>
    public enum GameAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Sprint,
        Fire, Aim, Reload, SwitchAmmo,
        Interact, Inventory, QuickHeal, Flashlight, NewBattery, RaiseCamera, Objective,
        RotateItem
    }

    /// <summary>One physical input: a keyboard key or a mouse button.</summary>
    public readonly struct Binding : IEquatable<Binding>
    {
        public readonly bool IsMouse;
        /// <summary>The Key enum value for a key, 0 left / 1 right / 2 middle / 3 forward / 4 back for a mouse button.</summary>
        public readonly int Code;

        public Binding(bool isMouse, int code)
        {
            IsMouse = isMouse;
            Code = code;
        }

        public static Binding Of(Key key) => new(false, (int)key);
        public static Binding OfMouse(int button) => new(true, button);

        public bool Equals(Binding other) => IsMouse == other.IsMouse && Code == other.Code;
        public override bool Equals(object obj) => obj is Binding other && Equals(other);
        public override int GetHashCode() => IsMouse ? 1000 + Code : Code;
    }

    /// <summary>
    /// The one place that knows which key does what. Every script asks "is Reload pressed" instead of looking at a key,
    /// so Settings > Controls can rebind anything and the HUD can show the real key names. Saved in PlayerPrefs.
    ///
    /// Menus (Esc to go back, clicking buttons, the menu's own WASD) are not rebindable on purpose.
    /// </summary>
    public static class KadrControls
    {
        private const string Prefix = "kadr.bind.";

        private static readonly Dictionary<GameAction, Binding> Defaults = new()
        {
            { GameAction.MoveForward, Binding.Of(Key.W) },
            { GameAction.MoveBack, Binding.Of(Key.S) },
            { GameAction.MoveLeft, Binding.Of(Key.A) },
            { GameAction.MoveRight, Binding.Of(Key.D) },
            { GameAction.Sprint, Binding.Of(Key.LeftShift) },
            { GameAction.Fire, Binding.OfMouse(0) },
            { GameAction.Aim, Binding.OfMouse(1) },
            { GameAction.Reload, Binding.Of(Key.R) },
            { GameAction.SwitchAmmo, Binding.Of(Key.X) },
            { GameAction.Interact, Binding.Of(Key.E) },
            { GameAction.Inventory, Binding.Of(Key.Tab) },
            { GameAction.QuickHeal, Binding.Of(Key.H) },
            { GameAction.Flashlight, Binding.Of(Key.Q) },
            { GameAction.NewBattery, Binding.Of(Key.B) },
            { GameAction.RaiseCamera, Binding.Of(Key.F) },
            { GameAction.Objective, Binding.Of(Key.J) },
            { GameAction.RotateItem, Binding.Of(Key.R) },
        };

        private static readonly Dictionary<GameAction, string> Labels = new()
        {
            { GameAction.MoveForward, "FORWARD" },
            { GameAction.MoveBack, "BACKWARD" },
            { GameAction.MoveLeft, "LEFT" },
            { GameAction.MoveRight, "RIGHT" },
            { GameAction.Sprint, "SPRINT" },
            { GameAction.Fire, "FIRE" },
            { GameAction.Aim, "AIM" },
            { GameAction.Reload, "RELOAD" },
            { GameAction.SwitchAmmo, "SWITCH AMMO" },
            { GameAction.Interact, "INTERACT" },
            { GameAction.Inventory, "INVENTORY" },
            { GameAction.QuickHeal, "QUICK HEAL" },
            { GameAction.Flashlight, "FLASHLIGHT" },
            { GameAction.NewBattery, "NEW BATTERY" },
            { GameAction.RaiseCamera, "RAISE CAMERA" },
            { GameAction.Objective, "OBJECTIVE" },
            { GameAction.RotateItem, "ROTATE ITEM" },
        };

        private static Dictionary<GameAction, Binding> _current;
        private static readonly Dictionary<GameAction, string> NameCache = new();

        /// <summary>Raised after any binding changed.</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            NameCache.Clear();
            Changed = null;
        }

        // ---------- asking ----------

        public static bool Pressed(GameAction action)
        {
            var control = ControlOf(action);
            return control != null && control.wasPressedThisFrame;
        }

        public static bool Held(GameAction action)
        {
            var control = ControlOf(action);
            return control != null && control.isPressed;
        }

        public static bool Released(GameAction action)
        {
            var control = ControlOf(action);
            return control != null && control.wasReleasedThisFrame;
        }

        /// <summary>The movement keys as a vector: x = right minus left, y = forward minus back. Never longer than 1.</summary>
        public static Vector2 Move()
        {
            var x = (Held(GameAction.MoveRight) ? 1f : 0f) - (Held(GameAction.MoveLeft) ? 1f : 0f);
            var y = (Held(GameAction.MoveForward) ? 1f : 0f) - (Held(GameAction.MoveBack) ? 1f : 0f);
            var move = new Vector2(x, y);
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        // ---------- names ----------

        public static string Label(GameAction action) => Labels[action];

        /// <summary>The key's name for the screen, like "E", "LEFT SHIFT", "MOUSE 2".</summary>
        public static string Name(GameAction action)
        {
            if (NameCache.TryGetValue(action, out var cached)) return cached;

            var name = NameOf(Get(action));
            NameCache[action] = name;
            return name;
        }

        public static string NameOf(Binding binding)
        {
            if (binding.IsMouse)
            {
                return binding.Code switch
                {
                    0 => "MOUSE 1",
                    1 => "MOUSE 2",
                    2 => "MOUSE 3",
                    3 => "MOUSE 4",
                    4 => "MOUSE 5",
                    _ => "MOUSE"
                };
            }

            var key = (Key)binding.Code;
            var text = key.ToString();

            if (text.StartsWith("Digit", StringComparison.Ordinal)) return text.Substring(5);
            if (text.StartsWith("Numpad", StringComparison.Ordinal)) return "NUM " + text.Substring(6).ToUpperInvariant();

            // "LeftShift" -> "LEFT SHIFT"
            var spaced = new System.Text.StringBuilder(text.Length + 2);
            for (var i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1])) spaced.Append(' ');
                spaced.Append(text[i]);
            }

            return spaced.ToString().ToUpperInvariant();
        }

        // ---------- changing ----------

        public static Binding Get(GameAction action)
        {
            Load();
            return _current[action];
        }

        /// <summary>
        /// Gives the action a new input. If another action of the same kind already used it, the two swap, so no
        /// action is ever left without a key. Returns the action that was swapped, or null.
        /// </summary>
        public static GameAction? Rebind(GameAction action, Binding binding)
        {
            Load();

            var old = _current[action];
            if (old.Equals(binding)) return null;

            GameAction? swapped = null;
            foreach (var other in Defaults.Keys)
            {
                if (other == action || Context(other) != Context(action) || !_current[other].Equals(binding)) continue;

                _current[other] = old;
                Save(other);
                swapped = other;
                break;
            }

            _current[action] = binding;
            Save(action);

            NameCache.Clear();
            PlayerPrefs.Save();
            Changed?.Invoke();
            return swapped;
        }

        public static void ResetAll()
        {
            Load();
            foreach (var pair in Defaults)
            {
                _current[pair.Key] = pair.Value;
                PlayerPrefs.DeleteKey(Prefix + pair.Key);
            }

            NameCache.Clear();
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Two actions only clash when they are used at the same time. Rotating an item only happens inside the inventory.</summary>
        private static int Context(GameAction action) => action == GameAction.RotateItem ? 1 : 0;

        // ---------- storing ----------

        private static void Load()
        {
            if (_current != null) return;

            _current = new Dictionary<GameAction, Binding>(Defaults);

            foreach (var action in Defaults.Keys)
            {
                var text = PlayerPrefs.GetString(Prefix + action, "");
                if (TryParse(text, out var binding)) _current[action] = binding;
            }
        }

        private static void Save(GameAction action)
        {
            var binding = _current[action];
            PlayerPrefs.SetString(Prefix + action, binding.IsMouse ? "M:" + binding.Code : "K:" + (Key)binding.Code);
        }

        private static bool TryParse(string text, out Binding binding)
        {
            binding = default;
            if (string.IsNullOrEmpty(text) || text.Length < 3) return false;

            var value = text.Substring(2);

            if (text[0] == 'M')
            {
                if (!int.TryParse(value, out var button) || button < 0 || button > 4) return false;
                binding = Binding.OfMouse(button);
                return true;
            }

            if (text[0] == 'K' && Enum.TryParse(value, out Key key) && key != Key.None)
            {
                binding = Binding.Of(key);
                return true;
            }

            return false;
        }

        private static ButtonControl ControlOf(GameAction action)
        {
            var binding = Get(action);

            if (binding.IsMouse)
            {
                var mouse = Mouse.current;
                if (mouse == null) return null;

                return binding.Code switch
                {
                    0 => mouse.leftButton,
                    1 => mouse.rightButton,
                    2 => mouse.middleButton,
                    3 => mouse.forwardButton,
                    4 => mouse.backButton,
                    _ => null
                };
            }

            var keyboard = Keyboard.current;
            return keyboard?[(Key)binding.Code];
        }
    }
}

using System;
using Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Menus
{
    /// <summary>
    /// The settings page: tabs for VIDEO, GRAPHICS, AUDIO and CONTROLS, drawn by the ViewfinderMenu. The main menu and
    /// the pause menu both use it:
    ///
    ///     var settings = SettingsMenu.Create(menu, backToMain);
    ///     menu.Show(settings.Page);
    ///
    /// On CONTROLS, click a line and press the new key (or a mouse button). Esc cancels. If the key already belongs to
    /// another action, the two swap, so nothing is ever left without a key.
    /// </summary>
    public class SettingsMenu : MonoBehaviour
    {
        public MenuPage Page { get; private set; }

        private ViewfinderMenu _menu;
        private Action _back;

        private bool _capturing;
        private GameAction _action;
        private int _captureFrame;
        private int _releaseFrame = -1;

        public static SettingsMenu Create(ViewfinderMenu menu, Action back)
        {
            var settings = menu.gameObject.AddComponent<SettingsMenu>();
            settings.Init(menu, back);
            return settings;
        }

        private void Init(ViewfinderMenu menu, Action back)
        {
            _menu = menu;
            _back = back;

            Page = new MenuPage
            {
                Heading = "CAMERA SETTINGS",
                Spacing = 62f,
                Compact = true,
                Width = 1100f,
                Back = Leave
            };

            BuildVideo(AddTab("VIDEO"));
            BuildGraphics(AddTab("GRAPHICS"));
            BuildAudio(AddTab("AUDIO"));
            BuildControls(AddTab("CONTROLS"));
        }

        private MenuTab AddTab(string name)
        {
            var tab = new MenuTab { Name = name };
            Page.Tabs.Add(tab);
            return tab;
        }

        private void Leave()
        {
            KadrSettings.Save();
            _back?.Invoke();
        }

        // ---------- tabs ----------

        private static void BuildVideo(MenuTab tab)
        {
            tab.Entries.Add(new MenuEntry
            {
                Label = "DISPLAY MODE",
                Value = () => KadrSettings.Mode switch
                {
                    DisplayMode.Exclusive => "FULLSCREEN",
                    DisplayMode.Windowed => "WINDOWED",
                    _ => "BORDERLESS"
                },
                Adjust = direction => KadrSettings.Mode = (DisplayMode)(((int)KadrSettings.Mode + direction + 3) % 3)
            });

            tab.Entries.Add(new MenuEntry
            {
                Label = "RESOLUTION",
                Value = () =>
                {
                    var size = KadrSettings.Resolutions[KadrSettings.ResolutionIndex()];
                    return size.x + " x " + size.y;
                },
                Adjust = direction =>
                {
                    var list = KadrSettings.Resolutions;
                    var next = Mathf.Clamp(KadrSettings.ResolutionIndex() + direction, 0, list.Count - 1);
                    KadrSettings.Resolution = list[next];
                }
            });

            tab.Entries.Add(new MenuEntry
            {
                Label = "VSYNC",
                Value = () => KadrSettings.VSync ? "ON" : "OFF",
                Adjust = _ => KadrSettings.VSync = !KadrSettings.VSync
            });

            tab.Entries.Add(new MenuEntry
            {
                Label = "FRAME LIMIT",
                Value = () => KadrSettings.VSync
                    ? "SET BY VSYNC"
                    : KadrSettings.FrameCap <= 0 ? "UNLIMITED" : KadrSettings.FrameCap + " FPS",
                Adjust = direction =>
                {
                    var caps = KadrSettings.FrameCaps;
                    var index = Array.IndexOf(caps, KadrSettings.FrameCap);
                    if (index < 0) index = 0;
                    KadrSettings.FrameCap = caps[(index + direction + caps.Length) % caps.Length];
                }
            });
        }

        private static void BuildGraphics(MenuTab tab)
        {
            tab.Entries.Add(new MenuEntry
            {
                Label = "QUALITY",
                Value = () => QualitySettings.names[Mathf.Clamp(KadrSettings.Quality, 0, QualitySettings.names.Length - 1)].ToUpperInvariant(),
                Adjust = direction =>
                {
                    var count = QualitySettings.names.Length;
                    KadrSettings.Quality = (KadrSettings.Quality + direction + count) % count;
                }
            });

            if (!KadrSettings.HasUrp) return;

            tab.Entries.Add(new MenuEntry
            {
                Label = "RENDER SCALE",
                Value = () => Mathf.RoundToInt(KadrSettings.RenderScale * 100f) + "%",
                Meter = () => Mathf.InverseLerp(0.5f, 1.25f, KadrSettings.RenderScale),
                Adjust = direction => KadrSettings.RenderScale += direction * 0.05f
            });

            tab.Entries.Add(new MenuEntry
            {
                Label = "ANTI-ALIASING",
                Value = () => KadrSettings.Msaa <= 1 ? "OFF" : KadrSettings.Msaa + "X",
                Adjust = direction =>
                {
                    var steps = KadrSettings.MsaaSteps;
                    var index = Mathf.Max(0, Array.IndexOf(steps, KadrSettings.Msaa));
                    KadrSettings.Msaa = steps[(index + direction + steps.Length) % steps.Length];
                }
            });

            tab.Entries.Add(new MenuEntry
            {
                Label = "SHADOW DISTANCE",
                Value = () => KadrSettings.ShadowNames[KadrSettings.ShadowLevel],
                Adjust = direction =>
                {
                    var count = KadrSettings.ShadowSteps.Length;
                    KadrSettings.ShadowLevel = (KadrSettings.ShadowLevel + direction + count) % count;
                }
            });
        }

        private static void BuildAudio(MenuTab tab)
        {
            tab.Entries.Add(new MenuEntry
            {
                Label = "MASTER VOLUME",
                Value = () => Mathf.RoundToInt(KadrSettings.MasterVolume * 100f) + "%",
                Meter = () => KadrSettings.MasterVolume,
                Adjust = direction => KadrSettings.MasterVolume = Mathf.Round((KadrSettings.MasterVolume + direction * 0.05f) * 20f) / 20f
            });
        }

        private void BuildControls(MenuTab tab)
        {
            Header(tab, "MOUSE");
            tab.Entries.Add(new MenuEntry
            {
                Label = "SENSITIVITY",
                Value = () => KadrSettings.MouseSensitivity.ToString("0.0#"),
                Meter = () => Mathf.InverseLerp(0.2f, 3f, KadrSettings.MouseSensitivity),
                Adjust = direction => KadrSettings.MouseSensitivity = Mathf.Round((KadrSettings.MouseSensitivity + direction * 0.1f) * 10f) / 10f
            });
            tab.Entries.Add(new MenuEntry
            {
                Label = "INVERT Y",
                Value = () => KadrSettings.InvertY ? "ON" : "OFF",
                Adjust = _ => KadrSettings.InvertY = !KadrSettings.InvertY
            });

            Header(tab, "MOVEMENT");
            Bind(tab, GameAction.MoveForward);
            Bind(tab, GameAction.MoveBack);
            Bind(tab, GameAction.MoveLeft);
            Bind(tab, GameAction.MoveRight);
            Bind(tab, GameAction.Sprint);

            Header(tab, "WEAPON");
            Bind(tab, GameAction.Fire);
            Bind(tab, GameAction.Aim);
            Bind(tab, GameAction.Reload);
            Bind(tab, GameAction.SwitchAmmo);

            Header(tab, "ITEMS");
            Bind(tab, GameAction.Interact);
            Bind(tab, GameAction.Inventory);
            Bind(tab, GameAction.QuickHeal);
            Bind(tab, GameAction.Flashlight);
            Bind(tab, GameAction.NewBattery);
            Bind(tab, GameAction.RaiseCamera);
            Bind(tab, GameAction.Objective);
            Bind(tab, GameAction.RotateItem);

            Header(tab, "");
            tab.Entries.Add(new MenuEntry
            {
                Label = "RESET TO DEFAULTS",
                Select = () =>
                {
                    KadrControls.ResetAll();
                    _menu.RefreshValues();
                    _menu.Message("CONTROLS RESET");
                }
            });
        }

        private static void Header(MenuTab tab, string title) => tab.Entries.Add(new MenuEntry { Label = title, Header = true });

        private void Bind(MenuTab tab, GameAction action)
        {
            tab.Entries.Add(new MenuEntry
            {
                Label = KadrControls.Label(action),
                Arrows = false,
                Value = () =>
                {
                    if (_capturing && _action == action) return Mathf.FloorToInt(Time.unscaledTime * 3f) % 2 == 0 ? "PRESS A KEY" : "";
                    return "[ " + KadrControls.Name(action) + " ]";
                },
                Select = () => BeginCapture(action)
            });
        }

        // ---------- rebinding ----------

        private void BeginCapture(GameAction action)
        {
            _capturing = true;
            _action = action;
            _captureFrame = Time.frameCount;
            _menu.Interactable = false; // the menu must not react to the key that is about to be pressed
            _menu.Message(KadrControls.Label(action) + ": PRESS A KEY OR MOUSE BUTTON  /  ESC CANCELS");
        }

        private void Update()
        {
            if (_capturing) PollCapture();

            // The menu gets its input back one frame later, so the key that was just bound does not also click a line.
            if (_releaseFrame >= 0 && Time.frameCount >= _releaseFrame)
            {
                _releaseFrame = -1;
                _menu.Interactable = true;
            }
        }

        private void PollCapture()
        {
            _menu.RefreshValues(); // the blinking "PRESS A KEY"

            if (Time.frameCount == _captureFrame) return; // the click that started this

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                EndCapture("CANCELLED");
                return;
            }

            if (!TryRead(out var binding)) return;

            var swapped = KadrControls.Rebind(_action, binding);
            _menu.Flash(0.3f);

            EndCapture(swapped.HasValue
                ? KadrControls.NameOf(binding) + " WAS " + KadrControls.Label(swapped.Value) + ": SWAPPED"
                : KadrControls.Label(_action) + " = " + KadrControls.NameOf(binding));
        }

        private void EndCapture(string message)
        {
            _capturing = false;
            _releaseFrame = Time.frameCount + 1;
            _menu.RefreshValues();
            _menu.Message(message);
        }

        private static bool TryRead(out Binding binding)
        {
            binding = default;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key.keyCode == Key.None || key.keyCode == Key.Escape || !key.wasPressedThisFrame) continue;

                    binding = Binding.Of(key.keyCode);
                    return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse == null) return false;

            if (mouse.leftButton.wasPressedThisFrame) binding = Binding.OfMouse(0);
            else if (mouse.rightButton.wasPressedThisFrame) binding = Binding.OfMouse(1);
            else if (mouse.middleButton.wasPressedThisFrame) binding = Binding.OfMouse(2);
            else if (mouse.forwardButton.wasPressedThisFrame) binding = Binding.OfMouse(3);
            else if (mouse.backButton.wasPressedThisFrame) binding = Binding.OfMouse(4);
            else return false;

            return true;
        }
    }
}

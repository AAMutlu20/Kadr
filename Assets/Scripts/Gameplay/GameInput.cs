using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Gameplay
{
    /// <summary>
    /// The only place gameplay code reads raw input, so changing the input setup or adding key rebinding
    /// touches this one file and no caller.
    ///
    /// Unity defines ENABLE_INPUT_SYSTEM when the project uses the new Input System package
    /// The other branch covers the old Input Manager.
    /// With "Both" selected, the new Input System is used.
    ///
    /// Polling is deliberate: Kadr needs four inputs right now. When rebinding or gamepad support is added,
    /// an Input Actions asset can replace the bodies of these methods without touching PlayerController.
    ///
    /// LookDelta is roughly "pixels the mouse moved this frame" in both branches, so the sensitivity value
    /// in PlayerController feels the same whichever system is active.
    /// </summary>
    public static class GameInput
    {
        /// <summary>Strafe (x) and forward/back (y), each -1 to 1, clamped so diagonals are not faster.</summary>
        public static Vector2 Move()
        {
            #if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            var x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            var y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            #else
            var raw = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            return Vector2.ClampMagnitude(raw, 1f);
            #endif
        }

        /// <summary>Mouse movement this frame.</summary>
        public static Vector2 LookDelta()
        {
            #if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse == null ? Vector2.zero : mouse.delta.ReadValue();
            #else
            // The legacy mouse axes carry a built-in 0.1 sensitivity, so scale back up to pixels.
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
            #endif
        }

        /// <summary>True only on the frame the jump key goes down.</summary>
        public static bool JumpPressed()
        {
            #if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
            #else
            return Input.GetKeyDown(KeyCode.Space);
            #endif
        }

        /// <summary>True while the sprint key is held.</summary>
        public static bool SprintHeld()
        {
            #if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.leftShiftKey.isPressed;
            #else
            return Input.GetKey(KeyCode.LeftShift);
            #endif
        }
    }
}
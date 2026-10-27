using UnityEngine;

namespace Gadd420
{
    public static class Input_Compat
    {

        public static float GetHorizontal()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);
#elif ENABLE_INPUT_SYSTEM
            float v = 0f;
            // Keyboard
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v += 1f;
            }
            // Gamepad
            var gp = UnityEngine.InputSystem.Gamepad.current;
            if (gp != null)
                v += gp.leftStick.x.ReadValue();

            return Mathf.Clamp(v, -1f, 1f);
#else
            return 0f;
#endif
        }

        public static float GetVertical()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Mathf.Clamp(Input.GetAxisRaw("Vertical"), -1f, 1f);
#elif ENABLE_INPUT_SYSTEM
            float v = 0f;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
            }
            var gp = UnityEngine.InputSystem.Gamepad.current;
            if (gp != null)
                v += gp.leftStick.y.ReadValue();

            return Mathf.Clamp(v, -1f, 1f);
#else
            return 0f;
#endif
        }

        public static bool GetForwardHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.wKey.isPressed || kb.upArrowKey.isPressed)) return true;
            return false;
#else
            return false;
#endif
        }

        public static bool GetBackwardHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.sKey.isPressed || kb.downArrowKey.isPressed)) return true;
            return false;
#else
            return false;
#endif
        }

        public static bool GetRightHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.dKey.isPressed || kb.rightArrowKey.isPressed)) return true;
            return false;
#else
            return false;
#endif
        }

        public static bool GetLeftHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.aKey.isPressed || kb.leftArrowKey.isPressed)) return true;
            return false;
#else
            return false;
#endif
        }


        public static bool GetBrakeHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.Space);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.spaceKey.isPressed) return true;

            var gp = UnityEngine.InputSystem.Gamepad.current;
            if (gp != null && gp.leftTrigger.ReadValue() > 0.1f) return true;

            return false;
#else
            return false;
#endif
        }

        public static bool GetHandbrakeHeld()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed)) return true;

            var gp = UnityEngine.InputSystem.Gamepad.current;
            if (gp != null && gp.buttonEast.isPressed) return true; // B / Circle

            return false;
#else
            return false;
#endif
        }

        public static float GetWheelieAxis()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            bool down = Input.GetKey(KeyCode.LeftControl);
            bool up = Input.GetKey(KeyCode.LeftShift);
            if (down && !up) return -1f;
            if (up && !down) return 1f;
            return 0f;
#elif ENABLE_INPUT_SYSTEM
            float v = 0f;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                bool down = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                bool up = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                if (down && !up) v = -1f;
                else if (up && !down) v = 1f;
            }
            return v;
#else
            return 0f;
#endif
        }

        public static float GetLeanAxis()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            bool left = Input.GetMouseButton(0);
            bool right = Input.GetMouseButton(1);
            if (left && !right) return -1f;
            if (right && !left) return 1f;
            return 0f;
#elif ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return 0f;

            bool left = mouse.leftButton.isPressed;
            bool right = mouse.rightButton.isPressed;

            if (left && !right) return -1f;
            if (right && !left) return 1f;
            return 0f;
#else
            return 0f;
#endif
        }

        public static Vector2 GetMouseDelta()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
    return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

#elif ENABLE_INPUT_SYSTEM
    var mouse = UnityEngine.InputSystem.Mouse.current;
    if (mouse == null) return Vector2.zero;

    const float NEW_MOUSE_SCALE = 0.05f;

    Vector2 d = mouse.delta.ReadValue();

    d = Vector2.ClampMagnitude(d, 50f);

    return d * NEW_MOUSE_SCALE;
#else
            return Vector2.zero;
#endif
        }

        public static bool GetEscapeDown()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

        public static bool GetLeftClickDown()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#elif ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
            return false;
#endif
        }

        public static float GetScrollDelta()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return 0f;

            // The new Input System reports raw platform values (±120 per notch
            // on Windows); normalize so one notch is roughly ±1 like legacy.
            float v = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(v) > 1f) v /= 120f;
            return v;
#else
            return 0f;
#endif
        }

        public static bool GetRestartPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
    return UnityEngine.Input.GetKeyDown(KeyCode.R);
#elif ENABLE_INPUT_SYSTEM
    var kb = UnityEngine.InputSystem.Keyboard.current;
    return kb != null && kb.rKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

    }
}

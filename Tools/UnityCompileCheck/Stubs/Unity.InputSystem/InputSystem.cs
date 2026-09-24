// Minimal API surface for the HoloTable compile check. Compile-check only.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.InputSystem
{
    namespace Controls
    {
        public class ButtonControl
        {
            public bool wasPressedThisFrame => false;
            public bool wasReleasedThisFrame => false;
            public bool isPressed => false;
        }

        public class KeyControl : ButtonControl { }

        public class Vector2Control
        {
            public Vector2 ReadValue() => default;
        }
    }

    public enum Key
    {
        None = 0, Space = 1, Enter = 2, Tab = 3,
        A = 15, C = 17, E = 19, F = 20, H = 22, N = 28, Q = 31, S = 33, V = 36, X = 38,
        Digit1 = 41, Digit2 = 42, Digit3 = 43, Digit4 = 44, Digit5 = 45, Digit6 = 46, Digit7 = 47, Digit8 = 48, Digit9 = 49, Digit0 = 50,
        LeftShift = 51, RightShift = 52, Escape = 60, Delete = 71,
    }

    public class Pointer
    {
        public static Pointer current => null;
        public Controls.ButtonControl press => null;
        public Controls.Vector2Control position => null;
        public Controls.Vector2Control delta => null;
    }

    public class Mouse : Pointer
    {
        public new static Mouse current => null;
        public Controls.ButtonControl leftButton => null;
        public Controls.ButtonControl rightButton => null;
        public Controls.ButtonControl middleButton => null;
        public Controls.Vector2Control scroll => null;
    }

    public class Keyboard
    {
        public static Keyboard current => null;
        public Controls.KeyControl this[Key key] => null;
    }
}

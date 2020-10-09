using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Squared.PRGUI {
    public static class UIEvents {
        public const string LostFocus = "LostFocus",
            GotFocus = "GotFocus",
            MouseDown = "MouseDown",
            // Mouse moved with no button(s) held
            MouseMove = "MouseMove",
            // Mouse moved with button(s) held
            MouseDrag = "MouseDrag",
            MouseUp = "MouseUp",
            MouseEnter = "MouseEnter",
            MouseLeave = "MouseLeave",
            Click = "Click",
            Scroll = "Scroll",
            KeyDown = "KeyDown",
            KeyPress = "KeyPress",
            KeyUp = "KeyUp",
            Moved = "Moved",
            ValueChanged = "ValueChanged",
            CheckedChanged = "CheckedChanged",
            RadioButtonSelected = "RadioButtonSelected",
            SelectionChanged = "SelectionChanged",
            Shown = "Shown",
            Closed = "Closed",
            OpacityTweenEnded = "OpacityTweenEnded",
            BackgroundColorTweenEnded = "BackgroundColorTweenEnded",
            TextColorTweenEnded = "TextColorTweenEnded";
    }

    public struct KeyboardModifiers {
        public bool Control => LeftControl || RightControl;
        public bool Shift => LeftShift || RightShift;
        public bool Alt => LeftAlt || RightAlt;

        public bool LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt;
    }

    public struct MouseEventArgs {
        public UIContext Context;

        public KeyboardModifiers Modifiers;
        public Control MouseOver, MouseCaptured, Hovering, Focused;
        public Vector2 GlobalPosition, LocalPosition;
        public Vector2 MouseDownPosition;
        public RectF Box, ContentBox;
        public bool MovedSinceMouseDown, DoubleClicking;
    }

    public struct KeyEventArgs {
        public UIContext Context;

        public KeyboardModifiers Modifiers;
        public Keys? Key;
        public char? Char;
    }
}

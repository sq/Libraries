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
            MouseMove = "MouseMove",
            MouseUp = "MouseUp",
            MouseEnter = "MouseEnter",
            MouseLeave = "MouseLeave",
            MouseCaptureChanged = "MouseCaptureChanged",
            // Another mouse button was pressed/released in addition to the one being held
            MouseButtonsChanged = "MouseButtonsChanged",
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
            ItemChosen = "ItemChosen",
            Shown = "Shown",
            Closed = "Closed",
            OpacityTweenEnded = "OpacityTweenEnded",
            BackgroundColorTweenEnded = "BackgroundColorTweenEnded",
            TextColorTweenEnded = "TextColorTweenEnded",
            ControlFixated = "ControlFixated";
    }

    public struct KeyboardModifiers {
        public bool Control => LeftControl || RightControl;
        public bool Shift => LeftShift || RightShift;
        public bool Alt => LeftAlt || RightAlt;

        public bool LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt;
    }

    public struct MouseEventArgs {
        public UIContext Context;

        public double Now;
        public long NowL;

        public KeyboardModifiers Modifiers;
        public Control MouseOver, MouseCaptured, Hovering, Focused;
        /// <summary>
        /// The global mouse location.
        /// </summary>
        public Vector2 GlobalPosition;
        /// <summary>
        /// The global mouse location taking into account transform matrices.
        /// </summary>
        public Vector2 RelativeGlobalPosition;
        /// <summary>
        /// The mouse location relative to the control's content box.
        /// </summary>
        public Vector2 LocalPosition;
        /// <summary>
        /// The global location of the mouse when the mouse was first pressed (if ever).
        /// </summary>
        public Vector2 MouseDownPosition;
        /// <summary>
        /// The time when the mouse was first pressed (if ever).
        /// </summary>
        public double MouseDownTimestamp;
        public RectF Box, ContentBox;
        public MouseButtons PreviousButtons, Buttons;
        public bool MovedSinceMouseDown, DoubleClicking;
        public int SequentialClickCount;
    }

    public struct KeyEventArgs {
        public UIContext Context;

        public KeyboardModifiers Modifiers;
        public Keys? Key;
        public char? Char;
    }
}

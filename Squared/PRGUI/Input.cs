using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Squared.PRGUI.Input {
    public interface IInputSource {
        void Update (UIContext context, ref InputState previous, ref InputState current);
        void SetTextInputState (UIContext context, bool enabled);
    }

    public struct KeyboardModifiers {
        public bool Control => LeftControl || RightControl;
        public bool Shift => LeftShift || RightShift;
        public bool Alt => LeftAlt || RightAlt;

        public bool LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt;
    }

    public struct InputState {
        public Vector2 CursorPosition;
        public MouseButtons Buttons;
        public KeyboardModifiers Modifiers;
        public float WheelValue;
        public bool AreAnyKeysHeld, SpacebarHeld;
    }

    public class KeyboardInputSource : IInputSource {
        public KeyboardState PreviousState, CurrentState;

        bool IsTextInputRegistered;
        Keys LastKeyEvent;
        double LastKeyEventFirstTime, LastKeyEventTime;
        UIContext Context;

        public KeyboardInputSource () {
            PreviousState = CurrentState = Keyboard.GetState();
        }

        public void Update (UIContext context, ref InputState previous, ref InputState current) {
            Context = context;
            PreviousState = CurrentState;
            var ks = CurrentState = Keyboard.GetState();

            var pk = ks.GetPressedKeys();
            current.AreAnyKeysHeld = pk.Length > 0;
            current.SpacebarHeld = ks.IsKeyDown(Keys.Space);
            current.Modifiers = new KeyboardModifiers {
                LeftControl = ks.IsKeyDown(Keys.LeftControl),
                RightControl = ks.IsKeyDown(Keys.RightControl),
                LeftShift = ks.IsKeyDown(Keys.LeftShift),
                RightShift = ks.IsKeyDown(Keys.RightShift),
                LeftAlt = ks.IsKeyDown(Keys.LeftAlt),
                RightAlt = ks.IsKeyDown(Keys.RightAlt),
            };

            if (context.IsCompositionActive)
                return;

            var now = context.Now;
            for (int i = 0; i < 255; i++) {
                var key = (Keys)i;

                bool shouldFilterKeyPress = false;
                var wasPressed = PreviousState.IsKeyDown(key);
                var isPressed = ks.IsKeyDown(key);

                if (isPressed || wasPressed) {
                    // Clumsily filter out keys that would generate textinput events
                    if (!current.Modifiers.Control && !current.Modifiers.Alt) {
                        if ((key >= Keys.D0) && (key <= Keys.Z))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.NumPad0) && (key <= Keys.Divide))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.OemSemicolon) && (key <= Keys.OemBackslash))
                            shouldFilterKeyPress = true;
                    }
                }

                if (isPressed != wasPressed) {
                    context.HandleKeyEvent(isPressed ? UIEvents.KeyDown : UIEvents.KeyUp, key, null);

                    if (isPressed && !shouldFilterKeyPress) {
                        // Modifier keys shouldn't break an active key repeat (i.e. you should be able to press/release shift)
                        if (!ks.IsKeyDown(LastKeyEvent) || !UIContext.ModifierKeys.Contains(key))
                            LastKeyEvent = key;

                        LastKeyEventTime = LastKeyEventFirstTime = now;
                        context.HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                } else if (isPressed && (LastKeyEvent == key)) {
                    if (
                        !UIContext.SuppressRepeatKeys.Contains(key) && 
                        !UIContext.ModifierKeys.Contains(key) &&
                        !shouldFilterKeyPress &&
                        context.UpdateRepeat(now, LastKeyEventFirstTime, ref LastKeyEventTime)
                    ) {
                        context.HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                }
            }
        }

        public void SetTextInputState (UIContext context, bool enabled) {
            if (!enabled) {
                TextInputEXT.StopTextInput();
                return;
            }

            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;

            if (!IsTextInputRegistered) {
                IsTextInputRegistered = true;
                TextInputEXT.TextInput += TextInputEXT_TextInput;
                TextInputEXT.TextEditing += TextInputEXT_TextEditing;
            }
                TextInputEXT.StartTextInput();
        }

        private void TextInputEXT_TextInput (char ch) {
            // Control characters will be handled through the KeyboardState path
            if (char.IsControl(ch))
                return;

            Context.HandleKeyEvent(UIEvents.KeyPress, null, ch);
        }

        private void TextInputEXT_TextEditing (string text, int cursorPosition, int length) {
            if ((text == null) || (text.Length == 0)) {
                Context.TerminateComposition();
                return;
            }

            Context.UpdateComposition(text, cursorPosition, length);
        }
    }

    public class MouseInputSource : IInputSource {
        /// <summary>
        /// Mouse wheel movements are scaled by this amount
        /// </summary>
        public float MouseWheelScale = 1.0f / 2.4f;
        /// <summary>
        /// The mouse position is offset by this distance
        /// </summary>
        public Vector2 Offset;

        public MouseState PreviousState, CurrentState;

        public MouseInputSource () {
            PreviousState = CurrentState = Mouse.GetState();
        }

        public void Update (UIContext context, ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var mouseState = CurrentState = Mouse.GetState();

            current.Buttons = ((mouseState.LeftButton == ButtonState.Pressed) ? MouseButtons.Left : MouseButtons.None) |
                ((mouseState.MiddleButton == ButtonState.Pressed) ? MouseButtons.Middle : MouseButtons.None) |
                ((mouseState.RightButton == ButtonState.Pressed) ? MouseButtons.Right : MouseButtons.None);

            current.CursorPosition = new Vector2(mouseState.X, mouseState.Y) + Offset;
            current.WheelValue = mouseState.ScrollWheelValue * MouseWheelScale;
        }

        public void SetTextInputState (UIContext context, bool enabled) {
        }
    }
}

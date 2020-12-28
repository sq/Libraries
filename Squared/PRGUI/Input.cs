using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Input {
    public interface IInputSource {
        void SetContext (UIContext context);
        void Update (ref InputState previous, ref InputState current);
        void SetTextInputState (bool enabled);
        void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer);
    }

    public struct KeyboardModifiers {
        public bool Control => LeftControl || RightControl;
        public bool Shift => LeftShift || RightShift;
        public bool Alt => LeftAlt || RightAlt;

        public bool LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt;
    }

    public struct InputState {
        public List<Keys> HeldKeys;
        public Vector2 CursorPosition;
        public MouseButtons Buttons;
        public KeyboardModifiers Modifiers;
        public float WheelValue;
        public bool AreAnyKeysHeld, ActivateKeyHeld, KeyboardNavigationEnded;
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

        public void SetContext (UIContext context) {
            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
        }

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var ks = CurrentState = Keyboard.GetState();

            current.ActivateKeyHeld |= ks.IsKeyDown(Keys.Space);
            bool lctrl = ks.IsKeyDown(Keys.LeftControl),
                rctrl = ks.IsKeyDown(Keys.RightControl),
                lalt = ks.IsKeyDown(Keys.LeftAlt),
                ralt = ks.IsKeyDown(Keys.RightAlt);
            current.Modifiers.LeftControl |= lctrl;
            current.Modifiers.RightControl |= rctrl;
            current.Modifiers.LeftShift |= ks.IsKeyDown(Keys.LeftShift);
            current.Modifiers.RightShift |= ks.IsKeyDown(Keys.RightShift);
            current.Modifiers.LeftAlt |= lalt;
            current.Modifiers.RightAlt |= ralt;

            if (Context.IsCompositionActive)
                return;

            var now = Context.Now;
            for (int i = 0; i < 255; i++) {
                var key = (Keys)i;

                bool shouldFilterKeyPress = false;
                var wasPressed = PreviousState.IsKeyDown(key);
                var isPressed = ks.IsKeyDown(key);
                if (isPressed)
                    current.HeldKeys.Add(key);

                if (isPressed || wasPressed) {
                    // Clumsily filter out keys that would generate textinput events
                    if (!lctrl && !rctrl && !lalt && !ralt) {
                        if ((key >= Keys.D0) && (key <= Keys.Z))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.NumPad0) && (key <= Keys.Divide))
                            shouldFilterKeyPress = true;
                        else if ((key >= Keys.OemSemicolon) && (key <= Keys.OemBackslash))
                            shouldFilterKeyPress = true;
                    }
                }

                if (isPressed != wasPressed) {
                    Context.HandleKeyEvent(isPressed ? UIEvents.KeyDown : UIEvents.KeyUp, key, null);

                    if (isPressed && !shouldFilterKeyPress) {
                        // Modifier keys shouldn't break an active key repeat (i.e. you should be able to press/release shift)
                        if (!ks.IsKeyDown(LastKeyEvent) || !UIContext.ModifierKeys.Contains(key))
                            LastKeyEvent = key;

                        LastKeyEventTime = LastKeyEventFirstTime = now;
                        Context.HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                } else if (isPressed && (LastKeyEvent == key)) {
                    if (
                        !UIContext.SuppressRepeatKeys.Contains(key) && 
                        !UIContext.ModifierKeys.Contains(key) &&
                        !shouldFilterKeyPress &&
                        Context.UpdateRepeat(now, LastKeyEventFirstTime, ref LastKeyEventTime)
                    ) {
                        Context.HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                }
            }
        }

        public void SetTextInputState (bool enabled) {
            if (!enabled) {
                TextInputEXT.StopTextInput();
                return;
            }

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

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer) {
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
        private bool HasState;
        UIContext Context;

        public MouseInputSource () {
        }

        public void SetContext (UIContext context) {
            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
        }

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var mouseState = CurrentState = Mouse.GetState();
            if (!HasState)
                PreviousState = CurrentState;

            current.Buttons |= ((mouseState.LeftButton == ButtonState.Pressed) ? MouseButtons.Left : MouseButtons.None);
            current.Buttons |= ((mouseState.MiddleButton == ButtonState.Pressed) ? MouseButtons.Middle : MouseButtons.None);
            current.Buttons |= ((mouseState.RightButton == ButtonState.Pressed) ? MouseButtons.Right : MouseButtons.None);

            var prevPosition = new Vector2(PreviousState.X, PreviousState.Y) + Offset;
            if (PreviousState.ScrollWheelValue != CurrentState.ScrollWheelValue)
                current.WheelValue = mouseState.ScrollWheelValue * MouseWheelScale;

            if (!HasState) {
                HasState = true;
                return;
            }

            if ((CurrentState.X != PreviousState.X) || (CurrentState.Y != PreviousState.Y)) {
                current.CursorPosition = new Vector2(mouseState.X, mouseState.Y) + Offset;
                current.KeyboardNavigationEnded = true;
                Context.PromoteInputSource(this);
            }
        }

        public void SetTextInputState (bool enabled) {
        }

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer) {
        }
    }

    public class GamepadVirtualKeyboardAndCursor : IInputSource {
        public float SlowPxPerSecond = 96f,
            FastPxPerSecond = 1024f;
        public float FastThreshold = 0.4f,
            FastRampSize = 0.6f,
            Deadzone = 0.2f;

        public GamePadState PreviousState, CurrentState;
        public PlayerIndex PlayerIndex;
        public bool EnableButtons = true,
            EnableStick = true;
        long PreviousUpdateTime;
        UIContext Context;
        Control SnapToControl;
        bool GenerateKeyPressForActivation = false;

        public GamepadVirtualKeyboardAndCursor (PlayerIndex playerIndex = PlayerIndex.One) {
            PlayerIndex = playerIndex;
            PreviousState = CurrentState = GamePad.GetState(PlayerIndex);
        }

        public void SetContext (UIContext context) {
            if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
        }

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;
            var gs = CurrentState = GamePad.GetState(PlayerIndex);
            var now = Context.NowL;

            Vector2? newPosition = null;

            if (current.KeyboardNavigationEnded)
                SnapToControl = null;

            var stick = PreviousState.ThumbSticks.Left;
            var length = stick.Length();
            if ((length >= Deadzone) && EnableStick) {
                var ramp = Arithmetic.Saturate((length - FastThreshold) / FastRampSize);
                var speed = Arithmetic.Lerp(
                    SlowPxPerSecond, FastPxPerSecond, ramp * ramp
                );
                var elapsedD = (now - PreviousUpdateTime) / (double)Time.SecondInTicks;
                stick.Normalize();
                var motion = speed * stick * (float)elapsedD * new Vector2(1, -1);
                newPosition = new Vector2(
                    current.CursorPosition.X + motion.X,
                    current.CursorPosition.Y + motion.Y
                );
                current.KeyboardNavigationEnded = true;
                GenerateKeyPressForActivation = false;
                SnapToControl = null;
            }

            if ((SnapToControl != null) && (Context.Focused != SnapToControl)) {
                if (GenerateKeyPressForActivation)
                    SnapToControl = Context.Focused;
                else
                    SnapToControl = null;
            }

            if (EnableButtons) {
                var mods = new KeyboardModifiers {
                    LeftControl = (gs.Buttons.Back == ButtonState.Pressed)
                };
                var shift = new KeyboardModifiers {
                    LeftControl = (gs.Buttons.Back == ButtonState.Pressed),
                    LeftShift = true
                };

                if (GenerateKeyPressForActivation) {
                    DispatchKeyEventsForButton(ref current, Keys.Space, mods, PreviousState.Buttons.A, gs.Buttons.A);
                } else {
                    if (gs.Buttons.A == ButtonState.Pressed)
                        current.Buttons |= MouseButtons.Left;
                }

                DispatchKeyEventsForButton(ref current, Keys.Escape, mods, PreviousState.Buttons.B, gs.Buttons.B);
                var wasArrowPressed = DispatchKeyEventsForButton(ref current, Keys.Up, mods, PreviousState.DPad.Up, gs.DPad.Up);
                wasArrowPressed |= DispatchKeyEventsForButton(ref current, Keys.Down, mods, PreviousState.DPad.Down, gs.DPad.Down);
                wasArrowPressed |= DispatchKeyEventsForButton(ref current, Keys.Left, mods, PreviousState.DPad.Left, gs.DPad.Left);
                wasArrowPressed |= DispatchKeyEventsForButton(ref current, Keys.Right, mods, PreviousState.DPad.Right, gs.DPad.Right);
                var focusChanged = DispatchKeyEventsForButton(ref current, Keys.Tab, shift, PreviousState.Buttons.LeftShoulder, gs.Buttons.LeftShoulder);
                focusChanged |= DispatchKeyEventsForButton(ref current, Keys.Tab, mods, PreviousState.Buttons.RightShoulder, gs.Buttons.RightShoulder);
                if (gs.Buttons.Y == ButtonState.Pressed)
                    current.Buttons |= MouseButtons.Right;

                if (focusChanged)
                    SnapToControl = Context.Focused;

                if (focusChanged || wasArrowPressed)
                    GenerateKeyPressForActivation = true;
            }

            if (SnapToControl != null)
                newPosition = SnapToControl.GetRect(Context.Layout, contentRect: true).Center;

            if (newPosition != null) {
                var x = Arithmetic.Clamp(newPosition.Value.X, 0, Context.CanvasSize.X);
                var y = Arithmetic.Clamp(newPosition.Value.Y, 0, Context.CanvasSize.Y);
                current.CursorPosition = new Vector2(x, y);
                Context.PromoteInputSource(this);
            }

            PreviousUpdateTime = now;
        }

        private bool DispatchKeyEventsForButton (ref InputState state, Keys key, ButtonState previous, ButtonState current) {
            return DispatchKeyEventsForButton(ref state, key, null, previous, current);
        }

        private bool DispatchKeyEventsForButton (ref InputState state, Keys key, KeyboardModifiers? modifiers, ButtonState previous, ButtonState current) {
            if (current == ButtonState.Pressed)
                state.HeldKeys.Add(key);

            if (previous == current)
                return false;

            var transition = (current == ButtonState.Pressed)
                ? UIEvents.KeyDown
                : UIEvents.KeyUp;

            Context.HandleKeyEvent(transition, key, null, modifiers);
            if (current == ButtonState.Released)
                Context.HandleKeyEvent(UIEvents.KeyPress, key, null, modifiers);

            return true;
        }

        public void SetTextInputState (bool enabled) {
        }

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer) {
            if (Context.InputSources.IndexOf(this) != 0)
                return;

            var decorator = context.DecorationProvider.VirtualCursor;
            if (decorator == null)
                return;
            var pos = Context.CurrentInputState.CursorPosition;
            var padding = decorator.Padding;
            var total = padding + decorator.Margins;
            var settings = new DecorationSettings {
                Box = new RectF(pos - new Vector2(total.Left, total.Top), new Vector2(total.X, total.Y)),
                ContentBox = new RectF(pos - new Vector2(padding.Left, padding.Top), new Vector2(padding.X, padding.Y)),
                State = GenerateKeyPressForActivation ? ControlStates.Disabled : default(ControlStates)
            };
            decorator.Rasterize(context, ref renderer, settings);
        }
    }
}

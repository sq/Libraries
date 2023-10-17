using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Input {
    public sealed class InputID {
        // FIXME: Move this
        public static Dictionary<Buttons, string> GamePadButtonLabels = new Dictionary<Buttons, string>();

        public string Label;
        public Keys Key;
        public KeyboardModifiers Modifiers;
        public string GamePadLabel;
        public Buttons? GamePadButton;

        private static StringBuilder CachedStringBuilder = new StringBuilder();

        public string ToString (bool gamePad) {
            var sb = Interlocked.Exchange(ref CachedStringBuilder, null);
            if (sb == null)
                sb = new StringBuilder();
            Format(sb, gamePad);
            var result = sb.ToString();
            Interlocked.CompareExchange(ref CachedStringBuilder, sb, null);
            return result;
        }

        public void Format (StringBuilder output, bool gamePad) {
            var length = output.Length;
            var splitPosition = Label?.IndexOf("{0}");
            if (splitPosition.GetValueOrDefault(-1) >= 0) {
                output.Append(Label, 0, splitPosition.Value);
            } else if (Label != null) {
                output.Append(Label);
                return;
            }

            if (gamePad && GamePadButton.HasValue) {
                if (GamePadButtonLabels.TryGetValue(GamePadButton.Value, out string buttonLabel))
                    output.Append(buttonLabel);
                else
                    // FIXME: Enum.ToString allocates for some reason
                    output.Append(GamePadButton.Value.ToString());
            } else {
                Modifiers.Format(output);
                if (output.Length > length)
                    output.Append("+");
                if ((Key >= Keys.D0) && (Key <= Keys.D9))
                    output.Append(Key - Keys.D0);
                else
                    // FIXME: Enum.ToString allocates for some reason
                    output.Append(Key.ToString());
            }

            if (splitPosition.HasValue)
                output.Append(Label, splitPosition.Value + 3, Label.Length - splitPosition.Value - 3);
        }

        public bool Equals (InputID obj) {
            return (obj.Key == Key);
        }

        public override bool Equals (object obj) {
            var iid = obj as InputID;
            if (iid == null)
                return false;
            return Equals(iid);
        }

        public override int GetHashCode () {
            return Key.GetHashCode();
        }

        public override string ToString () {
            return ToString(false);
        }
    }

    public interface IInputSource {
        void SetContext (UIContext context);
        void Update (ref InputState previous, ref InputState current);
        void SetTextInputState (bool enabled);
        void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer);
        void TryMoveCursor (Vector2 newPosition);
    }

    public struct KeyboardModifiers {
        public bool Any => (Control || Shift || Alt);
        public bool Control => LeftControl || RightControl;
        public bool Shift => LeftShift || RightShift;
        public bool Alt => LeftAlt || RightAlt;

        public bool LeftControl, RightControl, LeftShift, RightShift, LeftAlt, RightAlt;

        private static StringBuilder CachedStringBuilder = new StringBuilder();

        public bool Equals (KeyboardModifiers rhs) {
            return (Control == rhs.Control) && (Shift == rhs.Shift) && (Alt == rhs.Alt);
        }

        public override string ToString () {
            if (!Any)
                return "";

            var sb = Interlocked.Exchange(ref CachedStringBuilder, null);
            if (sb == null)
                sb = new StringBuilder();
            Format(sb);
            var result = sb.ToString();
            Interlocked.CompareExchange(ref CachedStringBuilder, sb, null);
            return result;
        }

        public void Format (StringBuilder output) {
            if (!Any)
                return;

            if (Control)
                output.Append("Ctrl");

            if (Alt && Control)
                output.Append("+Alt");
            else if (Alt)
                output.Append("Alt");

            if (Shift && (Control || Alt))
                output.Append("+Shift");
            else if (Shift)
                output.Append("Shift");
        }
    }

    public struct InputState {
        public List<Keys> HeldKeys;
        public Vector2 CursorPosition;
        public Vector2 ScrollDistance;
        public MouseButtons Buttons;
        public KeyboardModifiers Modifiers;
        public float WheelValue;
        public bool AreAnyKeysHeld, ActivateKeyHeld, KeyboardNavigationEnded, VirtualInputActive;
    }

    public sealed class KeyboardInputSource : IInputSource {
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
                        Context.HandleKeyEvent(UIEvents.KeyPress, key, null, isRepeat: true);
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

        public void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer) {
        }

        public void TryMoveCursor (Vector2 position) {
        }
    }

    public sealed class MouseInputSource : IInputSource {
        /// <summary>
        /// Mouse wheel movements are scaled by this amount
        /// </summary>
        public float MouseWheelScale = 1.0f / 2.4f;
        /// <summary>
        /// The input mouse position is offset by this amount (after scaling) to convert it to the UI coordinate space
        /// </summary>
        public Vector2 Offset;
        /// <summary>
        /// The input mouse position is scaled by this factor to convert it to the UI coordinate space
        /// </summary>
        public Vector2 Scale = Vector2.One;

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
            current.Buttons |= ((mouseState.XButton1 == ButtonState.Pressed) ? MouseButtons.X1 : MouseButtons.None);
            current.Buttons |= ((mouseState.XButton2 == ButtonState.Pressed) ? MouseButtons.X2 : MouseButtons.None);

            var prevPosition = new Vector2(PreviousState.X, PreviousState.Y) * Scale + Offset;
            var newPosition = new Vector2(mouseState.X, mouseState.Y) * Scale + Offset;
            if (PreviousState.ScrollWheelValue != CurrentState.ScrollWheelValue)
                current.WheelValue = mouseState.ScrollWheelValue * MouseWheelScale;

            if (!HasState) {
                HasState = true;
                return;
            }

            var isPriority = Context.IsPriorityInputSource(this);
            var insideWindow = Context.CanvasRect.Contains(newPosition);

            if ((CurrentState.X != PreviousState.X) || (CurrentState.Y != PreviousState.Y)) {
                if (insideWindow || isPriority) {
                    current.CursorPosition = newPosition;
                    current.KeyboardNavigationEnded = true;
                    if (!isPriority)
                        Context.PromoteInputSource(this);
                }
            }
        }

        public void SetTextInputState (bool enabled) {
        }

        public void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer) {
        }

        public void TryMoveCursor (Vector2 position) {
            Mouse.SetPosition((int)(position.X / Scale.X), (int)(position.Y / Scale.Y));
        }
    }

    public sealed class GamepadVirtualKeyboardAndCursor : IInputSource {
        public const float DpadNavigationSpeedMultiplier = 0.66f;

        public struct InputBindings {
            public static readonly InputBindings Default = new InputBindings {
                FocusBack = new [] { Buttons.LeftShoulder },
                FocusForward = new [] { Buttons.RightShoulder },
                Shift = new Buttons[] { },
                Control = new [] { Buttons.Back },
                Activate = new [] { Buttons.A },
                Spacebar = new [] { Buttons.A },
                Enter = new[] { Buttons.X },
                Escape = new [] { Buttons.B },
                Menu = new [] { Buttons.Y },
                UpArrow = new[] { Buttons.DPadUp },
                LeftArrow = new[] { Buttons.DPadLeft },
                RightArrow = new[] { Buttons.DPadRight },
                DownArrow = new[] { Buttons.DPadDown },
                Alt = new[] { Buttons.RightStick }
            };

            public Buttons[] FocusBack, FocusForward,
                WindowFocusBack, WindowFocusForward,
                Alt, Control, Shift,
                Activate, Spacebar, Enter, Escape, Menu,
                UpArrow, LeftArrow, RightArrow, DownArrow;
        }

        public bool ShowFuzzyRects = false, EnableFading = false;

        public float FuzzyHitTestDistance = 24f;
        public float SlowPxPerSecond = 64f,
            FastPxPerSecond = 1280f;
        public float AccelerationExponent = 2.4f,
            Deadzone = 0.1f;
        public float? FixedTimeStep = null;

        // Force feedback
        public Tween<float> LeftMotor, RightMotor;

        private InputBindings _Bindings = InputBindings.Default;
        public InputBindings Bindings {
            get => _Bindings;
            set {
                _Bindings = value;
                OnBindingsChanged();
            }
        }

        private Vector2? PreviousUnsnappedPosition, CurrentUnsnappedPosition;
        public GamePadState PreviousState, CurrentState;
        public PlayerIndex PlayerIndex;
        public bool EnableButtons = true,
            EnableStick = true,
            EnableDpadFocusNavigation = true;
        long PreviousUpdateTime;
        UIContext Context;
        Control SnapToControl;
        bool GenerateKeyPressForActivation = false;

        Keys LastKeyEvent;
        double LastKeyEventFirstTime, LastKeyEventTime, LastDpadEventFirstTime, LastDpadEventTime;

        private FuzzyHitTest FuzzyHitTest;

        public GamepadVirtualKeyboardAndCursor (PlayerIndex playerIndex = PlayerIndex.One) {
            PlayerIndex = playerIndex;
            PreviousState = CurrentState = GamePad.GetState(PlayerIndex);
        }

        // FIXME: Duplication
        private void UpdateBinding (Buttons[] buttons, Keys key, bool ctrl = false, bool alt = false, bool shift = false) {
            var mods = new KeyboardModifiers {
                LeftControl = ctrl, LeftAlt = alt, LeftShift = shift
            };
            var id = Context.GetInputID(key, mods);

            if ((buttons == null) || (buttons.Length == 0)) {
                id.GamePadButton = null;
                id.GamePadLabel = null;
                return;
            }

            var button = buttons[0];
            id.GamePadButton = button;
            InputID.GamePadButtonLabels.TryGetValue(button, out id.GamePadLabel);
        }

        private void OnBindingsChanged () {
            if (Context == null)
                return;

            Context.ClearInputIDButtons();
            UpdateBinding(Bindings.FocusBack, Keys.Tab, shift: true);
            UpdateBinding(Bindings.FocusForward, Keys.Tab);
            UpdateBinding(Bindings.WindowFocusBack, Keys.Tab, ctrl: true, shift: true);
            UpdateBinding(Bindings.WindowFocusForward, Keys.Tab, ctrl: true);
            UpdateBinding(Bindings.Alt, Keys.LeftAlt, alt: true);
            UpdateBinding(Bindings.Shift, Keys.LeftShift, shift: true);
            UpdateBinding(Bindings.Control, Keys.LeftControl, ctrl: true);
            UpdateBinding(Bindings.Activate, Keys.Space);
            UpdateBinding(Bindings.Enter, Keys.Enter);
            UpdateBinding(Bindings.Escape, Keys.Escape);
            UpdateBinding(Bindings.Menu, Keys.Apps);
            UpdateBinding(Bindings.UpArrow, Keys.Up);
            UpdateBinding(Bindings.LeftArrow, Keys.Left);
            UpdateBinding(Bindings.RightArrow, Keys.Right);
            UpdateBinding(Bindings.DownArrow, Keys.Down);
        }

        public void SetContext (UIContext context) {
            if (context == Context)
                return;
            else if ((context != Context) && (Context != null))
                throw new InvalidOperationException("This source has already been used with another context");
            Context = context;
            FuzzyHitTest = new FuzzyHitTest(context);
            OnBindingsChanged();
        }

        private void ProcessStick (Vector2 stick, out float speed, out Vector2 direction) {
            var length = stick.Length();
            if ((length >= Deadzone) && EnableStick) {
                var ramp = Arithmetic.Saturate((float)Math.Pow((length - Deadzone) / (1f - Deadzone), AccelerationExponent));
                speed = Arithmetic.Lerp(
                    SlowPxPerSecond, FastPxPerSecond, ramp
                );
                direction = stick * new Vector2(1, -1);
                direction.Normalize();
            } else {
                direction = Vector2.Zero;
                speed = 0f;
            }
        }

        private bool IsValidHoverTarget (Control hovering) {
            if (hovering == null)
                return false;

            // FIXME: Does focus beneficiary work if mouse input is disabled?
            return hovering.IsValidMouseInputTarget || (hovering.FocusBeneficiary != null) || !hovering.HasParent;
        }

        private bool IsHeld (ref GamePadState state, Buttons button) =>
            state.IsButtonDown(button);

        private bool IsHeld (ref GamePadState state, Buttons[] buttons) {
            foreach (var button in buttons)
                if (state.IsButtonDown(button))
                    return true;

            return false;
        }

        private bool IsHeld (Buttons button) =>
            IsHeld(ref CurrentState, button);

        private bool IsHeld (Buttons[] buttons) =>
            IsHeld(ref CurrentState, buttons);

        public void Update (ref InputState previous, ref InputState current) {
            PreviousState = CurrentState;

            GamePad.SetVibration(
                PlayerIndex, LeftMotor.Get(Context.NowL), RightMotor.Get(Context.NowL)
            );

            // If we're not the priority input source, we should just sync our unsnapped position with the cursor
            var isPriority = Context.IsPriorityInputSource(this);
            if (isPriority)
                current.VirtualInputActive = true;

            if (isPriority)
                PreviousUnsnappedPosition = CurrentUnsnappedPosition;
            else
                PreviousUnsnappedPosition = CurrentUnsnappedPosition = null;

            var gs = CurrentState = GamePad.GetState(PlayerIndex);
            var now = Context.NowL;

            var suppressSnapDueToHeldButton = IsHeld(Bindings.Activate) || 
                IsHeld(Bindings.Spacebar) || 
                IsHeld(Bindings.Escape);

            Vector2? newPosition = null;
            var shouldPromote = false;

            if (current.KeyboardNavigationEnded)
                SnapToControl = null;

            var elapsed = FixedTimeStep ?? (float)((now - PreviousUpdateTime) / (double)Time.SecondInTicks);

            ProcessStick(PreviousState.ThumbSticks.Left, out float cursorSpeed, out Vector2 cursorDirection);
            ProcessStick(PreviousState.ThumbSticks.Right, out float scrollSpeed, out Vector2 scrollDirection);

            if (cursorSpeed > 0) {
                shouldPromote = true;
                var motion = cursorSpeed * cursorDirection * elapsed;
                newPosition = new Vector2(
                    current.CursorPosition.X + motion.X,
                    current.CursorPosition.Y + motion.Y
                );
                if (PreviousUnsnappedPosition.HasValue)
                    CurrentUnsnappedPosition = Context.CanvasRect.Clamp(PreviousUnsnappedPosition.Value + motion);
                else
                    CurrentUnsnappedPosition = null;
                current.KeyboardNavigationEnded = true;
                GenerateKeyPressForActivation = false;
                SnapToControl = null;
            }

            var oldFocusedControl = Context.Focused;
            var focusedModal = Context.FocusedModal;
            var effectiveSnapTarget = SnapToControl;

            if (
                (SnapToControl != null) && 
                (Context.Focused != SnapToControl)
            ) {
                if (focusedModal?.FocusDonor == SnapToControl)
                    effectiveSnapTarget = Context.Focused;
                else if (GenerateKeyPressForActivation)
                    SnapToControl = Context.Focused;
                else
                    SnapToControl = null;
            }

            if (scrollSpeed > 0) {
                var motion = scrollSpeed * scrollDirection * elapsed;
                current.ScrollDistance += motion;
                // FIXME: It would be ideal if this didn't need to happen, but scrolling 
                //  a listbox will cause very strange snap behavior as its selected item
                //  leaves or enters the view
                SnapToControl = null;
            }

            if (EnableButtons) {
                if (Context.Focused != null) {
                    if (Context.Focused.GetRect().Contains(current.CursorPosition))
                        current.ActivateKeyHeld |= IsHeld(Bindings.Spacebar);
                }

                var mods = new KeyboardModifiers {
                    LeftControl = IsHeld(Bindings.Control),
                    LeftShift = IsHeld(Bindings.Shift),
                    LeftAlt = IsHeld(Bindings.Alt)
                };
                var shift = mods;
                shift.LeftShift = true;

                DispatchKeyEventsForButton(ref current, Keys.LeftAlt, mods, Bindings.Alt);
                DispatchKeyEventsForButton(ref current, Keys.LeftShift, mods, Bindings.Shift);
                DispatchKeyEventsForButton(ref current, Keys.LeftControl, mods, Bindings.Control);

                if (GenerateKeyPressForActivation) {
                    DispatchKeyEventsForButton(ref current, Keys.Space, mods, Bindings.Spacebar);
                } else {
                    if (gs.Buttons.A == ButtonState.Pressed)
                        current.Buttons |= MouseButtons.Left;
                }

                var currentSelectedControl = (Context.FixatedControl as ISelectionBearer)?.SelectedControl;
                var currentSelectionRect = (Context.FixatedControl as ISelectionBearer)?.SelectionRect;

                DispatchKeyEventsForButton(ref current, Keys.Enter, mods, Bindings.Enter);
                DispatchKeyEventsForButton(ref current, Keys.Escape, mods, Bindings.Escape);
                bool wasUpPressed = DispatchKeyEventsForButton(ref current, Keys.Up, mods, Bindings.UpArrow),
                    wasDownPressed = DispatchKeyEventsForButton(ref current, Keys.Down, mods, Bindings.DownArrow),
                    wasLeftPressed = DispatchKeyEventsForButton(ref current, Keys.Left, mods, Bindings.LeftArrow),
                    wasRightPressed = DispatchKeyEventsForButton(ref current, Keys.Right, mods, Bindings.RightArrow);
                var wasArrowPressed = (wasUpPressed || wasDownPressed || wasLeftPressed || wasRightPressed);
                var focusChanged = DispatchKeyEventsForButton(ref current, Keys.Tab, shift, Bindings.FocusBack);
                focusChanged |= DispatchKeyEventsForButton(ref current, Keys.Tab, mods, Bindings.FocusForward);
                // FIXME: Do this arrow fallback with a priority/consume model, make sure to handle weird transitions
                //  like 'Foo handled the keydown but didn't handle the keyup'
                if (!wasArrowPressed && EnableDpadFocusNavigation)
                    focusChanged |= PerformDpadFocusNavigation(ref current, mods);
                DispatchKeyEventsForButton(ref current, Keys.Apps, mods, Bindings.Menu);

                if (focusChanged || wasArrowPressed) {
                    var newSelectedControl = (Context.FixatedControl as ISelectionBearer)?.SelectedControl;
                    var newSelectionRect = (Context.FixatedControl as ISelectionBearer)?.SelectionRect;
                    if (
                        (focusChanged || (oldFocusedControl != Context.Focused)) || 
                        (newSelectedControl != currentSelectedControl) ||
                        (newSelectionRect != currentSelectionRect)
                    )
                        SnapToControl = Context.Focused;

                    GenerateKeyPressForActivation = true;
                    shouldPromote = true;
                }
            }

            if (effectiveSnapTarget != null) {
                // Controls like menus update their selected item when the cursor moves over them,
                //  so if possible when performing a cursor snap (for pad input) we want to snap to
                //  a point on top of the current selection to avoid changing it, instead of the center
                //  of the new snap target
                var sb = effectiveSnapTarget as ISelectionBearer;
                var sc = sb?.SelectionRect;
                var targetRect = effectiveSnapTarget.GetRect(contentRect: true, displayRect: true);
                if (sc.HasValue && sc.Value.Intersection(in targetRect, out RectF union)) {
                    newPosition = union.Center;
                } else
                    newPosition = targetRect.Center;
                CurrentUnsnappedPosition = newPosition.Value;
            } else if (!suppressSnapDueToHeldButton) {
                var testPosition = CurrentUnsnappedPosition ?? newPosition ?? current.CursorPosition;

                if (isPriority) {
                    // FIXME: We get garbage snap positions sometimes when the cursor is over a control
                    FuzzyHitTest.Run(testPosition, IsValidHoverTarget, maxDistance: FuzzyHitTestDistance);
                    if (FuzzyHitTest.Count > 0) {
                        /*
                        if (FuzzyHitTest.Count > 1) {
                            Console.WriteLine();
                            foreach (var r in FuzzyHitTest)
                                Console.WriteLine("{0} {1:000.00} {2}", r.Depth, r.Distance, r.Control);
                        }
                        */

                        var result = FuzzyHitTest[0];
                        CurrentUnsnappedPosition = CurrentUnsnappedPosition ?? testPosition;
                        if (result.Distance > 0)
                            newPosition = result.ClosestPoint;
                        else
                            newPosition = testPosition;
                    } else if (CurrentUnsnappedPosition.HasValue) {
                        // There are no fuzzy hit test candidates nearby, and there's no direct hit test result
                        //  which means we're not over any UI at all.
                        if (Context.HitTest(CurrentUnsnappedPosition.Value) == null)
                            newPosition = CurrentUnsnappedPosition.Value;
                    } else {
                        ;
                    }
                }
            }

            if (newPosition != null) {
                var xy = Context.CanvasRect.Clamp(newPosition.Value);
                if (shouldPromote || isPriority) {
                    current.CursorPosition = xy;
                    if (!CurrentUnsnappedPosition.HasValue)
                        CurrentUnsnappedPosition = xy;
                    Context.PromoteInputSource(this);
                }
            } else if (suppressSnapDueToHeldButton) {
                PreviousUnsnappedPosition = previous.CursorPosition;
                CurrentUnsnappedPosition = current.CursorPosition;
            } else if (!isPriority) {
                CurrentUnsnappedPosition = current.CursorPosition;
            }

            PreviousUpdateTime = now;
        }

        private bool PerformDpadFocusNavigation (ref InputState current, KeyboardModifiers mods) {
            int x = 0, y = 0;
            if (ShouldDpadNavigateInDirection(Buttons.DPadLeft, Keys.Left, out var leftHeld))
                x -= 1;
            if (ShouldDpadNavigateInDirection(Buttons.DPadRight, Keys.Right, out var rightHeld))
                x += 1;
            if (ShouldDpadNavigateInDirection(Buttons.DPadUp, Keys.Up, out var upHeld))
                y -= 1;
            if (ShouldDpadNavigateInDirection(Buttons.DPadDown, Keys.Down, out var downHeld))
                y += 1;

            if ((x != 0) || (y != 0)) {
                var stc = SnapToControl;
                SnapToControl = null;
                if (Context.TryMoveFocusDirectionally(x, y, true))
                    return true;
                SnapToControl = stc;
            }

            if (!leftHeld && !upHeld && !rightHeld && !downHeld) {
                // Reset repeat acceleration if none of the arrows are held.
                // It's possible this wouldn't happen otherwise, since PerformDpadFocusNavigation isn't called every frame.
                LastDpadEventFirstTime = Context.Now;
            }

            return false;
        }

        private bool ShouldDpadNavigateInDirection (Buttons button, Keys virtualKey, out bool isHeld) {
            var wasHeld = IsHeld(ref PreviousState, button);
            isHeld = IsHeld(ref CurrentState, button);

            if (wasHeld == isHeld) {
                if (
                    isHeld &&
                    Context.UpdateRepeat(Context.Now, LastDpadEventFirstTime, ref LastDpadEventTime, DpadNavigationSpeedMultiplier)
                ) {
                    LastDpadEventTime = Context.Now;
                    return true;
                } else
                    return false;
            }

            return wasHeld && !isHeld;
        }

        private bool DispatchKeyEventsForButton (ref InputState state, Keys key, Buttons[] buttons) {
            return DispatchKeyEventsForButton(ref state, key, null, buttons);
        }

        private bool DispatchKeyEventsForButton (ref InputState state, Keys key, KeyboardModifiers? modifiers, Buttons[] buttons) {
            var wasHeld = IsHeld(ref PreviousState, buttons);
            var isHeld = IsHeld(ref CurrentState, buttons);
            if (isHeld)
                state.HeldKeys.Add(key);

            if (wasHeld == isHeld) {
                if (
                    isHeld &&
                    (LastKeyEvent == key) &&
                    Context.UpdateRepeat(Context.Now, LastKeyEventFirstTime, ref LastKeyEventTime)
                ) {
                    LastKeyEventTime = Context.Now;
                    return Context.HandleKeyEvent(UIEvents.KeyPress, key, null, modifiers, isVirtual: true, isRepeat: true);
                } else
                    return false;
            } else if (isHeld) {
                LastKeyEventTime = LastKeyEventFirstTime = Context.Now;
            }

            var transition = isHeld
                ? UIEvents.KeyDown
                : UIEvents.KeyUp;

            LastKeyEvent = key;
            var ok = Context.HandleKeyEvent(transition, key, null, modifiers, true);
            if (!isHeld)
                ok |= Context.HandleKeyEvent(UIEvents.KeyPress, key, null, modifiers, true);

            return ok;
        }

        public void SetTextInputState (bool enabled) {
        }

        private void MakeSettingsForPosition (Vector2 position, Margins total, Margins padding, out DecorationSettings settings) {
            settings = new DecorationSettings {
                Box = new RectF(position - total.TopLeft, total.Size),
                ContentBox = new RectF(position - padding.TopLeft, padding.Size),
                State = GenerateKeyPressForActivation ? ControlStates.Disabled : default(ControlStates)
            };
        }

        public void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer) {
            if (!Context.IsPriorityInputSource(this))
                return;

            var decorator = context.DecorationProvider.VirtualCursor;
            if (decorator == null)
                return;
            var pos = Context.CurrentInputState.CursorPosition;
            var padding = decorator.Padding;
            var total = padding + decorator.Margins;

            var unsnapped = CurrentUnsnappedPosition ?? pos;
            MakeSettingsForPosition(unsnapped, total, padding, out var settings);
            decorator.Rasterize(ref context, ref renderer, ref settings);

            settings.Box = new RectF(unsnapped, pos - unsnapped);
            context.DecorationProvider.VirtualCursorAnchor?.Rasterize(ref context, ref renderer, ref settings);

            if (SnapToControl != null)
                return;

            if (ShowFuzzyRects)
            foreach (var result in FuzzyHitTest) {
                var box = result.Rect;
                var alpha = (1f - Arithmetic.Saturate(result.Distance / FuzzyHitTestDistance)) * 0.8f;
                renderer.RasterizeRectangle(
                    box.Position, box.Extent, radius: 1f, outlineRadius: 1.5f,
                    innerColor: Color.Transparent, outerColor: Color.Transparent,
                    outlineColor: Color.Red * alpha
                );
            }
        }

        public void TryMoveCursor (Vector2 position) {
            // FIXME: Will this work right?
            CurrentUnsnappedPosition = position;
        }
    }
}

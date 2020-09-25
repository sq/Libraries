using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public static class UIEvents {
        public static readonly string LostFocus = string.Intern("LostFocus"),
            GotFocus = string.Intern("GotFocus"),
            MouseDown = string.Intern("MouseDown"),
            // Mouse moved with no button(s) held
            MouseMove = string.Intern("MouseMove"),
            // Mouse moved with button(s) held
            MouseDrag = string.Intern("MouseDrag"),
            MouseUp = string.Intern("MouseUp"),
            MouseEnter = string.Intern("MouseEnter"),
            MouseLeave = string.Intern("MouseLeave"),
            Click = string.Intern("Click"),
            Scroll = string.Intern("Scroll"),
            KeyDown = string.Intern("KeyDown"),
            KeyPress = string.Intern("KeyPress"),
            KeyUp = string.Intern("KeyUp");
    }

    public class UIContext : IDisposable {
        private static readonly HashSet<Keys> SuppressRepeatKeys = new HashSet<Keys> {
            Keys.LeftAlt,
            Keys.LeftControl,
            Keys.LeftShift,
            Keys.RightAlt,
            Keys.RightControl,
            Keys.RightShift,
            Keys.Escape
        };

        public bool TextInsertionMode = true;

        // Double-clicks will only be tracked if this far apart or less
        public double DoubleClickWindowSize = 0.33;
        // If the mouse is only moved less than this far, it will be treated as no movement
        public float MinimumMovementDistance = 4;

        public double FirstKeyRepeatDelay = 0.4;
        public double KeyRepeatIntervalSlow = 0.09, KeyRepeatIntervalFast = 0.035;
        public double KeyRepeatAccelerationDelay = 4;

        public Vector2 CanvasSize;
        public EventBus EventBus = new EventBus();
        public readonly LayoutContext Layout = new LayoutContext();
        public IDecorationProvider Decorations;

        private KeyboardModifiers CurrentModifiers;

        private Vector2 LastMousePosition;
        private bool LastMouseButtonState = false;
        private Vector2? MouseDownPosition;
        private KeyboardState LastKeyboardState;

        private Vector2 LastClickPosition;
        private Control LastClickTarget;
        private double LastMouseDownTime, LastClickTime;
        private int SequentialClickCount;

        private Keys LastKeyEvent;
        private double LastKeyEventFirstTime, LastKeyEventTime;

        private Tooltip CachedTooltip;
        // If we show the tooltip immediately after creating it, it won't have had layout performed before it's painted
        private bool TooltipPending;

        public ControlCollection Controls = new ControlCollection(null);

        /// <summary>
        /// The control that currently has the mouse captured (if a button is pressed)
        /// </summary>
        public Control MouseCaptured { get; private set; }
        /// <summary>
        /// The control currently underneath the mouse cursor, as long as the mouse is not captured by another control
        /// </summary>
        public Control Hovering { get; private set; }
        /// <summary>
        /// The control currently underneath the mouse cursor
        /// </summary>
        public Control MouseOver { get; private set; }

        private Control _Focused;
        public Control Focused {
            get => _Focused;
            set {
                if (value != null && (!value.AcceptsFocus || !value.Enabled))
                    throw new InvalidOperationException();
                var previous = _Focused;
                _Focused = value;
                if (previous != null)
                    FireEvent(UIEvents.LostFocus, previous, _Focused);

                HandleNewFocusTarget(previous, _Focused);

                if (_Focused != null)
                    FireEvent(UIEvents.GotFocus, _Focused, previous);
            }
        }

        public UIContext (IGlyphSource font = null)
            : this (
                new DefaultDecorations {
                    DefaultFont = font
                }
            ) {
        }

        private void TextInputEXT_TextInput (char ch) {
            // Control characters will be handled through the KeyboardState path
            if (
                (ch < ' ') ||
                (ch == 0x7F) // delete
            )
                return;

            HandleKeyEvent(UIEvents.KeyPress, null, ch);
        }

        public UIContext (IDecorationProvider decorations) {
            Decorations = decorations;
        }

        internal bool FireEvent<T> (string name, Control target, T args) {
            // FIXME: Is this right?
            if (target == null)
                return false;
            if (EventBus == null)
                return true;
            if (EventBus.Broadcast(target, name, args))
                return true;
            return target.HandleEvent(name, args);
        }

        internal bool FireEvent (string name, Control target) {
            // FIXME: Is this right?
            if (target == null)
                return false;
            if (EventBus == null)
                return true;
            if (EventBus.Broadcast<object>(target, name, null))
                return true;
            return target.HandleEvent(name);
        }

        private void HandleNewFocusTarget (Control previous, Control target) {
            if (target?.AcceptsTextInput ?? false) {
                if (previous?.AcceptsTextInput ?? false) {
                } else {
                    TextInputEXT.StartTextInput();
                    TextInputEXT.TextInput += TextInputEXT_TextInput;
                }
            } else if (previous?.AcceptsTextInput ?? false) {
                TextInputEXT.TextInput -= TextInputEXT_TextInput;
                TextInputEXT.StopTextInput();
            }
        }

        private Tooltip GetTooltipInstance () {
            if (CachedTooltip == null) {
                CachedTooltip = new Tooltip {
                    PaintOrder = 9999
                };
                Controls.Add(CachedTooltip);
            }

            return CachedTooltip;
        }

        public void UpdateLayout () {
            var context = new UIOperationContext {
                UIContext = this,
                AnimationTime = (float)Time.Seconds,
                Modifiers = CurrentModifiers,
                MouseButtonHeld = LastMouseButtonState,
                MousePosition = LastMousePosition
            };

            Layout.Clear();

            Layout.SetFixedSize(Layout.Root, CanvasSize);
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            foreach (var control in Controls)
                control.GenerateLayoutTree(context, Layout.Root);

            Layout.Update();

            if (TooltipPending) {
                TooltipPending = false;
                CachedTooltip.Visible = true;
            }
        }

        public void UpdateInput (
            Vector2 mousePosition, bool leftButtonPressed, KeyboardState keyboardState,
            float mouseWheelDelta = 0
        ) {
            var previouslyHovering = Hovering;
            MouseOver = HitTest(mousePosition);

            if ((Focused != null) && !Focused.Enabled)
                Focused = null;

            if ((MouseOver != MouseCaptured) && (MouseCaptured != null))
                Hovering = null;
            else
                Hovering = MouseOver;

            if (Hovering != previouslyHovering)
                HandleHoverTransition(previouslyHovering, Hovering);

            var mouseEventTarget = MouseCaptured ?? Hovering;

            ProcessKeyboardState(ref LastKeyboardState, ref keyboardState);

            if (LastMousePosition != mousePosition) {
                if (leftButtonPressed)
                    HandleMouseDrag(mouseEventTarget, mousePosition);
                else
                    HandleMouseMove(mouseEventTarget, mousePosition);
            }

            if (!LastMouseButtonState && leftButtonPressed) {
                // FIXME: This one should probably always be Hovering
                HandleMouseDown(mouseEventTarget, mousePosition);
            } else if (LastMouseButtonState && !leftButtonPressed) {
                if (Hovering != null)
                    HandleMouseUp(mouseEventTarget, mousePosition);

                if (MouseCaptured != null) {
                    if (Hovering == MouseCaptured)
                        HandleClick(MouseCaptured, mousePosition);
                    else
                        HandleDrag(MouseCaptured, Hovering);
                }

                MouseCaptured = null;
            } else if (!leftButtonPressed) {
                // Shouldn't be necessary but whatever
                MouseCaptured = null;
            }

            if (mouseWheelDelta != 0)
                HandleScroll(MouseCaptured ?? Hovering, mouseWheelDelta);

            LastKeyboardState = keyboardState;
            LastMouseButtonState = leftButtonPressed;
            LastMousePosition = mousePosition;
        }

        private void ProcessKeyboardState (ref KeyboardState previous, ref KeyboardState current) {
            CurrentModifiers = new KeyboardModifiers {
                LeftControl = current.IsKeyDown(Keys.LeftControl),
                RightControl = current.IsKeyDown(Keys.RightControl),
                LeftShift = current.IsKeyDown(Keys.LeftShift),
                RightShift = current.IsKeyDown(Keys.RightShift),
                LeftAlt = current.IsKeyDown(Keys.LeftAlt),
                RightAlt = current.IsKeyDown(Keys.RightAlt),
            };

            var now = Time.Seconds;
            for (int i = 0; i < 255; i++) {
                var key = (Keys)i;

                // Clumsily filter out keys that would generate textinput events
                if (!CurrentModifiers.Control && !CurrentModifiers.Alt) {
                    if ((key >= Keys.D0) && (key <= Keys.NumPad9))
                        continue;
                    if ((key >= Keys.OemSemicolon) && (key <= Keys.OemBackslash))
                        continue;
                }

                var wasPressed = previous.IsKeyDown(key);
                var isPressed = current.IsKeyDown(key);

                if (isPressed != wasPressed) {
                    HandleKeyEvent(isPressed ? UIEvents.KeyDown : UIEvents.KeyUp, key, null);
                    if (isPressed) {
                        LastKeyEvent = key;
                        LastKeyEventTime = now;
                        LastKeyEventFirstTime = now;
                        HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                } else if (isPressed && (LastKeyEvent == key)) {
                    double repeatSpeed = Arithmetic.Lerp(KeyRepeatIntervalSlow, KeyRepeatIntervalFast, (float)((now - LastKeyEventFirstTime) / KeyRepeatAccelerationDelay));
                    if (
                        ((now - LastKeyEventFirstTime) >= FirstKeyRepeatDelay) &&
                        ((now - LastKeyEventTime) >= repeatSpeed) &&
                        !SuppressRepeatKeys.Contains(key)
                    ) {
                        LastKeyEventTime = now;
                        HandleKeyEvent(UIEvents.KeyPress, key, null);
                    }
                }
            }
        }

        public bool HandleKeyEvent (string name, Keys? key, char? ch) {
            if (Focused == null)
                return false;

            var evt = new KeyEventArgs {
                Context = this,
                Modifiers = CurrentModifiers,
                Key = key,
                Char = ch
            };

            // FIXME: Suppress events with a char if the target doesn't accept text input?
            if (Focused.HandleEvent(name, evt))
                return true;

            if (FireEvent(name, Focused, evt))
                return true;

            return false;
        }

        private MouseEventArgs MakeMouseEventArgs (Control target, Vector2 globalPosition) {
            if (target == null)
                return default(MouseEventArgs);

            var box = target.GetRect(Layout, contentRect: false);
            var contentBox = target.GetRect(Layout, contentRect: true);
            var mdp = MouseDownPosition ?? globalPosition;
            var travelDistance = (globalPosition - mdp).Length();
            return new MouseEventArgs {
                Context = this,
                Modifiers = CurrentModifiers,
                Focused = Focused,
                MouseOver = MouseOver,
                Hovering = Hovering,
                MouseCaptured = MouseCaptured,
                GlobalPosition = globalPosition,
                LocalPosition = globalPosition - contentBox.Position,
                Box = box,
                ContentBox = contentBox,
                MouseDownPosition = mdp,
                MovedSinceMouseDown = travelDistance >= MinimumMovementDistance,
                DoubleClicking = IsInDoubleClickWindow(target, globalPosition) && (MouseCaptured != null)
            };
        }

        private void HandleMouseDown (Control target, Vector2 globalPosition) {
            MouseDownPosition = globalPosition;
            if (target != null && (target.AcceptsMouseInput && target.Enabled))
                MouseCaptured = target;
            if (target == null || (target.AcceptsFocus && target.Enabled))
                Focused = target;
            // FIXME: Suppress if disabled?
            LastMouseDownTime = Time.Seconds;
            FireEvent(UIEvents.MouseDown, target, MakeMouseEventArgs(target, globalPosition));
        }

        private void HandleMouseUp (Control target, Vector2 globalPosition) {
            MouseDownPosition = null;
            // FIXME: Suppress if disabled?
            FireEvent(UIEvents.MouseUp, target, MakeMouseEventArgs(target, globalPosition));
        }

        private void HandleMouseMove (Control target, Vector2 globalPosition) {
            FireEvent(UIEvents.MouseMove, target, MakeMouseEventArgs(target, globalPosition));
        }

        private void HandleMouseDrag (Control target, Vector2 globalPosition) {
            // FIXME: Suppress if disabled?
            FireEvent(UIEvents.MouseDrag, target, MakeMouseEventArgs(target, globalPosition));
        }

        private void HandleScroll (Control control, float delta) {
            while (control != null) {
                if (FireEvent(UIEvents.Scroll, control, delta))
                    return;

                if (control.TryGetParent(out control))
                    continue;
            }
        }

        private void ClearTooltip () {
            if (CachedTooltip == null)
                return;

            CachedTooltip.Text = "";
            CachedTooltip.Visible = false;
            TooltipPending = false;
        }

        private void ShowTooltip (Control anchor, AbstractString text) {
            var instance = GetTooltipInstance();
            instance.Text = text;
            var rect = Layout.GetRect(anchor.LayoutKey);
            instance.MaximumWidth = CanvasSize.X * 0.75f;
            instance.Margins = new Margins(rect.Left, rect.Extent.Y + 8, 0, 0);
            instance.Visible = false;
            TooltipPending = true;
        }

        private void HandleHoverTransition (Control previous, Control current) {
            if (previous != null)
                FireEvent(UIEvents.MouseLeave, previous, current);

            if (current != null) {
                FireEvent(UIEvents.MouseEnter, current, previous);
                var ttc = current.TooltipContent;
                var tooltipText = ttc.Text;
                if (ttc.GetText != null)
                    tooltipText = ttc.GetText(current);

                if (!tooltipText.IsNull)
                    ShowTooltip(current, tooltipText);
                else
                    ClearTooltip();
            } else {
                ClearTooltip();
            }
        }

        private bool IsInDoubleClickWindow (Control target, Vector2 position) {
            var movedDistance = (position - LastClickPosition).Length();
            if (
                (LastClickTarget == target) &&
                (movedDistance < MinimumMovementDistance)
            ) {
                var elapsed = Time.Seconds - LastClickTime;
                return elapsed < DoubleClickWindowSize;
            }
            return false;
        }

        private void HandleClick (Control target, Vector2 mousePosition) {
            if (!target.Enabled)
                return;

            if (IsInDoubleClickWindow(target, mousePosition))
                SequentialClickCount++;
            else
                SequentialClickCount = 1;

            LastClickPosition = mousePosition;
            LastClickTarget = target;
            LastClickTime = LastMouseDownTime;
            FireEvent(UIEvents.Click, target, SequentialClickCount);
        }

        private void HandleDrag (Control originalTarget, Control finalTarget) {
            // FIXME
        }

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position, bool acceptsMouseInputOnly = false, bool acceptsFocusOnly = false) {
            var sorted = Controls.InOrder(Control.PaintOrderComparer.Instance);
            for (var i = sorted.Count - 1; i >= 0; i--) {
                var control = sorted.DangerousGetItem(i);
                var result = control.HitTest(Layout, position, acceptsMouseInputOnly, acceptsFocusOnly);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void RasterizePass (UIOperationContext context, Control control, RasterizePasses pass) {
            var passContext = context.Clone();
            passContext.Renderer = context.Renderer.MakeSubgroup();
            passContext.Renderer.DepthStencilState = DepthStencilState.None;
            passContext.Pass = pass;
            control.Rasterize(passContext);
        }

        public void Rasterize (ref ImperativeRenderer renderer) {
            var context = new UIOperationContext {
                UIContext = this,
                AnimationTime = (float)Time.Seconds,
                Modifiers = CurrentModifiers,
                MouseButtonHeld = LastMouseButtonState,
                MousePosition = LastMousePosition
            };

            // Ensure each control is rasterized in its own group of passes, so that top level controls can
            //  properly overlap each other
            var seq = Controls.InOrder(Control.PaintOrderComparer.Instance);
            foreach (var control in seq) {
                context.Renderer = renderer.MakeSubgroup();
                context.Renderer.DepthStencilState = DepthStencilState.None;
                RasterizePass(context, control, RasterizePasses.Below);
                RasterizePass(context, control, RasterizePasses.Content);
                RasterizePass(context, control, RasterizePasses.Above);
            }
        }

        public void Dispose () {
            Layout.Dispose();
        }
    }

    public class UIOperationContext {
        public UIContext UIContext;
        public IDecorationProvider DecorationProvider => UIContext.Decorations;
        public LayoutContext Layout => UIContext.Layout;
        public ImperativeRenderer Renderer;
        public RasterizePasses Pass;
        public float AnimationTime;
        public KeyboardModifiers Modifiers;
        public bool MouseButtonHeld;
        public Vector2 MousePosition;

        public UIOperationContext Clone () {
            return new UIOperationContext {
                UIContext = UIContext,
                Renderer = Renderer,
                Pass = Pass,
                AnimationTime = AnimationTime,
                Modifiers = Modifiers,
                MouseButtonHeld = MouseButtonHeld,
                MousePosition = MousePosition
            };
        }
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

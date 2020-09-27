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
            KeyUp = string.Intern("KeyUp"),
            Moved = string.Intern("Moved"),
            ValueChanged = string.Intern("ValueChanged");
    }

    public class UIContext : IDisposable {
        private static readonly HashSet<Keys> SuppressRepeatKeys = new HashSet<Keys> {
            Keys.LeftAlt,
            Keys.LeftControl,
            Keys.LeftShift,
            Keys.RightAlt,
            Keys.RightControl,
            Keys.RightShift,
            Keys.Escape,
            Keys.CapsLock,
            Keys.Scroll,
            Keys.NumLock,
            Keys.Insert
        };

        /// <summary>
        /// Globally tracks whether text editing should be in insert or overwrite mode
        /// </summary>
        public bool TextInsertionMode = true;

        /// <summary>
        /// Tooltips will only appear after the mouse remains over a single control for this long (in seconds)
        /// </summary>
        public double TooltipAppearanceDelay = 0.4;
        /// <summary>
        /// If the mouse leaves tooltip-bearing controls for this long (in seconds) the tooltip appearance delay will reset
        /// </summary>
        public double TooltipDisappearDelay = 0.25;

        public float TooltipSpacing = 8;

        /// <summary>
        /// Double-clicks will only be tracked if this far apart or less (in seconds)
        /// </summary>
        public double DoubleClickWindowSize = 0.4;
        /// <summary>
        /// If the mouse is only moved this far (in pixels) it will be treated as no movement for the purposes of click detection
        /// </summary>
        public float MinimumMovementDistance = 4;

        /// <summary>
        /// A key must be held for this long (in seconds) before repeating begins
        /// </summary>
        public double FirstKeyRepeatDelay = 0.4;
        /// <summary>
        /// Key repeating begins at this rate (in seconds)
        /// </summary>
        public double KeyRepeatIntervalSlow = 0.09;
        /// <summary>
        /// Key repeating accelerates to this rate (in seconds) over time
        /// </summary>
        public double KeyRepeatIntervalFast = 0.03;
        /// <summary>
        /// The key repeating rate accelerates over this period of time (in seconds)
        /// </summary>
        public double KeyRepeatAccelerationDelay = 4.5;

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
        private double? FirstTooltipHoverTime;
        private double LastTooltipHoverTime;

        private Tooltip CachedTooltip;
        private Controls.StaticText CachedCompositionPreview;

        private AutoRenderTarget ScratchRenderTarget;

        public ControlCollection Controls { get; private set; }
        public ITimeProvider TimeProvider;

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

        private bool IsTextInputRegistered = false;
        private bool IsCompositionActive = false;

        public float Now => (float)TimeProvider.Seconds;

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

        private void TextInputEXT_TextInput (char ch) {
            // Control characters will be handled through the KeyboardState path
            if (char.IsControl(ch))
                return;

            HandleKeyEvent(UIEvents.KeyPress, null, ch);
        }

        private void TerminateComposition () {
            if (IsCompositionActive)
                Console.WriteLine("Terminating composition");
            IsCompositionActive = false;

            if (CachedCompositionPreview != null) {
                CachedCompositionPreview.Text = "";
                CachedCompositionPreview.Visible = false;
            }
        }

        private void UpdateComposition (string currentText, int cursorPosition, int selectionLength) {
            IsCompositionActive = true;
            Console.WriteLine($"Composition text '{currentText}' with cursor at offset {cursorPosition}, selection length {selectionLength}");

            var instance = GetCompositionPreviewInstance();
            instance.Text = currentText;
            instance.Invalidate();

            var offset = Layout.GetRect(Focused.LayoutKey).Position;
            // HACK
            var editable = Focused as Controls.EditableText;
            if (editable != null) {
                var compositionOffset = editable.GetCursorPosition();
                offset += compositionOffset;
            }

            instance.Margins = new Margins(offset.X, offset.Y, 0, 0);
            instance.Visible = true;
        }

        private void TextInputEXT_TextEditing (string text, int cursorPosition, int length) {
            if ((text == null) || (text.Length == 0)) {
                TerminateComposition();
                return;
            }

            UpdateComposition(text, cursorPosition, length);
        }

        public UIContext (IGlyphSource font = null, ITimeProvider timeProvider = null)
            : this (
                decorations: new DefaultDecorations {
                    DefaultFont = font
                },
                timeProvider: timeProvider
            ) {
        }

        public UIContext (IDecorationProvider decorations, ITimeProvider timeProvider = null) {
            Controls = new ControlCollection(this);
            Decorations = decorations;
            TimeProvider = TimeProvider ?? new DotNetTimeProvider();
        }

        internal bool FireEvent<T> (string name, Control target, T args, bool suppressHandler = false) {
            // FIXME: Is this right?
            if (target == null)
                return false;
            if (EventBus == null)
                return true;
            if (EventBus.Broadcast(target, name, args))
                return true;
            if (suppressHandler)
                return false;
            else
                return target.HandleEvent(name, args);
        }

        internal bool FireEvent (string name, Control target, bool suppressHandler = false) {
            // FIXME: Is this right?
            if (target == null)
                return false;
            if (EventBus == null)
                return true;
            if (EventBus.Broadcast<object>(target, name, null))
                return true;
            if (suppressHandler)
                return false;
            else
                return target.HandleEvent(name);
        }

        private void HandleNewFocusTarget (Control previous, Control target) {
            if (target?.AcceptsTextInput ?? false) {
                if (previous?.AcceptsTextInput ?? false) {
                } else {
                    if (!IsTextInputRegistered) {
                        IsTextInputRegistered = true;
                        TextInputEXT.TextInput += TextInputEXT_TextInput;
                        TextInputEXT.TextEditing += TextInputEXT_TextEditing;
                    }
                    TextInputEXT.StartTextInput();
                }
            } else if (previous?.AcceptsTextInput ?? false) {
                TextInputEXT.StopTextInput();
                IsCompositionActive = false;
            }
        }

        private Tooltip GetTooltipInstance () {
            if (CachedTooltip == null) {
                CachedTooltip = new Tooltip {
                    PaintOrder = 9999,
                    Wrap = true,
                    Multiline = true
                };
                Controls.Add(CachedTooltip);
            }

            return CachedTooltip;
        }

        private Controls.StaticText GetCompositionPreviewInstance () {
            if (CachedCompositionPreview == null) {
                CachedCompositionPreview = new Controls.StaticText {
                    PaintOrder = 9999,
                    Wrap = false,
                    Multiline = false,
                    Intangible = true,
                    LayoutFlags = ControlFlags.Layout_Floating,
                    BackgroundColor = Color.White,
                    TextColor = Color.Black,
                    CustomDecorations = Decorations.CompositionPreview
                };
                Controls.Add(CachedCompositionPreview);
            }

            return CachedCompositionPreview;
        }

        public void UpdateLayout () {
            var context = MakeOperationContext();

            Layout.Clear();

            Layout.SetFixedSize(Layout.Root, CanvasSize);
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            foreach (var control in Controls)
                control.GenerateLayoutTree(context, Layout.Root);

            Layout.Update();
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

            UpdateTooltip();

            LastKeyboardState = keyboardState;
            LastMouseButtonState = leftButtonPressed;
            LastMousePosition = mousePosition;
        }

        private bool IsTooltipActive {
            get {
                return (CachedTooltip != null) && CachedTooltip.Visible;
            }
        }

        private void ResetTooltipShowTimer () {
            FirstTooltipHoverTime = null;
        }

        private AbstractString GetTooltipContent (Control target) {
            var ttc = target.TooltipContent;
            var tooltipText = ttc.Text;
            if (ttc.GetText != null)
                tooltipText = ttc.GetText(target);
            return tooltipText;
        }

        private void UpdateTooltip () {
            if (LastMouseButtonState)
                return;

            var now = Now;
            var tooltipContent = default(AbstractString);
            if (Hovering != null)
                tooltipContent = GetTooltipContent(Hovering);

            if (!tooltipContent.IsNull) {
                if (!FirstTooltipHoverTime.HasValue)
                    FirstTooltipHoverTime = now;

                if (IsTooltipActive)
                    LastTooltipHoverTime = now;

                var hoveringFor = now - FirstTooltipHoverTime;
                var disappearTimeout = now - LastTooltipHoverTime;

                if ((hoveringFor >= TooltipAppearanceDelay) || (disappearTimeout < TooltipDisappearDelay))
                    ShowTooltip(Hovering, tooltipContent);
            } else {
                ClearTooltip();

                var elapsed = now - LastTooltipHoverTime;
                if (elapsed >= TooltipDisappearDelay)
                    ResetTooltipShowTimer();
            }
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

            if (IsCompositionActive)
                return;

            var now = Now;
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

        private void HideTooltipForMouseInput () {
            ResetTooltipShowTimer();
            ClearTooltip();
            FirstTooltipHoverTime = null;
            LastTooltipHoverTime = 0;
        }

        private void HandleMouseDown (Control target, Vector2 globalPosition) {
            HideTooltipForMouseInput();

            MouseDownPosition = globalPosition;
            if (target != null && (target.AcceptsMouseInput && target.Enabled))
                MouseCaptured = target;
            if (target == null || (target.AcceptsFocus && target.Enabled))
                Focused = target;
            // FIXME: Suppress if disabled?
            LastMouseDownTime = Now;
            FireEvent(UIEvents.MouseDown, target, MakeMouseEventArgs(target, globalPosition));
        }

        private void HandleMouseUp (Control target, Vector2 globalPosition) {
            HideTooltipForMouseInput();
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
        }

        private void UpdateSubtreeLayout (Control subtreeRoot) {
            ControlKey parentKey;
            Control parent;
            if (!subtreeRoot.TryGetParent(out parent))
                parentKey = Layout.Root;
            else if (!parent.LayoutKey.IsInvalid)
                parentKey = parent.LayoutKey;
            else {
                // Just in case for some reason the control's parent also hasn't had layout happen...
                UpdateSubtreeLayout(parent);
                return;
            }

            var tempCtx = MakeOperationContext();
            subtreeRoot.GenerateLayoutTree(tempCtx, parentKey, subtreeRoot.LayoutKey.IsInvalid ? (ControlKey?)null : subtreeRoot.LayoutKey);
            Layout.UpdateSubtree(subtreeRoot.LayoutKey);
        }

        private void ShowTooltip (Control anchor, AbstractString text) {
            var instance = GetTooltipInstance();

            var textChanged = !instance.Text.Equals(text);

            var rect = Layout.GetRect(anchor.LayoutKey);

            if (textChanged || !instance.Visible) {
                var idealMaxWidth = CanvasSize.X * 0.35f;

                instance.Text = text;
                // FIXME: Shift it around if it's already too close to the right side
                instance.MaximumWidth = idealMaxWidth;
                instance.Invalidate();

                UpdateSubtreeLayout(instance);
            }

            var instanceBox = instance.GetRect(Layout);
            var newX = Arithmetic.Clamp(rect.Left + (rect.Width / 2f) - (instanceBox.Width / 2f), TooltipSpacing, CanvasSize.X - instanceBox.Width - (TooltipSpacing * 2));
            var newY = rect.Extent.Y + TooltipSpacing;
            if ((instanceBox.Height + rect.Extent.Y) >= CanvasSize.Y)
                newY = rect.Top - instanceBox.Height - TooltipSpacing;
            instance.Margins = new Margins(newX, newY, 0, 0);
            instance.Visible = true;
            UpdateSubtreeLayout(instance);
        }

        private void HandleHoverTransition (Control previous, Control current) {
            if (previous != null)
                FireEvent(UIEvents.MouseLeave, previous, current);

            if (current != null)
                FireEvent(UIEvents.MouseEnter, current, previous);

            ResetTooltipShowTimer();
        }

        private bool IsInDoubleClickWindow (Control target, Vector2 position) {
            var movedDistance = (position - LastClickPosition).Length();
            if (
                (LastClickTarget == target) &&
                (movedDistance < MinimumMovementDistance)
            ) {
                var elapsed = Now - LastClickTime;
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

        private UIOperationContext MakeOperationContext () {
            return new UIOperationContext {
                UIContext = this,
                Now = Now,
                Modifiers = CurrentModifiers,
                MouseButtonHeld = LastMouseButtonState,
                MousePosition = LastMousePosition
            };
        }

        internal AutoRenderTarget GetScratchRenderTarget (RenderCoordinator coordinator) {
            if (ScratchRenderTarget == null)
                ScratchRenderTarget = new AutoRenderTarget(coordinator, (int)CanvasSize.X, (int)CanvasSize.Y, preferredDepthFormat: DepthFormat.Depth24Stencil8);
            else
                ScratchRenderTarget.Resize((int)CanvasSize.X, (int)CanvasSize.Y);
            return ScratchRenderTarget;
        }

        internal void ReleaseScratchRenderTarget (AutoRenderTarget rt) {
            // FIXME: Do we need to do anything here?
        }

        public void Rasterize (ref ImperativeRenderer renderer) {
            var context = MakeOperationContext();
            context.PrepassRenderer = renderer.MakeSubgroup();

            // Ensure each control is rasterized in its own group of passes, so that top level controls can
            //  properly overlap each other
            var seq = Controls.InOrder(Control.PaintOrderComparer.Instance);
            foreach (var control in seq) {
                context.Renderer = renderer.MakeSubgroup();
                context.Renderer.DepthStencilState = DepthStencilState.None;
                control.Rasterize(context);
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
        public ImperativeRenderer PrepassRenderer;
        public ImperativeRenderer Renderer;
        public RasterizePasses Pass;
        public float Now { get; internal set; }
        public KeyboardModifiers Modifiers { get; internal set; }
        public bool MouseButtonHeld { get; internal set; }
        public Vector2 MousePosition { get; internal set; }

        public UIOperationContext Clone () {
            return new UIOperationContext {
                UIContext = UIContext,
                PrepassRenderer = PrepassRenderer,
                Renderer = Renderer,
                Pass = Pass,
                Now = Now,
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

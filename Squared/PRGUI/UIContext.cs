using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Input;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        /// <summary>
        /// Configures the size of the rendering canvas
        /// </summary>
        public Vector2 CanvasSize;
        public RectF CanvasRect => new RectF(0, 0, CanvasSize.X, CanvasSize.Y);

        /// <summary>
        /// Control events are broadcast on this bus
        /// </summary>
        public readonly EventBus EventBus;

        /// <summary>
        /// The layout engine used to compute control sizes and positions
        /// </summary>
        public readonly LayoutContext Layout = new LayoutContext();

        /// <summary>
        /// The top-level controls managed by the layout engine. Each one gets a separate rendering layer
        /// </summary>
        public ControlCollection Controls { get; private set; }

        internal void ClearKeyboardSelection () {
            SuppressAutoscrollDueToInputScroll = false;
            _KeyboardSelection = null;
            MousePositionWhenKeyboardSelectionWasLastUpdated = LastMousePosition;
        }

        internal void SetKeyboardSelection (Control control, bool forUser) {
            if (forUser) {
                if (control != null)
                    _PreferredTooltipSource = control;
                // FIXME: Do this after the equals check?
                SuppressAutoscrollDueToInputScroll = false;
            }
            if (control == _KeyboardSelection)
                return;
            _KeyboardSelection = control;
            MousePositionWhenKeyboardSelectionWasLastUpdated = LastMousePosition;
        }

        /// <summary>
        /// Indicates that the context is currently being interacted with by the user
        /// </summary>
        public bool IsActive {
            get =>
                (MouseOverLoose != null) ||
                    _LastInput.AreAnyKeysHeld ||
                    (KeyboardSelection != null) ||
                    (MouseCaptured != null) ||
                    AcceleratorOverlayVisible ||
                    (ModalStack.Count > 0);
        }

        /// <summary>
        /// Indicates that input is currently in progress (a key or button is held)
        /// </summary>
        public bool IsInputInProgress {
            get =>
                _LastInput.AreAnyKeysHeld ||
                    (MouseCaptured != null) ||
                    AcceleratorOverlayVisible;
        }

        internal void Log (string text) {
            if (OnLogMessage != null)
                OnLogMessage(text);
            else
                DefaultLogHandler(text);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        internal void DebugLog (string text) {
            if (OnLogMessage != null)
                OnLogMessage(text);
            else
                DefaultLogHandler(text);
        }

        internal void DefaultLogHandler (string text) {
            if (System.Diagnostics.Debugger.IsAttached)
                System.Diagnostics.Debug.WriteLine(text);
            else
                Console.WriteLine(text);
        }

        public UIContext (DefaultMaterialSet materials, IGlyphSource font = null, ITimeProvider timeProvider = null)
            : this (
                materials: materials,
                decorations: new DefaultDecorations(materials) {
                    DefaultFont = font
                },
                timeProvider: timeProvider
            ) {
        }

        public UIContext (
            DefaultMaterialSet materials, IDecorationProvider decorations, 
            IAnimationProvider animations = null, ITimeProvider timeProvider = null
        ) {
            EventBus = new EventBus();
            EventBus.AfterBroadcast += EventBus_AfterBroadcast;
            Controls = new ControlCollection(this);
            Decorations = decorations;
            Animations = animations ?? (decorations as IAnimationProvider);
            TimeProvider = TimeProvider ?? new DotNetTimeProvider();
            Materials = materials;
            TTS = new Accessibility.TTS(this);
            _LastInput = _CurrentInput = new InputState {
                CursorPosition = new Vector2(-99999)
            };
            _LastInput.HeldKeys = _LastHeldKeys;
            _CurrentInput.HeldKeys = _CurrentHeldKeys;
            CreateInputIDs();
        }

        public InputID GetInputID (Keys key, KeyboardModifiers modifiers) {
            foreach (var iid in InputIDs) {
                if ((iid.Key == key) && iid.Modifiers.Equals(modifiers))
                    return iid;
            }
            var result = new InputID { Key = key, Modifiers = modifiers };
            InputIDs.Add(result);
            return result;
        }

        private void EventBus_AfterBroadcast (EventBus sender, object eventSource, string eventName, object eventArgs, bool eventWasHandled) {
            if (eventWasHandled)
                return;

            UnhandledEvents.Add(new UnhandledEvent {
                Source = eventSource as Control,
                Name = eventName
            });
        }

        private Tooltip GetTooltipInstance () {
            if (CachedTooltip == null) {
                CachedTooltip = new Tooltip {
                    Appearance = { Opacity = 0 }
                };
                Controls.Add(CachedTooltip);
            }

            return CachedTooltip;
        }

        private Controls.StaticText GetCompositionPreviewInstance () {
            if (CachedCompositionPreview == null) {
                CachedCompositionPreview = new Controls.StaticText {
                    DisplayOrder = int.MaxValue,
                    Wrap = false,
                    Multiline = false,
                    Intangible = true,
                    LayoutFlags = ControlFlags.Layout_Floating,
                    Appearance = {
                        BackgroundColor = Color.White,
                        TextColor = Color.Black,
                        Decorator = Decorations.CompositionPreview
                    }
                };
                Controls.Add(CachedCompositionPreview);
            }

            return CachedCompositionPreview;
        }

        public bool CaptureMouse (Control target) {
            return CaptureMouse(target, out Control temp);
        }

        public bool CaptureMouse (Control target, out Control previous) {
            previous = Focused;
            if ((MouseCaptured != null) && (MouseCaptured != target))
                RetainCaptureRequested = target;
            // HACK: If we used IsValidFocusTarget here, it would break scenarios where a control is capturing
            //  focus before being shown or being enabled
            AutomaticallyTransferFocusOnTopLevelChange(target);
            if (target.AcceptsFocus)
                TrySetFocus(target, true, true);
            MouseCaptured = target;
            _PreferredTooltipSource = target;
            return (MouseCaptured == target);
        }

        public void RetainCapture (Control target) {
            RetainCaptureRequested = target;
        }

        public void ReleaseCapture (Control target, Control focusDonor) {
            if (Focused == target)
                Focused = focusDonor;
            if (Hovering == target)
                Hovering = null;
            if (MouseCaptured == target)
                MouseCaptured = null;
            ReleasedCapture = target;
            if (RetainCaptureRequested == target)
                RetainCaptureRequested = null;
        }

        private void DoUpdateLayoutInternal (ref UIOperationContext context, bool secondTime) {
            Layout.CanvasSize = CanvasSize;
            Layout.SetContainerFlags(Layout.Root, ControlFlags.Container_Row | ControlFlags.Container_Constrain_Size);

            _TopLevelControls.Clear();
            Controls.CopyTo(_TopLevelControls);

            foreach (var control in _TopLevelControls)
                control.GenerateLayoutTree(
                    ref context, Layout.Root, 
                    (secondTime && !control.LayoutKey.IsInvalid) 
                        ? control.LayoutKey 
                        : (ControlKey?)null
                );
        }

        private bool NotifyLayoutListeners (ref UIOperationContext context) {
            bool relayoutRequested = context.RelayoutRequestedForVisibilityChange;
            if (relayoutRequested && LogRelayoutRequests)
                Log($"Relayout requested due to visibility change");

            foreach (var listener in context.PostLayoutListeners) {
                var wasRequested = relayoutRequested;
                listener.OnLayoutComplete(ref context, ref relayoutRequested);
                if (relayoutRequested != wasRequested) {
                    var ctl = (Control)listener;
                    if (LogRelayoutRequests)
                        Log($"Relayout requested by {ctl.DebugLabel ?? listener.GetType().Name}");
                }
            }
            return relayoutRequested;
        }

        public void Update () {
            FrameIndex++;

            var context = MakeOperationContext();
            var pll = Interlocked.Exchange(ref _PostLayoutListeners, null);
            if (pll == null)
                pll = new UnorderedList<IPostLayoutListener>();
            else
                pll.Clear();
            context.PostLayoutListeners = pll;

            try {
                Layout.Clear();

                DoUpdateLayoutInternal(ref context, false);
                Layout.Update();

                if (NotifyLayoutListeners(ref context)) {
                    DoUpdateLayoutInternal(ref context, true);
                    Layout.Update();
                    NotifyLayoutListeners(ref context);
                }
            } finally {
                Interlocked.CompareExchange(ref _PostLayoutListeners, pll, null);
            }

            UpdateAutoscroll();
        }

        private void UpdateCaptureAndHovering (Vector2 mousePosition, Control exclude = null) {
            // FIXME: This breaks drag-to-scroll
            // MouseOver = HitTest(mousePosition, rejectIntangible: true);
            MouseOver = MouseOverLoose = HitTest(mousePosition, rejectIntangible: false);

            if ((MouseOver != MouseCaptured) && (MouseCaptured != null))
                Hovering = null;
            else
                Hovering = MouseOver;
        }

        public void ShowModal (IModal modal, bool topmost) {
            if (ModalStack.Contains(modal))
                throw new InvalidOperationException("Modal already visible");
            var ctl = (Control)modal;
            ctl.DisplayOrder = Controls.PickNewHighestDisplayOrder(ctl, topmost);
            if (!Controls.Contains(ctl))
                Controls.Add(ctl);
            NotifyModalShown(modal);
        }

        public bool CloseActiveModal (bool force = false) {
            if (ModalStack.Count <= 0)
                return false;
            return CloseModal(ModalStack[ModalStack.Count - 1], force);
        }

        public bool CloseModal (IModal modal, bool force = false) {
            if (!ModalStack.Contains(modal))
                return false;
            return modal.Close(force);
        }

        public void NotifyModalShown (IModal modal) {
            if (ModalStack.Contains(modal))
                throw new InvalidOperationException("Modal already visible");
            var ctl = (Control)modal;
            if (!Controls.Contains(ctl))
                throw new InvalidOperationException("Modal not in top level controls list");
            TopLevelFocusMemory.Remove(ctl);
            ModalStack.Add(modal);
            TrySetFocus(ctl, false, false);
            FireEvent(UIEvents.Shown, ctl);
        }

        public void NotifyModalClosed (IModal modal) {
            if (modal == null)
                return;

            var ctl = (Control)modal;
            var newFocusTarget = (TopLevelFocused == ctl)
                ? modal.FocusDonor
                : null;
            ReleaseCapture(ctl, modal.FocusDonor);
            if (!ModalStack.Contains(modal))
                return;
            ModalStack.Remove(modal);
            FireEvent(UIEvents.Closed, ctl);
            if (newFocusTarget != null)
                TrySetFocus(newFocusTarget, false, false);
        }

        public bool IsPriorityInputSource (IInputSource source) {
            if (ScratchInputSources.Count > 0)
                return ScratchInputSources.IndexOf(source) == 0;
            else
                return InputSources.IndexOf(source) == 0;
        }

        public void PromoteInputSource (IInputSource source) {
            var existingIndex = InputSources.IndexOf(source);
            if (existingIndex == 0)
                return;
            else if (existingIndex > 0)
                InputSources.RemoveAt(existingIndex);
            InputSources.Insert(0, source);
        }

        public void UpdateInput (bool processEvents = true) {
            Now = (float)TimeProvider.Seconds;
            NowL = TimeProvider.Ticks;

            if ((_CurrentInput.CursorPosition.X < -999) ||
                (_CurrentInput.CursorPosition.Y < -999))
                _CurrentInput.CursorPosition = CanvasSize / 2f;

            _LastInput = _CurrentInput;
            _LastInput.HeldKeys = _LastHeldKeys;
            _LastHeldKeys.Clear();
            foreach (var k in _CurrentHeldKeys)
                _LastHeldKeys.Add(k);

            _CurrentHeldKeys.Clear();
            _CurrentInput = new InputState {
                HeldKeys = _CurrentHeldKeys,
                CursorPosition = _LastInput.CursorPosition,
                WheelValue = _LastInput.WheelValue
            };

            ScratchInputSources.Clear();
            foreach (var src in InputSources)
                ScratchInputSources.Add(src);

            foreach (var src in ScratchInputSources) {
                src.SetContext(this);
                src.Update(ref _LastInput, ref _CurrentInput);
            }

            ScratchInputSources.Clear();

            _CurrentInput.AreAnyKeysHeld = _CurrentInput.HeldKeys.Count > 0;

            if (!processEvents)
                return;

            var mousePosition = _CurrentInput.CursorPosition;

            PreviousUnhandledEvents.Clear();
            PreviousUnhandledEvents.AddRange(UnhandledEvents);
            UnhandledEvents.Clear();

            if (
                (Focused != null) && 
                (!Focused.IsValidFocusTarget || (FindTopLevelAncestor(Focused) == null))
            ) {
                // If the current focused control is no longer enabled or present, attempt to
                //  focus something else in the current top level control, if possible
                if (Controls.Contains(TopLevelFocused))
                    Focused = TopLevelFocused;
                else
                    NotifyControlBecomingInvalidFocusTarget(Focused, false);
            }

            EnsureValidFocus();

            var previouslyFixated = FixatedControl;

            var previouslyHovering = Hovering;

            UpdateCaptureAndHovering(_CurrentInput.CursorPosition);
            var mouseEventTarget = MouseCaptured ?? Hovering;
            var topLevelTarget = (mouseEventTarget != null)
                ? FindTopLevelAncestor(mouseEventTarget)
                : null;

            var activeModal = ActiveModal;
            var wasInputBlocked = false;
            if (
                (activeModal?.BlockInput == true) && 
                (activeModal != topLevelTarget) &&
                (topLevelTarget?.DisplayOrder <= (activeModal as Control)?.DisplayOrder)
            ) {
                mouseEventTarget = null;
                wasInputBlocked = true;
            }

            // If the mouse moves after the keyboard selection was updated, clear it
            if (KeyboardSelection != null) {
                var movedDistance = mousePosition - MousePositionWhenKeyboardSelectionWasLastUpdated;
                if (movedDistance.Length() > MinimumMouseMovementDistance) {
                    if (_CurrentInput.KeyboardNavigationEnded)
                        ClearKeyboardSelection();
                }
            }

            var mouseOverLoose = MouseOverLoose;

            if (LastMousePosition != mousePosition) {
                if (DragToScrollTarget != null)
                    HandleMouseDrag((Control)DragToScrollTarget, mousePosition);
                else if (CurrentMouseButtons != MouseButtons.None)
                    HandleMouseDrag(mouseEventTarget, mousePosition);
                else
                    HandleMouseMove(mouseEventTarget ?? mouseOverLoose, mousePosition);
            }

            var mouseDownPosition = MouseDownPosition;
            var previouslyCaptured = MouseCaptured;
            var processClick = false;

            if ((LastMouseButtons == MouseButtons.None) && (CurrentMouseButtons != MouseButtons.None)) {
                if (
                    (mouseEventTarget == null) && 
                    (mouseOverLoose != null)
                ) {
                    HandleMouseDownPrologue();
                    HandleMouseDownEpilogue(false, mouseOverLoose, mousePosition, CurrentMouseButtons);
                } else {
                    HandleMouseDown(mouseEventTarget, mousePosition, CurrentMouseButtons);
                }
                mouseDownPosition = mouseDownPosition ?? mousePosition;
            } else if ((LastMouseButtons != MouseButtons.None) && (CurrentMouseButtons == MouseButtons.None)) {
                bool scrolled = false;
                if (Hovering != null)
                    scrolled = HandleMouseUp(mouseEventTarget, mousePosition, mouseDownPosition, LastMouseButtons);
                else if (DragToScrollTarget != null)
                    scrolled = TeardownDragToScroll(mouseEventTarget, mousePosition);
                else /* if (MouseCaptured != null) */
                    scrolled = HandleMouseUp(mouseEventTarget, mousePosition, mouseDownPosition, LastMouseButtons);

                // if (MouseCaptured != null) {
                var movedDistance = mousePosition - mouseDownPosition;
                var hasMoved = movedDistance.HasValue &&
                        (movedDistance.Value.Length() >= MinimumMouseMovementDistance);
                if (
                    !hasMoved &&
                    (!scrolled || !SuppressSingleClickOnMovementWhenAppropriate)
                )
                    processClick = true;
                // }

                if (MouseCaptured != null) {
                    if (RetainCaptureRequested == MouseCaptured) {
                        RetainCaptureRequested = null;
                    } else {
                        MouseCaptured = null;
                    }
                }

                // FIXME: Clear LastMouseDownTime?
            } else if (LastMouseButtons != CurrentMouseButtons) {
                FireEvent(UIEvents.MouseButtonsChanged, mouseEventTarget, MakeMouseEventArgs(mouseEventTarget, mousePosition, mouseDownPosition));
            }

            if (processClick && !wasInputBlocked) {
                // FIXME: if a menu is opened by a mousedown event, this will
                //  fire a click on the menu in response to its mouseup
                if (
                    ((Hovering == previouslyCaptured) && (previouslyCaptured != null)) ||
                    ((previouslyCaptured == null) && (Hovering == PreviousMouseDownTarget))
                ) {
                    if ((LastMouseButtons & MouseButtons.Left) == MouseButtons.Left)
                        // FIXME: Is this ?? right
                        HandleClick(previouslyCaptured ?? PreviousMouseDownTarget, mousePosition, mouseDownPosition ?? mousePosition);
                    else
                        ; // FIXME: Fire another event here?
                } else
                    HandleDrag(previouslyCaptured, Hovering);
            }

            var mouseWheelDelta = _CurrentInput.WheelValue - _LastInput.WheelValue;

            if (mouseWheelDelta != 0)
                HandleScroll(MouseOverLoose ?? previouslyCaptured, mouseWheelDelta);

            TickControl(KeyboardSelection, mousePosition, mouseDownPosition);
            if (Hovering != KeyboardSelection)
                TickControl(Hovering, mousePosition, mouseDownPosition);
            if ((MouseCaptured != KeyboardSelection) && (MouseCaptured != Hovering))
                TickControl(MouseCaptured, mousePosition, mouseDownPosition);

            UpdateTooltip((CurrentMouseButtons != MouseButtons.None));

            if (CurrentInputState.ScrollDistance.Length() >= 0.5f) {
                var implicitScrollTarget = CurrentImplicitScrollTarget;
                if ((implicitScrollTarget == null) || Control.IsRecursivelyTransparent(implicitScrollTarget, true))
                    implicitScrollTarget = KeyboardSelection ?? Hovering ?? MouseOverLoose ?? Focused;

                if (implicitScrollTarget != null) {
                    if (AttemptTargetedScroll(implicitScrollTarget, CurrentInputState.ScrollDistance, recursive: false))
                        CurrentImplicitScrollTarget = implicitScrollTarget;
                }
            } else {
                CurrentImplicitScrollTarget = null;
            }

            EnsureValidFocus();

            if (FixatedControl != previouslyFixated)
                HandleFixationChange(previouslyFixated, FixatedControl);
        }

        private void TickControl (Control control, Vector2 globalPosition, Vector2? mouseDownPosition) {
            if (control == null)
                return;
            control.Tick(MakeMouseEventArgs(control, globalPosition, mouseDownPosition));
        }

        private bool IsTooltipActive {
            get {
                return (CachedTooltip != null) && !CachedTooltip.IsTransparent;
            }
        }

        private void ResetTooltipShowTimer () {
            FirstTooltipHoverTime = null;
        }

        private bool IsTooltipPriority (Control control) {
            var ictt = control as ICustomTooltipTarget;
            if (ictt == null)
                return false;

            return (ictt.ShowTooltipWhileFocus || ictt.ShowTooltipWhileKeyboardFocus) && (control == Focused);
        }

        private Control PickTooltipTarget (bool leftButtonPressed) {
            var fixated = FixatedControl;
            if ((_PreferredTooltipSource != Focused) && (_PreferredTooltipSource != null) && _PreferredTooltipSource.AcceptsFocus)
                return fixated;

            if (!IsTooltipPriority(fixated) && IsTooltipPriority(_PreferredTooltipSource))
                return _PreferredTooltipSource;
            else
                return fixated ?? _PreferredTooltipSource;
        }

        private bool IsTooltipAllowedToAppear (Control target, bool leftButtonPressed) {
            var cttt = target as ICustomTooltipTarget;
            if (cttt == null)
                return !leftButtonPressed;

            var result = (leftButtonPressed
                ? cttt.ShowTooltipWhileMouseIsHeld
                : cttt.ShowTooltipWhileMouseIsNotHeld);
            if (target == KeyboardSelection)
                result |= cttt.ShowTooltipWhileKeyboardFocus;
            if (target == Focused)
                result |= cttt.ShowTooltipWhileFocus;
            return result;
        }

        public void HideTooltip (Control control) {
            if (PreviousTooltipAnchor != control)
                return;
            HideTooltip(true);
        }

        public void HideTooltip () {
            HideTooltip(true);
        }

        private void UpdateTooltip (bool leftButtonPressed) {
            var target = PickTooltipTarget(leftButtonPressed);
            if (!IsTooltipAllowedToAppear(target, leftButtonPressed))
                return;

            var cttt = target as ICustomTooltipTarget;

            var now = Now;
            var tooltipContent = default(AbstractTooltipContent);
            if (target != null) {
                if (cttt != null)
                    tooltipContent = cttt.GetContent();
                else
                    tooltipContent = target.TooltipContent;
            }
            var tooltipText = tooltipContent.Get(target);

            var disappearDelay = (cttt?.TooltipDisappearDelay ?? TooltipDisappearDelay);

            if (!tooltipText.IsNull) {
                if (!FirstTooltipHoverTime.HasValue)
                    FirstTooltipHoverTime = now;

                if (IsTooltipActive)
                    LastTooltipHoverTime = now;

                var hoveringFor = now - FirstTooltipHoverTime;
                var disappearTimeout = now - LastTooltipHoverTime;
                var version = target.TooltipContentVersion + target.TooltipContent.Version;

                if (
                    (hoveringFor >= (cttt?.TooltipAppearanceDelay ?? TooltipAppearanceDelay)) || 
                    (disappearTimeout < disappearDelay)
                ) {
                    ShowTooltip(cttt?.Anchor ?? target, tooltipText, tooltipContent, CurrentTooltipContentVersion != version);
                    CurrentTooltipContentVersion = version;
                }
            } else {
                var shouldDismissInstantly = (target != null) && IsTooltipActive && 
                    GetTooltipInstance().GetRect(context: this).Contains(LastMousePosition);
                // TODO: Instead of instantly hiding, maybe just fade the tooltip out partially?
                HideTooltip(shouldDismissInstantly);

                var elapsed = now - LastTooltipHoverTime;
                if (elapsed >= disappearDelay)
                    ResetTooltipShowTimer();
            }
        }

        /// <summary>
        /// Updates key/click repeat state for the current timestamp and returns true if a click should be generated
        /// </summary>
        public bool UpdateRepeat (double now, double firstTime, ref double mostRecentTime, double speedMultiplier = 1, double accelerationMultiplier = 1) {
            // HACK: Handle cases where mostRecentTime has not been initialized by the initial press
            if (mostRecentTime < firstTime)
                mostRecentTime = firstTime;

            double repeatSpeed = Arithmetic.Lerp(KeyRepeatIntervalSlow, KeyRepeatIntervalFast, (float)((now - firstTime) / KeyRepeatAccelerationDelay * accelerationMultiplier)) / speedMultiplier;
            if (
                ((now - firstTime) >= FirstKeyRepeatDelay) &&
                ((now - mostRecentTime) >= repeatSpeed)
            ) {
                mostRecentTime = now;
                return true;
            }

            return false;
        }

        private void HideTooltipForMouseInput (bool isMouseDown) {
            var cttt = PickTooltipTarget(isMouseDown) as ICustomTooltipTarget;
            if (cttt != null) {
                if (!cttt.HideTooltipOnMousePress)
                    return;
            }

            ResetTooltipShowTimer();
            HideTooltip(true);
            FirstTooltipHoverTime = null;
            LastTooltipHoverTime = 0;
        }

        private void HideTooltip (bool instant) {
            if (CachedTooltip == null)
                return;

            if (instant)
                CachedTooltip.Appearance.Opacity = 0;
            else if (IsTooltipVisible)
                CachedTooltip.Appearance.Opacity = Tween.StartNow(
                    CachedTooltip.Appearance.Opacity.Get(Now), 0, now: NowL, 
                    seconds: TooltipFadeDuration * (Animations?.AnimationDurationMultiplier ?? 1)
                );
            IsTooltipVisible = false;
        }

        /// <summary>
        /// Use at your own risk! Performs immediate layout of a control and its children.
        /// The results of this are not necessarily accurate, but can be used to infer its ideal size for positioning.
        /// </summary>
        public void UpdateSubtreeLayout (Control subtreeRoot) {
            var tempCtx = MakeOperationContext();

            var pll = Interlocked.Exchange(ref _PostLayoutListeners, null);
            if (pll == null)
                pll = new UnorderedList<IPostLayoutListener>();
            else
                pll.Clear();
            tempCtx.PostLayoutListeners = pll;

            var wasUpdatingSubtreeLayout = IsUpdatingSubtreeLayout;
            try {
                IsUpdatingSubtreeLayout = true;
                UpdateSubtreeLayout(ref tempCtx, subtreeRoot);

                if (NotifyLayoutListeners(ref tempCtx)) {
                    DoUpdateLayoutInternal(ref tempCtx, true);
                    UpdateSubtreeLayout(ref tempCtx, subtreeRoot);
                    NotifyLayoutListeners(ref tempCtx);
                }
            } finally {
                IsUpdatingSubtreeLayout = wasUpdatingSubtreeLayout;
                Interlocked.CompareExchange(ref _PostLayoutListeners, pll, null);
            }
        }

        private void UpdateSubtreeLayout (ref UIOperationContext context, Control subtreeRoot) {
            ControlKey parentKey;
            Control parent;
            if (!subtreeRoot.TryGetParent(out parent))
                parentKey = Layout.Root;
            else if (!parent.LayoutKey.IsInvalid)
                parentKey = parent.LayoutKey;
            else {
                // Just in case for some reason the control's parent also hasn't had layout happen...
                UpdateSubtreeLayout(ref context, parent);
                return;
            }

            subtreeRoot.GenerateLayoutTree(
                ref context, parentKey, 
                subtreeRoot.LayoutKey.IsInvalid 
                    ? (ControlKey?)null 
                    : subtreeRoot.LayoutKey
            );
            Layout.UpdateSubtree(subtreeRoot.LayoutKey);
        }

        private void ShowTooltip (Control anchor, AbstractString text, AbstractTooltipContent content, bool textIsInvalidated) {
            var instance = GetTooltipInstance();

            var textChanged = !instance.Text.TextEquals(text, StringComparison.Ordinal) || 
                textIsInvalidated;

            // HACK: Copy the target's decoration provider so the tooltip matches
            instance.Appearance.DecorationProvider = (anchor.Appearance.DecorationProvider ?? Decorations);
            instance.Move(anchor, content.Settings.AnchorPoint, content.Settings.ControlAlignmentPoint);

            instance.Visible = true;
            instance.DisplayOrder = int.MaxValue;

            if (textChanged || !IsTooltipVisible) {
                var idealMaxSize = CanvasSize * MaxTooltipSize;

                instance.Text = text;
                instance.ApplySettings(content.Settings);
                // FIXME: Shift it around if it's already too close to the right side
                instance.Width.Maximum = idealMaxSize.X;
                instance.Height.Maximum = idealMaxSize.Y;
                instance.Invalidate();

                UpdateSubtreeLayout(instance);
            }

            var currentOpacity = instance.Appearance.Opacity.Get(Now);
            if (!IsTooltipVisible)
                instance.Appearance.Opacity = Tween.StartNow(
                    currentOpacity, 1f, 
                    seconds: (currentOpacity > 0.1 ? TooltipFadeDurationFast : TooltipFadeDuration) * (Animations?.AnimationDurationMultiplier ?? 1), 
                    now: NowL
                );
            if ((anchor != PreviousTooltipAnchor) && (currentOpacity > 0))
                instance.Appearance.Opacity = 1f;

            PreviousTooltipAnchor = anchor;
            IsTooltipVisible = true;
            UpdateSubtreeLayout(instance);
        }

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position, bool acceptsMouseInputOnly = false, bool acceptsFocusOnly = false, bool rejectIntangible = false) {
            var areHitTestsBlocked = false;
            foreach (var m in ModalStack)
                if (m.BlockHitTests)
                    areHitTestsBlocked = true;

            var sorted = Controls.InDisplayOrder(FrameIndex);
            for (var i = sorted.Count - 1; i >= 0; i--) {
                var control = sorted[i];
                if (
                    areHitTestsBlocked && 
                    !ModalStack.Contains(control as IModal) &&
                    // HACK to allow floating controls over the modal stack
                    (control.DisplayOrder <= (ActiveModal as Control)?.DisplayOrder)
                )
                    continue;
                var result = control.HitTest(position, acceptsMouseInputOnly, acceptsFocusOnly, rejectIntangible);
                if (result != null)
                    return result;
            }

            return null;
        }

        internal UIOperationContext MakeOperationContext () {
            return new UIOperationContext {
                UIContext = this,
                Opacity = 1,
                Now = Now,
                NowL = NowL,
                Modifiers = CurrentModifiers,
                ActivateKeyHeld = _LastInput.ActivateKeyHeld,
                MouseButtonHeld = (LastMouseButtons != MouseButtons.None),
                MousePosition = LastMousePosition,
                VisibleRegion = new RectF(-VisibilityPadding, -VisibilityPadding, CanvasSize.X + (VisibilityPadding * 2), CanvasSize.Y + (VisibilityPadding * 2))
            };
        }

        public void Dispose () {
            Layout.Dispose();

            foreach (var rt in ScratchRenderTargets)
                rt.Dispose();

            ScratchRenderTargets.Clear();
        }
    }

    public struct UIOperationContext {
        public static UIOperationContext Default = default(UIOperationContext);

        public UIContext UIContext;
        public DefaultMaterialSet Materials => UIContext?.Materials;
        public LayoutContext Layout => UIContext?.Layout;
        public RasterizePasses Pass;
        public float Opacity { get; internal set; }
        public float Now { get; internal set; }
        public long NowL { get; internal set; }
        public KeyboardModifiers Modifiers { get; internal set; }
        public bool ActivateKeyHeld { get; internal set; }
        public bool MouseButtonHeld { get; internal set; }
        public Vector2 MousePosition { get; internal set; }
        public RectF VisibleRegion { get; internal set; }
        public BatchGroup Prepass;
        private DenseList<IDecorator> DecoratorStack, TextDecoratorStack;
        private DenseList<IDecorationProvider> DecorationProviderStack;
        internal DenseList<UIContext.ScratchRenderTarget> RenderTargetStack;
        internal UnorderedList<IPostLayoutListener> PostLayoutListeners;
        internal bool RelayoutRequestedForVisibilityChange;
        internal int HiddenCount;
        internal int Depth;

        private T GetStackTop<T> (ref DenseList<T> stack) {
            stack.TryGetItem(stack.Count - 1, out T result);
            return result;
        }

        private static void StackPush<T> (ref DenseList<T> stack, T value) {
            stack.Add(value);
        }

        private static void StackPop<T> (ref DenseList<T> stack) {
            if (stack.Count <= 0)
                throw new InvalidOperationException("Stack empty");
            stack.RemoveAt(stack.Count - 1);
        }

        public IDecorationProvider DecorationProvider => GetStackTop(ref DecorationProviderStack) ?? UIContext?.Decorations;
        public static void PushDecorationProvider (ref UIOperationContext context, IDecorationProvider value) => 
            StackPush(ref context.DecorationProviderStack, value);
        public static void PopDecorationProvider (ref UIOperationContext context) => 
            StackPop(ref context.DecorationProviderStack);
        public IDecorator DefaultDecorator => GetStackTop(ref DecoratorStack);
        public static void PushDecorator (ref UIOperationContext context, IDecorator value) => 
            StackPush(ref context.DecoratorStack, value);
        public static void PopDecorator (ref UIOperationContext context) => 
            StackPop(ref context.DecoratorStack);
        public IDecorator DefaultTextDecorator => GetStackTop(ref TextDecoratorStack);
        public static void PushTextDecorator (ref UIOperationContext context, IDecorator value) => 
            StackPush(ref context.TextDecoratorStack, value);
        public static void PopTextDecorator (ref UIOperationContext context) => 
            StackPop(ref context.TextDecoratorStack);

        public void Log (string text) {
            UIContext.Log(text);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public void DebugLog (string text) {
            UIContext.DebugLog(text);
        }

        public void Clone (out UIOperationContext result) {
            result = new UIOperationContext {
                UIContext = UIContext,
                Pass = Pass,
                Now = Now,
                NowL = NowL,
                Modifiers = Modifiers,
                ActivateKeyHeld = ActivateKeyHeld,
                MouseButtonHeld = MouseButtonHeld,
                MousePosition = MousePosition,
                VisibleRegion = VisibleRegion,
                RelayoutRequestedForVisibilityChange = RelayoutRequestedForVisibilityChange,
                Depth = Depth + 1,
                HiddenCount = HiddenCount,
                Opacity = Opacity,
                Prepass = Prepass,
            };
            RenderTargetStack.Clone(out result.RenderTargetStack);
            DecoratorStack.Clone(out result.DecoratorStack);
            TextDecoratorStack.Clone(out result.TextDecoratorStack);
            DecorationProviderStack.Clone(out result.DecorationProviderStack);
        }
    }
}

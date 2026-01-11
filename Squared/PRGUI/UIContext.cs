using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public sealed partial class UIContext : IDisposable {
        public readonly Thread OwnerThread;

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
        public readonly NewEngine.LayoutEngine Engine;

        /// <summary>
        /// The top-level controls managed by the layout engine. Each one gets a separate rendering layer
        /// </summary>
        public ControlCollection Controls { get; private set; }

        [Conditional("DEBUG")]
        internal void CheckCurrentThread () {
            if (OwnerThread != Thread.CurrentThread)
                throw new InvalidOperationException("Erroneous multi-threaded use of UIContext");
        }

        internal void ClearKeyboardSelection () {
            if (_PreferredTooltipSource == _KeyboardSelection)
                _PreferredTooltipSource = null;
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
            var previousControl = _KeyboardSelection;
            _KeyboardSelection = control;
            MousePositionWhenKeyboardSelectionWasLastUpdated = LastMousePosition;
            FireEvent(UIEvents.KeyboardSelectionChanged, control, previousControl);
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
            if (UseNewEngine)
                Engine = new NewEngine.LayoutEngine();
            OwnerThread = Thread.CurrentThread;
            EventBus = new EventBus();
            EventBus.AfterBroadcast += EventBus_AfterBroadcast;
            Controls = new ControlCollection(this);
            Decorations = decorations;
            Animations = animations ?? (decorations as IAnimationProvider);
            TimeProvider = TimeProvider ?? new DotNetTimeProvider();
            Materials = materials;
            try {
                TTS = new Accessibility.TTS(this);
            } catch {
            }
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

            lock (UnhandledEvents)
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
                // Pre-reserve a big buffer for our tooltip so it can hold lots of characters,
                //  without repeated growth/re-allocations during execution
                CachedTooltip.EnsureLayoutBufferCapacity(1280);
                Controls.Add(CachedTooltip);
            }

            return CachedTooltip;
        }

        public void TryMoveCursor (Vector2 newPosition) {
            foreach (var provider in this.InputSources)
                if (IsPriorityInputSource(provider))
                    provider.TryMoveCursor(newPosition);
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
                        Decorator = Decorations.CompositionPreview,
                        TextDecorator = Decorations.CompositionPreview,
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

        public void ReleaseCapture (Control target, Control focusDonor, bool isUserInitiated = true) {
            if (Focused == target)
                // Technically capture may be getting released by the user, but focus returning to a donor is not
                //  user-initiated - it happens automatically, may not be what they clicked, and should suppress
                //  animations
                TrySetFocus(focusDonor, isUserInitiated: isUserInitiated, suppressAnimations: true);
            if (Hovering == target)
                Hovering = null;
            if (MouseCaptured == target)
                MouseCaptured = null;
            ReleasedCapture = target;
            if (RetainCaptureRequested == target)
                RetainCaptureRequested = null;
        }

        private void DoUpdateLayoutInternal (ref UIOperationContext context, bool secondTime) {
            Engine.CanvasSize = CanvasSize;
            Engine.Root().Config.ChildDirection = NewEngine.Enums.ChildDirection.Row;
            Engine.Root().Tag = LayoutTags.Root;

            if (UseNewEngine) {
                ref var root = ref Engine.Root();
                Engine.CanvasSize = CanvasSize;
                // FIXME
                // root._ContainerFlags = ControlFlags.Container_Row;
                root.Tag = LayoutTags.Root;
            }

            _TopLevelControls.Clear();
            Controls.CopyTo(_TopLevelControls);

            foreach (var control in _TopLevelControls) {
                try {
                    control.GenerateLayoutTree(
                        ref context, Engine.Root().Key, 
                        (secondTime && !control.LayoutKey.IsInvalid) 
                            ? control.LayoutKey 
                            : (ControlKey?)null
                    );
                } catch (Exception exc) {
                    if ((OnUnhandledException == null) || !OnUnhandledException(control, exc))
                        throw;
                }
            }
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float? NegativeToNull (float value) => (value <= -1) ? (float?)null : value;

        public void Update () {
            FrameIndex++;

            MakeOperationContext(ref _UpdateFree, ref _UpdateInUse, out var context);
            var pll = Interlocked.Exchange(ref _PostLayoutListeners, null);
            if (pll == null)
                pll = new UnorderedList<IPostLayoutListener>();
            else
                pll.Clear();
            context.PostLayoutListeners = pll;

            IsUpdating = true;
            try {
                Engine.PrepareForUpdate(true);
                DoUpdateLayoutInternal(ref context, false);
                Engine.UnsafeUpdate();
                // TODO: Perform a pass after this that copies the LayoutResult into all of our controls,
                //  so that they will always have valid data from the most recent update even if we are
                //  in the middle of a new update
                // The easiest solution would be to always have the Controls[] array in Engine and copy
                //  everything into it at the end of Update. This is probably worthwhile since it is cache
                //  efficient and most controls will have their rect used at least once for rasterization
                //  or hit testing

                if (NotifyLayoutListeners(ref context)) {
                    IsPerformingRelayout = true;
                    Engine.PrepareForUpdate(false);
                    DoUpdateLayoutInternal(ref context, true);
                    Engine.UnsafeUpdate();
                    NotifyLayoutListeners(ref context);
                }

                UpdateAutoscroll();
            } finally {
                IsUpdating = false;
                IsPerformingRelayout = false;
                Interlocked.CompareExchange(ref _PostLayoutListeners, pll, null);
                context.Shared.InUse = false;
            }
        }

        private void UpdateCaptureAndHovering (Vector2 mousePosition, Control exclude = null) {
            // FIXME: This breaks drag-to-scroll
            // MouseOver = HitTest(mousePosition, rejectIntangible: true);
            MouseOver = MouseOverLoose = HitTest(mousePosition);

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
            // Clearing the keyboard selection is necessary for arrow navigation to work right once a modal is opened
            ClearKeyboardSelection();
            NotifyModalShown(modal);
        }

        public bool CloseActiveModal (ModalCloseReason reason) {
            if (ModalStack.Count <= 0)
                return false;
            var modal = ActiveModal;
            return CloseModal(modal, reason);
        }

        public bool CloseModal (IModal modal, ModalCloseReason reason) {
            if (!ModalStack.Contains(modal))
                return false;
            return modal.Close(reason);
        }

        public void NotifyModalShown (IModal modal) {
            var ctl = (Control)modal;
            if (!Controls.Contains(ctl))
                throw new InvalidOperationException("Modal not in top level controls list");
            TopLevelFocusMemory.Remove(ctl);
            // FIXME: Reopening a menu really quick can cause this, it's probably harmless?
            if (false && ModalStack.Contains(modal))
                throw new InvalidOperationException("Modal already visible");
            else
                ModalStack.Add(modal);
            // HACK: Record which modal we've pushed so that a modal can retain focus even while it's fading in
            MostRecentlyPushedModal = modal;
            modal.OnShown();
            SetOrQueueFocus(ctl, false, false);
            FireEvent(UIEvents.Shown, ctl);
        }

        public void NotifyModalClosed (IModal modal) {
            if (modal == null)
                return;

            var ctl = (Control)modal;
            var newFocusTarget = (TopLevelFocused == ctl)
                ? modal.FocusDonor
                : null;
            // FIXME: Track user initated flag?
            ReleaseCapture(ctl, modal.FocusDonor, false);
            if (!ModalStack.Contains(modal))
                return;
            ModalStack.Remove(modal);

            // The focus donor might be an invalid target, but try it first
            if (newFocusTarget != null) {
                if (TrySetFocus(newFocusTarget, false, false))
                    return;
            }

            // We failed to set focus to the focus donor so try the active modal
            newFocusTarget = (ActiveModal as Control);
            if (newFocusTarget != null) {
                TrySetFocus(newFocusTarget, false, false);
            }
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
            FocusedAtStartOfUpdate = Focused;

            // HACK: Suppress persistent leaks of top level controls
            Controls.EraseOrderedItems();

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

            foreach (var mea in PurgatoryMouseEventArgs) {
                mea.Clear();
                if (SpareMouseEventArgs.Count >= 32)
                    break;
                SpareMouseEventArgs.Add(mea);
            }
            PurgatoryMouseEventArgs.Clear();

            foreach (var mea in UsedMouseEventArgs)
                PurgatoryMouseEventArgs.Add(mea);
            UsedMouseEventArgs.Clear();

            PreviousUnhandledEvents.Clear();
            // HACK: This really shouldn't be necessary...
            lock (UnhandledEvents)
                foreach (var evt in UnhandledEvents)
                    PreviousUnhandledEvents.Add(evt);
            UnhandledEvents.Clear();

            // We need to make sure we only exit after clearing lists like UnhandledEvents, otherwise we can leak memory.
            if (!processEvents) {
                // This purges the InvalidFocusTargets dictionary, which prevents an unbounded memory leak while inactive
                EnsureValidFocus();
                return;
            }

            var mousePosition = _CurrentInput.CursorPosition;
            var queuedFocus = QueuedFocus;
            Control queuedFocusResult = null;
            var activeModal = ActiveModal;

            if (queuedFocus.value != null) {
                if (
                    (activeModal == null) || 
                    // Attempting to set focus to something outside of a modal can cause it to close
                    Control.IsEqualOrAncestor(queuedFocus.value, (Control)activeModal)
                ) {
                    var oldFocus = Focused;
                    var queuedOk = TrySetFocus(queuedFocus.value, queuedFocus.force, queuedFocus.isUserInitiated, queuedFocus.suppressAnimations, queuedFocus.overrideKeyboardSelection);
                    queuedFocusResult = Focused;
                    // TrySetFocus may have failed but changed the current focus. Treat that as equivalent and clear the queued focus.
                    if (queuedOk || (queuedFocusResult != oldFocus))
                        QueuedFocus = default;
                }
            }

            bool purgeTopLevelFocus = false;
            if ((TopLevelFocused != null) && (Controls.IndexOf(TopLevelFocused) < 0))
                purgeTopLevelFocus = true;

            if (
                (Focused != null) && 
                (!Focused.IsValidFocusTarget || (FindTopLevelAncestor(Focused) == null) || purgeTopLevelFocus)
            ) {
                // If the current focused control is no longer enabled or present, attempt to
                //  focus something else in the current top level control, if possible
                if (!purgeTopLevelFocus) {
                    // HACK: Unfortunately, there's probably nothing useful to do here
                    // I suppose a focusable child could appear out of nowhere? But I don't think we'd want to
                    //  suddenly change focus if that happened. If we don't do this we waste a ton of CPU
                    //  doing pointless treewalks and allocating garbage for nothing.
                    if ((TopLevelFocused != Focused) && TopLevelFocused?.IsValidFocusTarget == true)
                        Focused = TopLevelFocused;
                    else 
                        ;
                } else
                    NotifyControlBecomingInvalidFocusTarget(Focused, false);
            }

            EnsureValidFocus();

            // Detect that while we successfully applied queued focus, it was reset somehow, and queue it again
            if (
                (queuedFocus.value != null) && (QueuedFocus.value == null) &&
                ((Focused != queuedFocus.value) && (Focused != queuedFocusResult))
            ) {
                // FIXME: This shouldn't really happen
                QueuedFocus = queuedFocus;
            }

            // We want to do this check once per frame since during a given frame, the focus
            //  may move multiple times and we don't want to pointlessly start the animation
            //  if focus ends up going back to where it originally was
            if (_PreviouslyFocusedForTimestampUpdate != _Focused) {
                if ((_Focused == _MouseCaptured) || SuppressFocusChangeAnimationsThisStep)
                    LastFocusChange = 0;
                else
                    LastFocusChange = NowL;
            }
            SuppressFocusChangeAnimationsThisStep = false;
            _PreviouslyFocusedForTimestampUpdate = _Focused;

            var previouslyHovering = Hovering;

            UpdateCaptureAndHovering(_CurrentInput.CursorPosition);
            var mouseEventTarget = MouseCaptured ?? Hovering;
            var topLevelTarget = (mouseEventTarget != null)
                ? FindTopLevelAncestor(mouseEventTarget)
                : null;

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

            if (processClick) {
                if (wasInputBlocked) {
                    HandleFailedClick(previouslyCaptured ?? PreviousMouseDownTarget, mousePosition, mouseDownPosition ?? mousePosition, $"Input blocked by modal {ActiveModal}");
                } else {
                    // FIXME: if a menu is opened by a mousedown event, this will
                    //  fire a click on the menu in response to its mouseup
                    if (
                        ((Hovering == previouslyCaptured) && (previouslyCaptured != null)) ||
                        ((previouslyCaptured == null) && (Hovering == PreviousMouseDownTarget))
                    ) {
                        // FIXME: Is this ?? right
                        var clickTarget = previouslyCaptured ?? PreviousMouseDownTarget;
                        if (
                            (clickTarget?.AcceptsNonLeftClicks == true) || 
                            ((LastMouseButtons & MouseButtons.Left) == MouseButtons.Left)
                        )
                            HandleClick(clickTarget, mousePosition, mouseDownPosition ?? mousePosition);
                        else
                            HandleFailedClick(clickTarget, mousePosition, mouseDownPosition ?? mousePosition, "Non-left-click not accepted by target");
                    } else
                        HandleDrag(previouslyCaptured, Hovering);
                }
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

            if (FixatedControl != PreviouslyFixated)
                HandleFixationChange(PreviouslyFixated, FixatedControl);
            PreviouslyFixated = FixatedControl;
            PreviousGlobalMousePosition = mousePosition;
        }

        private void TickControl (Control control, Vector2 globalPosition, Vector2? mouseDownPosition) {
            if (control == null)
                return;
            control.Tick(MakeMouseEventArgs(control, globalPosition, mouseDownPosition));
        }

        private bool IsTooltipActive {
            get {
                return CachedTooltip?.Visible == true;
            }
        }

        private void ResetTooltipShowTimer () {
            FirstTooltipHoverTime = null;
        }

        private bool IsTooltipPriority (Control control) {
            var ictt = control as ICustomTooltipTarget;
            if (ictt == null)
                return false;
            var tts = ictt.TooltipSettings;
            if (tts == null)
                return false;

            return (tts.ShowWhileFocused || tts.ShowWhileKeyboardFocused) && (control == Focused);
        }

        private Control PickTooltipTarget (bool leftButtonPressed) {
            // Fixes case where a menu is closed while it's hosting a tooltip
            if (_PreferredTooltipSource?.IsValidFocusTarget == false)
                _PreferredTooltipSource = null;

            var fixated = FixatedControl;
            if (
                ((Focused as ICustomTooltipTarget)?.TooltipSettings?.HostsChildTooltips == true) &&
                ((fixated == null) || Control.IsEqualOrAncestor(fixated, Focused))
            ) {
                if (IsTooltipPriority(_PreferredTooltipSource))
                    return _PreferredTooltipSource;
                else
                    return Focused;
            } else {
                if ((_PreferredTooltipSource != Focused) && (_PreferredTooltipSource != null) && _PreferredTooltipSource.AcceptsFocus)
                    return fixated;

                if (!IsTooltipPriority(fixated) && IsTooltipPriority(_PreferredTooltipSource))
                    return _PreferredTooltipSource;
                else
                    return fixated ?? _PreferredTooltipSource;
            }
        }

        private bool IsTooltipAllowedToAppear (Control target, bool leftButtonPressed) {
            var tts = (target as ICustomTooltipTarget)?.TooltipSettings;
            if (tts == null)
                return !leftButtonPressed;

            var result = leftButtonPressed
                ? tts.ShowWhileMouseIsHeld
                : tts.ShowWhileMouseIsNotHeld;
            if (target == KeyboardSelection)
                result |= tts.ShowWhileKeyboardFocused;
            if (target == Focused)
                result |= tts.ShowWhileFocused;
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

        public AbstractString GetTooltipTextForControl (Control target, out AbstractTooltipContent content) {
            content = default;
            var cttt = target as ICustomTooltipTarget;
            var tts = cttt?.TooltipSettings;

            if (target != null) {
                if (cttt != null)
                    content = cttt.GetContent();
                else
                    content = target.TooltipContent;
            }
            // FIXME: UserData
            return content.Get(target, out _);
        }

        private AbstractString GetTooltipTextForControl (Control target, bool leftButtonPressed, out AbstractTooltipContent content) {
            content = default;
            if (!IsTooltipAllowedToAppear(target, leftButtonPressed))
                return default;

            return GetTooltipTextForControl(target, out content);
        }

        private void UpdateTooltip (bool leftButtonPressed) {
            var target = PickTooltipTarget(leftButtonPressed);
            var tooltipText = GetTooltipTextForControl(target, leftButtonPressed, out AbstractTooltipContent tooltipContent);
            if (tooltipText.IsNull) {
                // HACK: If the focused control explicitly requests to have its tooltip visible while it's focused,
                //  make sure we fall back to showing its tooltip if we didn't pick a better target
                if (
                    // FIXME: This sucks
                    false &&
                    (Focused is ICustomTooltipTarget ictt) && 
                    ictt.TooltipSettings.ShowWhileFocused &&
                    (Hovering?.AcceptsMouseInput != true) &&
                    (MouseOverLoose?.AcceptsMouseInput != true)
                ) {
                    target = Focused;
                    tooltipText = GetTooltipTextForControl(target, leftButtonPressed, out tooltipContent);
                }
            }

            var gto = GlobalTooltipOpacity.Get(NowL);
            if (gto <= 0)
                return;
            var cttt = target as ICustomTooltipTarget;
            var tts = cttt?.TooltipSettings;

            var now = Now;
            var disappearDelay = (tts?.DisappearDelay ?? TooltipDisappearDelay);

            // FIXME: When a menu appears, its tooltip will appear in the wrong spot for a frame or two
            //  until the menu shows up. Right now menus hack around this by disabling their tooltips,
            //  but it would be better to have a robust general solution for that problem

            if (
                !tooltipText.IsNull && 
                // HACK: Setting .Visible = false on the current tooltip target or one of its
                //  parents will normally leave the tooltip open unless we do this
                !Control.IsRecursivelyTransparent(target, true)
            ) {
                if (!FirstTooltipHoverTime.HasValue)
                    FirstTooltipHoverTime = now;

                if (IsTooltipActive)
                    LastTooltipHoverTime = now;

                var hoveringFor = now - FirstTooltipHoverTime;
                var disappearTimeout = now - LastTooltipHoverTime;
                var version = target.TooltipContentVersion + target.TooltipContent.Version;

                if (
                    (hoveringFor >= (tts?.AppearDelay ?? TooltipAppearanceDelay)) || 
                    (disappearTimeout < disappearDelay)
                ) {
                    ShowTooltip(
                        target, cttt, tooltipText, 
                        tooltipContent, CurrentTooltipContentVersion != version
                    );
                    CurrentTooltipContentVersion = version;
                }

                if (gto < 1)
                    GetTooltipInstance().Appearance.Opacity = GlobalTooltipOpacity;
            } else {
                var shouldDismissInstantly = (target != null) && IsTooltipActive && 
                    GetTooltipInstance().GetRect(context: this).Contains(LastMousePosition);

                // TODO: Instead of instantly hiding, maybe just fade the tooltip out partially?
                HideTooltip(shouldDismissInstantly, disappearDelay);

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
            var tts = cttt?.TooltipSettings;
            if (tts != null) {
                if (!tts.HideOnMousePress)
                    return;
            }

            ResetTooltipShowTimer();
            HideTooltip(true);
            FirstTooltipHoverTime = null;
            LastTooltipHoverTime = 0;
        }

        private void HideTooltip (bool instant, float disappearDelay = 0f) {
            if (CachedTooltip == null)
                return;

            if (instant)
                CachedTooltip.Appearance.Opacity = 0;
            else if (IsTooltipVisible)
                CachedTooltip.Appearance.Opacity = Tween.StartNow(
                    CachedTooltip.Appearance.Opacity.Get(Now), 0, now: NowL, delay: disappearDelay,
                    seconds: TooltipFadeDuration * (Animations?.AnimationDurationMultiplier ?? 1)
                );
            IsTooltipVisible = false;
        }

        /// <summary>
        /// Use at your own risk! Performs immediate layout of a control and its children.
        /// The results of this are not necessarily accurate, but can be used to infer its ideal size for positioning.
        /// </summary>
        /// <returns>Whether any of the controls laid out requested a relayout.</returns>
        public bool UpdateSubtreeLayout (Control subtreeRoot) {
            MakeOperationContext(out var tempCtx);
            return UpdateSubtreeLayout(ref tempCtx, subtreeRoot);
        }

        internal bool UpdateSubtreeLayout (ref UIOperationContext context, Control subtreeRoot) {
            var pll = Interlocked.Exchange(ref _PostLayoutListeners, null);
            if (pll == null)
                pll = new UnorderedList<IPostLayoutListener>();
            else
                pll.Clear();
            context.PostLayoutListeners = pll;

            var wasUpdatingSubtreeLayout = IsUpdatingSubtreeLayout;
            bool result = false;
            try {
                IsUpdatingSubtreeLayout = true;
                UpdateSubtreeLayoutPass(ref context, subtreeRoot);

                result = NotifyLayoutListeners(ref context);
            } finally {
                context.PostLayoutListeners = null;
                IsUpdatingSubtreeLayout = wasUpdatingSubtreeLayout;
                Interlocked.CompareExchange(ref _PostLayoutListeners, pll, null);
            }

            return result;
        }

        private void UpdateSubtreeLayoutPass (ref UIOperationContext context, Control subtreeRoot) {
            ControlKey parentKey;
            Control parent;
            if (!subtreeRoot.TryGetParent(out parent))
                parentKey = Engine.Root().Key;
            else if (!parent.LayoutKey.IsInvalid)
                parentKey = parent.LayoutKey;
            else {
                // Just in case for some reason the control's parent also hasn't had layout happen...
                UpdateSubtreeLayout(ref context, parent);
                return;
            }

            var subtreeKey = subtreeRoot.LayoutKey.ID;
            subtreeRoot.GenerateLayoutTree(
                ref context, parentKey,
                // FIXME: Why is this necessary?
                (subtreeKey >= 0) && (subtreeKey < Engine.Count)
                    ? subtreeRoot.LayoutKey
                    : (ControlKey?)null
            );
            // HACK: Workaround layout listeners not always being registered because the control has an existing key
            if (subtreeRoot is IPostLayoutListener ipll)
                context.PostLayoutListeners?.Add(ipll);

            Engine.UpdateSubtree(subtreeRoot.LayoutKey);
        }

        public Vector2 PlaceTooltipContentIntoTooltip (
            Tooltip instance, Control target, ICustomTooltipTarget cttt, AbstractString text, 
            AbstractTooltipContent content
        ) {
            var tts = cttt?.TooltipSettings;
            var anchor = cttt?.Anchor ?? target;
            // HACK: Copy the target's decoration provider so the tooltip matches
            instance.Appearance.DecorationProvider = (
                anchor.Appearance.DecorationProvider ?? 
                target.Appearance.DecorationProvider ?? 
                Decorations
            );

            var idealMaxSize = CanvasSize * (content.Settings.MaxSize ?? tts?.MaxSize ?? MaxTooltipSize);
            instance.Text = text;
            instance.RichTextUserData = content.UserData;
            instance.ApplySettings(content.Settings);
            return idealMaxSize;
        }

        private void ShowTooltip (
            Control target, ICustomTooltipTarget cttt, AbstractString text, 
            AbstractTooltipContent content, bool textIsInvalidated
        ) {
            var instance = GetTooltipInstance();

            var textChanged = !instance.Text.TextEquals(text, StringComparison.Ordinal) || 
                textIsInvalidated;

            var tts = cttt?.TooltipSettings;
            var anchor = cttt?.Anchor ?? target;
            var fireEvent = (anchor != PreviousTooltipAnchor) || !IsTooltipVisible;

            // FIXME: For menus and perhaps list boxes, keyboard navigation sets the tooltip target
            //  to be the selected item instead of the list/menu and this ignores the container's settings
            instance.Move(
                content.Settings.OverrideAnchor ?? anchor, 
                tts?.AnchorPoint ?? content.Settings.AnchorPoint, 
                tts?.ControlAlignmentPoint ?? content.Settings.ControlAlignmentPoint
            );

            instance.Visible = true;
            instance.DisplayOrder = int.MaxValue - 1;

            // HACK: TextLayoutIsIncomplete == true indicates that an image embedded in the tooltip content is
            //  still loading. We need to keep recalculating our size until all the images have loaded, since
            //  the images can change the size of our tooltip content
            if (textChanged || !IsTooltipVisible || instance.TextLayoutIsIncomplete) {
                /*
                if (instance.TextLayoutIsIncomplete)
                    System.Diagnostics.Debug.WriteLine($"TextLayoutIsIncomplete {FrameIndex}");
                */

                var idealMaxSize = PlaceTooltipContentIntoTooltip(instance, target, cttt, text, content);

                // HACK: Set a width constraint on the tooltip based on its ideal max size.
                instance.Width.Maximum = idealMaxSize.X;
                // We don't want to set a height constraint because right now it doesn't inform the text layout
                //  engine, so the text would overflow the top and/or bottom if it doesn't fit.
                // instance.Height.Maximum = idealMaxSize.Y;
                instance.Invalidate();

                // FIXME: Sometimes this keeps happening every frame
                MakeOperationContext(ref _TooltipContext1, ref _TooltipContext2, out var ctx);
                UpdateSubtreeLayout(ref ctx, instance);
                ctx.Shared.InUse = false;

                /*
                if (instance.TextLayoutIsIncomplete)
                    System.Diagnostics.Debug.WriteLine($"TextLayoutStillIncomplete {FrameIndex}");
                */
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

            {
                // FIXME: Does this really need to happen every frame? It's expensive
                MakeOperationContext(ref _TooltipContext1, ref _TooltipContext2, out var ctx);
                UpdateSubtreeLayout(ref ctx, instance);
                ctx.Shared.InUse = false;
            }

            if (fireEvent)
                FireEvent(UIEvents.TooltipShown, anchor);
        }

        public Control HitTest (Vector2 position) {
            return HitTest(position, default, out _);
        }

        public Control HitTest (Vector2 position, in HitTestOptions options) {
            return HitTest(position, in options, out _);
        }

        internal bool ShouldModalBlockHitTests (IModal m) =>
            (m != null) && m.BlockHitTests && !Control.IsRecursivelyTransparent((Control)m, includeOpacityAsOfTime: NowL, ignoreFadeIn: true);

        // Position is relative to the top-left corner of the canvas
        public Control HitTest (Vector2 position, in HitTestOptions options, out Vector2 localPosition) {
            localPosition = default;

            var areHitTestsBlocked = false;
            foreach (var m in ModalStack)
                if (ShouldModalBlockHitTests(m))
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
                var result = control.HitTest(position, in options, out localPosition);
                if (result != null)
                    return result;
            }

            return null;
        }

        private volatile UIOperationContextShared _RasterizeFree, _RasterizeInUse, _UpdateFree, _UpdateInUse,
            _TooltipContext1, _TooltipContext2;

        internal void MakeOperationContext (ref UIOperationContextShared _free, ref UIOperationContextShared _inUse, out UIOperationContext result) {
            var free = Interlocked.Exchange(ref _free, null);
            if (free?.InUse != false)
                free = new UIOperationContextShared();

            var inUse = Interlocked.Exchange(ref _inUse, null);
            if (inUse?.InUse == false)
                Interlocked.CompareExchange(ref _free, inUse, null);

            Interlocked.CompareExchange(ref _inUse, free, null);

            InitializeOperationContextShared(free);
            MakeOperationContextFromInitializedShared(free, out result);
        }

        private void InitializeOperationContextShared (UIOperationContextShared shared) {
            shared.InUse = true;
            shared.Context = this;
            shared.Now = Now;
            shared.NowL = NowL;
            shared.Modifiers = CurrentModifiers;
            shared.ActivateKeyHeld = _LastInput.ActivateKeyHeld;
            shared.MouseButtonHeld = (LastMouseButtons != MouseButtons.None);
            shared.MousePosition = LastMousePosition;
        }

        internal void MakeOperationContext (out UIOperationContext result) {
            var shared = new UIOperationContextShared();
            InitializeOperationContextShared(shared);
            MakeOperationContextFromInitializedShared(shared, out result);
        }

        private void MakeOperationContextFromInitializedShared (UIOperationContextShared shared, out UIOperationContext result) {
            if (!shared.InUse)
                throw new Exception("Not initialized");

            result = new UIOperationContext {
                Shared = shared,
                Opacity = 1,
                VisibleRegion = new RectF(-VisibilityPadding, -VisibilityPadding, CanvasSize.X + (VisibilityPadding * 2), CanvasSize.Y + (VisibilityPadding * 2))
            };
        }

        public void Dispose () {
            // Engine.Dispose();

            foreach (var rt in ScratchRenderTargets)
                rt.Dispose();

            ScratchRenderTargets.Clear();
        }
    }

    internal class UIOperationContextShared {
        public UIContext Context;
        public float Now;
        public long NowL;
        public KeyboardModifiers Modifiers;
        public bool ActivateKeyHeld;
        public bool MouseButtonHeld;
        public Vector2 MousePosition;
        internal volatile bool InUse;
    }

    public struct UIOperationContext {
        public static UIOperationContext Default = default(UIOperationContext);

        internal UIOperationContextShared Shared;

        public UIContext UIContext => Shared?.Context;
        public RenderCoordinator RenderCoordinator => Prepass.Coordinator;
        public DefaultMaterialSet Materials => Shared?.Context?.Materials;
        public NewEngine.LayoutEngine Engine => Shared?.Context?.Engine;

        internal UnorderedList<IPostLayoutListener> PostLayoutListeners;

        public float Now => Shared?.Now ?? 0f;
        public long NowL => Shared?.NowL ?? 0;
        public KeyboardModifiers Modifiers => Shared?.Modifiers ?? default;
        public bool ActivateKeyHeld => Shared?.ActivateKeyHeld ?? false;
        public bool MouseButtonHeld => Shared?.MouseButtonHeld ?? false;
        public ref readonly Vector2 MousePosition => ref Shared.MousePosition;

        public float Opacity { get; internal set; }
        public RectF VisibleRegion { get; internal set; }
        public BatchGroup Prepass;
        public AutoRenderTarget CompositingTarget { get; internal set; }
        private DenseList<IDecorationProvider> DecorationProviderStack;
        internal DenseList<UIContext.ScratchRenderTarget> RenderTargetStack;
        internal short HiddenCount, Depth, TransformsActive;
        internal bool RelayoutRequestedForVisibilityChange;
        public bool InsideSelectedControl;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StackPush<T> (ref DenseList<T> stack, T value) {
            stack.Add(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void StackPop<T> (ref DenseList<T> stack) {
            var index = stack.Count - 1;
            if (index < 0)
                throw new InvalidOperationException("Stack empty");
            stack.RemoveAt(index);
        }

        public IDecorationProvider DecorationProvider => DecorationProviderStack.LastOrDefault() ?? Shared?.Context?.Decorations;
        public static void PushDecorationProvider (ref UIOperationContext context, IDecorationProvider value) => 
            StackPush(ref context.DecorationProviderStack, value);
        public static void PopDecorationProvider (ref UIOperationContext context) => 
            StackPop(ref context.DecorationProviderStack);

        public void Log (string text) {
            UIContext.Log(text);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public void DebugLog (string text) {
            UIContext.DebugLog(text);
        }

        public void Clone (out UIOperationContext result) {
            result = new UIOperationContext {
                Shared = Shared,
                VisibleRegion = VisibleRegion,
                Depth = (short)(Depth + 1),
                HiddenCount = HiddenCount,
                Opacity = Opacity,
                Prepass = Prepass,
                RelayoutRequestedForVisibilityChange = RelayoutRequestedForVisibilityChange,
                CompositingTarget = CompositingTarget,
                TransformsActive = TransformsActive,
                InsideSelectedControl = InsideSelectedControl,
            };
            RenderTargetStack.Clone(ref result.RenderTargetStack, true);
            DecorationProviderStack.Clone(ref result.DecorationProviderStack, true);
        }
    }
}

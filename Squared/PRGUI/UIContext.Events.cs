using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Input;
using Squared.Render;
using Squared.Threading;
using Squared.Util;

namespace Squared.PRGUI {
    public sealed partial class UIContext : IDisposable {
        internal struct UnhandledEvent {
            internal sealed class Comparer : IEqualityComparer<UnhandledEvent> {
                public static readonly Comparer Instance = new Comparer();

                public bool Equals (UnhandledEvent x, UnhandledEvent y) {
                    return x.Equals(y);
                }

                public int GetHashCode (UnhandledEvent obj) {
                    return obj.GetHashCode();
                }
            }

            public Control Source;
            public string Name;

            public override int GetHashCode () {
                return Name?.GetHashCode() ?? 0;
            }

            public bool Equals (UnhandledEvent rhs) {
                return (Source == rhs.Source) &&
                    (Name == rhs.Name);
            }

            public override bool Equals (object obj) {
                if (obj is UnhandledEvent)
                    return Equals((UnhandledEvent)obj);
                else
                    return false;
            }
        }

        public event Action<Control, AbstractTextureReference> OnTextureUsed;
        public event Func<string, Keys?, char?, bool> OnKeyEvent;

        public bool FireEvent<T> (string name, Control target, T args, bool suppressHandler = false, bool targetHandlesFirst = false, bool filtersOnly = false) {
            // FIXME: Is this right?
            if (target == null)
                target = Control.None;
            if (EventBus == null)
                return true;

            if (!targetHandlesFirst && EventBus.Broadcast(target, name, args))
                return true;
            if (targetHandlesFirst && !filtersOnly && target.HandleEvent(name, args))
                return true;

            if (suppressHandler) {
                if (!targetHandlesFirst)
                    target.InvokeEventFilter(name, args);
                return false;
            }

            if (targetHandlesFirst && EventBus.Broadcast(target, name, args))
                return true;
            else if (!filtersOnly && target.HandleEvent(name, args))
                return true;

            Control ctl = target, parent = null;
            while (ctl?.TryGetParent(out parent) == true) {
                var filter = (parent as IControlContainer).ChildEventFilter;
                if ((filter != null) && filter.OnEvent(target, name, args))
                    return true;
                ctl = parent;
            }

            return false;
        }

        public bool FireEvent (string name, Control target, bool suppressHandler = false, bool targetHandlesFirst = false, bool filtersOnly = false) {
            // FIXME: Is this right?
            if (target == null)
                target = Control.None;
            if (EventBus == null)
                return true;

            if (!targetHandlesFirst && EventBus.Broadcast<object>(target, name, null))
                return true;
            if (targetHandlesFirst && !filtersOnly && target.HandleEvent(name))
                return true;

            if (suppressHandler) {
                if (!targetHandlesFirst)
                    target.InvokeEventFilter(name, NoneType.None);
                return false;
            }

            if (targetHandlesFirst && EventBus.Broadcast<object>(target, name, null))
                return true;
            else if (!filtersOnly && target.HandleEvent(name))
                return true;

            Control ctl = target, parent = null;
            while (ctl?.TryGetParent(out parent) == true) {
                var filter = (parent as IControlContainer).ChildEventFilter;
                if ((filter != null) && filter.OnEvent(target, name, NoneType.None))
                    return true;
                ctl = parent;
            }

            return false;
        }

        private int FindChildEvent (List<UIContext.UnhandledEvent> events, Control parent, string eventName, out Control source) {
            for (int i = 0, c = events.Count; i < c; i++) {
                if (events[i].Name == eventName) {
                    // FIXME: Handle parents that are not top level controls
                    if (parent != null) {
                        var topLevel = FindTopLevelAncestor(events[i].Source);
                        if (topLevel != parent)
                            continue;
                    }
                    source = events[i].Source;
                    return i;
                }
            }

            source = null;
            return -1;
        }

        private static int FindEvent (List<UIContext.UnhandledEvent> events, ref UIContext.UnhandledEvent evt) {
            for (int i = 0, c = events.Count; i < c; i++) {
                if (evt.Equals(events[i]))
                    return i;
            }

            return -1;
        }

        public void NotifyTextureUsed (Control source, AbstractTextureReference texture) {
            if (!texture.IsInitialized)
                return;
            if (OnTextureUsed != null)
                OnTextureUsed(source, texture);
        }

        public bool GetUnhandledChildEvent (Control parent, string eventName, out Control source) {
            var index = FindChildEvent(PreviousUnhandledEvents, parent, eventName, out source);
            if (index >= 0) {
                PreviousUnhandledEvents.RemoveAt(index);
                return true;
            }

            index = FindChildEvent(UnhandledEvents, parent, eventName, out source);
            if (index >= 0) {
                UnhandledEvents.RemoveAt(index);
                return true;
            }

            return false;
        }

        public bool GetUnhandledEvent (Control source, string eventName) {
            var key = new UnhandledEvent { Source = source, Name = eventName };
            var index = FindEvent(PreviousUnhandledEvents, ref key);
            if (index >= 0) {
                PreviousUnhandledEvents.RemoveAt(index);
                return true;
            }

            index = FindEvent(UnhandledEvents, ref key);
            if (index >= 0) {
                UnhandledEvents.RemoveAt(index);
                return true;
            }

            return false;
        }

        private void HandleNewFocusTarget (
            Control previousTopLevel, Control newTopLevel,
            Control previous, Control target, bool isUserInitiated
        ) {
            var topLevelParent = FindTopLevelAncestor(target);
            if (topLevelParent != null) {
                if (!TopLevelFocusMemory.TryGetValue(topLevelParent, out var cell)) {
                    cell = new FocusMemoryCell();
                    TopLevelFocusMemory.Add(topLevelParent, cell);
                }
                cell.Value = target;
            }

            if (target?.AcceptsTextInput ?? false) {
                if (previous?.AcceptsTextInput ?? false) {
                } else {
                    foreach (var src in InputSources)
                        src.SetTextInputState(true);
                }
            } else if (previous?.AcceptsTextInput ?? false) {
                foreach (var src in InputSources)
                    src.SetTextInputState(false);
                IsCompositionActive = false;
            }

            if (previous != target) {
                if (target != null) {
                    var chain = target;
                    while (true) {
                        if (!chain.TryGetParent(out Control parent) || (parent == null))
                            break;

                        var icc = parent as IControlContainer;
                        icc?.DescendantReceivedFocus(target, isUserInitiated);
                        chain = parent;
                    }
                }

                TTS.FocusedControlChanged(newTopLevel, target);
            }
        }

        private void HandleHoverTransition (Control previous, Control current) {
            // If the mouse enters a new control, clear the keyboard selection
            if (_CurrentInput.KeyboardNavigationEnded)
                ClearKeyboardSelection();

            if (previous != null)
                FireEvent(UIEvents.MouseLeave, previous, current);

            if (current != null) {
                FireEvent(UIEvents.MouseEnter, current, previous);

                // HACK: Clear temporary tooltip source lock
                if (current != _PreferredTooltipSource)
                    _PreferredTooltipSource = null;
            }

            ResetTooltipShowTimer();
        }

        private bool IsInDoubleClickWindow (Control target, Vector2 position) {
            if (target == null)
                return false;

            var movedDistance = (position - LastClickPosition).Length();
            if (
                (PreviousClickTarget == target) &&
                (movedDistance < MinimumMouseMovementDistance)
            ) {
                var elapsed = Now - LastClickTime;
                return elapsed < DoubleClickWindowSize;
            }
            return false;
        }

        private void HandleClick (Control target, Vector2 mousePosition, Vector2 mouseDownPosition) {
            if (target == null)
                return;

            // HACK: For click events, we want to walk up the parent chain to try and find a control that
            //  can handle the click. This lets you set a click handler on a control's parent and be certain
            //  that any children that reject mouse input won't stop you from finding out that a click happened.
            while (!target.IsValidMouseInputTarget) {
                if (!target.TryGetParent(out target))
                    break;
            }

            if (target == null)
                return;

            if (!target.IsValidMouseInputTarget) {
                TTS.ControlClicked(target, MouseOver);
                return;
            }

            if (IsInDoubleClickWindow(target, mousePosition))
                SequentialClickCount++;
            else
                SequentialClickCount = 1;

            LastClickPosition = mousePosition;
            PreviousClickTarget = target;
            LastClickTime = LastMouseDownTime;
            FireEvent(UIEvents.Click, target, MakeMouseEventArgs(target, mousePosition, mouseDownPosition, true));

            TTS.ControlClicked(target, MouseOver);
        }

        private void HandleDrag (Control originalTarget, Control finalTarget) {
            // FIXME
        }

        private bool HasPressedKeySincePressingAlt = false;
        private Control LastNonRepeatKeyPressTarget = null;

        public bool HandleKeyEvent (string name, Keys? key, char? ch, KeyboardModifiers? modifiers = null, bool isVirtual = false, bool isRepeat = false) {
            // HACK to simplify writing input sources
            if (name == null)
                return false;

            // HACK: Ensure that key repeat doesn't continue if the original repeat target becomes defocused
            var suppressRepeat = isRepeat && (LastNonRepeatKeyPressTarget != Focused);

            var evt = new KeyEventArgs {
                Context = this,
                Modifiers = modifiers ?? CurrentModifiers,
                Key = key,
                Char = ch,
                IsVirtualInput = isVirtual,
                IsRepeat = isRepeat && !suppressRepeat
            };

            if (name == UIEvents.KeyDown) {
                if ((key == Keys.LeftAlt) || (key == Keys.RightAlt)) {
                    HasPressedKeySincePressingAlt = false;
                } else if (key == Keys.Escape) {
                    AcceleratorOverlayVisible = false;
                } else if (key == Keys.Tab) {
                } else if (key.HasValue && !ModifierKeys.Contains(key.Value)) {
                    if (evt.Modifiers.Alt)
                        HasPressedKeySincePressingAlt = true;
                    AcceleratorOverlayVisible = false;
                }
            } else if (name == UIEvents.KeyUp) {
                if ((key == Keys.LeftAlt) || (key == Keys.RightAlt)) {
                    if (!HasPressedKeySincePressingAlt)
                        AcceleratorOverlayVisible = !AcceleratorOverlayVisible;
                }
            }

            if ((name == UIEvents.KeyPress) && !isRepeat)
                LastNonRepeatKeyPressTarget = Focused;

            // FIXME: Suppress events with a char if the target doesn't accept text input?
            if (FireEvent(name, Focused, evt, filtersOnly: suppressRepeat))
                return true;

            // HACK: For modifier key inputs, dispatch them to the top level control as well since it may be a window.
            // This allows a window to handle things like Ctrl-F4
            if (
                evt.Modifiers.Control || 
                evt.Modifiers.Alt || 
                SuppressRepeatKeys.Contains(evt.Key.GetValueOrDefault()) ||
                ForwardToTopLevelKeys.Contains(evt.Key.GetValueOrDefault())
            ) {
                if (FireEvent(name, TopLevelFocused, evt, filtersOnly: suppressRepeat))
                    return true;
            }

            bool needsToClearFocus = false;

            if (name == UIEvents.KeyPress) {
                switch (key) {
                    case Keys.Escape:
                        needsToClearFocus = true;
                        break;
                    case Keys.Tab:
                        int tabDelta = evt.Modifiers.Shift ? -1 : 1;
                        return RotateFocus(topLevel: evt.Modifiers.Control, delta: tabDelta, isUserInitiated: true);
                    case Keys.Space:
                        if (Focused?.IsValidMouseInputTarget == true)
                            return FireSyntheticClick(Focused);
                        break;
                }
            }

            if (suppressRepeat)
                return false;

            var activeModal = ActiveModal;

            if ((activeModal != null) && activeModal.OnUnhandledKeyEvent(name, evt))
                return true;

            // FIXME: Allow OnKeyEvent to block this?
            if (needsToClearFocus)
                Focused = null;

            if (OnKeyEvent != null)
                return OnKeyEvent(name, key, ch);

            return false;
        }

        public bool FireSyntheticClick (Control target) {
            var targetRect = target.GetRect(contentRect: true, context: this);
            // HACK: If the mouse is currently over the control, put the synthesized click under the mouse
            // Otherwise, center it on the control. Otherwise, the synthetic click will be 'placed' somewhere random
            var position = targetRect.Contains(LastMousePosition)
                ? LastMousePosition
                : targetRect.Center;
            var args = MakeMouseEventArgs(target, position, null, true);
            args.PreviousButtons = MouseButtons.Left;
            args.Buttons = MouseButtons.None;
            args.IsSynthetic = true;
            // FIXME: implement double-click for double-space-press
            args.SequentialClickCount = 1;
            return FireEvent(UIEvents.Click, target, args);
        }

        public bool PerformAutoscroll (Control target, float? speed = null) {
            var scrollContext = ChooseAutoscrollContext(target, out RectF parentRect, out RectF controlRect, out RectF intersectedRect);
            if (scrollContext != null) {
                // For huge controls, as long as its top-left corner and most of its body
                //  is visible we don't need to scroll
                if (
                    (
                        (
                            (controlRect.Width > parentRect.Width) &&
                            (intersectedRect.Width >= (parentRect.Width - AutoscrollMargin))
                        ) || 
                        (
                            (controlRect.Height > parentRect.Height) &&
                            (intersectedRect.Height >= (parentRect.Height - AutoscrollMargin))
                        )
                    ) && 
                    (intersectedRect.Left <= controlRect.Left) &&
                    (intersectedRect.Top <= controlRect.Top)
                ) {
                    return false;
                }

                // If the control is partially visible, we want to scroll its top-left corner into view.
                // Otherwise, just go for it and try to center the control in the viewport
                var centered = (intersectedRect.Size.Length() < 4);
                var anchor = centered ? controlRect.Center : controlRect.Position - (Vector2.One * AutoscrollMargin);
                var idealCenter = centered ? parentRect.Center : parentRect.Position;
                var maximumDisplacement = anchor - idealCenter;
                // If the necessary scroll displacement is very small, don't bother scrolling - it'd just
                //  be an annoyance.
                if (maximumDisplacement.Length() < 1.5f)
                    return false;

                // Compute a new scroll offset that shifts our anchor into view, and constrain it
                var currentScrollOffset = scrollContext.ScrollOffset;
                var newScrollOffset = currentScrollOffset + maximumDisplacement;
                var min = scrollContext.MinScrollOffset ?? Vector2.Zero;
                var max = scrollContext.MaxScrollOffset;
                newScrollOffset.X = Math.Max(min.X, newScrollOffset.X);
                newScrollOffset.Y = Math.Max(min.Y, newScrollOffset.Y);
                if (max.HasValue) {
                    newScrollOffset.X = Math.Min(max.Value.X, newScrollOffset.X);
                    newScrollOffset.Y = Math.Min(max.Value.Y, newScrollOffset.Y);
                }

                // Compute our actual displacement based on the constrained offset and then clamp
                //  that displacement to our autoscroll speed
                var displacement = newScrollOffset - currentScrollOffset;
                // The autoscroll speed starts slow for short distances and speeds up
                float speedX, speedY;
                if (speed.HasValue) {
                    speedX = speedY = speed.Value;
                } else {
                    if (Math.Abs(displacement.X) >= AutoscrollInstantThreshold) {
                        speedX = 99999;
                    } else {
                        speedX = Math.Abs(displacement.X) / AutoscrollFastThreshold;
                        speedX = Arithmetic.Lerp(AutoscrollSpeedSlow, AutoscrollSpeedFast, speedX);
                    }
                    if (Math.Abs(displacement.Y) >= AutoscrollInstantThreshold) {
                        speedY = 99999;
                    } else {
                        speedY = Math.Abs(displacement.Y) / AutoscrollFastThreshold;
                        speedY = Arithmetic.Lerp(AutoscrollSpeedSlow, AutoscrollSpeedFast, speedX);
                    }
                }
                displacement.X = Math.Min(Math.Abs(displacement.X), speedX) * Math.Sign(displacement.X);
                displacement.Y = Math.Min(Math.Abs(displacement.Y), speedY) * Math.Sign(displacement.Y);
                var newOffset = currentScrollOffset + displacement;
                if (newOffset != scrollContext.ScrollOffset)
                    scrollContext.TrySetScrollOffset(newOffset, true);
                return true;
            }

            return false;
        }

        private void UpdateAutoscroll () {
            if (CurrentMouseButtons != MouseButtons.None)
                return;
            if (SuppressAutoscrollDueToInputScroll)
                return;

            PerformAutoscroll(KeyboardSelection, null);
        }

        private bool AttemptTargetedScroll (Control control, Vector2 displacement, bool recursive) {
            if (control == null)
                return false;

            do {
                var context = control as IScrollableControl;
                if (context != null) {
                    var currentScrollOffset = context.ScrollOffset;
                    var newScrollOffset = currentScrollOffset + CurrentInputState.ScrollDistance;
                    if (context.MinScrollOffset.HasValue) {
                        newScrollOffset.X = Math.Max(context.MinScrollOffset.Value.X, newScrollOffset.X);
                        newScrollOffset.Y = Math.Max(context.MinScrollOffset.Value.Y, newScrollOffset.Y);
                    }
                    if (context.MaxScrollOffset.HasValue) {
                        newScrollOffset.X = Math.Min(context.MaxScrollOffset.Value.X, newScrollOffset.X);
                        newScrollOffset.Y = Math.Min(context.MaxScrollOffset.Value.Y, newScrollOffset.Y);
                    }

                    if (currentScrollOffset != newScrollOffset) {
                        if (context.TrySetScrollOffset(newScrollOffset, true)) {
                            SuppressAutoscrollDueToInputScroll = true;
                            return true;
                        }
                    } else if (!recursive) {
                        if (context.MaxScrollOffset.HasValue && context.MaxScrollOffset.Value.Length() >= 1)
                            return false;
                    }
                }

                control.TryGetParent(out control);
            } while (control != null);

            return false;
        }

        private IScrollableControl ChooseAutoscrollContext (Control control, out RectF parentRect, out RectF controlRect, out RectF intersectedRect) {
            parentRect = controlRect = intersectedRect = default(RectF);
            if (control == null)
                return null;

            var _ = control;
            controlRect = control.GetRect();
            while (control.TryGetParent(out control)) {
                var result = control as IScrollableControl;
                if (result == null)
                    continue;

                parentRect = control.GetRect(contentRect: true);
                controlRect.Intersection(in parentRect, out intersectedRect);
                if (!intersectedRect.Equals(controlRect))
                    return result;
            }

            return null;
        }

        public Control FindTopLevelAncestor (Control control) {
            if (control == null)
                return null;

            while (true) {
                if (!control.TryGetParent(out Control parent)) {
                    if (Controls.Contains(control))
                        return control;
                    else
                        return null;
                }

                control = parent;
            }
        }

        private List<Control> TemporaryParentChain = new List<Control>();

        public Vector2 CalculateRelativeGlobalPosition (Control relativeTo, Vector2 globalPosition) {
            if (relativeTo == null)
                return globalPosition;

            // Scan upwards to build a chain of controls to apply coordinate transforms from
            TemporaryParentChain.Clear();
            TemporaryParentChain.Add(relativeTo);
            // If the mouse is currently captured, we *always* want to apply the appropriate transform chain to
            //  the mouse coordinates, instead of only applying it when the mouse intersects the transformed box
            var foundMouseCapture = (relativeTo == _MouseCaptured) && (_MouseCaptured != null);
            var search = relativeTo;
            while (search.TryGetParent(out search)) {
                if ((search == _MouseCaptured) && (_MouseCaptured != null))
                    foundMouseCapture = true;
                TemporaryParentChain.Add(search);
            }

            var transformedGlobalPosition = globalPosition;

            // Walk top-to-bottom, transforming coordinates if necessary
            for (int i = TemporaryParentChain.Count - 1; i >= 0; i--) {
                var ctl = TemporaryParentChain[i];
                var box = ctl.GetRect();
                transformedGlobalPosition = ctl.ApplyLocalTransformToGlobalPosition(transformedGlobalPosition, ref box, foundMouseCapture);
            }

            return transformedGlobalPosition;
        }

        internal MouseEventArgs MakeMouseEventArgs (Control target, Vector2 globalPosition, Vector2? mouseDownPosition, bool forClick = false) {
            var transformedGlobalPosition = CalculateRelativeGlobalPosition(target, globalPosition);

            {
                RectF box, contentBox;
                if (target == null) {
                    box = contentBox = CanvasRect;
                } else {
                    target.GetRects(out box, out contentBox);
                }
                var mdp = MouseDownPosition ?? mouseDownPosition ?? globalPosition;
                var travelDistance = (globalPosition - mdp).Length();
                if (!SpareMouseEventArgs.TryPopBack(out MouseEventArgs result))
                    result = new MouseEventArgs();
                result.Context = this;
                result.Now = Now;
                result.NowL = NowL;
                result.Modifiers = CurrentModifiers;
                result.Focused = Focused;
                result.MouseOver = MouseOver;
                result.MouseOverLoose = MouseOverLoose;
                result.Hovering = Hovering;
                result.MouseCaptured = MouseCaptured;
                result.PreviousGlobalPosition = PreviousGlobalMousePosition;
                result.GlobalPosition = globalPosition;
                result.RelativeGlobalPosition = transformedGlobalPosition;
                result.LocalPosition = transformedGlobalPosition - contentBox.Position;
                result.Box = box;
                result.ContentBox = contentBox;
                result.MouseDownPosition = mdp;
                result.MouseDownTimestamp = LastMouseDownTime;
                result.MovedSinceMouseDown = travelDistance >= MinimumMouseMovementDistance;
                result.PreviousButtons = LastMouseButtons;
                result.Buttons = CurrentMouseButtons;
                result.SequentialClickCount = (target == PreviousClickTarget)
                    ? SequentialClickCount
                    : 0;
                result.IsSynthetic = false;
                var doubleClicking = IsInDoubleClickWindow(target, globalPosition) && result.SequentialClickCount == 2;
                if (!forClick)
                    doubleClicking = doubleClicking && (MouseCaptured != null);
                result.DoubleClicking = doubleClicking;
                UsedMouseEventArgs.Add(result);
                if (target is IMouseEventArgsFilter imeaf)
                    imeaf.FilterMouseEventArgs(result);
                return result;
            }
        }

        private void HandleMouseDownPrologue () {
            AcceleratorOverlayVisible = false;
            ClearKeyboardSelection();
            HideTooltipForMouseInput(true);
        }

        private bool HandleMouseDownEpilogue (bool handled, Control target, Vector2 globalPosition, MouseButtons newButtons) {
            PreviousMouseDownTarget = target;

            if (!handled && EnableDragToScroll && (newButtons == MouseButtons.Left))
                return InitDragToScroll(target, globalPosition);

            return handled;
        }

        private bool HandleMouseDown (Control target, Vector2 globalPosition, MouseButtons newButtons) {
            // System.Diagnostics.Debug.WriteLine($"HandleMouseDown {target} {globalPosition} {newButtons}");

            HandleMouseDownPrologue();
            var relinquishedHandlers = new HashSet<Control>();
            bool result = false;

            var targetModal = (ActiveModal ?? FocusedModal);
            // HACK: If the active modal is blocking hit tests, then we frequently won't have
            //  a target for a mouse event. In that case, send it to the modal
            if ((target == null) && ShouldModalBlockHitTests(targetModal))
                target = (Control)targetModal;

            // HACK: Prevent infinite repeat in corner cases
            int steps = 5;
            while (steps-- > 0) {
                RetainCaptureRequested = null;

                MouseDownPosition = globalPosition;
                if (target != null && target.IsValidMouseInputTarget) {
                    AutomaticallyTransferFocusOnTopLevelChange(target);
                    MouseCaptured = target;
                    _PreferredTooltipSource = target;
                }
                // FIXME: Should focus changes only occur on mouseup in order to account for drag-to-scroll?
                if (target == null || target.IsValidFocusTarget)
                    Focused = target;
                // FIXME: Suppress if disabled?
                LastMouseDownTime = Now;
                var previouslyCaptured = MouseCaptured;
                var args = MakeMouseEventArgs(target, globalPosition, null);
                var ok = FireEvent(UIEvents.MouseDown, target, args);

                // HACK: A control can pre-emptively relinquish focus to pass the mouse event on to someone else
                if (
                    (previouslyCaptured == target) &&
                    (ReleasedCapture == target) &&
                    (target != null)
                ) {
                    relinquishedHandlers.Add(target);
                    UpdateCaptureAndHovering(globalPosition, target);
                    target = MouseCaptured ?? Hovering;
                    continue;
                } else {
                    ReleasedCapture = null;
                    result = ok;
                    break;
                }
            }

            return HandleMouseDownEpilogue(result, target, globalPosition, newButtons);
        }

        private bool HandleMouseUp (Control target, Vector2 globalPosition, Vector2? mouseDownPosition, MouseButtons releasedButtons) {
            ClearKeyboardSelection();
            HideTooltipForMouseInput(false);
            MouseDownPosition = null;
            // FIXME: Suppress if disabled?
            var ok = FireEvent(UIEvents.MouseUp, target, MakeMouseEventArgs(target, globalPosition, mouseDownPosition));
            if (releasedButtons == MouseButtons.Left)
                return TeardownDragToScroll(MouseCaptured ?? target, globalPosition);
            else
                return false;
        }

        private void HandleMouseMove (Control target, Vector2 globalPosition) {
            if (
                EnableDragToScroll && 
                CurrentMouseButtons != MouseButtons.None && 
                UpdateDragToScroll(MouseCaptured ?? target, globalPosition)
            )
                return;

            if (!FireEvent(UIEvents.MouseMove, target, MakeMouseEventArgs(target, globalPosition, null)))
                return;
        }

        private void HandleMouseDrag (Control target, Vector2 globalPosition) {
            if (
                EnableDragToScroll && 
                CurrentMouseButtons != MouseButtons.None && 
                UpdateDragToScroll(MouseCaptured ?? target, globalPosition)
            )
                return;

            // FIXME: Suppress if disabled?
            FireEvent(UIEvents.MouseMove, target, MakeMouseEventArgs(target, globalPosition, null));
        }

        private bool InitDragToScroll (Control target, Vector2 globalPosition) {
            IScrollableControl scrollable = target as IScrollableControl;

            // HACK: Don't allow drag-to-scroll when pressing on interactive controls like buttons
            if ((target?.AcceptsMouseInput == true) && target.Enabled && (scrollable == null)) {
                if (DragToScrollTarget != null)
                    TeardownDragToScroll((Control)DragToScrollTarget, globalPosition);
                DragToScrollTarget = null;
                return false;
            }

            while (target != null) {
                if (
                    (scrollable != null) && 
                    // Some controls are scrollable but do not currently have any hidden content, so don't drag them
                    (scrollable.MaxScrollOffset ?? scrollable.MinScrollOffset) != scrollable.MinScrollOffset
                )
                    break;
                if (!target.TryGetParent(out target))
                    break;
                scrollable = target as IScrollableControl;
            }

            if (
                (scrollable == null) || 
                !scrollable.AllowDragToScroll ||
                ((scrollable.MaxScrollOffset ?? scrollable.MinScrollOffset) == scrollable.MinScrollOffset)
            ) {
                if (DragToScrollTarget != null)
                    TeardownDragToScroll((Control)DragToScrollTarget, globalPosition);
                DragToScrollTarget = null;
                return false;
            }

            DragToScrollInitialPosition = globalPosition;
            DragToScrollTarget = scrollable;

            DragToScrollInitialOffset = scrollable.ScrollOffset;
            FireEvent(UIEvents.DragToScrollStart, (Control)scrollable);

            return true;
        }

        private bool UpdateDragToScroll (Control target, Vector2 globalPosition) {
            if (DragToScrollTarget == null)
                return false;
            if (!DragToScrollInitialOffset.HasValue)
                return false;

            if (!DragToScrollTarget.AllowDragToScroll) {
                if (DragToScrollInitialOffset.HasValue) {
                    DragToScrollTarget.TrySetScrollOffset(DragToScrollInitialOffset.Value, false);
                    DragToScrollTarget = null;
                    DragToScrollInitialOffset = null;
                }
                return false;
            }

            var minScrollOffset = DragToScrollTarget.MinScrollOffset ?? Vector2.Zero;
            var maxScrollOffset = DragToScrollTarget.MaxScrollOffset ?? Vector2.Zero;
            var positionDelta = (globalPosition - DragToScrollInitialPosition);

            var newOffset = DragToScrollInitialOffset.Value - (positionDelta * DragToScrollSpeed);
            if (DragToScrollTarget.MinScrollOffset.HasValue) {
                newOffset.X = Math.Max(minScrollOffset.X, newOffset.X);
                newOffset.Y = Math.Max(minScrollOffset.Y, newOffset.Y);
            }
            if (DragToScrollTarget.MaxScrollOffset.HasValue) {
                newOffset.X = Math.Min(maxScrollOffset.X, newOffset.X);
                newOffset.Y = Math.Min(maxScrollOffset.Y, newOffset.Y);
            }

            var actualDelta = newOffset - DragToScrollInitialOffset.Value;
            var actualDeltaScaled = actualDelta * (1.0f / DragToScrollSpeed);
            if (actualDeltaScaled.Length() < MinimumMouseMovementDistance) {
                actualDelta = Vector2.Zero;
                newOffset = DragToScrollInitialOffset.Value;
            }

            if (newOffset != DragToScrollInitialOffset) {
                if (DragToScrollTarget.TrySetScrollOffset(newOffset, true))
                    return (DragToScrollTarget.ScrollOffset != DragToScrollInitialOffset);
                else
                    return false;
            } else
                return false;
        }

        public void OverrideKeyboardSelection (Control target, bool forUser) {
            SetKeyboardSelection(target, forUser);
        }

        private bool TeardownDragToScroll (Control target, Vector2 globalPosition) {
            var scrolled = UpdateDragToScroll(target, globalPosition);
            if (DragToScrollTarget != null)
                FireEvent(UIEvents.DragToScrollEnd, (Control)DragToScrollTarget);
            DragToScrollTarget = null;
            DragToScrollInitialOffset = null;
            return scrolled;
        }

        private void HandleScroll (Control control, float delta) {
            ClearKeyboardSelection();

            while (control != null) {
                if (FireEvent(UIEvents.Scroll, control, delta))
                    return;

                if (control.TryGetParent(out control))
                    continue;
            }
        }

        private void HandleFixationChange (Control previous, Control current) {
            FireEvent(UIEvents.ControlFixated, current, previous);
            TTS.FixatedControlChanged(current);
        }

        public void TerminateComposition () {
            if (IsCompositionActive)
                Log("Terminating composition");
            IsCompositionActive = false;

            if (CachedCompositionPreview != null) {
                CachedCompositionPreview.Text = "";
                CachedCompositionPreview.Visible = false;
            }
        }

        public void UpdateComposition (string currentText, int cursorPosition, int selectionLength) {
            IsCompositionActive = true;
            Log($"Composition text '{currentText}' with cursor at offset {cursorPosition}, selection length {selectionLength}");

            var instance = GetCompositionPreviewInstance();
            instance.Text = currentText;
            instance.Invalidate();

            var offset = Engine.Result(Focused.LayoutKey).Rect.Position;
            // HACK
            var editable = Focused as Controls.EditableText;
            if (editable != null) {
                var compositionOffset = editable.GetCursorPosition();
                offset += compositionOffset;
            }

            instance.Margins = new Margins(offset.X, offset.Y, 0, 0);
            instance.Visible = true;
        }
    }
}

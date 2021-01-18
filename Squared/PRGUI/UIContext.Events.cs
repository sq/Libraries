using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Input;
using Squared.Util;

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        public event Func<string, Keys?, char?, bool> OnKeyEvent;

        internal bool FireEvent<T> (string name, Control target, T args, bool suppressHandler = false, bool targetHandlesFirst = false) {
            // FIXME: Is this right?
            if (target == null)
                target = Control.None;
            if (EventBus == null)
                return true;

            if (!targetHandlesFirst && EventBus.Broadcast(target, name, args))
                return true;
            if (targetHandlesFirst && target.HandleEvent(name, args))
                return true;

            if (suppressHandler)
                return false;

            if (targetHandlesFirst)
                return EventBus.Broadcast(target, name, args);
            else
                return target.HandleEvent(name, args);
        }

        internal bool FireEvent (string name, Control target, bool suppressHandler = false, bool targetHandlesFirst = false) {
            // FIXME: Is this right?
            if (target == null)
                target = Control.None;
            if (EventBus == null)
                return true;

            if (!targetHandlesFirst && EventBus.Broadcast<object>(target, name, null))
                return true;
            if (targetHandlesFirst && target.HandleEvent(name))
                return true;

            if (suppressHandler)
                return false;

            if (targetHandlesFirst)
                return EventBus.Broadcast<object>(target, name, null);
            else
                return target.HandleEvent(name);
        }

        private int FindChildEvent (List<UIContext.UnhandledEvent> events, Control parent, string eventName, out Control source) {
            for (int i = 0, c = events.Count; i < c; i++) {
                if (events[i].Name == eventName) {
                    // FIXME: Handle parents that are not top level controls
                    var topLevel = FindTopLevelAncestor(events[i].Source);
                    if (topLevel != parent)
                        continue;
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
                TopLevelFocusMemory.Remove(topLevelParent);
                TopLevelFocusMemory.Add(topLevelParent, target);
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

            if (current != null)
                FireEvent(UIEvents.MouseEnter, current, previous);

            ResetTooltipShowTimer();
        }

        private bool IsInDoubleClickWindow (Control target, Vector2 position) {
            if (target == null)
                return false;

            var movedDistance = (position - LastClickPosition).Length();
            if (
                (LastClickTarget == target) &&
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

            if (!target.IsValidMouseInputTarget) {
                TTS.ControlClicked(target);
                return;
            }

            if (IsInDoubleClickWindow(target, mousePosition))
                SequentialClickCount++;
            else
                SequentialClickCount = 1;

            LastClickPosition = mousePosition;
            LastClickTarget = target;
            LastClickTime = LastMouseDownTime;
            FireEvent(UIEvents.Click, target, MakeMouseEventArgs(target, mousePosition, mouseDownPosition));

            TTS.ControlClicked(target);
        }

        private void HandleDrag (Control originalTarget, Control finalTarget) {
            // FIXME
        }

        private bool HasPressedKeySincePressingAlt = false;

        public bool HandleKeyEvent (string name, Keys? key, char? ch, KeyboardModifiers? modifiers = null, bool isVirtual = false, bool isRepeat = false) {
            // HACK to simplify writing input sources
            if (name == null)
                return false;

            var evt = new KeyEventArgs {
                Context = this,
                Modifiers = modifiers ?? CurrentModifiers,
                Key = key,
                Char = ch,
                IsVirtualInput = isVirtual,
                IsRepeat = isRepeat
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

            // FIXME: Suppress events with a char if the target doesn't accept text input?
            if (FireEvent(name, Focused, evt))
                return true;

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
            var args = MakeMouseEventArgs(target, LastMousePosition, null);
            args.SequentialClickCount = 1;
            return FireEvent(UIEvents.Click, target, args);
        }

        private Control PickRotateFocusTarget (bool topLevel, int delta) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            if (topLevel) {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                // HACK
                var inTabOrder = Controls.InTabOrder(FrameIndex, false)
                    .Where(c => 
                        ((c is IControlContainer) || c.AcceptsFocus) &&
                        c.Enabled && !c.IsTransparent
                    )
                    .ToList();
                var currentIndex = inTabOrder.IndexOf(currentTopLevel);
                var newIndex = Arithmetic.Wrap(currentIndex + delta, 0, inTabOrder.Count - 1);
                var target = inTabOrder[newIndex];
                return target;
            } else {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                var newTarget = PickNextFocusTarget(Focused, delta, true);
                var newTopLevel = FindTopLevelAncestor(newTarget);
                // HACK: We don't want to change top-level controls during a regular tab
                if ((newTopLevel != currentTopLevel) && (currentTopLevel != null))
                    return Focused;
                else
                    return newTarget;
            }
        }

        internal bool RotateFocusFrom (Control location, int delta, bool isUserInitiated) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            var target = PickNextFocusTarget(location, delta, true);
            return TrySetFocus(target, isUserInitiated: isUserInitiated);
        }

        public bool RotateFocus (bool topLevel, int delta, bool isUserInitiated) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            var target = PickRotateFocusTarget(topLevel, delta);
            if (topLevel) {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                if ((target != null) && (target != currentTopLevel)) {
                    Log($"Top level tab {currentTopLevel} -> {target}");
                    if (TrySetFocus(target, isUserInitiated: isUserInitiated)) {
                        SetKeyboardSelection(Focused, isUserInitiated);
                        return true;
                    }
                }
            } else {
                Log($"Tab {Focused} -> {target}");
                if ((target != null) && TrySetFocus(target, isUserInitiated: isUserInitiated)) {
                    SetKeyboardSelection(Focused, isUserInitiated);
                    return true;
                }
            }
            return false;
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

        private bool AttemptTargetedScroll (Control control, Vector2 displacement) {
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
                controlRect.Intersection(ref parentRect, out intersectedRect);
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

        /// <summary>
        /// Transfers focus to a target control (or no control), if possible
        /// </summary>
        /// <param name="force">If true, focus will be transferred even if the control is not a valid focus target</param>
        public bool TrySetFocus (
            Control value, 
            bool force = false, 
            bool isUserInitiated = true
        ) {
            var newFocusTarget = value;

            var topLevelAncestor = FindTopLevelAncestor(value);

            // Detect attempts to focus a control that is no longer in the hierarchy
            if (topLevelAncestor == null)
                value = null;

            if (!AllowNullFocus && (value == null)) {
                // Handle cases where the focused control became disabled or invisible
                if (Focused?.IsValidFocusTarget == false)
                    newFocusTarget = value = PickNextFocusTarget(Focused, 1, false);
                else
                    newFocusTarget = value = Focused ?? Controls.FirstOrDefault();
            }

            // Top-level controls should pass focus on to their children if possible
            if (Controls.Contains(value)) {
                Control childTarget;
                if (
                    !TopLevelFocusMemory.TryGetValue(value, out childTarget) ||
                    (FindTopLevelAncestor(childTarget) == null) ||
                    Control.IsRecursivelyTransparent(childTarget)
                ) {
                    var container = value as IControlContainer;
                    if (container != null)
                        childTarget = container.Children.InTabOrder(FrameIndex, true).FirstOrDefault();
                }

                // HACK: If the focus is shifted to an invalid focus target but it's a container,
                //  attempt to recursively pick a suitable focus target inside it
                if (childTarget?.IsValidFocusTarget == false) {
                    var childContainer = (childTarget as IControlContainer);
                    if (childContainer != null)
                        childTarget = FindFocusableSibling(childContainer.Children, null, 1, true);
                }

                if (childTarget != null)
                    newFocusTarget = childTarget;
            }

            if (newFocusTarget != null) {
                while (newFocusTarget.FocusBeneficiary != null) {
                    var beneficiary = newFocusTarget.FocusBeneficiary;
                    newFocusTarget = beneficiary;
                    if (newFocusTarget == value)
                        throw new Exception("Cycle found in focus beneficiary chain");
                }

                // Attempting to set focus to a top level control is valid even if no child was selected
                var isTopLevel = Controls.Contains(newFocusTarget);

                if (!newFocusTarget.IsValidFocusTarget && !isTopLevel) {
                    var collection = (newFocusTarget as IControlContainer);
                    if (collection != null) {
                        var childTarget = FindFocusableSibling(collection.Children, null, 1, true);
                        if (childTarget == newFocusTarget)
                            return false;

                        newFocusTarget = childTarget;
                    } else if (!force)
                        return false;
                }
            }

            if (!AllowNullFocus && (newFocusTarget == null))
                return false;

            var previous = _Focused;
            if (previous != newFocusTarget)
                PreviousFocused = previous;

            var newTopLevelAncestor = FindTopLevelAncestor(newFocusTarget);
            var activeModal = ActiveModal;
            if ((activeModal?.RetainFocus == true) && (newTopLevelAncestor != activeModal))
                return false;
            if (previous != newFocusTarget)
                ;
            _Focused = newFocusTarget;

            var previousTopLevel = TopLevelFocused;
            TopLevelFocused = newTopLevelAncestor;
            if ((TopLevelFocused == null) && (_Focused != null) && Controls.Contains(_Focused))
                TopLevelFocused = _Focused;
            if (TopLevelFocused != previousTopLevel)
                PreviousTopLevelFocused = previousTopLevel;

            var fm = _Focused as IModal ?? TopLevelFocused as IModal;
            ModalFocusDonor = fm?.FocusDonor;
            TopLevelModalFocusDonor = FindTopLevelAncestor(ModalFocusDonor);

            if ((previous != null) && (previous != newFocusTarget))
                FireEvent(UIEvents.LostFocus, previous, newFocusTarget);

            // HACK: Handle cases where focus changes re-entrantly so we don't go completely bonkers
            if (_Focused == newFocusTarget)
                HandleNewFocusTarget(
                    previousTopLevel, newTopLevelAncestor,
                    previous, newFocusTarget, isUserInitiated
                );

            if ((_Focused != null) && (previous != newFocusTarget) && (_Focused == newFocusTarget))
                FireEvent(UIEvents.GotFocus, newFocusTarget, previous);

            return true;
        }

        public Control FindFocusableSibling (ControlCollection collection, Control current, int delta, bool recursive) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            var tabOrdered = collection.InTabOrder(FrameIndex, false);
            if (tabOrdered.Count < 1)
                return null;

            int tabIndex = tabOrdered.IndexOf(current), newIndex, endIndex, idx;
            if (tabIndex < 0) {
                newIndex = (delta > 0 ? 0 : tabOrdered.Count - 1);
                endIndex = (delta > 0 ? tabOrdered.Count : -1);
            } else {
                newIndex = tabIndex + delta;
                endIndex = Arithmetic.Wrap(tabIndex - delta, 0, tabOrdered.Count - 1);
            }

            var initialIndex = newIndex;

            while (newIndex != endIndex) {
                if (collection.Parent == null)
                    idx = Arithmetic.Wrap(newIndex, 0, tabOrdered.Count - 1);
                else if (newIndex >= tabOrdered.Count)
                    return null;
                else if (newIndex < 0)
                    return null;
                else
                    idx = newIndex;

                var control = tabOrdered[idx];

                if (control.Visible) {
                    if (control.Enabled && (control.EligibleForFocusRotation || control.IsFocusProxy)) {
                        return control;
                    } else if (recursive && (control is IControlContainer)) {
                        var child = FindFocusableSibling(((IControlContainer)control).Children, null, delta, recursive);
                        if (child != null)
                            return child;
                    }
                }

                newIndex += delta;
                if (collection.Parent == null)
                    newIndex = Arithmetic.Wrap(newIndex, 0, tabOrdered.Count - 1);

                if (newIndex == initialIndex)
                    break;
            }

            foreach (var item in tabOrdered) {
                if (item == current)
                    continue;
                else if (item.IsFocusProxy)
                    return item;
                else if (!item.EligibleForFocusRotation)
                    continue;
                else if (!item.Enabled)
                    continue;

                return item;
            }

            return null;
        }

        private Control PickNextFocusTarget (Control current, int delta, bool recursive) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            if (current == null)
                return FindFocusableSibling(Controls, null, delta, recursive);

            /*
            // HACK: If for some reason a top-level container is set as focused, we want tab to focus one of its children
            var isTopLevel = !current.TryGetParent(out Control _);
            if (isTopLevel) {
                var container = current as IControlContainer;
                if (container != null) {
                    var idealTarget = (delta > 0) ? container.Children.FirstOrDefault() : container.Children.LastOrDefault();
                    if (!idealTarget.IsValidFocusTarget) {
                        var focusableSibling = FindFocusableSibling(container.Children, container.Children.FirstOrDefault(), delta, recursive);
                        if (focusableSibling != null)
                            return focusableSibling;
                    } else
                        return idealTarget;
                }
            }
            */

            var ineligible = current;
            Control prior;
            ControlCollection parentCollection;

            while (current != null) {
                if (!current.TryGetParent(out Control parent))
                    parentCollection = Controls;
                else
                    parentCollection = (parent as IControlContainer)?.Children;

                var sibling = FindFocusableSibling(parentCollection, current, delta, recursive);
                if ((sibling != null) && (sibling != ineligible))
                    return sibling;

                var currentIndex = parentCollection.IndexOf(current);
                var nextIndex = currentIndex + delta;
                var nextSibling = (nextIndex >= 0) && (nextIndex < parentCollection.Count)
                    ? parentCollection[nextIndex]
                    : null;

                prior = current;
                if (nextSibling != null) {
                    var nextContainer = (nextSibling as IControlContainer);
                    if (nextContainer != null) {
                        var possibleResult = FindFocusableSibling(nextContainer.Children, null, delta, recursive);
                        if ((possibleResult != null) && (possibleResult != ineligible))
                            return possibleResult;
                    }

                    current = nextSibling;
                } else if (parent == null) {
                    break;
                } else {
                    current = parentCollection?.Parent;
                }
            }

            return null;
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

        internal MouseEventArgs MakeMouseEventArgs (Control target, Vector2 globalPosition, Vector2? mouseDownPosition) {
            var transformedGlobalPosition = CalculateRelativeGlobalPosition(target, globalPosition);

            {
                var box = target?.GetRect(contentRect: false) ?? CanvasRect;
                var contentBox = target?.GetRect(contentRect: true) ?? CanvasRect;
                var mdp = MouseDownPosition ?? mouseDownPosition ?? globalPosition;
                var travelDistance = (globalPosition - mdp).Length();
                return new MouseEventArgs {
                    Context = this,
                    Now = Now,
                    NowL = NowL,
                    Modifiers = CurrentModifiers,
                    Focused = Focused,
                    MouseOver = MouseOver,
                    MouseOverLoose = MouseOverLoose,
                    Hovering = Hovering,
                    MouseCaptured = MouseCaptured,
                    GlobalPosition = globalPosition,
                    RelativeGlobalPosition = transformedGlobalPosition,
                    LocalPosition = transformedGlobalPosition - contentBox.Position,
                    Box = box,
                    ContentBox = contentBox,
                    MouseDownPosition = mdp,
                    MouseDownTimestamp = LastMouseDownTime,
                    MovedSinceMouseDown = travelDistance >= MinimumMouseMovementDistance,
                    DoubleClicking = IsInDoubleClickWindow(target, globalPosition) && (MouseCaptured != null),
                    PreviousButtons = LastMouseButtons,
                    Buttons = CurrentMouseButtons,
                    SequentialClickCount = (target == LastClickTarget)
                        ? SequentialClickCount 
                        : 0
                };
            }
        }

        private bool HandleMouseDown (Control target, Vector2 globalPosition, MouseButtons newButtons) {
            var relinquishedHandlers = new HashSet<Control>();

            AcceleratorOverlayVisible = false;
            ClearKeyboardSelection();
            HideTooltipForMouseInput(true);

            bool result = false;

            // HACK: Prevent infinite repeat in corner cases
            int steps = 5;
            while (steps-- > 0) {
                RetainCaptureRequested = null;

                MouseDownPosition = globalPosition;
                if (target != null && target.IsValidMouseInputTarget) {
                    AutomaticallyTransferFocusOnTopLevelChange(target);
                    MouseCaptured = target;
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

            PreviousMouseDownTarget = target;

            if (!result && EnableDragToScroll && (newButtons == MouseButtons.Left))
                return InitDragToScroll(target, globalPosition);

            return result;
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
            IScrollableControl scrollable = null;
            while (target != null) {
                scrollable = target as IScrollableControl;
                if (
                    (scrollable != null) && 
                    // Some controls are scrollable but do not currently have any hidden content, so don't drag them
                    (scrollable.MaxScrollOffset ?? scrollable.MinScrollOffset) != scrollable.MinScrollOffset
                )
                    break;
                if (!target.TryGetParent(out target))
                    break;
            }

            DragToScrollInitialPosition = globalPosition;
            DragToScrollTarget = scrollable;

            if ((scrollable == null) || !scrollable.AllowDragToScroll) {
                DragToScrollInitialOffset = null;
                return false;
            } else {
                DragToScrollInitialOffset = scrollable.ScrollOffset;
                return true;
            }
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
    }
}

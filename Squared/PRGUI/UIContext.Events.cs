using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.Util;

namespace Squared.PRGUI {
    public partial class UIContext : IDisposable {
        internal bool FireEvent<T> (string name, Control target, T args, bool suppressHandler = false, bool targetHandlesFirst = false) {
            // FIXME: Is this right?
            if (target == null)
                return false;
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
                return false;
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

        private void HandleNewFocusTarget (Control previous, Control target, bool isUserInitiated) {
            var topLevelParent = FindTopLevelAncestor(target);
            if (topLevelParent != null) {
                TopLevelFocusMemory.Remove(topLevelParent);
                TopLevelFocusMemory.Add(topLevelParent, target);
            }

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

                TTS.FocusedControlChanged(target);
            }
        }

        private void HandleHoverTransition (Control previous, Control current) {
            // If the mouse enters a new control, clear the keyboard selection
            KeyboardSelection = null;

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
                (movedDistance < MinimumMouseMovementDistance)
            ) {
                var elapsed = Now - LastClickTime;
                return elapsed < DoubleClickWindowSize;
            }
            return false;
        }

        private void HandleClick (Control target, Vector2 mousePosition, Vector2 mouseDownPosition) {
            if (!target.IsValidMouseInputTarget)
                return;

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

        public bool HandleKeyEvent (string name, Keys? key, char? ch) {
            var evt = new KeyEventArgs {
                Context = this,
                Modifiers = CurrentModifiers,
                Key = key,
                Char = ch
            };

            if (name == UIEvents.KeyDown) {
                if ((key == Keys.LeftAlt) || (key == Keys.RightAlt)) {
                    HasPressedKeySincePressingAlt = false;
                } else if (key == Keys.Escape) {
                    AcceleratorOverlayVisible = false;
                } else if (key == Keys.Tab) {
                } else if (key.HasValue && !ModifierKeys.Contains(key.Value)) {
                    if (CurrentModifiers.Alt)
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

            if (name != UIEvents.KeyPress)
                return false;

            switch (key) {
                case Keys.Escape:
                    Focused = null;
                    break;
                case Keys.Tab:
                    int tabDelta = CurrentModifiers.Shift ? -1 : 1;
                    return RotateFocus(topLevel: CurrentModifiers.Control, delta: tabDelta, isUserInitiated: true);
                case Keys.Space:
                    if (Focused?.IsValidMouseInputTarget == true) {
                        var args = MakeMouseEventArgs(Focused, LastMousePosition, null);
                        args.SequentialClickCount = 1;
                        return FireEvent(UIEvents.Click, Focused, args);
                    } else
                        return false;
            }

            return false;
        }

        private Control PickRotateFocusTarget (bool topLevel, int delta) {
            if (topLevel) {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                // HACK
                var inTabOrder = Controls.InTabOrder(false)
                    .Where(c => 
                        ((c is IControlContainer) || c.AcceptsFocus) &&
                        c.Enabled && c.Visible
                    )
                    .ToList();
                var currentIndex = inTabOrder.IndexOf(currentTopLevel);
                var newIndex = Arithmetic.Wrap(currentIndex + delta, 0, inTabOrder.Count - 1);
                var target = inTabOrder[newIndex];
                return target;
            } else {
                return PickNextFocusTarget(Focused, delta, true);
            }
        }

        internal bool RotateFocusFrom (Control location, int delta, bool isUserInitiated) {
            var target = PickNextFocusTarget(location, delta, true);
            return TrySetFocus(target, isUserInitiated: isUserInitiated);
        }

        public bool RotateFocus (bool topLevel, int delta, bool isUserInitiated) {
            var target = PickRotateFocusTarget(topLevel, delta);
            if (topLevel) {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                if ((target != null) && (target != currentTopLevel)) {
                    Log($"Top level tab {currentTopLevel} -> {target}");
                    if (TrySetFocus(target, isUserInitiated: isUserInitiated)) {
                        KeyboardSelection = Focused;
                        return true;
                    }
                }
            } else {
                Log($"Tab {Focused} -> {target}");
                if ((target != null) && TrySetFocus(target, isUserInitiated: isUserInitiated)) {
                    KeyboardSelection = Focused;
                    return true;
                }
            }
            return false;
        }

        public bool PerformAutoscroll (Control target, float? speed = null) {
            var scrollContext = ChooseScrollContext(target, out RectF parentRect, out RectF controlRect, out RectF intersectedRect);
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
                    speedX = Math.Abs(displacement.X) / AutoscrollFastThreshold;
                    speedY = Math.Abs(displacement.Y) / AutoscrollFastThreshold;
                    speedX = Arithmetic.Lerp(AutoscrollSpeedSlow, AutoscrollSpeedFast, speedX);
                    speedY = Arithmetic.Lerp(AutoscrollSpeedSlow, AutoscrollSpeedFast, speedX);
                }
                displacement.X = Math.Min(Math.Abs(displacement.X), speedX) * Math.Sign(displacement.X);
                displacement.Y = Math.Min(Math.Abs(displacement.Y), speedY) * Math.Sign(displacement.Y);
                var newOffset = currentScrollOffset + displacement;
                if (newOffset != scrollContext.ScrollOffset)
                    scrollContext.TrySetScrollOffset(newOffset, false); // FIXME: true?
                return true;
            }

            return false;
        }

        private void UpdateAutoscroll () {
            if (CurrentMouseButtons != MouseButtons.None)
                return;

            PerformAutoscroll(KeyboardSelection, null);
        }

        private IScrollableControl ChooseScrollContext (Control control, out RectF parentRect, out RectF controlRect, out RectF intersectedRect) {
            parentRect = controlRect = intersectedRect = default(RectF);
            if (control == null)
                return null;

            var _ = control;
            controlRect = control.GetRect(Layout);
            while (control.TryGetParent(out control)) {
                var result = control as IScrollableControl;
                if (result == null)
                    continue;

                parentRect = control.GetRect(Layout, contentRect: true);
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

            // Detect attempts to focus a control that is no longer in the hierarchy
            if (FindTopLevelAncestor(value) == null)
                value = null;

            // Top-level controls should pass focus on to their children if possible
            if (Controls.Contains(value)) {
                Control childTarget;
                if (!TopLevelFocusMemory.TryGetValue(value, out childTarget) ||
                    (FindTopLevelAncestor(childTarget) == null)
                ) {
                    var container = value as IControlContainer;
                    if (container != null)
                        childTarget = container.Children.InTabOrder(true).FirstOrDefault();
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

                // FIXME: Should we throw here?
                if (!newFocusTarget.IsValidFocusTarget && !force)
                    return false;
            }

            var previous = _Focused;
            if (previous != _Focused)
                PreviousFocused = _Focused;
            _Focused = newFocusTarget;

            var previousTopLevel = TopLevelFocused;
            TopLevelFocused = FindTopLevelAncestor(_Focused);
            if ((TopLevelFocused == null) && (_Focused != null) && Controls.Contains(_Focused))
                TopLevelFocused = _Focused;
            if (TopLevelFocused != previousTopLevel)
                PreviousTopLevelFocused = previousTopLevel;

            var fd = _Focused?.FocusDonor;
            TopLevelFocusDonor = FindTopLevelAncestor(fd);

            if ((previous != null) && (previous != newFocusTarget))
                FireEvent(UIEvents.LostFocus, previous, newFocusTarget);

            // HACK: Handle cases where focus changes re-entrantly so we don't go completely bonkers
            if (_Focused == newFocusTarget)
                HandleNewFocusTarget(previous, newFocusTarget, isUserInitiated);

            if ((_Focused != null) && (previous != newFocusTarget) && (_Focused == newFocusTarget))
                FireEvent(UIEvents.GotFocus, newFocusTarget, previous);

            return true;
        }

        public Control FindFocusableSibling (ControlCollection collection, Control current, int delta, bool recursive) {
            var tabOrdered = collection.InTabOrder(false);
            if (tabOrdered.Count < 1)
                return null;

            int tabIndex = tabOrdered.IndexOf(current), newIndex, endIndex, idx;
            if (tabIndex < 0) {
                newIndex = (delta > 0 ? 0 : tabOrdered.Count - 1);
                endIndex = (delta > 0 ? tabOrdered.Count - 1 : 0);
            } else {
                newIndex = tabIndex + delta;
                endIndex = Arithmetic.Wrap(tabIndex - delta, 0, tabOrdered.Count - 1);
            }

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

                if (control.Enabled && control.IsValidFocusTarget && (control.FocusBeneficiary == null)) {
                    return control;
                } else if (recursive && (control is IControlContainer)) {
                    var child = FindFocusableSibling(((IControlContainer)control).Children, null, delta, recursive);
                    if (child != null)
                        return child;
                }

                newIndex += delta;
                if (collection.Parent == null)
                    newIndex = Arithmetic.Wrap(newIndex, 0, tabOrdered.Count - 1);
            }

            return tabOrdered[0];
        }

        private Control PickNextFocusTarget (Control current, int delta, bool recursive) {
            ControlCollection collection;

            if (current == null)
                return FindFocusableSibling(Controls, null, delta, recursive);

            while (current != null) {
                if (current != null) {
                    if (!current.TryGetParent(out Control parent))
                        return null;
                    collection = (parent as IControlContainer)?.Children;
                } else {
                    collection = Controls;
                }

                var sibling = FindFocusableSibling(collection, current, delta, recursive);
                if (sibling != null)
                    return sibling;

                current = collection.Parent;
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
                var box = ctl.GetRect(Layout);
                transformedGlobalPosition = ctl.ApplyLocalTransformToGlobalPosition(Layout, transformedGlobalPosition, ref box, foundMouseCapture);
            }

            return transformedGlobalPosition;
        }

        internal MouseEventArgs MakeMouseEventArgs (Control target, Vector2 globalPosition, Vector2? mouseDownPosition) {
            if (target == null)
                return default(MouseEventArgs);

            var transformedGlobalPosition = CalculateRelativeGlobalPosition(target, globalPosition);

            {
                var box = target.GetRect(Layout, contentRect: false);
                var contentBox = target.GetRect(Layout, contentRect: true);
                var mdp = MouseDownPosition ?? mouseDownPosition ?? globalPosition;
                var travelDistance = (globalPosition - mdp).Length();
                return new MouseEventArgs {
                    Context = this,
                    Now = Now,
                    NowL = NowL,
                    Modifiers = CurrentModifiers,
                    Focused = Focused,
                    MouseOver = MouseOver,
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

        private bool HandleMouseDown (Control target, Vector2 globalPosition) {
            var relinquishedHandlers = new HashSet<Control>();

            AcceleratorOverlayVisible = false;
            KeyboardSelection = null;
            HideTooltipForMouseInput(true);

            // HACK: Prevent infinite repeat in corner cases
            int steps = 5;
            while (steps-- > 0) {
                SuppressNextCaptureLoss = false;
                MouseDownPosition = globalPosition;
                if (target != null && target.IsValidMouseInputTarget) {
                    AutomaticallyTransferFocusOnTopLevelChange(target);
                    MouseCaptured = target;
                }
                if (target == null || target.IsValidFocusTarget)
                    Focused = target;
                // FIXME: Suppress if disabled?
                LastMouseDownTime = Now;
                var previouslyCaptured = MouseCaptured;
                var ok = FireEvent(UIEvents.MouseDown, target, MakeMouseEventArgs(target, globalPosition, null));

                // HACK: A control can pre-emptively relinquish focus to pass the mouse event on to someone else
                if (
                    (previouslyCaptured == target) &&
                    (ReleasedCapture == target)
                ) {
                    relinquishedHandlers.Add(target);
                    UpdateCaptureAndHovering(globalPosition, target);
                    target = MouseCaptured ?? Hovering;
                    continue;
                } else {
                    ReleasedCapture = null;
                    if (ok)
                        return true;
                }
            }

            if (EnableDragToScroll)
                return InitDragToScroll(target, globalPosition);

            return false;
        }

        private bool HandleMouseUp (Control target, Vector2 globalPosition, Vector2? mouseDownPosition) {
            KeyboardSelection = null;
            HideTooltipForMouseInput(false);
            MouseDownPosition = null;
            // FIXME: Suppress if disabled?
            FireEvent(UIEvents.MouseUp, target, MakeMouseEventArgs(target, globalPosition, mouseDownPosition));
            return TeardownDragToScroll(MouseCaptured ?? target, globalPosition);
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
                return DragToScrollTarget.TrySetScrollOffset(newOffset, true);
            } else
                return false;
        }

        public void OverrideKeyboardSelection (Control target) {
            KeyboardSelection = target;
        }

        private bool TeardownDragToScroll (Control target, Vector2 globalPosition) {
            var scrolled = UpdateDragToScroll(target, globalPosition);
            DragToScrollTarget = null;
            DragToScrollInitialOffset = null;
            return scrolled;
        }

        private void HandleScroll (Control control, float delta) {
            KeyboardSelection = null;

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

        private void TextInputEXT_TextInput (char ch) {
            // Control characters will be handled through the KeyboardState path
            if (char.IsControl(ch))
                return;

            HandleKeyEvent(UIEvents.KeyPress, null, ch);
        }

        private void TerminateComposition () {
            if (IsCompositionActive)
                Log("Terminating composition");
            IsCompositionActive = false;

            if (CachedCompositionPreview != null) {
                CachedCompositionPreview.Text = "";
                CachedCompositionPreview.Visible = false;
            }
        }

        private void UpdateComposition (string currentText, int cursorPosition, int selectionLength) {
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

        private void TextInputEXT_TextEditing (string text, int cursorPosition, int length) {
            if ((text == null) || (text.Length == 0)) {
                TerminateComposition();
                return;
            }

            UpdateComposition(text, cursorPosition, length);
        }
    }
}

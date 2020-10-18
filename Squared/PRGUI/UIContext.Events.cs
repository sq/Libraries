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

        private void HandleNewFocusTarget (Control previous, Control target) {
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
                (movedDistance < MinimumMouseMovementDistance)
            ) {
                var elapsed = Now - LastClickTime;
                return elapsed < DoubleClickWindowSize;
            }
            return false;
        }

        private void HandleClick (Control target, Vector2 mousePosition) {
            if (!target.IsValidMouseInputTarget)
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

        public bool HandleKeyEvent (string name, Keys? key, char? ch) {
            var evt = new KeyEventArgs {
                Context = this,
                Modifiers = CurrentModifiers,
                Key = key,
                Char = ch
            };

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
                    if (CurrentModifiers.Control) {
                        var currentTopLevel = FindTopLevelAncestor(Focused);
                        // HACK
                        var inTabOrder = Controls.InTabOrder(false)
                            .Where(c => 
                                ((c is IControlContainer) || c.AcceptsFocus) &&
                                c.Enabled && c.Visible
                            )
                            .ToList();
                        var currentIndex = inTabOrder.IndexOf(currentTopLevel);
                        var newIndex = Arithmetic.Wrap(currentIndex + tabDelta, 0, inTabOrder.Count - 1);
                        var target = inTabOrder[newIndex];
                        if ((target != null) && (target != currentTopLevel)) {
                            Console.WriteLine($"Top level tab {currentTopLevel} -> {target}");
                            return TrySetFocus(target, false);
                        } else
                            return false;
                    } else {
                        var target = PickNextFocusTarget(Focused, tabDelta, true);
                        Console.WriteLine($"Tab {Focused} -> {target}");
                        if (target != null)
                            return TrySetFocus(target, false);
                        else
                            return false;
                    }
                case Keys.Space:
                    if (Focused == null)
                        return false;
                    if (!Focused.AcceptsMouseInput)
                        return false;
                    return FireEvent(UIEvents.Click, Focused, 1);
            }

            return false;
        }

        private Control FindTopLevelAncestor (Control control) {
            if (control == null)
                return null;

            while (true) {
                if (!control.TryGetParent(out Control parent))
                    return control;

                control = parent;
            }
        }

        public bool TrySetFocus (Control value, bool force) {
            var newFocusTarget = value;

            // Top-level controls should pass focus on to their children if possible
            if (Controls.Contains(value)) {
                Control childTarget;
                if (!TopLevelFocusMemory.TryGetValue(value, out childTarget)) {
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
            _Focused = newFocusTarget;
            if ((previous != null) && (previous != newFocusTarget))
                FireEvent(UIEvents.LostFocus, previous, newFocusTarget);

            // HACK: Handle cases where focus changes re-entrantly so we don't go completely bonkers
            if (_Focused == newFocusTarget)
                HandleNewFocusTarget(previous, newFocusTarget);

            if ((_Focused != null) && (previous != newFocusTarget) && (_Focused == newFocusTarget))
                FireEvent(UIEvents.GotFocus, newFocusTarget, previous);

            return true;
        }

        private Control FindFocusableSibling (ControlCollection collection, Control current, int delta, bool recursive) {
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

                if (control.Enabled && control.IsValidFocusTarget) {
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

            return null;
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

        private MouseEventArgs MakeMouseEventArgs (Control target, Vector2 globalPosition, Vector2? mouseDownPosition) {
            if (target == null)
                return default(MouseEventArgs);

            var box = target.GetRect(Layout, contentRect: false);
            var contentBox = target.GetRect(Layout, contentRect: true);
            var mdp = MouseDownPosition ?? mouseDownPosition ?? globalPosition;
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
                MovedSinceMouseDown = travelDistance >= MinimumMouseMovementDistance,
                DoubleClicking = IsInDoubleClickWindow(target, globalPosition) && (MouseCaptured != null),
                PreviousButtons = LastMouseButtons,
                Buttons = CurrentMouseButtons
            };
        }

        private bool HandleMouseDown (Control target, Vector2 globalPosition) {
            var relinquishedHandlers = new HashSet<Control>();

            HideTooltipForMouseInput();

            // HACK: Prevent infinite repeat in corner cases
            int steps = 5;
            while (steps-- > 0) {
                SuppressNextCaptureLoss = false;
                MouseDownPosition = globalPosition;
                if (target != null && target.IsValidMouseInputTarget)
                    MouseCaptured = target;
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
            HideTooltipForMouseInput();
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
            FireEvent(UIEvents.MouseDrag, target, MakeMouseEventArgs(target, globalPosition, null));
        }

        private bool InitDragToScroll (Control target, Vector2 globalPosition) {
            IScrollableControl scrollable = null;
            while (target != null) {
                scrollable = target as IScrollableControl;
                if (scrollable != null)
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
                    DragToScrollTarget.ScrollOffset = DragToScrollInitialOffset.Value;
                    DragToScrollTarget = null;
                    DragToScrollInitialOffset = null;
                }
                return false;
            }

            var minScrollOffset = DragToScrollTarget.MinScrollOffset ?? Vector2.Zero;
            var maxScrollOffset = DragToScrollTarget.MaxScrollOffset ?? Vector2.Zero;
            var positionDelta = (globalPosition - DragToScrollInitialPosition);

            var newOffset = DragToScrollInitialOffset.Value + (positionDelta * DragToScrollSpeed);
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

            DragToScrollTarget.ScrollOffset = newOffset;

            if (newOffset != DragToScrollInitialOffset)
                return true;
            else
                return false;
        }

        private bool TeardownDragToScroll (Control target, Vector2 globalPosition) {
            var scrolled = UpdateDragToScroll(target, globalPosition);
            DragToScrollTarget = null;
            DragToScrollInitialOffset = null;
            return scrolled;
        }

        private void HandleScroll (Control control, float delta) {
            while (control != null) {
                if (FireEvent(UIEvents.Scroll, control, delta))
                    return;

                if (control.TryGetParent(out control))
                    continue;
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
    }
}

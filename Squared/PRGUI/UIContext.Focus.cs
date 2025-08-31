using System;
using System.Collections;
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
    public sealed partial class UIContext : IDisposable {
        private readonly Dictionary<Control, Control> InvalidFocusTargets = 
            new Dictionary<Control, Control>(Control.Comparer.Instance);

        private HashSet<Control> FocusSearchHistory = new HashSet<Control>();
        private (Control value, bool force, bool isUserInitiated, bool? suppressAnimations, bool overrideKeyboardSelection) QueuedFocus;
        // HACK: Avoid spamming the debug log with this warning every frame
        private (IModal, Control) LastRetainedFocusWarning;

        private void AutomaticallyTransferFocusOnTopLevelChange (Control target) {
            if (target.AcceptsFocus)
                return;

            var previousTopLevel = FindTopLevelAncestor(Focused);
            var newTopLevel = FindTopLevelAncestor(target);
            if ((newTopLevel != previousTopLevel) && (newTopLevel != null)) {
                Log($"Automatically transfering focus to new top level ancestor {newTopLevel}");
                TrySetFocus(newTopLevel, isUserInitiated: false);
            }
        }

        public bool ReleaseFocus (Control target, bool forward) {
            if (Focused != target)
                return false;

            // FIXME
            var isUserInitiated = false;
            if (!RotateFocus(false, forward ? 1 : -1, isUserInitiated)) {
                // Forward is a best-effort request, go backward instead if necessary
                if (forward && RotateFocus(false, -1, isUserInitiated))
                    return true;
                return TrySetFocus(null);
            }

            return true;
        }

        private void RemoveFromFocusMemory (Control control) {
            TopLevelFocusMemory.Remove(control);

            foreach (var topLevel in Controls) {
                if (!TopLevelFocusMemory.TryGetValue(topLevel, out var cell))
                    continue;
                if (control == cell.Value)
                    cell.Value = null;
            }
        }

        // FIXME: This operation can shift the focus out of view, should it perform auto-scroll?
        public bool ReleaseDescendantFocus (Control container, bool forward) {
            if (Focused == null)
                return false;
            if (container == null)
                return false;

            // FIXME
            var isUserInitiated = false;

            if (TopLevelFocused == container)
                return RotateFocus(true, forward ? 1 : -1, isUserInitiated);
            
            var chain = Focused;
            while (chain != null) {
                // If focus memory points to this control we're defocusing, clear it
                RemoveFromFocusMemory(chain);

                if (chain == container) {
                    if (!RotateFocusFrom(container, forward ? 1 : -1, isUserInitiated)) {
                        if (forward)
                            return RotateFocusFrom(container, -1, isUserInitiated);
                        else
                            return TrySetFocus(null, true);
                    } else
                        return true;
                }

                if (!chain.TryGetParent(out Control parent) || (parent == null))
                    return false;
                chain = parent;
            }

            return false;
        }

        private void DefocusInvalidFocusTargets () {
            Control idealNewTarget = null;

            // HACK: Not sure why this is necessary
            int iterations = 10;
            while (
                (Focused != null) && 
                InvalidFocusTargets.TryGetValue(Focused, out idealNewTarget)
            ) {
                InvalidFocusTargets.Remove(Focused);
                var current = Focused;
                var ok = (idealNewTarget != null) && TrySetFocus(idealNewTarget);

                if (!ok) {
                    var interim = Focused;
                    idealNewTarget = PickIdealNewFocusTargetForInvalidFocusTarget(interim);
                    if ((idealNewTarget == interim) || (idealNewTarget == current) || !TrySetFocus(idealNewTarget)) {
                        // Log($"Could not move focus from invalid target {current}");
                        break;
                    } else
                        ; // Log($"Moved focus from invalid target {current} to {Focused} through {interim}");
                } else {
                    // Log($"Moved focus from invalid target {current} to {Focused}");
                }

                if (iterations-- <= 0)
                    break;
            }
            InvalidFocusTargets.Clear();
        }

        private Control PickIdealNewFocusTargetForInvalidFocusTarget (Control control) {
            var fm = FocusedModal;
            Control idealNewTarget = null;
            // FIXME: TopLevelFocused fixes some behaviors here but breaks others :(
            if ((fm?.FocusDonor != null) && Control.IsEqualOrAncestor(Focused, control))
                idealNewTarget = fm.FocusDonor;

            // Attempt to auto-shift focus as long as our parent chain is focusable
            if (
                !Control.IsRecursivelyTransparent(control, includeSelf: false, ignoreFadeIn: true) && 
                // We shouldn't auto-shift focus for modals since it's somewhat expensive and we don't
                //  want to focus a random top level control after they close
                (control is not IModal) &&
                FindTopLevelAncestor(control) != null
            )
                idealNewTarget = idealNewTarget ?? PickFocusableSiblingForRotation(control, 1, false, out bool temp);

            // Auto-shifting failed, so try to return to the most recently focused control
            idealNewTarget = idealNewTarget ?? PreviousFocused ?? PreviousTopLevelFocused;
            var tla = FindTopLevelAncestor(idealNewTarget);
            if (tla == null)
                idealNewTarget = null;
            else if ((tla is TitledContainer tc) && tc.Collapsed)
                // HACK: Don't return focus to inside of a collapsed window, since it would un-collapse it.
                idealNewTarget = null;

            // HACK: If focus is stuck on a modal that's no longer a valid target, pick the topmost modal (if any) and focus that
            if (idealNewTarget == null)
                idealNewTarget = ModalStack.LastOrDefault() as Control;

            return idealNewTarget;
        }

        // Clean up when a control is removed in case it has focus or mouse capture,
        //  and attempt to return focus to the most recent place it occupied (for modals)
        public void NotifyControlBecomingInvalidFocusTarget (Control control, bool removed) {
            if (control == CachedTooltip) {
                // HACK: Handle Controls.Clear();
                CachedTooltip = null;
            }

            RemoveFromFocusMemory(control);

            if (PreviousFocused == control)
                PreviousFocused = null;

            if (PreviousTopLevelFocused == control) {
                PreviousTopLevelFocused = null;
                PreviousFocused = null;
            }

            if (Control.IsEqualOrAncestor(_MouseCaptured, control))
                MouseCaptured = null;

            if (control.AcceptsFocus) {
                if ((_Focused != control) && Control.IsEqualOrAncestor(_Focused, control))
                    InvalidFocusTargets[_Focused] =
                        PickIdealNewFocusTargetForInvalidFocusTarget(control);

                if (_Focused == control)
                    InvalidFocusTargets[control] =
                        PickIdealNewFocusTargetForInvalidFocusTarget(control);
            }

            if (Control.IsEqualOrAncestor(KeyboardSelection, control))
                ClearKeyboardSelection();

            if (PreviousFocused == control)
                PreviousFocused = null;

            if (PreviousTopLevelFocused == control) {
                PreviousTopLevelFocused = null;
                PreviousFocused = null;
            }

            if (removed)
                NotifyModalClosed(control as IModal);
        }

        private void EnsureValidFocus () {
            DefocusInvalidFocusTargets();

            if ((Focused == null) && !AllowNullFocus)
                Focused = PickFocusableChild(null);
        }

        private Control PickRotateFocusTarget (bool topLevel, int delta) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            // FIXME: Introduce new system where we build a flattened 'focus target list' of the whole tree of focusable controls
            //  at a specified depth. I.e. for top-level we build the list of all top level focus targets only, and for non top-level
            //  we walk the whole tree recursively pushing controls into the list based on the traversal criteria.
            // This would also allow getting rid of TraverseChildren
            if (topLevel) {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                // HACK
                var inTabOrder = Controls.InTabOrder(FrameIndex, false)
                    .ToDenseList(where: c => 
                        (((c as IControlContainer)?.ChildrenAcceptFocus ?? false) || c.AcceptsFocus) &&
                        (c.Enabled || c.AcceptsFocusWhenDisabled) && !Control.IsRecursivelyTransparent(c, true, NowL, ignoreFadeIn: true) &&
                        (c is not FocusProxy)
                    );
                var currentIndex = inTabOrder.IndexOf(currentTopLevel);
                var newIndex = Arithmetic.Wrap(currentIndex + delta, 0, inTabOrder.Count - 1);
                var target = inTabOrder[newIndex];
                return target;
            } else {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                var newTarget = PickFocusableSiblingForRotation(Focused, delta, null, out bool didFollowProxy);
                var newTopLevel = FindTopLevelAncestor(newTarget);
                // HACK: We don't want to change top-level controls during a regular tab
                if ((newTopLevel != currentTopLevel) && (currentTopLevel != null) && Focused.IsValidFocusTarget && !didFollowProxy)
                    return Focused;
                else
                    return newTarget;
            }
        }

        internal bool RotateFocusFrom (Control location, int delta, bool isUserInitiated) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            var target = PickFocusableSiblingForRotation(location, delta, null, out bool temp);
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

        public Control ControlQueuedForFocus => QueuedFocus.value;

        /// <summary>
        /// Requests that a control be focused as soon as possible (for example, after it becomes visible)
        /// </summary>
        /// <param name="force">If true, focus will be transferred even if the control is not a valid focus target</param>
        /// <returns>The control previously queued for focus, if any</returns>
        public Control QueueFocus (
            Control value, 
            bool force = false, 
            bool isUserInitiated = true,
            bool? suppressAnimations = null,
            bool overrideKeyboardSelection = false
        ) {
            var result = QueuedFocus.value;
            QueuedFocus = (value, force, isUserInitiated, suppressAnimations, overrideKeyboardSelection);
            return result;
        }

        /// <summary>
        /// Attempts to focus a control immediately, and if that fails, queues it instead.
        /// </summary>
        /// <param name="force">If true, focus will be transferred even if the control is not a valid focus target</param>
        /// <returns>true if focus was set immediately, false if focus was queued instead.</returns>
        public bool SetOrQueueFocus (
            Control value, 
            bool force = false, 
            bool isUserInitiated = true,
            bool? suppressAnimations = null,
            bool overrideKeyboardSelection = false
        ) {
            if (!TrySetFocus(value, force, isUserInitiated, suppressAnimations, overrideKeyboardSelection)) {
                QueueFocus(value, force, isUserInitiated, suppressAnimations, overrideKeyboardSelection);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Transfers focus to a target control (or no control), if possible
        /// </summary>
        /// <param name="force">If true, focus will be transferred even if the control is not a valid focus target</param>
        public bool TrySetFocus (
            Control value, 
            bool force = false, 
            bool isUserInitiated = true,
            bool? suppressAnimations = null,
            bool overrideKeyboardSelection = false
        ) {
            var newFocusTarget = value;
            var topLevelAncestor = FindTopLevelAncestor(value);

            // Detect attempts to focus a control that is no longer in the hierarchy
            if (topLevelAncestor == null)
                value = null;

            if (!AllowNullFocus && (value == null)) {
                // Handle cases where the focused control became disabled or invisible
                if (Focused?.IsValidFocusTarget == false)
                    newFocusTarget = value = PickFocusableSiblingForRotation(Focused, 1, false, out bool temp);
                else
                    newFocusTarget = value = Focused ?? Controls.FirstOrDefault();
            }

            FocusSearchHistory.Clear();

            // Top-level controls should pass focus on to their children if possible
            if (Controls.Contains(value)) {
                Control childTarget = null;
                if (TopLevelFocusMemory.TryGetValue(value, out var cell))
                    childTarget = cell.Value;

                if (
                    (childTarget == null) ||
                    (FindTopLevelAncestor(childTarget) == null) ||
                    Control.IsRecursivelyTransparent(childTarget, ignoreFadeIn: true)
                ) {
                    var container = value as IControlContainer;
                    if (IsValidContainerToSearchForFocusableControls(container))
                         childTarget = container.DefaultFocusTarget ?? PickFocusableChild((Control)container);
                }

                // HACK: If the focus is shifted to an invalid focus target but it's a container,
                //  attempt to recursively pick a suitable focus target inside it
                if (childTarget?.IsValidFocusTarget == false) {
                    var childContainer = (childTarget as IControlContainer);
                    if (IsValidContainerToSearchForFocusableControls(childContainer))
                        childTarget = PickFocusableChild((Control)childContainer);
                }

                if (childTarget != null)
                    newFocusTarget = childTarget;
            }

            while (newFocusTarget != null) {
                if (FocusSearchHistory.Contains(newFocusTarget))
                    throw new Exception($"Cycle found when walking focus graph from {value}");
                FocusSearchHistory.Add(newFocusTarget);

                while (newFocusTarget.FocusBeneficiary != null) {
                    var beneficiary = newFocusTarget.FocusBeneficiary;
                    newFocusTarget = beneficiary;
                }

                // Attempting to set focus to a top level control is valid even if no child was selected
                var isTopLevel = Controls.Contains(newFocusTarget);

                if (!newFocusTarget.IsValidFocusTarget) {
                    var collection = (newFocusTarget as IControlContainer);
                    if (IsValidContainerToSearchForFocusableControls(collection)) {
                        var childTarget = PickFocusableChild((Control)collection);
                        if (childTarget == newFocusTarget) {
                            if (!force && !isTopLevel)
                                return false;
                        } else if (!isTopLevel || (childTarget != null)) {
                            // The new focus target may currently not have any children that are eligible
                            //  to receive focus, in which case if it's top-level we want to focus it anyway
                            newFocusTarget = childTarget;
                            continue;
                        } else {
                            ;
                        }
                    } else if (!force && !isTopLevel)
                        return false;
                }

                break;
            }

            if (!AllowNullFocus && (newFocusTarget == null))
                return false;

            var previous = _Focused;
            if (previous != newFocusTarget) {
                PreviousFocused = previous;

                // On programmatic focus swaps, suppress the animation
                if (suppressAnimations ?? !isUserInitiated)
                    SuppressFocusChangeAnimationsThisStep = true;
            }

            var newTopLevelAncestor = FindTopLevelAncestor(newFocusTarget);
            var activeModal = ActiveModal;
            if (
                (activeModal?.RetainFocus == true) && 
                (newTopLevelAncestor != activeModal) && 
                ((Control)ActiveModal).Enabled &&
                !Control.IsRecursivelyTransparent(((Control)ActiveModal), ignoreFadeIn: true) &&
                (TopLevelFocused == activeModal)
            ) {
                var tup = (activeModal, newFocusTarget);
                if (LastRetainedFocusWarning != tup) {
                    LastRetainedFocusWarning = tup;
                    Log($"Modal {activeModal} is retaining focus and blocked focus change to {newFocusTarget}");
                }
                return false;
            }

            if (previous != newFocusTarget) {
                FocusChain.Clear();
                var current = newFocusTarget;
                while (current != null) {
                    FocusChain.Add(current);
                    current.TryGetParent(out current);
                }
            }

            // HACK: Without doing this, something like SetFocus in a list's selectionchanged won't clear the
            //  keyboard selection, so the list will keep showing its tooltip
            if (_CurrentInput.KeyboardNavigationEnded)
                ClearKeyboardSelection();

            _Focused = newFocusTarget;
            _FocusedModal = newFocusTarget as IModal;

            var previousTopLevel = TopLevelFocused;
            TopLevelFocused = newTopLevelAncestor;
            if ((TopLevelFocused == null) && (_Focused != null) && Controls.Contains(_Focused))
                TopLevelFocused = _Focused;
            if (TopLevelFocused != previousTopLevel)
                PreviousTopLevelFocused = previousTopLevel;

            var fm = _FocusedModal ?? TopLevelFocused as IModal;
            ModalFocusDonor = fm?.FocusDonor;
            TopLevelModalFocusDonor = FindTopLevelAncestor(ModalFocusDonor);

            if ((previous != null) && (previous != newFocusTarget))
                FireEvent(UIEvents.LostFocus, previous, newFocusTarget);
            if ((PreviousTopLevelFocused != null) && (PreviousTopLevelFocused != TopLevelFocused))
                FireEvent(UIEvents.LostTopLevelFocus, PreviousTopLevelFocused, TopLevelFocused);

            // HACK: Handle cases where focus changes re-entrantly so we don't go completely bonkers
            if (_Focused == newFocusTarget)
                HandleNewFocusTarget(
                    previousTopLevel, newTopLevelAncestor,
                    previous, newFocusTarget, isUserInitiated
                );

            if ((TopLevelFocused != null) && (PreviousTopLevelFocused != TopLevelFocused))
                FireEvent(UIEvents.GotTopLevelFocus, TopLevelFocused, PreviousTopLevelFocused);
            if ((_Focused != null) && (previous != newFocusTarget) && (_Focused == newFocusTarget))
                FireEvent(UIEvents.GotFocus, newFocusTarget, previous);

            if (overrideKeyboardSelection)
                OverrideKeyboardSelection(_Focused, isUserInitiated);

            if (_Focused != null) {
                if (InvalidFocusTargets.Remove(_Focused))
                    ;
            }

            return true;
        }

        private static bool IsValidContainerToSearchForFocusableControls (IControlContainer icc) {
            return IsValidContainerToSearchForFocusableControls(icc as Control);
        }

        private static bool IsValidContainerToSearchForFocusableControls (Control control) {
            if (control is not IControlContainer ic)
                return false;
            else if (!ic.ChildrenAcceptFocus)
                return false;
            return (control.Enabled || control.AcceptsFocusWhenDisabled)
                && control.Visible && !Control.IsRecursivelyTransparent(control, ignoreFadeIn: true);
        }

        public bool TryMoveFocusDirectionally (int x, int y, bool isUserInitiated = true, Control relativeTo = null) {
            // FIXME: It seems like when the gamepad controller invokes this it can ignore modals' focus retention
            // Modals' block hit test should probably apply to this too.
            relativeTo = relativeTo ?? Focused;

            var focusRect = relativeTo.GetRect(displayRect: true, context: this);
            (Control control, float distance) result = (null, 999999f);

            var context = FindTopLevelAncestor(relativeTo);
            Scan(context, ref result);
            
            void Scan (Control control, ref (Control control, float distance) closest) {
                if ((control is IControlContainer container) && IsValidContainerToSearchForFocusableControls(control)) {
                    foreach (var candidate in container.Children) {
                        if (candidate == relativeTo)
                            continue;

                        Scan(candidate, ref closest);

                        if (!candidate.IsValidFocusTarget || !candidate.AcceptsFocus)
                            continue;

                        var currentRect = candidate.GetRect(displayRect: true, context: this);
                        var displacement = currentRect.Center - focusRect.Center;
                        /*
                        var displacement = new Vector2(
                            currentRect.Left > focusRect.Right
                                ? currentRect.Left - focusRect.Right
                                : (currentRect.Right < focusRect.Left
                                    ? -(focusRect.Left - currentRect.Right)
                                    : 0),
                            currentRect.Top > focusRect.Bottom
                                ? currentRect.Top - focusRect.Bottom
                                : (currentRect.Bottom < focusRect.Top
                                    ? -(focusRect.Top - currentRect.Bottom)
                                    : 0)
                        );

                        // HACK: If controls are exactly neighbors, fake a small distance
                        if (currentRect.Left == focusRect.Right)
                            displacement.X = 0.1f;
                        else if (focusRect.Left == currentRect.Right)
                            displacement.X = -0.1f;
                        if (currentRect.Top == focusRect.Bottom)
                            displacement.Y = 0.1f;
                        else if (focusRect.Top == currentRect.Bottom)
                            displacement.Y = -0.1f;
                        */

                        if ((x != 0) && Math.Sign(displacement.X) != x)
                            continue;
                        if ((y != 0) && Math.Sign(displacement.Y) != y)
                            continue;
                        // We want to prefer controls that are close to aligned with the current one on the desired axis.
                        // We do this by amplifying the distance on the other axis  
                        float modifiedDistance = (displacement * new Vector2(x != 0 ? 1 : 2, y != 0 ? 1 : 2)).Length();
                        (Control control, float distance) current = (candidate, modifiedDistance);
                        if (current.distance < closest.distance)
                            closest = current;
                    }
                }
            }

            if (result.control != null) {
                if (TrySetFocus(result.control, isUserInitiated: isUserInitiated)) {
                    // HACK: Fixes tooltips from previous controls getting stuck during keyboard navigation
                    SetKeyboardSelection(result.control, isUserInitiated);
                    return true;
                }
            }

            return false;
        }
    }
}

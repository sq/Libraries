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
    public partial class UIContext : IDisposable {
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
            foreach (var topLevel in Controls) {
                if (!TopLevelFocusMemory.TryGetValue(topLevel, out Control memory))
                    continue;
                if (control == memory)
                    TopLevelFocusMemory.Remove(topLevel);
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

        private readonly Dictionary<Control, Control> InvalidFocusTargets = 
            new Dictionary<Control, Control>(new ReferenceComparer<Control>());

        private void DefocusInvalidFocusTargets () {
            while ((Focused != null) && !Focused.IsValidFocusTarget && InvalidFocusTargets.TryGetValue(Focused, out Control idealNewTarget)) {
                InvalidFocusTargets.Remove(Focused);
                var current = Focused;
                var ok = (idealNewTarget == null) && TrySetFocus(idealNewTarget);

                if (!ok) {
                    var interim = Focused;
                    idealNewTarget = PickIdealNewFocusTargetForInvalidFocusTarget(Focused);
                    if (!TrySetFocus(idealNewTarget)) {
                        // Log($"Could not move focus from invalid target {current}");
                        break;
                    } else
                        ; // Log($"Moved focus from invalid target {current} to {Focused} through {interim}");
                } else {
                    // Log($"Moved focus from invalid target {current} to {Focused}");
                }
            }
            InvalidFocusTargets.Clear();
        }

        private Control PickIdealNewFocusTargetForInvalidFocusTarget (Control control) {
            var fm = Focused as IModal;
            Control idealNewTarget = null;
            // FIXME: TopLevelFocused fixes some behaviors here but breaks others :(
            if ((fm?.FocusDonor != null) && Control.IsEqualOrAncestor(Focused, control))
                idealNewTarget = fm.FocusDonor;

            // Attempt to auto-shift focus as long as our parent chain is focusable
            if (
                !Control.IsRecursivelyTransparent(control, includeSelf: false) && 
                // We shouldn't auto-shift focus for modals since it's somewhat expensive and we don't
                //  want to focus a random top level control after they close
                !(control is IModal)
            )
                idealNewTarget = idealNewTarget ?? PickFocusableSiblingForRotation(control, 1, false, out bool temp);

            // Auto-shifting failed, so try to return to the most recently focused control
            idealNewTarget = idealNewTarget ?? PreviousFocused ?? PreviousTopLevelFocused;

            return idealNewTarget;
        }

        // Clean up when a control is removed in case it has focus or mouse capture,
        //  and attempt to return focus to the most recent place it occupied (for modals)
        public void NotifyControlBecomingInvalidFocusTarget (Control control, bool removed) {
            RemoveFromFocusMemory(control);

            if (PreviousFocused == control)
                PreviousFocused = null;
            if (PreviousTopLevelFocused == control)
                PreviousTopLevelFocused = null;
            if (Control.IsEqualOrAncestor(_MouseCaptured, control))
                MouseCaptured = null;

            InvalidFocusTargets[control] = 
                PickIdealNewFocusTargetForInvalidFocusTarget(control);

            if (Control.IsEqualOrAncestor(KeyboardSelection, control))
                ClearKeyboardSelection();

            if (PreviousFocused == control)
                PreviousFocused = null;
            if (PreviousTopLevelFocused == control)
                PreviousTopLevelFocused = null;

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

            if (topLevel) {
                var currentTopLevel = FindTopLevelAncestor(Focused);
                // HACK
                var inTabOrder = Controls.InTabOrder(FrameIndex, false)
                    .Where(c => 
                        (((c as IControlContainer)?.ChildrenAcceptFocus ?? false) || c.AcceptsFocus) &&
                        c.Enabled && !c.IsTransparent &&
                        !(c is FocusProxy)
                    )
                    .ToDenseList();
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

        private HashSet<Control> FocusSearchHistory = new HashSet<Control>();

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
                    newFocusTarget = value = PickFocusableSiblingForRotation(Focused, 1, false, out bool temp);
                else
                    newFocusTarget = value = Focused ?? Controls.FirstOrDefault();
            }

            FocusSearchHistory.Clear();

            // Top-level controls should pass focus on to their children if possible
            if (Controls.Contains(value)) {
                Control childTarget;
                if (
                    !TopLevelFocusMemory.TryGetValue(value, out childTarget) ||
                    (FindTopLevelAncestor(childTarget) == null) ||
                    Control.IsRecursivelyTransparent(childTarget)
                ) {
                    var container = value as IControlContainer;
                    if (IsValidContainerToSearchForFocusableControls(container))
                        childTarget = container.DefaultFocusTarget ?? container.Children.InTabOrder(FrameIndex, true).FirstOrDefault();
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
            if (previous != newFocusTarget)
                PreviousFocused = previous;

            var newTopLevelAncestor = FindTopLevelAncestor(newFocusTarget);
            var activeModal = ActiveModal;
            if (
                (activeModal?.RetainFocus == true) && 
                (newTopLevelAncestor != activeModal) && 
                ((Control)ActiveModal).Enabled &&
                !Control.IsRecursivelyTransparent(((Control)ActiveModal))
            )
                return false;

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

            if (_Focused != newFocusTarget) {
                if (isUserInitiated)
                    LastFocusChange = NowL;
                else // For instant changes (like focus donor transfers) suppress the animation, to avoid glitches
                    LastFocusChange = 0;
            }
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

        private bool IsValidContainerToSearchForFocusableControls (IControlContainer icc) {
            return IsValidContainerToSearchForFocusableControls(icc as Control);
        }

        private bool IsValidContainerToSearchForFocusableControls (Control control) {
            if (!(control is IControlContainer ic))
                return false;
            else if (!ic.ChildrenAcceptFocus)
                return false;
            return control.Enabled && control.Visible && !Control.IsRecursivelyTransparent(control);
        }
    }
}

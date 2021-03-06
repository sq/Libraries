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
            if (!Control.IsRecursivelyTransparent(control, includeSelf: false))
                idealNewTarget = PickNextFocusTarget(control, 1, true);
            else
                // Auto-shifting failed, so try to return to the most recently focused control
                idealNewTarget = PreviousFocused ?? PreviousTopLevelFocused;

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
                Focused = PickNextFocusTarget(null, 1, true);
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
                var newTarget = PickFocusableSibling(Focused, delta, null);
                var newTopLevel = FindTopLevelAncestor(newTarget);
                // HACK: We don't want to change top-level controls during a regular tab
                if ((newTopLevel != currentTopLevel) && (currentTopLevel != null) && Focused.IsValidFocusTarget)
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
                    newFocusTarget = value = PickNextFocusTarget(Focused, 1, false);
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
                        childTarget = container.Children.InTabOrder(FrameIndex, true).FirstOrDefault();
                }

                // HACK: If the focus is shifted to an invalid focus target but it's a container,
                //  attempt to recursively pick a suitable focus target inside it
                if (childTarget?.IsValidFocusTarget == false) {
                    var childContainer = (childTarget as IControlContainer);
                    if (IsValidContainerToSearchForFocusableControls(childContainer))
                        childTarget = FindFocusableSibling(childContainer.Children, null, 1, true);
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
                        var childTarget = FindFocusableSibling(collection.Children, null, 1, true);
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
            if ((activeModal?.RetainFocus == true) && (newTopLevelAncestor != activeModal) && ((Control)ActiveModal).IsValidFocusTarget)
                return false;

            if (previous != newFocusTarget) {
                FocusChain.Clear();
                var current = newFocusTarget;
                while (current != null) {
                    FocusChain.Add(current);
                    current.TryGetParent(out current);
                }
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
            if (!(control is IControlContainer))
                return false;
            return control.Enabled && control.Visible && !Control.IsRecursivelyTransparent(control);
        }

        public Control FindFocusableSibling (ControlCollection collection, Control current, int delta, bool recursive) {
            if (delta == 0)
                throw new ArgumentOutOfRangeException("delta");

            var tabOrdered = collection.InTabOrder(FrameIndex, false);
            if (tabOrdered.Count < 1)
                return null;

            int initialIndex = tabOrdered.IndexOf(current), newIndex, idx;
            if (initialIndex < 0)
                newIndex = (delta > 0 ? 0 : tabOrdered.Count - 1);
            else
                newIndex = initialIndex + delta;

            while (newIndex != initialIndex) {
                if (collection.Host == null)
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
                    } else if (recursive && IsValidContainerToSearchForFocusableControls(control)) {
                        var child = FindFocusableSibling(((IControlContainer)control).Children, null, delta, recursive);
                        if (child != null)
                            return child;
                    }
                }

                newIndex += delta;
                if (initialIndex < 0) {
                    if ((newIndex < 0) || (newIndex >= tabOrdered.Count))
                        break;
                }

                if (collection.Host == null)
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

            if (IsValidContainerToSearchForFocusableControls(current)) {
                var child = FindFocusableSibling(((IControlContainer)current).Children, null, delta, recursive);
                if (child != null)
                    return child;
            }

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
                if ((nextSibling != null) && (nextSibling != current)) {
                    var nextContainer = (nextSibling as IControlContainer);
                    if (nextContainer != null) {
                        var possibleResult = FindFocusableSibling(nextContainer.Children, null, delta, recursive);
                        if ((possibleResult != null) && (possibleResult != ineligible))
                            return possibleResult;
                    }

                    current = nextSibling;
                    if (current.Enabled && current.IsValidFocusTarget && !Control.IsRecursivelyTransparent(current))
                        return current;
                } else if (parent == null) {
                    break;
                } else {
                    current = parentCollection?.Host;
                }
            }

            return null;
        }
    }
}

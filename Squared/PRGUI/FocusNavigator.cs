using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Controls;
using Squared.Task;
using Squared.Util;

namespace Squared.PRGUI {
    public interface IFocusNavigator {
        bool TryPickRotateFocusTarget (
            UIContext context, Control from, 
            int delta, out Control result
        );

        bool TryMoveFocusDirectionally (
            UIContext context, Control container, Control from,
            int x, int y, out Control result
        );
    }

    public enum NavigationDirection {
        Up,
        Right,
        Down,
        Left
    }

    public interface ICustomDirectionalNavigation {
        Control GetDirectionalNavigationTarget (NavigationDirection direction);
    }

    public class DefaultFocusNavigator : IFocusNavigator {
        public static readonly DefaultFocusNavigator Instance = new ();

        public bool TryPickRotateFocusTarget (
            UIContext context, Control from, int delta, out Control result
        ) {
            result = null;
            from ??= context.Focused;

            // FIXME
            if (from == null)
                return false;

            var topLevel = !from.TryGetParent(out var parent);
            // FIXME: Introduce new system where we build a flattened 'focus target list' of the whole tree of focusable controls
            //  at a specified depth. I.e. for top-level we build the list of all top level focus targets only, and for non top-level
            //  we walk the whole tree recursively pushing controls into the list based on the traversal criteria.
            // This would also allow getting rid of TraverseChildren
            if (topLevel) {
                // HACK
                var inTabOrder = context.Controls.InTabOrder(context.FrameIndex, false)
                    .ToDenseList(where: c =>
                        (((c as IControlContainer)?.ChildrenAcceptFocus ?? false) || c.AcceptsFocus) &&
                        (c.Enabled || c.AcceptsFocusWhenDisabled) && !Control.IsRecursivelyTransparent(c, true, context.NowL, ignoreFadeIn: true) &&
                        (c is not FocusProxy)
                    );
                var currentIndex = inTabOrder.IndexOf(from);
                var newIndex = Arithmetic.Wrap(currentIndex + delta, 0, inTabOrder.Count - 1);
                result = inTabOrder[newIndex];
                return (result != from);
            } else {
                var currentTopLevel = context.FindTopLevelAncestor(from);
                var newTarget = context.PickFocusableSiblingForRotation(from, delta, null, out bool didFollowProxy);
                var newTopLevel = context.FindTopLevelAncestor(newTarget);

                // HACK: We don't want to change top-level controls during a regular tab
                if ((newTopLevel != currentTopLevel) && (currentTopLevel != null) && (from != null) && from.IsValidFocusTarget && !didFollowProxy)
                    return false;

                result = newTarget;
                return true;
            }
        }

        public bool TryMoveFocusDirectionally (
            UIContext context, Control container, Control from, 
            int x, int y, out Control _result
        ) {
            if (from is ICustomDirectionalNavigation icdn) {
                NavigationDirection nd = (NavigationDirection)(-1);
                if (x > 0)
                    nd = NavigationDirection.Right;
                else if (x < 0)
                    nd = NavigationDirection.Left;
                else if (y > 0)
                    nd = NavigationDirection.Down;
                else if (y < 0)
                    nd = NavigationDirection.Up;

                if (nd >= (NavigationDirection)0) {
                    _result = icdn.GetDirectionalNavigationTarget(nd);
                    if (_result != null)
                        return true;
                }
            }

            var focusRect = from.GetRect(displayRect: true, context: context);
            (Control control, float distance) result = (null, 999999f);

            Scan(container, ref result);
            
            void Scan (Control control, ref (Control control, float distance) closest) {
                if ((control is IControlContainer container) && UIContext.IsValidContainerToSearchForFocusableControls(control)) {
                    foreach (var candidate in container.Children) {
                        if (candidate == from)
                            continue;

                        Scan(candidate, ref closest);

                        if (!candidate.IsValidFocusTarget || !candidate.AcceptsFocus)
                            continue;

                        var currentRect = candidate.GetRect(displayRect: true, context: context);
                        var displacement = currentRect.Center - focusRect.Center;

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

            _result = result.control;
            return (_result != null) && (_result != from);
        }
    }
}

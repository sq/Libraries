using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Input;
using Squared.Util;

namespace Squared.PRGUI {
    public sealed partial class UIContext : IDisposable {
        public struct TraversalInfo {
            public Control Control, RedirectTarget;
            public IControlContainer Container;

            public ControlCollection Children => Container?.Children;

            public bool IsProxy => (Control is FocusProxy);
            public bool ContainsChildren => (Container != null) && (Container.Children.Count > 0);
        }

        public struct TraverseSettings {
            public bool AllowDescend, AllowAscend, StartWithDefault;
            // HACK: Will default to true for Window and false for everything else
            public bool? AllowWrap;
            public int Direction;
            public Func<Control, bool> Predicate;
            public bool FollowProxies;

            internal int FrameIndex;

            public bool[] DidFollowProxy;
        }

        private TraversalInfo Traverse_MakeInfo (Control control) {
            return new TraversalInfo {
                Control = control,
                RedirectTarget = control.FocusBeneficiary,
                Container = (control as IControlContainer)
            };
        }

        private bool Traverse_CanDescend (ref TraversalInfo info, ref TraverseSettings settings) {
            if (!settings.AllowDescend)
                return false;
            if (!info.ContainsChildren)
                return false;
            if (!info.Control.Enabled)
                return false;
            // FIXME: Optimize this check
            if (Control.IsRecursivelyTransparent(info.Control, true))
                return false;

            return true;
        }

        private Control FindFocusableChildOfDefaultFocusTarget (Control defaultFocusTarget, TraverseSettings settings) {
            if (defaultFocusTarget.IsValidFocusTarget)
                return defaultFocusTarget;
            else if ((defaultFocusTarget is IControlContainer icc) && icc.ChildrenAcceptFocus) {
                TraverseChildren(icc.Children, ref settings, out TraverseChildrenEnumerable enumerable);
                return enumerable.FirstOrDefault().Control;
            } else
                return null;
        }

        private TraverseChildrenEnumerable TraverseChildren (ControlCollection collection, ref TraverseSettings settings) {
            TraverseChildren(collection, ref settings, out TraverseChildrenEnumerable result);
            return result;
        }

        private void TraverseChildren (ControlCollection collection, ref TraverseSettings settings, out TraverseChildrenEnumerable result) {
	        result = new TraverseChildrenEnumerable();
		    result.state = -2;
	        result.context = this;
	        result.collection = collection;
	        result.settings = settings;
        }

        private IEnumerable<TraversalInfo> _TraverseChildren (ControlCollection collection, TraverseSettings settings) {
            if (collection.Count <= 0)
                yield break;

            int i = (settings.Direction > 0) ? 0 : collection.Count - 1;
            var tabOrdered = collection.InTabOrder(FrameIndex, false);
            var icc = collection.Host as IControlContainer;
            if (settings.StartWithDefault && (icc?.DefaultFocusTarget != null)) {
                var dft = icc.DefaultFocusTarget;
                if (Control.IsEqualOrAncestor(dft, collection.Host)) {
                    var actualTarget = FindFocusableChildOfDefaultFocusTarget(dft, settings);
                    if (actualTarget != null) {
                        var info = Traverse_MakeInfo(actualTarget);
                        if ((settings.Predicate == null) || settings.Predicate(info.Control))
                            yield return info;
                    }
                }

                var defaultIndex = tabOrdered.IndexOf(dft);
                if (defaultIndex >= 0)
                    i = defaultIndex;
            }

            while (true) {
                if ((i < 0) || (i >= tabOrdered.Count))
                    break;

                var child = tabOrdered[i];
                var info = Traverse_MakeInfo(child);

                if ((settings.Predicate == null) || settings.Predicate(child))
                    yield return info;

                if (info.IsProxy && settings.FollowProxies)
                    yield break;

                if (Traverse_CanDescend(ref info, ref settings)) {
                    foreach (var subchild in TraverseChildren(info.Container.Children, ref settings))
                        yield return subchild;
                }

                i += settings.Direction;
            }
        }

        private void SearchForSiblings (ControlCollection collection, Control startingPosition, ref TraverseSettings settings, out SearchForSiblingsEnumerable result) {
	        result = new SearchForSiblingsEnumerable();
            result.state = -2;
	        result.context = this;
	        result.collection = collection;
	        result.startingPosition = startingPosition;
	        result.settings = settings;
        }

        private IEnumerable<TraversalInfo> _SearchForSiblings (ControlCollection collection, Control startingPosition, TraverseSettings settings) {
            if (startingPosition == null)
                throw new ArgumentNullException(nameof(startingPosition));

            var visitedProxyTargets = new DenseList<Control>();
            var descendSettings = settings;
            descendSettings.AllowAscend = false;
            // FIXME
            // descendSettings.FollowProxies = false;
            var currentCollection = collection ?? ((startingPosition as IControlContainer)?.Children);
            if (currentCollection == null) {
                if (!startingPosition.TryGetParent(out Control startingParent) || !(startingParent is IControlContainer icc))
                    yield break;

                currentCollection = icc.Children;
            }

            var currentStartingPosition = startingPosition;
            var didFollowProxy = false;
            while (true) {
                var tabOrdered = currentCollection.InTabOrder(FrameIndex, false);
                int motion = didFollowProxy ? 0 : settings.Direction, 
                    index = tabOrdered.IndexOf(currentStartingPosition), 
                    i = index + motion;
                Control proxyTarget = null;

                while (true) {
                    // FIXME: Wrap
                    if ((i < 0) || (i >= tabOrdered.Count))
                        break;

                    var child = tabOrdered[i];
                    var info = Traverse_MakeInfo(child);

                    if (
                        info.IsProxy && settings.FollowProxies && info.Control.Enabled &&
                        (visitedProxyTargets.IndexOf(info.RedirectTarget) < 0)
                    ) {
                        proxyTarget = info.RedirectTarget;
                        visitedProxyTargets.Add(proxyTarget);
                        break;
                    } else if ((settings.Predicate == null) || settings.Predicate(child)) {
                        yield return info;
                    }

                    if (Traverse_CanDescend(ref info, ref settings)) {
                        foreach (var subchild in TraverseChildren(info.Container.Children, ref descendSettings)) {
                            if (subchild.IsProxy && settings.FollowProxies && (visitedProxyTargets.IndexOf(subchild.RedirectTarget) < 0)) {
                                proxyTarget = info.RedirectTarget;
                                visitedProxyTargets.Add(proxyTarget);
                                break;
                            }

                            yield return subchild;
                        }

                        if (proxyTarget != null)
                            break;
                    }

                    i += settings.Direction;
                    if (i == index)
                        break;
                }

                if (proxyTarget != null) {
                    currentStartingPosition = proxyTarget;
                    didFollowProxy = true;
                    settings.DidFollowProxy[0] = true;
                } else {
                    currentStartingPosition = currentCollection.Host;
                    didFollowProxy = false;
                    if (!settings.AllowAscend)
                        break;
                }

                if (!currentStartingPosition.TryGetParent(out Control parent))
                    break;

                if (!(parent is IControlContainer parentContainer))
                    break;

                currentCollection = parentContainer.Children;
            }
        }

        private bool _FocusablePredicate (Control control) => control.IsValidFocusTarget;
        private bool _RotatablePredicate (Control control) => control.EligibleForFocusRotation;

        Func<Control, bool> FocusablePredicate, RotatablePredicate;

        private TraverseSettings MakeSettingsForPick (Control container, int direction) {
            return new TraverseSettings {
                AllowDescend = true,
                AllowAscend = false,
                AllowWrap = false,
                StartWithDefault = true,
                Direction = direction,
                // FIXME: Maybe do this?
                FollowProxies = false,
                FrameIndex = FrameIndex,
                Predicate = FocusablePredicate ?? (FocusablePredicate = _FocusablePredicate)
            };
        }

        public Control FindChild (Control container, Func<Control, bool> predicate, int direction = 1) {
            var settings = MakeSettingsForPick(container, direction);
            settings.Predicate = predicate;
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            return TraverseChildren(collection, ref settings).FirstOrDefault().Control;
        }

        public DenseList<Control> FindFocusableChildren (Control container, int direction = 1, int limit = int.MaxValue) {
            var settings = MakeSettingsForPick(container, direction);
            // FIXME: Handle cases where the control isn't a container
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            // DebugLog($"Finding focusable child in {container} in direction {direction}");
            var result = new DenseList<Control>();
            foreach (var ti in TraverseChildren(collection, ref settings)) {
                if (limit-- <= 0)
                    break;
                result.Add(ti.Control);
            }
            return result;
        }

        public DenseList<T> FindFocusableChildrenOfType<T> (Control container, int direction = 1, int limit = int.MaxValue)
            where T : Control
        {
            var settings = MakeSettingsForPick(container, direction);
            // FIXME: Handle cases where the control isn't a container
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            // DebugLog($"Finding focusable child in {container} in direction {direction}");
            var result = new DenseList<T>();
            foreach (var ti in TraverseChildren(collection, ref settings)) {
                if (limit <= 0)
                    break;

                if (ti.Control is T tc) {
                    result.Add(tc);
                    limit--;
                }
            }
            return result;
        }

        public Control PickFocusableChild (Control container, int direction = 1) {
            var settings = MakeSettingsForPick(container, direction);
            // FIXME: Handle cases where the control isn't a container
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            // DebugLog($"Finding focusable child in {container} in direction {direction}");
            return TraverseChildren(collection, ref settings).FirstOrDefault().Control;
        }

        private bool[] _ScratchPickForRotationCell;

        public Control PickFocusableSiblingForRotation (Control child, int direction, bool? allowLoop, out bool didFollowProxy) {
            var cell = Interlocked.Exchange(ref _ScratchPickForRotationCell, null);
            cell = cell ?? new bool[1];
            cell[0] = false;

            var settings = new TraverseSettings {
                StartWithDefault = false,
                DidFollowProxy = cell,
                AllowDescend = true,
                AllowAscend = true,
                AllowWrap = allowLoop,
                Direction = direction,
                FollowProxies = true,
                // FIXME: Prevent top level rotate here?
                FrameIndex = FrameIndex,
                Predicate = RotatablePredicate ?? (RotatablePredicate = _RotatablePredicate)
            };

            // DebugLog($"Finding sibling for {child} in direction {direction}");
            SearchForSiblings(null, child, ref settings, out SearchForSiblingsEnumerable enumerable);
            var result = enumerable.FirstOrDefault().Control;
            didFollowProxy = cell[0];

            Interlocked.CompareExchange(ref _ScratchPickForRotationCell, cell, null);
            return result;
        }
    }
}

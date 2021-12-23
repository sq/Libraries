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
    public partial class UIContext : IDisposable {
        public struct TraversalInfo {
            public Control Control, RedirectTarget;
            public IControlContainer Container;

            public ControlCollection Children => Container?.Children;

            public bool IsProxy => (Control is FocusProxy);
            public bool ContainsChildren => (Container != null) && (Container.Children.Count > 0);
        }

        public struct TraverseSettings {
            public bool AllowDescend, AllowDescendIfDisabled, AllowDescendIfInvisible, AllowAscend, StartWithDefault;
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
            if (!settings.AllowDescendIfDisabled && !info.Control.Enabled)
                return false;
            // FIXME: Optimize this check
            if (!settings.AllowDescendIfInvisible && Control.IsRecursivelyTransparent(info.Control, true))
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

        // this sucks
        /*
        private struct SearchForSiblingsEnumerable : IEnumerable<TraversalInfo> {
            private UIContext Context;
            private ControlCollection Collection;
            private Control StartingPosition;
            private TraverseSettings Settings;

            public SearchForSiblingsEnumerable (UIContext context, ControlCollection collection, Control startingPosition, TraverseSettings settings) {
                Context = context;
                Collection = collection;
                StartingPosition = startingPosition;
                Settings = settings;
            }

            public SearchForSiblingsEnumerator GetEnumerator () {
                return new SearchForSiblingsEnumerator(Context, Collection, StartingPosition, Settings);
            }

            IEnumerator<TraversalInfo> IEnumerable<TraversalInfo>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }

        private struct SearchForSiblingsEnumerator : IEnumerator<TraversalInfo> {
            private UIContext Context;
            private ControlCollection Collection, CurrentCollection;
            private Control StartingPosition, CurrentStartingPosition, ProxyTarget;
            private TraverseSettings Settings, DescendSettings;
            // FIXME: Reuse this to prevent allocation if it gets big
            private DenseList<Control> VisitedProxyTargets;
            private bool IsFirstStep, Halted, DidFollowProxy;
            private List<Control> TabOrdered;
            private int Position, OuterIndex, InnerStep;
            private IEnumerator<TraversalInfo> DescendEnumerator;
            private TraversalInfo Info;

            public SearchForSiblingsEnumerator (UIContext context, ControlCollection collection, Control startingPosition, TraverseSettings settings) {
                Context = context;
                Collection = collection;
                StartingPosition = startingPosition;
                Settings = settings;
                DescendSettings = settings;
                DescendSettings.AllowAscend = false;
                VisitedProxyTargets = default;
                Current = default;
                ProxyTarget = default;
                IsFirstStep = true;
                CurrentCollection = collection ?? ((startingPosition as IControlContainer)?.Children);
                CurrentStartingPosition = startingPosition;
                Halted = false;
                DidFollowProxy = false;
                TabOrdered = default;
                Position = -1;
                InnerStep = -1;
                OuterIndex = -1;
                Info = default;
                DescendEnumerator = default;

                if (CurrentCollection == null) {
                    if (!startingPosition.TryGetParent(out Control startingParent) || !(startingParent is IControlContainer icc)) {
                        Halted = true;
                        return;
                    }

                    CurrentCollection = icc.Children;
                }
            }

            public TraversalInfo Current { get; private set; }
            object IEnumerator.Current => Current;

            public void Dispose () {
                Halted = true;
            }

            public bool MoveNext () {
                if (IsFirstStep) {
                    IsFirstStep = false;
                    return !Halted;
                } else {
                    var result = Iterate();
                    if (!result)
                        Halted = true;
                    return result;
                }
            }

            private bool IterateOuter () {
                TabOrdered = CurrentCollection.InTabOrder(Context.FrameIndex, false);
                int motion = DidFollowProxy ? 0 : Settings.Direction;
                OuterIndex = TabOrdered.IndexOf(CurrentStartingPosition);
                Position = OuterIndex + motion;
                InnerStep = -1;

                while (InnerStep < 0) {
                    if (!IterateInner())
                        return false;

                    if (InnerStep < 0) {
                        if (ProxyTarget != null) {
                            CurrentStartingPosition = ProxyTarget;
                            DidFollowProxy = true;
                            Settings.DidFollowProxy[0] = true;
                        } else {
                            CurrentStartingPosition = CurrentCollection.Host;
                            DidFollowProxy = false;
                            if (!Settings.AllowAscend)
                                return false;
                        }

                        if (!CurrentStartingPosition.TryGetParent(out Control parent))
                            return false;

                        if (!(parent is IControlContainer parentContainer))
                            return false;

                        CurrentCollection = parentContainer.Children;
                    }
                }

                return true;
            }

            private bool IterateInner () {
                switch (InnerStep) {
                    case 0: {
                        if ((Position < 0) || (Position >= TabOrdered.Count)) {
                            // FIXME: Wrap
                            InnerStep = -1;
                            return true;
                        }

                        var child = TabOrdered[Position];
                        Info = Context.Traverse_MakeInfo(child);

                        if (
                            Info.IsProxy && Settings.FollowProxies && Info.Control.Enabled &&
                            (VisitedProxyTargets.IndexOf(Info.RedirectTarget) < 0)
                        ) {
                            ProxyTarget = Info.RedirectTarget;
                            VisitedProxyTargets.Add(ProxyTarget);
                            InnerStep = -1;
                            return true;
                        } else if ((Settings.Predicate == null) || Settings.Predicate(child)) {
                            Current = Info;
                            InnerStep = 1;
                            return true;
                        }
                    }
                    break;
                    case 1: {
                        if (Context.Traverse_CanDescend(ref Info, ref Settings)) {
                            InnerStep = 2;
                            DescendEnumerator = Context.TraverseChildren(Info.Container.Children, DescendSettings).GetEnumerator();
                            
                            while (InnerStep == 2) {
                                if (!IterateDescend())
                                    return false;
                            }
                        }

                        Position += Settings.Direction;
                        if (Position == OuterIndex)
                            InnerStep = -1;
                    }
                    break;
                    case 2: {
                        while (InnerStep == 2) {
                            if (!IterateDescend())
                                return false;
                        }
                    }
                    break;
                    case 3: {
                        if (!IterateDescend())
                            return false;
                    }
                    break;
                    case 4: {
                        if (ProxyTarget != null)
                            InnerStep = 4;
                        InnerStep = -1;
                        return true;
                    }
                    break;
                }
            }

            private bool IterateDescend () {
                if (!DescendEnumerator.MoveNext()) {
                    // FIXME: Is this right?
                    InnerStep = -1;
                    return true;
                }

                var subchild = DescendEnumerator.Current;
                    
                if (subchild.IsProxy && Settings.FollowProxies && (VisitedProxyTargets.IndexOf(subchild.RedirectTarget) < 0)) {
                    ProxyTarget = Info.RedirectTarget;
                    VisitedProxyTargets.Add(ProxyTarget);
                    // FIXME: Is this right? It was 'break' before
                    InnerStep = -1;
                    return true;
                }

                Current = subchild;
                InnerStep = 4;
                return true;
            }

            private bool Iterate () {
                if (Halted)
                    throw new InvalidOperationException();

                if (InnerStep < 0)
                    return IterateOuter();
                else
                    return IterateInner();
            }

            public void Reset () {
                throw new NotImplementedException();
            }
        }

        private SearchForSiblingsEnumerable SearchForSiblings (ControlCollection collection, Control startingPosition, TraverseSettings settings) {
            if (startingPosition == null)
                throw new ArgumentNullException(nameof(startingPosition));

            return new SearchForSiblingsEnumerable(this, collection, startingPosition, settings);
        }

        */

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

            ;
        }

        private bool _FocusablePredicate (Control control) => control.IsValidFocusTarget;
        private bool _RotatablePredicate (Control control) => control.EligibleForFocusRotation;

        Func<Control, bool> FocusablePredicate, RotatablePredicate;

        private TraverseSettings MakeSettingsForPick (Control container, int direction) {
            return new TraverseSettings {
                AllowDescend = true,
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
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

        public IEnumerable<Control> FindFocusableChildren (Control container, int direction = 1) {
            var settings = MakeSettingsForPick(container, direction);
            // FIXME: Handle cases where the control isn't a container
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            // DebugLog($"Finding focusable child in {container} in direction {direction}");
            return TraverseChildren(collection, ref settings).Controls;
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
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
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

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
        public struct TraversalInfo {
            public Control Control, RedirectTarget;
            public IControlContainer Container;

            public ControlCollection Children => Container?.Children;

            public bool IsProxy => (Control is FocusProxy);
            public bool ContainsChildren => (Container != null) && (Container.Children.Count > 0);
        }

        public struct TraverseSettings {
            public bool AllowDescend, AllowDescendIfDisabled, AllowDescendIfInvisible, AllowAscend;
            // HACK: Will default to true for Window and false for everything else
            public bool? AllowWrap;
            // public bool FollowProxies;
            public int Direction;
            public Func<Control, bool> Predicate;

            internal int FrameIndex;
        }

        // FIXME: What a nightmare
        /*
        public struct TraversalEnumerator : IEnumerator<TraversalInfo> {
            private struct StackEntry {
                public int Position;
                public ControlCollection Collection;
            }

            ControlCollection StartingCollection;
            Control StartingPoint;
            TraverseSettings Settings;
            
            bool IsDisposed, IsInitialized, IsAtEnd, DescendPending;
            List<Control> TabOrdered;
            ControlCollection SearchCollection;
            int Position;
            DenseList<StackEntry> SearchStack;
            DenseList<Control> RedirectsFollowed;

            public TraversalInfo Current { get; private set; }
            object IEnumerator.Current => Current;

            internal TraversalEnumerator (
                ControlCollection startingCollection, 
                Control startingPoint,
                ref TraverseSettings settings
            ) {
                if ((settings.Direction != 1) && (settings.Direction != -1))
                    throw new ArgumentOutOfRangeException("settings.Direction");

                StartingPoint = startingPoint;
                StartingCollection = startingCollection;
                Settings = settings;
                IsDisposed = false;
                IsInitialized = false;
                IsAtEnd = false;
                Position = int.MinValue;
                Current = default(TraversalInfo);
                SearchStack = default(DenseList<StackEntry>);
                RedirectsFollowed = default(DenseList<Control>);
                SearchCollection = null;
                TabOrdered = null;
                DescendPending = false;
            }

            public void Dispose () {
                Reset();
                IsDisposed = true;
            }

            private void Push (ControlCollection newCollection) {
                SearchStack.Add(new StackEntry {
                    Position = Position,
                    Collection = SearchCollection,
                });
                Position = Settings.Direction > 0
                    ? -1
                    : SearchCollection.Count;
                SetSearchCollection(newCollection);
            }

            private bool TryPop () {
                if (SearchStack.Count <= 0)
                    return false;
                var item = SearchStack.LastOrDefault();
                SearchStack.RemoveTail(1);
                if ((item.Position == Position) && (item.Collection == SearchCollection))
                    throw new Exception();
                SearchCollection = item.Collection;
                TabOrdered = SearchCollection.InTabOrder(Settings.FrameIndex, false);
                SetCurrent(item.Position);
                return true;
            }

            private void SetCurrent (Control control, Control initial = null) {
                var index = (control == null)
                    ? -1
                    // HACK: The control may have been removed from the container since the tab ordered list
                    //  was last updated.
                    : TabOrdered.IndexOf(control);
                if (index < 0) {
                    SetCurrent(
                        Settings.Direction > 0
                            ? 0
                            : TabOrdered.Count - 1, initial
                    );
                } else {
                    SetCurrent(index, initial ?? control);
                }
            }

            private void SetCurrent (int position, Control initial = null) {
                Position = position;
                var control = (position == -1) 
                    ? SearchCollection.Host
                    : TabOrdered[position];
                Current = new TraversalInfo {
                    Control = control,
                    RedirectTarget = control.FocusBeneficiary,
                    Container = control as IControlContainer,
                    Index = position
                };

                Trace($"SetCurrent idx={position} ctl={control}");

                if (!Settings.FollowProxies || !Current.IsProxy)
                    return;

                // FIXME: Detect cycles
                var rt = Current.RedirectTarget;
                if (rt == initial)
                    throw new Exception($"Found cycle when following redirects involving {control} and {initial}");
                if (RedirectsFollowed.IndexOf(rt) >= 0)
                    return;
                RedirectsFollowed.Add(rt);
                SetCurrent(rt, initial ?? control);
            }

            private void SetSearchCollection (ControlCollection collection) {
                SearchCollection = collection;
                TabOrdered = SearchCollection.InTabOrder(Settings.FrameIndex, false);
            }

            private void SetSearchContainer (IControlContainer container) {
                SearchCollection = container.Children;
                TabOrdered = SearchCollection.InTabOrder(Settings.FrameIndex, false);
            }

            private bool AdvanceInward () {
                if (!Current.ContainsChildren)
                    return false;
                if (!Settings.AllowDescend)
                    return false;
                if (!Settings.AllowDescendIfDisabled && !Current.Control.Enabled)
                    return false;
                if (!Settings.AllowDescendIfInvisible && Control.IsRecursivelyTransparent(Current.Control, true))
                    return false;
                if (Position < 0)
                    throw new InvalidOperationException();

                var prev = Current;
                var cc = Current.Children;
                Trace($"Climb down into {Current.Control}");
                Push(cc);

                if (!AdvanceLaterally(false)) {
                    if (!TryPop())
                        throw new Exception();

                    bool yieldAfterPop = false; // FIXME
                    if (yieldAfterPop) {
                        Trace($"Lateral advance failed so popping and yielding");
                        return true;
                    } else {
                        Trace($"Lateral advance failed so popping");
                    }

                    Current = prev;
                    return false;
                }

                return true;
            }

            private bool AdvanceOutward (out bool result) {
                result = false;
                if (Current.Control == Settings.AscendNoFurtherThan)
                    return false;

                if (TryPop()) {
                    bool yieldAfterPop = false; // FIXME
                    if (yieldAfterPop) {
                        Trace($"Climb back up to {Current.Control} and yield");
                        return true;
                    } else {
                        Trace($"Climb back up to {Current.Control} and advance");
                        // We previously descended so we're climbing back out
                        result = AdvanceLaterally(true);
                        return true;
                    }
                } else {
                    // Climbing beyond our start point
                    var parent = SearchCollection.Host;
                    if (parent == null)
                        return false;
                    if (!parent.TryGetParent(out Control superParent))
                        return false;
                    var icc = superParent as IControlContainer;
                    if (icc == null)
                        return false;
                    Trace($"Climb out into {superParent}");
                    SetSearchCollection(icc.Children);
                    SetCurrent(parent);
                    result = AdvanceLaterally(false);
                    return true;
                }
            }

            private bool AdvanceLaterally (bool allowAscend) {
                int currentIndex = Position,
                    direction = Settings.Direction,
                    newIndex;

                var prev = Current.Control;
                var minIndex = ((StartingPoint == prev) || (SearchCollection.Host == null))
                    ? 0
                    : -1;

                if (currentIndex >= -1) {
                    newIndex = currentIndex + direction;
                    if ((newIndex < minIndex) || (newIndex >= TabOrdered.Count)) {
                        var prevTo = TabOrdered;
                        bool outwardResult = false;
                        var didAscend = allowAscend && AdvanceOutward(out outwardResult);
                        if (!didAscend) {
                            if (prevTo != TabOrdered)
                                throw new Exception();
                            // HACK
                            if (Settings.AllowLoop ?? (SearchCollection.Host is Window))
                                newIndex = Arithmetic.Wrap(newIndex, 0, TabOrdered.Count - 1);
                            else
                                return false;
                        } else {
                            return outwardResult;
                        }
                    }
                } else if (direction > 0) {
                    newIndex = minIndex;
                } else {
                    newIndex = TabOrdered.Count - 1;
                }

                if (newIndex == currentIndex)
                    return false;

                SetCurrent(newIndex);
                DescendPending = Current.ContainsChildren && ((Position >= 0) || (Current.Control == StartingPoint));
                Trace($"Lateral movement from [{currentIndex}] {prev} to [{Position}] {Current.Control}");
                return true;
            }

            // FIXME: Prevent infinite loops
            private bool MoveNextImpl () {
                if (DescendPending) {
                    DescendPending = false;
                    if (AdvanceInward())
                        return true;
                }

                if (!AdvanceLaterally(true)) {
                    AdvanceOutward(out bool outwardResult);
                    return outwardResult;
                }

                return true;
            }

            private bool MoveFirstImpl () {
                if (SearchStack.Count > 0)
                    throw new Exception();

                if ((StartingPoint == null) && (StartingCollection != null)) {
                    SetSearchCollection(StartingCollection);
                    Position = -1;
                    DescendPending = false;
                    return MoveNextImpl();
                }

                if (!StartingPoint.TryGetParent(out Control parent)) {
                    SetSearchCollection(StartingPoint.Context.Controls);
                } else if (parent is IControlContainer icc) {
                    SetSearchContainer(icc);
                } else {
                    throw new Exception("Found no valid parent to search in");
                }

                SetCurrent(StartingPoint);
                DescendPending = true;
                // FIXME
                return MoveNextImpl();
            }

            public bool MoveNext () {
                if (IsDisposed)
                    throw new ObjectDisposedException("TraversalEnumerator");
                else if (IsAtEnd)
                    throw new InvalidOperationException("End of traversal");

                bool result;
                if (!IsInitialized)
                    result = MoveFirstImpl();
                else
                    result = MoveNextImpl();

                if (!result)
                    IsAtEnd = true;
                else
                    IsInitialized = true;

                return result;
            }

            public void Reset () {
                IsAtEnd = false;
                IsInitialized = false;
                Current = default(TraversalInfo);
                SearchStack.Clear();
                SearchCollection = null;
            }

            // [System.Diagnostics.Conditional("FOCUS_TRACE")]
            private static void Trace (string text) {
                Console.WriteLine(text);
            }
        }

        public struct TraversalEnumerable : IEnumerable<TraversalInfo> {
            public Control StartingPoint;
            public TraverseSettings Settings;

            public TraversalEnumerator GetEnumerator () {
                return new TraversalEnumerator(null, StartingPoint, ref Settings);
            }

            IEnumerator<TraversalInfo> IEnumerable<TraversalInfo>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }

        */

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

        private IEnumerable<TraversalInfo> TraverseChildren (ControlCollection collection, TraverseSettings settings) {
            if (collection.Count <= 0)
                yield break;

            int i = (settings.Direction > 0) ? 0 : collection.Count - 1;

            while (true) {
                var child = collection[i];
                var info = Traverse_MakeInfo(child);
                if ((settings.Predicate == null) || settings.Predicate(child))
                    yield return info;

                if (Traverse_CanDescend(ref info, ref settings)) {
                    foreach (var subchild in TraverseChildren(info.Container.Children, settings))
                        yield return subchild;
                }

                i += settings.Direction;
                if ((i < 0) || (i >= collection.Count))
                    break;
            }
        }

        private IEnumerable<TraversalInfo> SearchForSiblings (ControlCollection collection, Control startingPosition, TraverseSettings settings) {
            if (startingPosition == null)
                throw new ArgumentNullException(nameof(startingPosition));

            var descendSettings = settings;
            descendSettings.AllowAscend = false;
            var currentCollection = collection;
            if (currentCollection == null) {
                if (!startingPosition.TryGetParent(out Control startingParent) || !(startingParent is IControlContainer icc))
                    throw new ArgumentNullException(nameof(collection));
                currentCollection = icc.Children;
            }

            var currentStartingPosition = startingPosition;
            while (true) {
                int index = currentCollection.IndexOf(currentStartingPosition), i = index + settings.Direction;

                while (true) {
                    // FIXME: Wrap
                    if ((i < 0) || (i >= currentCollection.Count) || (i == index))
                        break;

                    var child = currentCollection[i];
                    var info = Traverse_MakeInfo(child);
                    if ((settings.Predicate == null) || settings.Predicate(child))
                        yield return info;

                    if (Traverse_CanDescend(ref info, ref settings)) {
                        foreach (var subchild in TraverseChildren(info.Container.Children, descendSettings))
                            yield return subchild;
                    }

                    i += settings.Direction;
                }

                if (!settings.AllowAscend)
                    break;

                currentStartingPosition = currentCollection.Host;
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

        public Control PickFocusableChild (Control container, int direction = 1) {
            var settings = new TraverseSettings {
                AllowDescend = true,
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
                AllowAscend = false,
                AllowWrap = false,
                Direction = direction,
                // FIXME: Maybe do this?
                // FollowProxies = false,
                FrameIndex = FrameIndex,
                Predicate = FocusablePredicate ?? (FocusablePredicate = _FocusablePredicate)
            };
            // FIXME: Handle cases where the control isn't a container
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            DebugLog($"Finding focusable child in {container} in direction {direction}");
            return TraverseChildren(collection, settings).FirstOrDefault().Control;
        }

        public Control PickFocusableSiblingForRotation (Control child, int direction, bool? allowLoop) {
            var settings = new TraverseSettings {
                AllowDescend = true,
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
                AllowAscend = true,
                AllowWrap = allowLoop,
                Direction = direction,
                // FIXME
                // FollowProxies = true,
                // FIXME: Prevent top level rotate here?
                FrameIndex = FrameIndex,
                Predicate = RotatablePredicate ?? (RotatablePredicate = _RotatablePredicate)
            };

            DebugLog($"Finding sibling for {child} in direction {direction}");
            return SearchForSiblings(null, child, settings).FirstOrDefault().Control;
        }
    }
}

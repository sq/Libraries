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
            public bool AllowDescend, AllowLoop, 
                AllowDescendIfDisabled, AllowDescendIfInvisible;
            public bool FollowProxies;
            public Control AscendNoFurtherThan;
            public int Direction;
        }

        public struct TraversalEnumerator : IEnumerator<TraversalInfo> {
            private struct StackEntry {
                public Control Control;
                public ControlCollection Collection;
            }

            IControlContainer StartingContainer;
            Control StartingPoint;
            TraverseSettings Settings;

            bool IsDisposed, IsInitialized, IsAtEnd;
            ControlCollection SearchCollection;
            DenseList<StackEntry> SearchStack;

            public TraversalInfo Current { get; private set; }
            TraversalInfo IEnumerator<TraversalInfo>.Current => Current;
            object IEnumerator.Current => Current;

            internal TraversalEnumerator (
                IControlContainer startingContainer, 
                Control startingPoint, 
                ref TraverseSettings settings
            ) {
                if ((settings.Direction != 1) && (settings.Direction != -1))
                    throw new ArgumentOutOfRangeException("settings.Direction");

                StartingPoint = startingPoint;
                StartingContainer = startingContainer;
                Settings = settings;
                IsDisposed = false;
                IsInitialized = false;
                IsAtEnd = false;
                Current = default(TraversalInfo);
                SearchStack = default(DenseList<StackEntry>);
                SearchCollection = null;
            }

            public void Dispose () {
                Reset();
                IsDisposed = true;
            }

            private void Push () {
                SearchStack.Add(new StackEntry {
                    Control = Current.Control,
                    Collection = SearchCollection
                });
            }

            private bool TryPop () {
                if (SearchStack.Count <= 0)
                    return false;
                var item = SearchStack.LastOrDefault();
                SearchStack.RemoveTail(1);
                if (item.Control == Current.Control)
                    throw new Exception();
                SearchCollection = item.Collection;
                SetCurrent(item.Control);
                return true;
            }

            private bool MoveFirstImpl () {
                if (SearchStack.Count > 0)
                    throw new Exception();
                if ((StartingPoint == null) && (StartingContainer != null)) {
                    SetSearchContainer(StartingContainer);
                } else if (!StartingPoint.TryGetParent(out Control parent)) {
                    SetSearchCollection(StartingPoint.Context.Controls);
                } else if (parent is IControlContainer icc) {
                    SetSearchContainer(icc);
                } else {
                    throw new Exception("Found no valid parent to search in");
                }

                SetCurrent(StartingPoint);
                return MoveNextImpl();
            }

            private void SetCurrent (Control control) {
                var newTarget = control;
                while (true) {
                    Current = new TraversalInfo {
                        Control = newTarget,
                        RedirectTarget = newTarget?.FocusBeneficiary,
                        Container = newTarget as IControlContainer
                    };
                    if (!Settings.FollowProxies || !Current.IsProxy)
                        break;
                    newTarget = Current.RedirectTarget;
                    if (newTarget == control)
                        throw new Exception($"Found cycle in focus redirect chain involving {Current.Control} and {newTarget}");
                }
            }

            private void SetSearchCollection (ControlCollection collection) {
                SearchCollection = collection;
            }

            private void SetSearchContainer (IControlContainer container) {
                SearchCollection = container.Children;
            }

            private bool AdvanceInward () {
                if (!Current.ContainsChildren)
                    throw new InvalidOperationException();
                if (!Settings.AllowDescend)
                    return false;
                if (!Settings.AllowDescendIfDisabled && !Current.Control.Enabled)
                    return false;
                if (!Settings.AllowDescendIfInvisible && Control.IsRecursivelyTransparent(Current.Control, true))
                    return false;

                var prev = Current;
                var cc = Current.Children;
                Console.WriteLine($"Climb down into {Current.Control}");
                Push();
                SetSearchCollection(cc);

                if (!AdvanceLaterally(null)) {
                    if (SearchStack.LastOrDefault().Collection != cc)
                        throw new Exception();
                    if (!TryPop())
                        throw new Exception();
                    Current = prev;
                    return false;
                }

                return true;
            }

            private bool AdvanceOutward () {
                if (Current.Control == Settings.AscendNoFurtherThan)
                    return false;

                if (TryPop()) {
                    Console.WriteLine($"Climb back up to {Current.Control}");
                    // We previously descended so we're climbing back out
                    return AdvanceLaterally(Current.Control);
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
                    Console.WriteLine($"Climb out into {superParent}");
                    SetSearchCollection(icc.Children);
                    return AdvanceLaterally(parent);
                }
            }

            private bool AdvanceLaterally (Control from) {
                int currentIndex = SearchCollection.IndexOf(from),
                    count = SearchCollection.Count,
                    direction = Settings.Direction,
                    newIndex;

                if (currentIndex >= 0) {
                    newIndex = currentIndex + direction;
                    if ((newIndex < 0) || (newIndex >= count)) {
                        if (!AdvanceOutward()) {
                            if (Settings.AllowLoop)
                                newIndex = Arithmetic.Wrap(newIndex, 0, count - 1);
                            else
                                return false;
                        } else
                            return true;
                    }
                } else if (direction > 0)
                    newIndex = 0;
                else
                    newIndex = count - 1;

                if (newIndex == currentIndex)
                    return false;

                var newControl = SearchCollection[newIndex];
                SetCurrent(newControl);
                return true;
            }

            // FIXME: Prevent infinite loops
            private bool MoveNextImpl () {
                if (Current.ContainsChildren && (Settings.Direction == 1)) {
                    if (AdvanceInward())
                        return true;
                }

                if (!AdvanceLaterally(Current.Control))
                    return AdvanceOutward();

                if (Current.ContainsChildren && (Settings.Direction == -1)) {
                    if (AdvanceInward())
                        return true;
                }

                return true;
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

        public Control PickFocusableChild (Control container, int direction = 1) {
            var settings = new TraverseSettings {
                AllowDescend = true,
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
                AllowLoop = false,
                Direction = direction,
                // FIXME: Maybe allow a climb?
                AscendNoFurtherThan = container,
                // FIXME: Maybe do this?
                FollowProxies = false
            };
            var e = new TraversalEnumerator((IControlContainer)container, null, ref settings);
            using (e) {
                while (e.MoveNext()) {
                    if (e.Current.Control.IsValidFocusTarget)
                        return e.Current.Control;
                }
            }
            return null;
        }

        public Control PickFocusableSibling (Control child, int direction, bool allowLoop) {
            var settings = new TraverseSettings {
                AllowDescend = true,
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
                AllowLoop = allowLoop,
                Direction = direction,
                FollowProxies = true,
                // FIXME: Prevent top level rotate here?
            };
            var e = new TraversalEnumerator(null, child, ref settings);
            using (e) {
                while (e.MoveNext()) {
                    if (e.Current.Control.EligibleForFocusRotation)
                        return e.Current.Control;
                }
            }
            return null;
        }

        public TraversalEnumerable Traverse (Control startingPoint, TraverseSettings settings) {
            return new TraversalEnumerable { StartingPoint = startingPoint, Settings = settings };
        }

        public Control TraverseToNext (Control startingPoint, TraverseSettings settings, Func<Control, bool> predicate = null) {
            var e = new TraversalEnumerator(null, startingPoint, ref settings);
            using (e) {
                while (e.MoveNext()) {
                    if ((predicate == null) || predicate(e.Current.Control))
                        return e.Current.Control;
                }
            }
            return null;
        }
    }
}

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
            public bool AllowDescend, AllowDescendIfDisabled, AllowDescendIfInvisible;
            // HACK: Will default to true for Window and false for everything else
            public bool? AllowLoop;
            public bool FollowProxies;
            public Control AscendNoFurtherThan;
            public int Direction;
        }

        public struct TraversalEnumerator : IEnumerator<TraversalInfo> {
            private struct StackEntry {
                public int Position;
                public ControlCollection Collection;
            }

            ControlCollection StartingCollection;
            Control StartingPoint;
            TraverseSettings Settings;

            int FrameIndex;
            bool IsDisposed, IsInitialized, IsAtEnd;
            List<Control> TabOrdered;
            ControlCollection SearchCollection;
            int Position;
            DenseList<StackEntry> SearchStack;

            public TraversalInfo Current { get; private set; }
            TraversalInfo IEnumerator<TraversalInfo>.Current => Current;
            object IEnumerator.Current => Current;

            internal TraversalEnumerator (
                ControlCollection startingCollection, 
                Control startingPoint, int frameIndex,
                ref TraverseSettings settings
            ) {
                if ((settings.Direction != 1) && (settings.Direction != -1))
                    throw new ArgumentOutOfRangeException("settings.Direction");

                FrameIndex = frameIndex;
                StartingPoint = startingPoint;
                StartingCollection = startingCollection;
                Settings = settings;
                IsDisposed = false;
                IsInitialized = false;
                IsAtEnd = false;
                Position = int.MinValue;
                Current = default(TraversalInfo);
                SearchStack = default(DenseList<StackEntry>);
                SearchCollection = null;
                TabOrdered = null;
            }

            public void Dispose () {
                Reset();
                IsDisposed = true;
            }

            private void Push (bool yieldAfterPop) {
                SearchStack.Add(new StackEntry {
                    Position = Position,
                    Collection = SearchCollection,
                });
            }

            private bool TryPop () {
                if (SearchStack.Count <= 0)
                    return false;
                var item = SearchStack.LastOrDefault();
                SearchStack.RemoveTail(1);
                if ((item.Position == Position) && (item.Collection == SearchCollection))
                    throw new Exception();
                SearchCollection = item.Collection;
                TabOrdered = SearchCollection.InTabOrder(FrameIndex, false);
                SetCurrent(item.Position);
                return true;
            }

            private bool MoveFirstImpl () {
                if (SearchStack.Count > 0)
                    throw new Exception();
                if ((StartingPoint == null) && (StartingCollection != null)) {
                    SetSearchCollection(StartingCollection);
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

            private void SetCurrent (Control control, Control initial = null) {
                if (control == null) {
                    SetCurrent(
                        Settings.Direction > 0
                            ? 0
                            : SearchCollection.Count - 1, initial
                    );
                } else {
                    var index = SearchCollection.IndexOf(control);
                    if (index < 0)
                        throw new IndexOutOfRangeException("Control not in current search collection");
                    SetCurrent(index, initial ?? control);
                }
            }

            private void SetCurrent (int position, Control initial = null) {
                Position = position;
                var control = SearchCollection[position];
                Current = new TraversalInfo {
                    Control = control,
                    RedirectTarget = control.FocusBeneficiary,
                    Container = control as IControlContainer
                };
                if (!Settings.FollowProxies || !Current.IsProxy)
                    return;

                // FIXME: Detect cycles
                var rt = Current.RedirectTarget;
                if (rt == initial)
                    throw new Exception($"Found cycle when following redirects involving {control} and {initial}");
                SetCurrent(rt, initial ?? control);
            }

            private void SetSearchCollection (ControlCollection collection) {
                SearchCollection = collection;
                TabOrdered = SearchCollection.InTabOrder(FrameIndex, false);
            }

            private void SetSearchContainer (IControlContainer container) {
                SearchCollection = container.Children;
                TabOrdered = SearchCollection.InTabOrder(FrameIndex, false);
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
                Trace($"Climb down into {Current.Control}");
                // HACK
                Push((Settings.Direction == -1));
                SetSearchCollection(cc);

                if (!AdvanceLaterally(null)) {
                    if (SearchStack.LastOrDefault().Collection != cc)
                        throw new Exception();
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
                        result = AdvanceLaterally(Current.Control);
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
                    result = AdvanceLaterally(parent);
                    return true;
                }
            }

            private bool AdvanceLaterally (Control from) {
                int currentIndex = TabOrdered.IndexOf(from),
                    direction = Settings.Direction,
                    newIndex;

                if (currentIndex >= 0) {
                    newIndex = currentIndex + direction;
                    if ((newIndex < 0) || (newIndex >= TabOrdered.Count)) {
                        var prevTo = TabOrdered;
                        if (!AdvanceOutward(out bool outwardResult)) {
                            if (prevTo != TabOrdered)
                                throw new Exception();
                            // HACK
                            if (Settings.AllowLoop ?? (SearchCollection.Host is Window))
                                newIndex = Arithmetic.Wrap(newIndex, 0, TabOrdered.Count - 1);
                            else
                                return false;
                        } else
                            return outwardResult;
                    }
                } else if (direction > 0)
                    newIndex = 0;
                else
                    newIndex = TabOrdered.Count - 1;

                if (newIndex == currentIndex)
                    return false;

                var newControl = TabOrdered[newIndex];
                SetCurrent(newControl);
                if (Current.ContainsChildren && (Settings.Direction == -1)) {
                    Trace($"Lateral movement from {from} to {newControl}, attempting to advance inward");
                    if (AdvanceInward())
                        return true;
                    else
                        ;
                } else {
                    Trace($"Lateral movement from {from} to {newControl}");
                }
                return true;
            }

            // FIXME: Prevent infinite loops
            private bool MoveNextImpl () {
                if (Current.ContainsChildren && (Settings.Direction == 1)) {
                    if (AdvanceInward())
                        return true;
                }

                if (!AdvanceLaterally(Current.Control)) {
                    AdvanceOutward(out bool outwardResult);
                    return outwardResult;
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

            // [System.Diagnostics.Conditional("FOCUS_TRACE")]
            private static void Trace (string text) {
                Console.WriteLine(text);
            }
        }

        public struct TraversalEnumerable : IEnumerable<TraversalInfo> {
            public Control StartingPoint;
            public TraverseSettings Settings;
            public int FrameIndex;

            public TraversalEnumerator GetEnumerator () {
                return new TraversalEnumerator(null, StartingPoint, FrameIndex, ref Settings);
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
            // FIXME: Handle cases where the control isn't a container
            var collection = ((container as IControlContainer)?.Children) ?? Controls;
            DebugLog($"Finding focusable child in {container} in direction {direction}");
            var e = new TraversalEnumerator(collection, null, FrameIndex, ref settings);
            using (e) {
                while (e.MoveNext()) {
                    if (e.Current.Control.IsValidFocusTarget)
                        return e.Current.Control;
                }
            }
            return null;
        }

        public Control PickFocusableSibling (Control child, int direction, bool? allowLoop) {
            var settings = new TraverseSettings {
                AllowDescend = true,
                AllowDescendIfDisabled = false,
                AllowDescendIfInvisible = false,
                AllowLoop = allowLoop,
                Direction = direction,
                FollowProxies = true,
                // FIXME: Prevent top level rotate here?
            };
            DebugLog($"Finding sibling for {child} in direction {direction}");
            var e = new TraversalEnumerator(null, child, FrameIndex, ref settings);
            using (e) {
                while (e.MoveNext()) {
                    if (e.Current.Control.EligibleForFocusRotation)
                        return e.Current.Control;
                }
            }
            return null;
        }

        public TraversalEnumerable Traverse (Control startingPoint, TraverseSettings settings) {
            return new TraversalEnumerable { StartingPoint = startingPoint, Settings = settings, FrameIndex = FrameIndex };
        }

        public Control TraverseToNext (Control startingPoint, TraverseSettings settings, Func<Control, bool> predicate = null) {
            var e = new TraversalEnumerator(null, startingPoint, FrameIndex, ref settings);
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

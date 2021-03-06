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

            // FIXME: Cache this?
            public bool IsValidFocusTarget => Control.IsValidFocusTarget;
            public bool WillRedirectFocus => (RedirectTarget != null);
            public bool ContainsChildren => (Container != null) && (Container.Children.Count > 0);
        }

        public struct TraverseSettings {
            public bool AllowDescend, AllowLoop, 
                AllowDescendIfDisabled, AllowDescendIfInvisible;
            public bool FollowRedirects;
            public Control AscendNoFurtherThan;
            public int Direction;
        }

        public struct TraversalEnumerator : IEnumerator<TraversalInfo> {
            IControlContainer StartingContainer;
            Control StartingPoint;
            TraverseSettings Settings;

            bool IsDisposed, IsInitialized, IsAtEnd;
            ControlCollection SearchCollection;
            DenseList<ControlCollection> SearchStack;

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
                SearchStack = default(DenseList<ControlCollection>);
                SearchCollection = null;
            }

            public void Dispose () {
                Reset();
                IsDisposed = true;
            }

            private bool MoveFirstImpl () {
                if (StartingPoint == null)
                    return false;
                if (SearchStack.Count > 0)
                    throw new Exception();
                if (!StartingPoint.TryGetParent(out Control parent)) {
                    SetSearchCollection(StartingPoint.Context.Controls);
                    SearchStack.Add(StartingPoint.Context.Controls);
                } else if (parent is IControlContainer icc) {
                    SetSearchContainer(icc);
                    SearchStack.Add(icc.Children);
                } else if (StartingContainer != null) {
                    SetSearchContainer(StartingContainer);
                    SearchStack.Add(StartingContainer.Children);
                } else {
                    throw new Exception("Parent is not a container");
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
                    if (!Settings.FollowRedirects || !Current.WillRedirectFocus)
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
                if (!Settings.AllowDescendIfDisabled && !Current.Control.Enabled)
                    return false;
                if (!Settings.AllowDescendIfInvisible && Control.IsRecursivelyTransparent(Current.Control, true))
                    return false;
                var prev = Current;
                var cc = Current.Children;
                SearchStack.Add(cc);
                SetSearchCollection(cc);

                if (!AdvanceLaterally(null)) {
                    if (SearchStack.LastOrDefault() != cc)
                        throw new Exception();
                    SearchStack.RemoveTail(1);
                    Current = prev;
                    return false;
                }

                return true;
            }

            private bool AdvanceOutward () {
                if (SearchStack.Count == 0)
                    return false;
                if (Current.Control == Settings.AscendNoFurtherThan)
                    return false;
                var newContext = SearchStack.LastOrDefault();
                SearchStack.RemoveTail(1);
                SetSearchCollection(newContext);
                return AdvanceLaterally(Current.Control);
            }

            private bool AdvanceLaterally (Control from) {
                int currentIndex = SearchCollection.IndexOf(from),
                    count = SearchCollection.Count,
                    direction = Settings.Direction,
                    newIndex;

                if (Current.ContainsChildren && (direction == 1)) {
                    if (AdvanceInward())
                        return true;
                }

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

                if (Current.ContainsChildren && (direction == -1)) {
                    if (AdvanceInward())
                        return true;
                }

                SetCurrent(newControl);
                return true;
            }

            // FIXME: Prevent infinite loops
            private bool MoveNextImpl () {
                if (AdvanceLaterally(Current.Control))
                    return true;

                return false;
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

        public Control PickFocusableChild (Control container, TraverseSettings settings) {
            var e = new TraversalEnumerator((IControlContainer)container, null, ref settings);
            using (e) {
                if (!e.MoveNext())
                    return null;
                return e.Current.Control;
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

        public TraversalEnumerable Traverse (Control startingPoint, TraverseSettings settings) {
            return new TraversalEnumerable { StartingPoint = startingPoint, Settings = settings };
        }
    }
}

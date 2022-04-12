using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        public unsafe struct SiblingEnumerator : IEnumerator<ControlKey> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey FirstItem;
            public readonly ControlKey? LastItem;
            private bool Started;
            private int Version;

            public SiblingEnumerator (LayoutEngine engine, ControlKey firstItem, ControlKey? lastItem) {
                Engine = engine;
                Version = engine.Version;
                FirstItem = firstItem;
                LastItem = lastItem;
                Started = false;
                Current = ControlKey.Invalid;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void CheckVersion () {
                if (Version == Engine.Version)
                    return;

                Engine.AssertionFailed("Context was modified");
            }

            public ControlKey Current { get; private set; }
            object IEnumerator.Current => Current;

            public void Dispose () {
                Current = ControlKey.Invalid;
                Version = -1;
            }

            public bool MoveNext () {
                CheckVersion();

                if (Current.IsInvalid) {
                    if (Started)
                        return false;

                    Started = true;
                    Current = FirstItem;
                } else {
                    ref var pCurrent = ref Engine[Current];
                    if (Current == LastItem)
                        Current = ControlKey.Invalid;
                    else if (pCurrent.NextSibling.IsInvalid)
                        Current = ControlKey.Invalid;
                    else
                        Current = pCurrent.NextSibling;
                }

                return !Current.IsInvalid;
            }

            void IEnumerator.Reset () {
                CheckVersion();
                Current = ControlKey.Invalid;
            }
        }

        public struct SiblingsEnumerable : IEnumerable<ControlKey> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey FirstItem;
            public readonly ControlKey? LastItem;

            internal SiblingsEnumerable (LayoutEngine engine, ControlKey firstItem, ControlKey? lastItem) {
                Engine = engine;
                FirstItem = firstItem;
                LastItem = lastItem;
            }

            public SiblingEnumerator GetEnumerator () {
                return new SiblingEnumerator(Engine, FirstItem, LastItem);
            }

            IEnumerator<ControlKey> IEnumerable<ControlKey>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }

        public struct ChildrenEnumerable : IEnumerable<ControlKey> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey Parent;

            internal ChildrenEnumerable (LayoutEngine engine, ControlKey parent) {
                Engine = engine;
                Parent = parent;
            }

            public SiblingEnumerator GetEnumerator () {
                ref var rec = ref Engine[Parent];
                return new SiblingEnumerator(Engine, rec.FirstChild, rec.LastChild);
            }

            IEnumerator<ControlKey> IEnumerable<ControlKey>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }

        public unsafe struct RunEnumerator : IEnumerator<int> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey Parent;
            private int Version;

            public RunEnumerator (LayoutEngine engine, ControlKey parent) {
                Engine = engine;
                Version = engine.Version;
                Parent = parent;
                Current = -2;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void CheckVersion () {
                if (Version == Engine.Version)
                    return;

                Engine.AssertionFailed("Context was modified");
            }

            public int Current { get; private set; }
            object IEnumerator.Current => Current;

            public void Dispose () {
                Current = -3;
                Version = -1;
            }

            public bool MoveNext () {
                CheckVersion();

                if (Current >= 0) {
                    ref var run = ref Engine.Run(Current);
                    Current = run.NextRunIndex;
                    // TODO: Loop detection
                    return (Current >= 0);
                } else if (Current != -2) {
                    return false;
                }

                if (Parent.IsInvalid)
                    return false;

                ref var rec = ref Engine.UnsafeResult(Parent);
                Current = rec.FirstRunIndex;
                return Current >= 0;
            }

            void IEnumerator.Reset () {
                CheckVersion();
                Current = -2;
            }
        }

        public struct RunEnumerable : IEnumerable<int> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey Parent;

            internal RunEnumerable (LayoutEngine engine, ControlKey parent) {
                Engine = engine;
                Parent = parent;
            }

            public RunEnumerator GetEnumerator () {
                return new RunEnumerator(Engine, Parent);
            }

            IEnumerator<int> IEnumerable<int>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }
    }
}

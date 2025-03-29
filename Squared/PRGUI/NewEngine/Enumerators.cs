using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        // TODO: Combine all of these into single hybrid enumerable/enumerator types to reduce the overhead of constructing them
        public struct SiblingEnumerator : IEnumerator<ControlKey> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey FirstItem;
            public readonly ControlKey? LastItem;
            private bool Started, Reverse;

            public SiblingEnumerator (LayoutEngine engine, ControlKey firstItem, ControlKey? lastItem, bool reverse = false) {
                Engine = engine;
                FirstItem = firstItem;
                LastItem = lastItem;
                Started = false;
                _Current = ControlKey.Invalid;
                Reverse = reverse;
            }

            private ControlKey _Current;
            public ControlKey Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _Current;
            }
            object IEnumerator.Current => _Current;

            public void Dispose () {
                _Current = ControlKey.Invalid;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                var cur = _Current;
                if (cur.ID < 0)
                    return MoveNext_Slow();

                var nextItem = Engine.UnsafeNextSibling(cur, Reverse);
                if ((cur == LastItem) || (nextItem.ID < 0)) {
                    _Current = ControlKey.Invalid;
                    return false;
                }

                _Current = nextItem;
                return nextItem.ID >= 0;
            }

            private bool MoveNext_Slow () {
                if (Started)
                    return false;

                Started = true;
                _Current = FirstItem;
                return _Current.ID >= 0;
            }

            void IEnumerator.Reset () {
                _Current = ControlKey.Invalid;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ControlKey UnsafeNextSibling (ControlKey control, bool reverse) {
            ref var rec = ref Records.UnsafeItem(control.ID);
            return reverse ? rec.PreviousSibling : rec.NextSibling;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]        
        private int UnsafeNextRunIndex (int runIndex) {
            return RunBuffer.UnsafeItem(runIndex).NextRunIndex;
        }

        public readonly struct SiblingsEnumerable : IEnumerable<ControlKey> {
            public readonly LayoutEngine Engine;
            public readonly ControlKey FirstItem;
            public readonly ControlKey? LastItem;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal SiblingsEnumerable (LayoutEngine engine, ControlKey firstItem, ControlKey? lastItem) {
                Engine = engine;
                FirstItem = firstItem;
                LastItem = lastItem;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        public readonly struct ChildrenEnumerable : IEnumerable<ControlKey> {
            public readonly LayoutEngine Engine;
            public readonly bool Reverse;
            public readonly ControlKey FirstChild, LastChild;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ChildrenEnumerable (LayoutEngine engine, ControlKey parent, bool reverse)
                : this (engine, ref engine[parent], reverse) {
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal ChildrenEnumerable (LayoutEngine engine, ref BoxRecord parent, bool reverse) {
                Engine = engine;
                FirstChild = parent.FirstChild;
                LastChild = parent.LastChild;
                Reverse = reverse;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public SiblingEnumerator GetEnumerator () {
                return new SiblingEnumerator(
                    Engine, Reverse ? LastChild : FirstChild, 
                    Reverse ? FirstChild : LastChild, Reverse
                );
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

            private bool _Started;
            private int _Current;
            private int Version;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public RunEnumerator (LayoutEngine engine, int floatingRunIndex, int firstRunIndex) {
                Engine = engine;
                Version = engine.Version;                
                // HACK: Ensure that we start with the floating run, and if we do, ensure
                //  that its next run is the first non-floating run. This simplifies MoveNext
                var current = floatingRunIndex;
                if (current < 0)
                    current = firstRunIndex;
                _Current = current;
                _Started = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            [Conditional("DEBUG")]
            private void CheckVersion () {
                if (Version == Engine.Version)
                    return;

                Engine.AssertionFailed("Context was modified");
            }

            public int Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _Current;
            }
            object IEnumerator.Current => _Current;

            public void Dispose () {
                _Started = true;
                _Current = -1;
                Version = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                CheckVersion();

                if (_Started)
                    _Current = Engine.UnsafeNextRunIndex(_Current);
                else
                    _Started = true;

                // TODO: Loop detection
                return (_Current >= 0);
            }

            public void Reset () {
                throw new NotImplementedException();
            }
        }

        public readonly struct RunEnumerable : IEnumerable<int> {
            public readonly LayoutEngine Engine;
            public readonly int FloatingRunIndex, FirstRunIndex;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal RunEnumerable (LayoutEngine engine, ref BoxLayoutResult parent) {
                Engine = engine;
                FloatingRunIndex = parent.FloatingRunIndex;
                FirstRunIndex = parent.FirstRunIndex;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public RunEnumerator GetEnumerator () {
                return new RunEnumerator(Engine, FloatingRunIndex, FirstRunIndex);
            }

            IEnumerator<int> IEnumerable<int>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }

        // TODO: Reimplement this, we should be able to compute accurate content size during layout
        public bool TryMeasureContent (ControlKey container, out RectF result) {
            ref var pItem = ref this[container];
            ref var res = ref Result(container);
            result = default;
            result.Position = res.ContentRect.Position;
            result.Size = res.ContentSize;
            return true;
        }
    }
}

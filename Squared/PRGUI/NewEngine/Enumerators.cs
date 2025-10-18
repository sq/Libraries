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
        public struct SiblingEnumerator {
            private readonly SegmentedArray<BoxRecord> Boxes;
            public readonly ControlKey FirstItem;
            public readonly ControlKey LastItem;
            private readonly bool Reverse;
            private ControlKey _Current;

            public SiblingEnumerator (LayoutEngine engine, ControlKey firstItem, ControlKey? lastItem, bool reverse = false) {
                Boxes = engine.Records;
                FirstItem = firstItem;
                LastItem = lastItem ?? ControlKey.Invalid;
                Reverse = reverse;
                _Current = ControlKey.Corrupt;
            }

            public readonly ref BoxRecord Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref (_Current.IsInvalid 
                    ? ref InvalidValues.Record
                    : ref Boxes.UnsafeItem(_Current.ID));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                var current = _Current;
                if (current.ID == ControlKey.CorruptId) {
                    _Current = FirstItem;
                    return !_Current.IsInvalid;
                } else if (current == LastItem) {
                    _Current = ControlKey.Invalid;
                    return false;
                }

                ref var currentRecord = ref Boxes.UnsafeItem(current.ID);
                var nextItem = Reverse
                    ? currentRecord.PreviousSibling
                    : currentRecord.NextSibling;

                _Current = nextItem;
                return !nextItem.IsInvalid;
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

        internal readonly struct SiblingsEnumerable {
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
        }

        internal readonly struct ChildrenEnumerable {
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
        }

        internal unsafe struct RunEnumerator {
            private readonly SegmentedArray<LayoutRun> _RunBuffer;
            private bool _Started;
            private int _Current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public RunEnumerator (LayoutEngine engine, int floatingRunIndex, int firstRunIndex) {
                _RunBuffer = engine.RunBuffer;
                // HACK: Ensure that we start with the floating run, and if we do, ensure
                //  that its next run is the first non-floating run. This simplifies MoveNext
                var current = floatingRunIndex;
                if (current < 0)
                    current = firstRunIndex;
                _Current = current;
                _Started = false;
            }

            public ref LayoutRun Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _RunBuffer.UnsafeItem(_Current);
            }

            public void Dispose () {
                _Started = true;
                _Current = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                if (_Started)
                    _Current = _RunBuffer.UnsafeItem(_Current).NextRunIndex;
                else
                    _Started = true;

                // TODO: Loop detection
                return (_Current >= 0);
            }

            public void Reset () {
                throw new NotImplementedException();
            }
        }

        internal readonly struct RunEnumerable {
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

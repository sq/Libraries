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

            public SiblingEnumerator GetEnumerator () => this;

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

        internal struct RunEnumerator {
            private readonly SegmentedArray<LayoutRun> _RunBuffer;
            private bool _Started;
            private int _Current;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public RunEnumerator (LayoutEngine engine, ref BoxLayoutResult parent) {
                _RunBuffer = engine.RunBuffer;
                // HACK: Ensure that we start with the floating run, and if we do, ensure
                //  that its next run is the first non-floating run. This simplifies MoveNext
                var current = parent.FloatingRunIndex;
                if (current < 0)
                    current = parent.FirstRunIndex;
                _Current = current;
                _Started = false;
            }

            public RunEnumerator GetEnumerator () => this;

            public ref LayoutRun Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _RunBuffer.UnsafeItem(_Current);
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

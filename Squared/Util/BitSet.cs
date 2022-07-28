using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util.Containers {
    public struct BitSet : IEnumerable<int> {
        public struct IndicesEnumerator : IEnumerable<int>, IEnumerator<int> {
            internal int Index;
            internal UInt64 A, B;

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal IndicesEnumerator (UInt64 a, UInt64 b) {
                A = a;
                B = b;
                Index = -1;
            }

            public void Dispose () {
            }

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                while (++Index < Length) {
                    ref var slot = ref FindBit(ref A, ref B, Index, out var mask);
                    if ((slot & mask) != 0)
                        return true;
                }

                return false;
            }

            public void Reset () {
                Index = -1;
            }

            public IndicesEnumerator GetEnumerator () {
                return new IndicesEnumerator(A, B);
            }

            public int Current {
                [TargetedPatchingOptOut("")]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Index;
            }
            object IEnumerator.Current => Current;

            IEnumerator<int> IEnumerable<int>.GetEnumerator () => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator () => GetEnumerator();
        }

        public const int Length = 128;

        internal UInt64 A, B;

        internal static void BoundsCheckFailed () {
            throw new ArgumentOutOfRangeException("index");
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ref UInt64 FindBit (ref UInt64 a, ref UInt64 b, int index, out UInt64 mask) {
            if ((index < 0) || (index >= Length))
                BoundsCheckFailed();
            int slotIndex = index / 64, localIndex = index % 64;
            mask = 1UL << localIndex;
            if (slotIndex == 0)
                return ref a;
            else
                return ref b;
        }

        public bool this [int index] {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                ref var slot = ref FindBit(ref A, ref B, index, out var mask);
                return (slot & mask) != 0;
            }
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                ref var slot = ref FindBit(ref A, ref B, index, out var mask);
                if (value)
                    slot |= mask;
                else
                    slot &= ~mask;
            }
        }

        public void Add (int index) {
            ref var slot = ref FindBit(ref A, ref B, index, out var mask);
            slot |= mask;
        }

        public IndicesEnumerator Indices => new IndicesEnumerator(A, B);

        IEnumerator<int> IEnumerable<int>.GetEnumerator () => Indices.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => Indices.GetEnumerator();

        /// <summary>
        /// Sets the value at an index and returns whether it was changed.
        /// </summary>
        /// <returns>true if the new value is different from the old value</returns>
        public bool Change (int index, bool value) {
            ref var slot = ref FindBit(ref A, ref B, index, out var mask);
            if (value) {
                if ((slot & mask) != 0)
                    return false;

                slot |= mask;
                return true;
            } else if ((slot & mask) == 0)
                return false;

            slot &= ~mask;
            return true;
        }

        public void SetAll () {
            A = B = ~0UL;
        }

        public void Clear () {
            A = B = default;
        }
    }
}

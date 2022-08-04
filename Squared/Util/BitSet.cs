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
            internal UInt64 A, B, C, D;

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal IndicesEnumerator (UInt64 a, UInt64 b, UInt64 c, UInt64 d) {
                A = a;
                B = b;
                C = c;
                D = d;
                Index = -1;
            }

            public void Dispose () {
            }

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                while (++Index < Length) {
#if !NOSPAN
                    ref var slot = ref FindBit(ref A, Index, out var mask);
#else
                    ref var slot = ref FindBit(ref A, ref B, ref C, ref D, Index, out var mask);
#endif
                    if ((slot & mask) != 0)
                        return true;
                }

                return false;
            }

            public void Reset () {
                Index = -1;
            }

            public IndicesEnumerator GetEnumerator () {
                return new IndicesEnumerator(A, B, C, D);
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

        public const int Length = 256;

        internal UInt64 A, B, C, D;

        internal static void BoundsCheckFailed () {
            throw new ArgumentOutOfRangeException("index");
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if !NOSPAN
        internal static ref UInt64 FindBit (ref UInt64 a, int index, out UInt64 mask) {
#else
        internal static ref UInt64 FindBit (ref UInt64 a, ref UInt64 b, ref UInt64 c, ref UInt64 d, int index, out UInt64 mask) {
#endif
            if ((index < 0) || (index >= Length))
                BoundsCheckFailed();
            int slotIndex = index / 64, localIndex = index % 64;
            mask = 1UL << localIndex;
#if !NOSPAN
            return ref Unsafe.Add(ref a, slotIndex);
#else
            switch (slotIndex) {
                case 0:
                    return ref a;
                case 1:
                    return ref b;
                case 2:
                    return ref c;
                default:
                    return ref d;
            }
#endif
        }

        public bool this [int index] {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
#if !NOSPAN
                ref var slot = ref FindBit(ref A, index, out var mask);
#else
                ref var slot = ref FindBit(ref A, ref B, ref C, ref D, index, out var mask);
#endif
                return (slot & mask) != 0;
            }
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
#if !NOSPAN
                ref var slot = ref FindBit(ref A, index, out var mask);
#else
                ref var slot = ref FindBit(ref A, ref B, ref C, ref D, index, out var mask);
#endif
                if (value)
                    slot |= mask;
                else
                    slot &= ~mask;
            }
        }

        public void Add (int index) {
#if !NOSPAN
            ref var slot = ref FindBit(ref A, index, out var mask);
#else
            ref var slot = ref FindBit(ref A, ref B, ref C, ref D, index, out var mask);
#endif
            slot |= mask;
        }

        public IndicesEnumerator Indices => new IndicesEnumerator(A, B, C, D);

        IEnumerator<int> IEnumerable<int>.GetEnumerator () => Indices.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => Indices.GetEnumerator();

        /// <summary>
        /// Sets the value at an index and returns whether it was changed.
        /// </summary>
        /// <returns>true if the new value is different from the old value</returns>
        public bool Change (int index, bool value) {
#if !NOSPAN
            ref var slot = ref FindBit(ref A, index, out var mask);
#else
            ref var slot = ref FindBit(ref A, ref B, ref C, ref D, index, out var mask);
#endif
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
            A = B = C = D = ~0UL;
        }

        public void Clear () {
            A = B = C = D = default;
        }
    }
}

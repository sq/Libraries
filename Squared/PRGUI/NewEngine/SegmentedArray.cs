using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.PRGUI.NewEngine {
    internal class SegmentedArray<T>
        where T : struct
    {
        public const int SegmentSize = 512;

        private readonly T[][] Segments;

        private int _Count;

        public int Count => _Count;
        public readonly int Capacity;

        public SegmentedArray (int capacity) {
            int segmentCount = (capacity + (SegmentSize - 1)) / SegmentSize;
            Capacity = capacity = segmentCount * SegmentSize;
            Segments = new T[segmentCount][];
        }

        public void Clear () {
            _Count = 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T New (out int index) {
            index = Interlocked.Increment(ref _Count) - 1;
            if (index >= Capacity) {
                _Count = Capacity;
                ThrowIndexOutOfRange();
            }

            int segmentIndex = index / SegmentSize,
                localIndex = index % SegmentSize;

            var segment = GetOrCreateSegment(segmentIndex);

            ref T result = ref segment[localIndex];
            result = default;
            return ref result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T[] GetOrCreateSegment (int segmentIndex) {
            var segment = Segments[segmentIndex];
            if (segment == null)
                return CreateSegment(segmentIndex);
            return segment;
        }

        private T[] CreateSegment (int segmentIndex) {
            var segment = new T[SegmentSize];
            segment = Interlocked.CompareExchange(ref Segments[segmentIndex], segment, null) ?? segment;
            return segment;
        }

        public void Grow (int newCount) {
            int lastSegmentIndex = newCount / SegmentSize,
                oldCount = _Count;

            if (oldCount < newCount) {
                for (int i = 0; i <= lastSegmentIndex; i++)
                    GetOrCreateSegment(i);

                do {
                    var temp = Interlocked.CompareExchange(ref _Count, newCount, oldCount);
                    if (temp == oldCount)
                        break;
                    newCount = Math.Max(temp, newCount);
                } while (true);
            }
        }

        public ref T this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if ((index < 0) || (index >= Count))
                    ThrowIndexOutOfRange();

                int segmentIndex = index / SegmentSize,
                    localIndex = index % SegmentSize;
                return ref Segments[segmentIndex][localIndex];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T UnsafeItem (int index) {
            int segmentIndex = index / SegmentSize,
                localIndex = index % SegmentSize;
            return ref Segments[segmentIndex][localIndex];
        }

        private void ThrowIndexOutOfRange () {
            throw new IndexOutOfRangeException("index");
        }

        internal void AddRange (T[] items) {
            foreach (var item in items)
                (New(out _)) = item;
        }

        internal object ToArray () {
            var result = new T[Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = this[i];
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Squared.Util;

namespace Squared.Render {
    public interface IListBatch {
        int Count { get; }
    }

    public struct ListBatchDrawCalls<T> : IDisposable {
        public static readonly T Invalid;

        private Frame _Frame;
        private Frame.Slab<T> _Slab;
        private ArraySegment<T> _Buffer;
        private int _Count;
        private static Frame.Slab<T> _MostRecentSlab;

        /// <summary>
        /// The backing buffer for the draw call list
        /// </summary>
        public ArraySegment<T> Buffer {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Buffer;
        }
        /// <summary>
        /// The backing buffer for the draw call list, trimmed to only the occupied slots
        /// WARNING: Don't use foreach on this! ArraySegment's enumerator is a class!
        /// </summary>
        public ArraySegment<T> Items {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var buffer = _Buffer;
                if (buffer.Array == null)
                    return new ArraySegment<T>(Array.Empty<T>(), 0, 0);
                else
                    return new ArraySegment<T>(buffer.Array, buffer.Offset, _Count);
            }
        }
        /// <summary>
        /// The number of slots occupied in the backing buffer
        /// </summary>
        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Count;
        }
        /// <summary>
        /// The number of items the backing buffer can hold
        /// </summary>
        public int Capacity {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _Buffer.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T FirstOrDefault () => ref (_Count > 0
            ? ref this[0]
            : ref Invalid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T LastOrDefault () => ref (_Count > 0
            ? ref this[_Count - 1]
            : ref Invalid);

        public ref T this[int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                int count = _Count;
                if ((index < 0) || (index >= count))
                    throw new ArgumentOutOfRangeException(nameof(index));
                var buffer = _Buffer;
                return ref buffer.Array[buffer.Offset + index];
            }
        }

        public void EnsureCapacity (int capacity) {
            var buffer = _Buffer;
            if (capacity <= buffer.Count)
                return;

            var newSize = UnorderedList<T>.PickGrowthSize(buffer.Count, capacity);
            if (newSize <= buffer.Count)
                throw new Exception("Internal error resizing buffer");

            var mrs = _MostRecentSlab;
            ArraySegment<T> newBuffer;
            if ((mrs?.Frame != _Frame) || !mrs.TryAllocate(newSize, out newBuffer)) {
                var tup = _Frame.AllocateFromSlab<T>(newSize);
                _MostRecentSlab = mrs = tup.slab;
                newBuffer = tup.result;
            }

            var itemsToCopy = Math.Min(_Count, buffer.Count);
            if (itemsToCopy > 0) {
                Array.Copy(buffer.Array, buffer.Offset, newBuffer.Array, newBuffer.Offset, itemsToCopy);
                if (Frame.Slab<T>.ContainsReferences)
                    Array.Clear(buffer.Array, buffer.Offset, buffer.Count);
            }

            _Slab?.Free(buffer);
            _Slab = mrs;
            _Buffer = newBuffer;
        }

        public void Clear () {
            if (_Count == 0)
                return;

            var buffer = _Buffer;
            Array.Clear(buffer.Array, buffer.Offset, _Count);
            _Count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add (ref T item) {
            if (item == null)
                throw new Exception();

            var index = _Count++;
            var buffer = _Buffer;
            if (index >= buffer.Count) {
                EnsureCapacity(index + 1);
                buffer = _Buffer;
            }
            ref T resultRef = ref buffer.Array[buffer.Offset + index];
            resultRef = item;
            return ref resultRef;
        }

        public void AddRange (T[] items) =>
            AddRange(new ArraySegment<T>(items));

        public void AddRange (ArraySegment<T> items) {
            var offset = _Count;
            var newCount = offset + items.Count;
            EnsureCapacity(newCount);
            _Count = newCount;
            var buffer = _Buffer;

            Array.Copy(items.Array, items.Offset, buffer.Array, buffer.Offset + offset, items.Count);
        }

        public void RemoveAtOrdered (int index) {
            if ((index < 0) || (index >= _Count))
                throw new ArgumentOutOfRangeException(nameof(index));

            var buffer = _Buffer;
            _Count--;
            if (index < _Count)
                Array.Copy(buffer.Array, buffer.Offset + index + 1, buffer.Array, buffer.Offset + index, _Count - index);
            buffer.Array[buffer.Offset + _Count] = default;
        }

        public ArraySegment<T> ReserveSpace (int count) {
            var newCount = _Count + count;
            EnsureCapacity(newCount);
            var oldCount = _Count;
            var buffer = _Buffer;
            _Count = newCount;
            return new ArraySegment<T>(buffer.Array, buffer.Offset + oldCount, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sort<TComparer> (TComparer comparer, int[] indices = null)
            where TComparer : IRefComparer<T>
        {
            if (indices == null)
                Util.Sort.FastCLRSortRef(Items, comparer);
            else
                Util.Sort.IndexedSortRef(Items, new ArraySegment<int>(indices), comparer);
        }

        public void UnsafeFastClear () {
            _Count = 0;
        }

        public void Dispose () {
            _Slab?.Free(_Buffer);
            _Count = 0;
            _Buffer = default;
            _Slab = null;
            _Frame = null;
        }

        internal void Initialize (Frame frame, bool unsafeFastClear) {
            _Slab = null;
            _Buffer = default;
            _Count = 0;

            _Frame = frame;
        }
    }

    public abstract class ListBatch<T> : Batch, IListBatch {
        public const int BatchCapacityLimit = 512;

        protected ListBatchDrawCalls<T> _DrawCalls;
        private static bool _CanFastClearDrawCalls = false;

        public ListBatch ()
            : base () 
        {
        }

        public T FirstDrawCall {
            get {
                return _DrawCalls.FirstOrDefault();
            }
        }

        public T LastDrawCall {
            get {
                return _DrawCalls.LastOrDefault();
            }
        }

        protected void Initialize (
            IBatchContainer container, int layer, Material material,
            bool addToContainer, int? capacity = null
        ) {
            _DrawCalls.Initialize(container.Frame, _CanFastClearDrawCalls);
            base.Initialize(container, layer, material, addToContainer);
        }

        public static void AdjustPoolCapacities (
            int? itemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            /*
            _ListPool.SmallPoolMaxItemSize = itemSizeLimit.GetValueOrDefault(_ListPool.SmallPoolMaxItemSize);
            _ListPool.LargePoolMaxItemSize = largeItemSizeLimit.GetValueOrDefault(_ListPool.LargePoolMaxItemSize);
            _ListPool.SmallPoolCapacity = smallPoolCapacity.GetValueOrDefault(_ListPool.SmallPoolCapacity);
            _ListPool.LargePoolCapacity = largePoolCapacity.GetValueOrDefault(_ListPool.LargePoolCapacity);
            */
        }

        public static void ConfigureClearBehavior (bool enableFastClear) {
            _CanFastClearDrawCalls = enableFastClear;
            // _ListPool.FastClearEnabled = enableFastClear;
        }

        public int Count {
            get {
                return _DrawCalls.Count;
            }
        }

        public void EnsureCapacity (int capacity) {
            _DrawCalls.EnsureCapacity(capacity);
        }

        protected void Add (ref T item) {
            _DrawCalls.Add(ref item);
        }

        protected override void OnReleaseResources() {
            _DrawCalls.Dispose();
            base.OnReleaseResources();
        }

        public override string ToString () {
            if (Name != null)
                return string.Format("{0} x{6} '{5}' #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, FormatName(), Count);
            else
                return string.Format("{0} x{5} #{1} {2} layer={4} material={3}", GetType().Name, InstanceId, StateString, Material, Layer, Count);
        }
    }
}

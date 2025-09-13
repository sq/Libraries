using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime;

namespace Squared.Util {
    public class UnorderedList<T> : IEnumerable<T> {
        public static class Allocator {
            public static ArraySegment<T> Allocate (int minimumSize) {
                return new ArraySegment<T>(new T[minimumSize]);
            }

            public static ArraySegment<T> Resize (ArraySegment<T> buffer, int minimumSize) {
                if (minimumSize < buffer.Count)
                    return buffer;

                var array = buffer.Array;
                var newBuffer = Allocate(minimumSize);
                Array.Copy(array, buffer.Offset, newBuffer.Array, newBuffer.Offset, buffer.Count);
                return newBuffer;
            }
        }

        public static int DefaultSize = 16;

        /// <summary>
        /// This value will be incremented every time the underlying buffer is re-allocated
        /// </summary>
        public int BufferVersion;

        protected T[] _Items;
        internal int _Count;

        public struct Enumerator : IEnumerator<T> {
            UnorderedList<T> _List;
            int _Index, _Offset, _Count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator (UnorderedList<T> list, int start, int count) {
                _List = list;
                _Index = -1;
                _Offset = start;
                _Count = count;
            }

            T IEnumerator<T>.Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _List._Items[_Index + _Offset];
            }

            public ref readonly T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _List._Items[_Index + _Offset];
            }

            public void Dispose () {
            }

            object IEnumerator.Current {
                get => _List._Items[_Index + _Offset];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetCurrent (in T newValue) {
                _List._Items[_Index + _Offset] = newValue;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool GetCurrent (out T value) {
                if ((_Index < 0) || (_Index >= _Count)) {
                    value = default(T);
                    return false;
                }
                value = _List._Items[_Index + _Offset];
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool PeekNext (out T value) {
                if ((_Index < -1) || (_Index >= (_Count - 1))) {
                    value = default(T);
                    return false;
                }
                value = _List._Items[_Index + _Offset + 1];
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool GetNext (out T nextItem) {
                _Index += 1;

                if (_Index < _Count) {
                    nextItem = _List._Items[_Index + _Offset];
                    return true;
                } else {
                    nextItem = default(T);
                    return false;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                _Index += 1;
                return (_Index < _Count);
            }

            public void Reset () {
                _Index = -1;
            }

            public int Index {
                get {
                    return _Index;
                }
            }

            public bool ReplaceCurrent (ref T newValue) {
                if (_Index >= _Count)
                    return false;

                _List.DangerousSetItem(_Index + _Offset, ref newValue);
                return false;
            }

            public bool RemoveCurrentOrdered () {
                if (_Index >= _Count)
                    return false;

                _List.RemoveAtOrdered(_Index + _Offset);
                _Count -= 1;
                _Index -= 1;
                return true;
            }

            public bool RemoveCurrent () {
                if (_Index >= _Count)
                    return false;

                _List.DangerousRemoveAt(_Index + _Offset);
                _Count -= 1;
                _Index -= 1;
                return true;
            }

            public bool RemoveToHere (int startOffset = 0) {
                if (_Index >= _Count)
                    return false;
                var count = _Index - startOffset + 1;
                _List.DangerousRemoveRange(startOffset, count);
                _Count -= count;
                _Index -= count;
                return (_Count == _List.Count);
            }
        }

        private void AllocateNewBuffer (int size) {
            var buffer = Allocator.Allocate(size);
            BufferVersion++;
            _Items = buffer.Array;
        }

        public UnorderedList () {
            _Count = 0;
            AllocateNewBuffer(DefaultSize);
        }

        public UnorderedList (int size) {
            _Count = 0;
            AllocateNewBuffer(Math.Max(DefaultSize, size));
        }

        public UnorderedList (T[] values) {
            AllocateNewBuffer(Math.Max(DefaultSize, values.Length));
            _Count = values.Length;
            Array.Copy(values, 0, _Items, 0, _Count);
        }

        public Enumerator GetParallelEnumerator (int partitionIndex, int partitionCount) {
            int partitionSize = (int)Math.Ceiling(_Count / (float)partitionCount);
            int start = partitionIndex * partitionSize;
            return new Enumerator(this, start, Math.Min(_Count - start, partitionSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator () {
            return new Enumerator(this, 0, _Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(this, 0, _Count);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(this, 0, _Count);
        }

        public static int PickGrowthSize (int currentSize, int targetSize) {
            if (targetSize < 4)
                targetSize = 4;

            var currentIncreased = currentSize * 145 / 100;
            var newCapacity = Math.Max(currentIncreased, targetSize);

            return newCapacity;
        }

        protected virtual void Grow (int targetCapacity) {
            ArraySegment<T> newBuffer, oldBuffer = new ArraySegment<T>(_Items, 0, _Items.Length);
            var newCapacity = PickGrowthSize(_Items.Length, targetCapacity);
            newBuffer = Allocator.Resize(oldBuffer, newCapacity);

            BufferVersion++;
            _Items = newBuffer.Array;
        }

        protected void BoundsCheckFailed () {
            throw new ArgumentOutOfRangeException("index");
        }

        public void InsertOrdered (int index, in T item) {
            EnsureCapacity(_Count + 1);
            if ((index < 0) || (index > _Count))
                BoundsCheckFailed();
            if (index < Count)
                Array.Copy(_Items, index, _Items, index + 1, Count - index);
            _Items[index] = item;
            _Count += 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity (int capacity) {
            if (_Items.Length >= capacity)
                return;

            Grow(capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousEnsureSize (int size) {
            if (size <= _Count)
                return;
            DangerousSetCount(size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousSetCount (int count, bool clearEmptySpace = true) {
            if (count == _Count)
                return;
            EnsureCapacity(count);

            if (clearEmptySpace) {
                // Either clear the space that will be occupied by new items, or clear
                //  the space that was previously occupied by items and is no longer used
                if (count > _Count)
                    Array.Clear(_Items, _Count, count - _Count);
                else
                    Array.Clear(_Items, count, _Count - count);
            }
            _Count = count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            int newCount = _Count + 1;
            EnsureCapacity(newCount);

            _Items[newCount - 1] = item;
            _Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            int newCount = _Count + 1;
            EnsureCapacity(newCount);

            _Items[newCount - 1] = item;
            _Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T CreateSlot () {
            int newCount = _Count + 1;
            EnsureCapacity(newCount);

            _Count = newCount;
            return ref _Items[newCount - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (T[] items, int sourceOffset, int count) {
            int newCount = _Count + count;
            EnsureCapacity(newCount);

            int insertOffset = newCount - count;
            Array.Copy(items, sourceOffset, _Items, insertOffset, count);

            _Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (T[] items) {
            AddRange(items, 0, items.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<T> items) {
            AddRange(items.Array, items.Offset, items.Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (List<T> items) {
            var listCount = items.Count;
            int newCount = _Count + listCount;
            EnsureCapacity(newCount);

            int insertOffset = newCount - listCount;
            for (var i = 0; i < listCount; i++)
                _Items[insertOffset + i] = items[i];

            _Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (IEnumerable<T> items) {
            var array = items as T[];
            var list = items as List<T>;
            var ulist = items as UnorderedList<T>;

            if (array != null) {
                AddRange(array);
            } else if (list != null) {
                AddRange(list);
            } else if (ulist != null) {
                var buffer = ulist.GetBuffer();
                AddRange(buffer.Array, buffer.Offset, ulist.Count);
            } else {
                foreach (var item in items)
                    Add(item);
            }
        }

        public bool Contains (in T item) {
            var index = Array.IndexOf(_Items, item, 0, _Count);
            return (index >= 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T Item1 () => ref _Items[0];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly T DangerousReadItem (int index) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            return ref _Items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T DangerousItem (int index) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            return ref _Items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T DangerousGetItem (int index) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            return _Items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousGetItem (int index, out T result) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            result = _Items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DangerousTryGetItem (int index, out T result) {
            if ((index < 0) || (index >= _Count)) {
                result = default(T);
                return false;
            }

            result = _Items[index];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T DangerousItemOrDefault (int index) {
            if ((index < 0) || (index >= _Count))
                return default(T);

            return _Items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousSetItem (int index, T newValue) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            _Items[index] = newValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousSetItem (int index, ref T newValue) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            _Items[index] = newValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousClearItem (int index) {
            if ((index < 0) || (index >= _Count))
                BoundsCheckFailed();

            _Items[index] = default;
        }

        /// <summary>
        /// WARNING: This will not preserve the order of the array! If you want that, use the Ordered version
        /// </summary>
        public void DangerousRemoveAt (int index) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            var newCount = _Count - 1;

            if (index < newCount) {
                ref var deadSlot = ref _Items[newCount];
                _Items[index] = deadSlot;
                deadSlot = default(T);
            } else {
                _Items[index] = default(T);
            }

            _Count = newCount;
        }

        public void RemoveAtOrdered (int index) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            _Count--;
            if (index < _Count)
                Array.Copy(_Items, index + 1, _Items, index, _Count - index);
            _Items[_Count] = default(T);
        }

        public void DangerousRemoveRange (int index, int count) {
            if (count <= 0)
                return;
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();
            if ((index + count) > _Count)
                throw new ArgumentOutOfRangeException("count");

            _Count -= count;
            if (index < _Count)
                Array.Copy(_Items, index + count, _Items, index, _Count - index);

            Array.Clear(_Items, _Count, count);
        }

        /// <summary>
        /// WARNING: This will not preserve the order of the list! If you want that, use the Ordered version
        /// </summary>
        public bool TryPopFront (out T result) {
            if (_Count == 0) {
                result = default(T);
                return false;
            }

            result = _Items[0];
            DangerousRemoveAt(0);
            return true;
        }

        public bool TryPopFrontOrdered (out T result) {
            if (_Count == 0) {
                result = default(T);
                return false;
            }

            result = _Items[0];
            RemoveAtOrdered(0);
            return true;
        }

        /// <summary>
        /// WARNING: This will not preserve the order of the list! If you want that, use the Ordered version
        /// </summary>
        public bool TryPopFront (out T result, out bool empty) {
            if (_Count == 0) {
                result = default(T);
                empty = true;
                return false;
            }

            result = _Items[0];
            DangerousRemoveAt(0);
            empty = _Count == 0;
            return true;
        }

        public bool TryPopFrontOrdered (out T result, out bool empty) {
            if (_Count == 0) {
                result = default(T);
                empty = true;
                return false;
            }

            result = _Items[0];
            RemoveAtOrdered(0);
            empty = _Count == 0;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryPopBack (out T result) {
            if (_Count == 0) {
                result = default(T);
                return false;
            }

            var index = --_Count;
            ref var slot = ref _Items[index];
            result = slot;
            slot = default(T);
            return true;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _Count;
            }
        }

        public int Capacity {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _Items.Length;
            }
        }

        public void Clear () {
            // FIXME: Determine whether we need to clear excess
            Array.Clear(_Items, 0, _Items.Length);
            _Count = 0;
        }

        /// <summary>
        /// Sets the count to 0 without clearing the contents of the internal buffer.
        /// Don't use this unless you know what you're doing! 
        /// </summary>
        public void UnsafeFastClear () {
            _Count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] GetBufferArray () {
            return _Items;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArraySegment<T> GetBuffer (bool trimmed = false) {
            return new ArraySegment<T>(_Items, 0, trimmed ? _Count : _Items.Length);
        }

        public T[] ToArray () {
            var result = new T[_Count];
            Array.Copy(_Items, 0, result, 0, _Count);
            return result;
        }

        public void CopyTo (IList<T> output) {
            for (int i = 0; i < _Count; i++)
                output.Add(_Items[i]);
        }

        public void CopyTo (UnorderedList<T> output) {
            var segment = output.ReserveSpace(_Count);
            Array.Copy(_Items, 0, segment.Array, segment.Offset, _Count);
        }

        public void CopyTo (T[] buffer, int offset, int count) {
            if (count > _Count)
                count = _Count;

            Array.Copy(_Items, 0, buffer, offset, count);
        }

        public void Sort (IComparer<T> comparer = null) {
            Array.Sort(_Items, 0, _Count, comparer);
        }

        public void FastCLRSort<TComparer> (TComparer comparer, int offset = 0, int count = int.MaxValue)
            where TComparer : IComparer<T>
        {
            var items = new ArraySegment<T>(_Items, 0, _Count);
            count = Math.Min(count, _Count - offset);
            if (count < 0)
                throw new ArgumentOutOfRangeException("offset or count out of range");
            Util.Sort.FastCLRSort(items, comparer, offset, count);
        }

        public void IndexedSort<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IComparer<T>
        {
            var items = new ArraySegment<T>(_Items, 0, _Count);
            var _indices = new ArraySegment<int>(indices, 0, _Count);
            Util.Sort.IndexedSort(items, _indices, comparer);
        }

        public void FastCLRSortRef<TComparer> (TComparer comparer, int offset = 0, int count = int.MaxValue)
            where TComparer : IRefComparer<T>
        {
            var items = new ArraySegment<T>(_Items, 0, _Count);
            count = Math.Min(count, _Count - offset);
            if (count < 0)
                throw new ArgumentOutOfRangeException("offset or count out of range");
            Util.Sort.FastCLRSortRef(items, comparer, offset, count);
        }

        public void IndexedSortRef<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T>
        {
            var items = new ArraySegment<T>(_Items, 0, _Count);
            var _indices = new ArraySegment<int>(indices, 0, _Count);
            Util.Sort.IndexedSortRef(items, _indices, comparer);
        }

        public ArraySegment<T> ReserveSpace (int count) {
            var newCount = _Count + count;
            EnsureCapacity(newCount);
            var oldCount = _Count;
            _Count = newCount;
            return new ArraySegment<T>(_Items, oldCount, count);
        }

        public bool SequenceEqual (UnorderedList<T> rhs) => SequenceEqual(rhs, EqualityComparer<T>.Default);

        public bool SequenceEqual<TEqualityComparer> (UnorderedList<T> rhs, TEqualityComparer comparer)
            where TEqualityComparer : IEqualityComparer<T>
        {
            if (Count != rhs.Count)
                return false;

            for (int i = 0, c = Count; i < c; i++) {
                ref var itemL = ref this.DangerousItem(i);
                ref var itemR = ref rhs.DangerousItem(i);
                if (!comparer.Equals(itemL, itemR))
                    return false;
            }

            return true;
        }

        public void ReplaceWith (UnorderedList<T> newItems, bool clearEmptySpace = true) {
            if (newItems == this)
                return;

            int oldCount = _Count, newCount = newItems._Count;
            EnsureCapacity(newCount);
            _Count = newCount;
            newItems.CopyTo(_Items, 0, newCount);
            if (clearEmptySpace && (newCount < oldCount))
                Array.Clear(_Items, newCount, oldCount - newCount);
        }

        internal void Accept (ref DenseList<T> source, int count) {
            int i = _Count;
            int newCount = i + count;
            EnsureCapacity(newCount);
            _Count = newCount;
            var items = _Items;

            if (count > 0)
                items[i++] = source.Item1;
            if (count > 1)
                items[i++] = source.Item2;
            if (count > 2)
                items[i++] = source.Item3;
            if (count > 3)
                items[i++] = source.Item4;
        }
    }
}

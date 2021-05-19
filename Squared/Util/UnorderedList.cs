using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Squared.Util {
    public class UnorderedList<T> : IEnumerable<T> {
        public abstract class Allocator {
            public static readonly DefaultAllocator Default = new DefaultAllocator();

            public abstract ArraySegment<T> Allocate (int minimumSize);
            public abstract ArraySegment<T> Resize (ArraySegment<T> buffer, int minimumSize);
        }

        public class DefaultAllocator : Allocator {
            public static DefaultAllocator Instance = new DefaultAllocator();

            public override ArraySegment<T> Allocate (int minimumSize) {
                return new ArraySegment<T>(new T[minimumSize]);
            }

            public override ArraySegment<T> Resize (ArraySegment<T> buffer, int minimumSize) {
                if (minimumSize < buffer.Count)
                    return buffer;

                var array = buffer.Array;
                if (buffer.Offset == 0) {
                    Array.Resize(ref array, minimumSize);
                    return new ArraySegment<T>(array);
                }  else {
                    var newBuffer = Allocate(minimumSize);
                    Array.Copy(array, buffer.Offset, newBuffer.Array, newBuffer.Offset, buffer.Count);
                    return newBuffer;
                }
            }
        }

        public class BasicSlabAllocator : DefaultAllocator {
            public int SlabSize = 1024 * 64;

            private T[] PreviousSlab = null, CurrentSlab = null;
            private int PreviousSlabOffset = -1, CurrentSlabOffset = -1;

            private void AllocateNewSlab () {
                PreviousSlab = CurrentSlab;
                PreviousSlabOffset = CurrentSlabOffset;
                CurrentSlab = new T[SlabSize];
                CurrentSlabOffset = 0;
            }

            private bool TryAllocateFromSlab (int minimumSize, T[] slab, ref int slabOffset, out ArraySegment<T> result) {
                result = default(ArraySegment<T>);

                if (slab == null)
                    return false;

                var remainingSpace = slab.Length - slabOffset;
                if (minimumSize > remainingSpace)
                    return false;

                var offset = slabOffset;
                slabOffset += minimumSize;
                result = new ArraySegment<T>(slab, offset, minimumSize);
                return true;
            }

            public override ArraySegment<T> Allocate (int minimumSize) {
                if (minimumSize > SlabSize)
                    return new ArraySegment<T>(new T[minimumSize]);

                ArraySegment<T> result;
                if (TryAllocateFromSlab(minimumSize, PreviousSlab, ref PreviousSlabOffset, out result))
                    return result;
                else if (TryAllocateFromSlab(minimumSize, CurrentSlab, ref CurrentSlabOffset, out result))
                    return result;

                AllocateNewSlab();
                if (!TryAllocateFromSlab(minimumSize, CurrentSlab, ref CurrentSlabOffset, out result))
                    throw new Exception("Failed to allocate from slab");
                return result;
            }
        }

        public static int DefaultSize = 128;
        public static int FirstGrowTarget = 1024;

        /// <summary>
        /// This value will be incremented every time the underlying buffer is re-allocated
        /// </summary>
        public int BufferVersion;

        protected Allocator _Allocator;
        protected T[] _Items;
        protected int _BufferOffset, _BufferSize;
        protected int _Count;

        public struct Enumerator : IEnumerator<T>{
            UnorderedList<T> _List;
            int _Index, _Offset, _Count;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator (UnorderedList<T> list, int start, int count) {
                _List = list;
                _Index = -1;
                _Offset = start;
                _Count = count;
            }

            public T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get { return _List._Items[_Index + _Offset]; }
            }

            public void Dispose () {
            }

            object System.Collections.IEnumerator.Current {
                get { return _List._Items[_Index + _Offset]; }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetCurrent (ref T newValue) {
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
            var buffer = _Allocator.Allocate(size);
            BufferVersion++;
            _Items = buffer.Array;
            _BufferOffset = buffer.Offset;
            _BufferSize = buffer.Count;
        }

        public UnorderedList () {
            _Allocator = Allocator.Default;
            _Count = 0;
            AllocateNewBuffer(DefaultSize);
        }

        public UnorderedList (Allocator allocator) {
            _Allocator = allocator ?? Allocator.Default;
            _Count = 0;
            AllocateNewBuffer(DefaultSize);
        }

        public UnorderedList (int size) {
            _Allocator = Allocator.Default;
            _Count = 0;
            AllocateNewBuffer(Math.Max(DefaultSize, size));
        }

        public UnorderedList (int size, Allocator allocator) {
            _Allocator = allocator ?? Allocator.Default;
            _Count = 0;
            AllocateNewBuffer(Math.Max(DefaultSize, size));
        }

        public UnorderedList (T[] values, Allocator allocator = null) {
            _Allocator = allocator ?? Allocator.Default;
            AllocateNewBuffer(Math.Max(DefaultSize, values.Length));
            _Count = values.Length;
            Array.Copy(values, 0, _Items, _BufferOffset, _Count);
        }

        public Enumerator GetParallelEnumerator (int partitionIndex, int partitionCount) {
            int partitionSize = (int)Math.Ceiling(_Count / (float)partitionCount);
            int start = partitionIndex * partitionSize;
            return new Enumerator(this, start + _BufferOffset, Math.Min(_Count - start, partitionSize));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator () {
            return new Enumerator(this, _BufferOffset, _Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(this, _BufferOffset, _Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(this, _BufferOffset, _Count);
        }

        public static int PickGrowthSize (int currentSize, int targetSize) {
            var newCapacity = currentSize;
            if (newCapacity < 8)
                newCapacity = 8;

            while (newCapacity < targetSize)
                // 1.45 growth ratio is better than 2.0, and linear growth ratio is Bad
                newCapacity = (newCapacity * 145 / 100);

            return newCapacity;
        }

        private void Grow (int targetCapacity) {
            ArraySegment<T> newBuffer, oldBuffer = new ArraySegment<T>(_Items, _BufferOffset, _BufferSize);
            if (targetCapacity <= FirstGrowTarget) {
                newBuffer = _Allocator.Resize(oldBuffer, FirstGrowTarget);
            } else {
                var newCapacity = PickGrowthSize(_BufferSize, targetCapacity);
                newBuffer = _Allocator.Resize(oldBuffer,  newCapacity);
            }

            BufferVersion++;
            _Items = newBuffer.Array;
            _BufferOffset = newBuffer.Offset;
            _BufferSize = newBuffer.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity (int capacity) {
            if (_BufferSize >= capacity)
                return;

            Grow(capacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousEnsureSize (int size) {
            if (size <= _Count)
                return;
            EnsureCapacity(size);

            var growth = size - _Count;
            Array.Clear(_Items, _BufferOffset + _Count, growth);
            _Count = size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            int newCount = _Count + 1;
            EnsureCapacity(newCount);

            _Items[_BufferOffset + newCount - 1] = item;
            _Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            int newCount = _Count + 1;
            EnsureCapacity(newCount);

            _Items[_BufferOffset + newCount - 1] = item;
            _Count = newCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (T[] items, int sourceOffset, int count) {
            int newCount = _Count + count;
            EnsureCapacity(newCount);

            int insertOffset = _BufferOffset + newCount - count;
            /*
            for (var i = 0; i < count; i++)
                _Items[insertOffset + i] = items[i + sourceOffset];
            */
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

            int insertOffset = _BufferOffset + newCount - listCount;
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

        public bool Contains (T item) {
            var index = Array.IndexOf(_Items, item, _BufferOffset, _Count);
            return (index >= 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T DangerousGetItem (int index) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            return _Items[_BufferOffset + index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousGetItem (int index, out T result) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            result = _Items[_BufferOffset + index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DangerousTryGetItem (int index, out T result) {
            if ((index < 0) || (index >= _Count)) {
                result = default(T);
                return false;
                throw new IndexOutOfRangeException();
            }

            result = _Items[_BufferOffset + index];
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DangerousSetItem (int index, ref T newValue) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            _Items[_BufferOffset + index] = newValue;
        }

        /// <summary>
        /// WARNING: This will not preserve the order of the array! If you want that, use the Ordered version
        /// </summary>
        public void DangerousRemoveAt (int index) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            var newCount = _Count - 1;

            if (index < newCount) {
                _Items[_BufferOffset + index] = _Items[_BufferOffset + newCount];
                _Items[_BufferOffset + newCount] = default(T);
            } else {
                _Items[_BufferOffset + index] = default(T);
            }

            _Count = newCount;
        }

        public void RemoveAtOrdered (int index) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            _Count--;
            if (index < _Count)
                Array.Copy(_Items, _BufferOffset + index + 1, _Items, _BufferOffset + index, _Count - index);
            _Items[_BufferOffset + _Count] = default(T);
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
                Array.Copy(_Items, _BufferOffset + index + count, _Items, _BufferOffset + index, _Count - index);

            Array.Clear(_Items, _BufferOffset + _Count, count);
        }

        /// <summary>
        /// WARNING: This will not preserve the order of the list! If you want that, use the Ordered version
        /// </summary>
        public bool TryPopFront (out T result) {
            if (_Count == 0) {
                result = default(T);
                return false;
            }

            result = _Items[_BufferOffset + 0];
            DangerousRemoveAt(0);
            return true;
        }

        public bool TryPopFrontOrdered (out T result) {
            if (_Count == 0) {
                result = default(T);
                return false;
            }

            result = _Items[_BufferOffset + 0];
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

            result = _Items[_BufferOffset + 0];
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

            result = _Items[_BufferOffset + 0];
            RemoveAtOrdered(0);
            empty = _Count == 0;
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
                return _BufferSize;
            }
        }

        public void Clear () {
            Array.Clear(_Items, _BufferOffset, _Count);
            _Count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] GetBufferArray () {
            if (_BufferOffset != 0)
                throw new InvalidOperationException("This buffer is a subregion of an array. Use GetBuffer.");
            return _Items;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArraySegment<T> GetBuffer () {
            return new ArraySegment<T>(_Items, _BufferOffset, _BufferSize);
        }

        public T[] ToArray () {
            var result = new T[_Count];
            Array.Copy(_Items, _BufferOffset, result, 0, _Count);
            return result;
        }

        public void CopyTo (UnorderedList<T> output) {
            output.EnsureCapacity(output.Count + Count);
            for (int i = 0; i < _Count; i++)
                output.Add(ref _Items[_BufferOffset + i]);
        }

        public void CopyTo (T[] buffer, int offset, int count) {
            if (count > _Count)
                count = _Count;

            Array.Copy(_Items, _BufferOffset, buffer, offset, count);
        }

        public void Sort (IComparer<T> comparer = null) {
            Array.Sort(_Items, _BufferOffset, _Count, comparer);
        }

        public void FastCLRSort<TComparer> (TComparer comparer)
            where TComparer : IComparer<T>
        {
            var items = new ArraySegment<T>(_Items, _BufferOffset, _Count);
            Util.Sort.FastCLRSort(items, comparer, 0, _Count);
        }

        public void IndexedSort<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IComparer<T>
        {
            var items = new ArraySegment<T>(_Items, _BufferOffset, _Count);
            var _indices = new ArraySegment<int>(indices, 0, _Count);
            Util.Sort.IndexedSort(items, _indices, comparer);
        }

        public void FastCLRSortRef<TComparer> (TComparer comparer)
            where TComparer : IRefComparer<T>
        {
            var items = new ArraySegment<T>(_Items, _BufferOffset, _Count);
            Util.Sort.FastCLRSortRef(items, comparer, 0, _Count);
        }

        public void IndexedSortRef<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T>
        {
            var items = new ArraySegment<T>(_Items, _BufferOffset, _Count);
            var _indices = new ArraySegment<int>(indices, 0, _Count);
            Util.Sort.IndexedSortRef(items, _indices, comparer);
        }

        public ArraySegment<T> ReserveSpace (int count) {
            var newCount = _Count + count;
            EnsureCapacity(newCount);
            var oldCount = _Count;
            _Count = newCount;
            return new ArraySegment<T>(_Items, oldCount + _BufferOffset, count);
        }
    }
}

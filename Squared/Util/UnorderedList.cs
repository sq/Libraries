using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Squared.Util {
    public class UnorderedList<T> : IEnumerable<T> {
        public const int DefaultSize = 128;

        protected T[] _Items;
        protected int _Count;

        public struct Enumerator : IEnumerator<T>{
            UnorderedList<T> _List;
            int _Index, _Offset, _Count;

            internal Enumerator (UnorderedList<T> list, int start, int count) {
                _List = list;
                _Index = -1;
                _Offset = start;
                _Count = count;
            }

            public T Current {
                get { return _List._Items[_Index + _Offset]; }
            }

            public void Dispose () {
            }

            object System.Collections.IEnumerator.Current {
                get { return _List._Items[_Index + _Offset]; }
            }

            public void SetCurrent (ref T newValue) {
                _List._Items[_Index + _Offset] = newValue;
            }

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

            public bool MoveNext () {
                _Index += 1;
                return (_Index < _Count);
            }

            public void Reset () {
                _Index = -1;
            }

            public void RemoveCurrent () {
                _List.RemoveAt(_Index + _Offset);
                _Count -= 1;
                _Index -= 1;
            }
        }

        public UnorderedList () {
            _Items = new T[DefaultSize];
            _Count = 0;
        }

        public UnorderedList (int size) {
            _Items = new T[Math.Max(DefaultSize, size)];
            _Count = 0;
        }

        public UnorderedList (T[] values) {
            _Items = new T[Math.Max(DefaultSize, values.Length)];
            _Count = values.Length;
            Array.Copy(values, _Items, _Count);
        }

        public Enumerator GetParallelEnumerator (int partitionIndex, int partitionCount) {
            int partitionSize = (int)Math.Ceiling(_Count / (float)partitionCount);
            int start = partitionIndex * partitionSize;
            return new Enumerator(this, start, Math.Min(_Count - start, partitionSize));
        }

        public Enumerator GetEnumerator () {
            return new Enumerator(this, 0, _Count);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(this, 0, _Count);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(this, 0, _Count);
        }

        public void EnsureCapacity (int capacity) {
            if (_Items.Length >= capacity)
                return;

            var newCapacity = 1 << (int)Math.Ceiling(Math.Log(capacity, 2));
            var oldItems = _Items;
            _Items = new T[newCapacity];
            Array.Copy(oldItems, _Items, _Count);
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
        public void AddRange (T[] items) {
            int newCount = _Count + items.Length;
            EnsureCapacity(newCount);

            int insertOffset = newCount - items.Length;
            for (var i = 0; i < items.Length; i++)
                _Items[insertOffset + i] = items[i];

            _Count = newCount;
        }

        public bool Contains (T item) {
            var index = Array.IndexOf(_Items, item, 0, _Count);
            return (index >= 0);
        }

        // FIXME: These really shouldn't be here
        public void RemoveAt (int index) {
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();

            var newCount = _Count - 1;

            if (index < newCount) {
                _Items[index] = _Items[newCount];
                _Items[newCount] = default(T);
            } else {
                _Items[index] = default(T);
            }

            _Count = newCount;
        }

        public void RemoveRange (int index, int count) {
            if (count <= 0)
                return;
            if ((index < 0) || (index >= _Count))
                throw new IndexOutOfRangeException();
            if ((index + count) > _Count)
                throw new ArgumentOutOfRangeException("count");

            var replacementCount = Math.Min(count, _Count - (index + count));

            if (replacementCount > 0)
                Array.Copy(_Items, index + count, _Items, index, replacementCount);

            Array.Clear(_Items, index + replacementCount, count);

            _Count -= count;
        }

        public bool TryPopFront (out T result) {
            if (_Count == 0) {
                result = default(T);
                return false;
            }

            result = _Items[0];
            RemoveAt(0);
            return true;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _Count;
            }
        }

        public int Capacity {
            get {
                return _Items.Length;
            }
        }

        public void Clear () {
            Array.Clear(_Items, 0, _Count);
            _Count = 0;
        }

        public T[] GetBuffer () {
            return _Items;
        }

        public T[] ToArray () {
            var result = new T[_Count];
            Array.Copy(_Items, result, _Count);
            return result;
        }

        public void CopyTo (T[] buffer, int offset, int count) {
            if (count > _Count)
                count = _Count;

            Array.Copy(_Items, 0, buffer, offset, count);
        }

        public void Sort (IComparer<T> comparer = null) {
            Array.Sort(_Items, 0, _Count, comparer);
        }

        public void Timsort (Comparison<T> comparison = null) {
            Squared.Util.Sort.Timsort(_Items, 0, _Count, comparison);
        }

        public void Timsort (IComparer<T> comparer = null) {
            Squared.Util.Sort.Timsort(_Items, 0, _Count, comparer);
        }

        public ArraySegment<T> ReserveSpace (int count) {
            var newCount = _Count + count;
            EnsureCapacity(newCount);
            var oldCount = _Count;
            _Count = newCount;
            return new ArraySegment<T>(_Items, oldCount, count);
        }
    }
}

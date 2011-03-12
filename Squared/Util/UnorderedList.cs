using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections;

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
                return (_Index < _List._Count);
            }

            public void Reset () {
                _Index = -1;
            }

            public void RemoveCurrent () {
                _List.RemoveAt(_Index + _Offset);
                _Index -= 1;
            }
        }

        public UnorderedList () {
            _Items = new T[DefaultSize];
            _Count = 0;
        }

        public UnorderedList (int size) {
            _Items = new T[size];
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

        private void GrowBuffer () {
            var oldItems = _Items;
            _Items = new T[oldItems.Length * 2];
            Array.Copy(oldItems, _Items, _Count);
        }

        public void Add (T item) {
            Add(ref item);
        }

        public void Add (ref T item) {
            int newCount = _Count + 1;
            if (newCount >= _Items.Length)
                GrowBuffer();

            _Items[newCount - 1] = item;
            _Count = newCount;
        }

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
            get {
                return _Count;
            }
        }

        public void Clear () {
            _Count = 0;

            Array.Clear(_Items, 0, _Items.Length);
        }

        public T[] GetBuffer () {
            return _Items;
        }

        public T[] ToArray () {
            var result = new T[_Count];
            Array.Copy(_Items, result, _Count);
            return result;
        }
    }
}

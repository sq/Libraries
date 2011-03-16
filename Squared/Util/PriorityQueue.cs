using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading;

namespace Squared.Util {
    public static class HeapQueue<T> {
        public static void SiftDown (T[] buffer, int startPosition, int endPosition, Comparison<T> comparer) {
            T newItem = buffer[endPosition];
            int newItemPos = endPosition;

            while (endPosition > startPosition) {
                int sourcePosition = (endPosition - 1) >> 1;
                T sourceItem = buffer[sourcePosition];

                if (comparer(sourceItem, newItem) <= 0)
                    break;

                buffer[sourcePosition] = default(T);
                buffer[endPosition] = sourceItem;

                endPosition = sourcePosition;
            }

            if (comparer(buffer[newItemPos], newItem) == 0)
                buffer[newItemPos] = default(T);

            buffer[endPosition] = newItem;
        }

        public static void SiftUp (T[] buffer, int position, int endPosition, Comparison<T> comparer) {
            int startPosition = position;
            T newItem = buffer[position];
            int newItemPos = position;

            int childPosition = 2 * position + 1;
            int rightPosition;

            while (childPosition < endPosition) {
                rightPosition = childPosition + 1;
                if ((rightPosition < endPosition) && (
                    comparer(buffer[rightPosition], buffer[childPosition]) <= 0
                ))
                    childPosition = rightPosition;

                buffer[position] = buffer[childPosition];
                buffer[childPosition] = default(T);

                position = childPosition;
                childPosition = 2 * position + 1;
            }

            if (comparer(buffer[newItemPos], newItem) == 0)
                buffer[newItemPos] = default(T);

            buffer[position] = newItem;

            SiftDown(buffer, startPosition, position, comparer);
        }

        public static void Heapify (T[] buffer, int count, Comparison<T> comparer) {
            var positions = Enumerable.Range(0, count / 2).Reverse();
            foreach (int i in positions)
                SiftUp(buffer, i, count, comparer);
        }
    }

    public class PriorityQueue<T> : IEnumerable<T>, ICollection
        where T : IComparable<T> {
        public const int DefaultSize = 16;

        private T[] _Buffer = null;
        private int _Count = 0;
        private object _SyncRoot = new object();

        public readonly Comparison<T> Comparer;

        public PriorityQueue ()
            : this (DefaultSize) {
        }

        public PriorityQueue (Comparison<T> comparer) {
            Comparer = comparer;
            _Buffer = new T[DefaultSize];
        }

        public PriorityQueue (Comparison<T> comparer, int capacity) {
            Comparer = comparer;
            _Buffer = new T[capacity];
        }

        public PriorityQueue (int capacity) {
            Comparer = Comparer<T>.Default.Compare;
            _Buffer = new T[capacity];
        }

        public PriorityQueue (IEnumerable<T> contents) {
            Comparer = Comparer<T>.Default.Compare;
            _Buffer = contents.ToArray();
            _Count = _Buffer.Length;
            HeapQueue<T>.Heapify(_Buffer, _Count, Comparer);
        }

        public int Capacity {
            get {
                return _Buffer.Length;
            }
        }

        public int Count {
            get {
                return _Count;
            }
        }

        private void Resize (int newSize) {
            T[] newBuffer = new T[newSize];
            T[] oldBuffer = _Buffer;
            Array.Copy(oldBuffer, newBuffer, Math.Min(oldBuffer.Length, newBuffer.Length));
            _Buffer = newBuffer;
        }

        private void Grow (int numberOfItems) {
            _Count += numberOfItems;
            if (_Count > _Buffer.Length)
                Resize(_Buffer.Length * 2);
        }

        public void Enqueue (T value) {
            Grow(1);
            _Buffer[_Count - 1] = value;
            HeapQueue<T>.SiftDown(_Buffer, 0, _Count - 1, Comparer);
        }

        public T Dequeue () {
            if (_Count <= 0)
                throw new InvalidOperationException("The queue is empty.");

            _Count -= 1;
            T result = _Buffer[0];
            if (_Count > 0) {
                _Buffer[0] = _Buffer[_Count];
                _Buffer[_Count] = default(T);
                HeapQueue<T>.SiftUp(_Buffer, 0, _Count, Comparer);
            }

            return result;
        }

        public bool Peek (out T result) {
            if (_Count <= 0) {
                result = default(T);
                return false;
            } else {
                result = _Buffer[0];
                return true;
            }
        }

        public void Clear () {
            _Count = 0;
            Array.Clear(_Buffer, 0, _Buffer.Length);
        }

        public IEnumerator<T> GetEnumerator () {
            return _Buffer.TakeWhile((item, index) => index < _Count).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return _Buffer.TakeWhile((item, index) => index < _Count).GetEnumerator();
        }

        void ICollection.CopyTo (Array array, int index) {
            Array.Copy(_Buffer, 0, array, index, _Count);
        }

        bool ICollection.IsSynchronized {
            get { return false; }
        }

        object ICollection.SyncRoot {
            get { return _SyncRoot; }
        }
    }
}

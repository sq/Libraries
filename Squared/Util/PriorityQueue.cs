using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading;

namespace Squared.Util {
    public class PriorityQueue<T> where T : IComparable<T> {
        public delegate int ComparisonFunction (T lhs, T rhs);

        public const int DefaultSize = 16;

        private T[] _Buffer = null;
        private int _Count = 0;

        public ComparisonFunction Comparer;

        public static ComparisonFunction DefaultComparer {
            get {
                return (lhs, rhs) => (lhs.CompareTo(rhs));
            }
        }

        public PriorityQueue ()
            : this (DefaultSize) {
        }

        public PriorityQueue (int capacity) {
            Comparer = DefaultComparer;
            _Buffer = new T[capacity];
        }

        public PriorityQueue (IEnumerable<T> contents) {
            Comparer = DefaultComparer;
            _Buffer = contents.ToArray();
            _Count = _Buffer.Length;
            Heapify(_Buffer, _Count, Comparer);
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

        public static void SiftDown (T[] buffer, int startPosition, int endPosition, ComparisonFunction comparer) {
            T newItem = buffer[endPosition];
            while (endPosition > startPosition) {
                int sourcePosition = (endPosition - 1) >> 1;
                T sourceItem = buffer[sourcePosition];
                if (comparer(sourceItem, newItem) <= 0)
                    break;
                buffer[endPosition] = sourceItem;
                endPosition = sourcePosition;
            }
            buffer[endPosition] = newItem;
        }

        public static void SiftUp (T[] buffer, int position, int endPosition, ComparisonFunction comparer) {
            int startPosition = position;
            T newItem = buffer[position];
            int childPosition = 2 * position + 1;
            int rightPosition;
            while (childPosition < endPosition) {
                rightPosition = childPosition + 1;
                if ((rightPosition < endPosition) && (comparer(buffer[rightPosition], buffer[childPosition]) <= 0))
                    childPosition = rightPosition;
                buffer[position] = buffer[childPosition];
                position = childPosition;
                childPosition = 2 * position + 1;
            }
            buffer[position] = newItem;
            SiftDown(buffer, startPosition, position, comparer);
        }

        public static void Heapify (T[] buffer, int count, ComparisonFunction comparer) {
            var positions = Enumerable.Range(0, count / 2).Reverse();
            foreach (int i in positions)
                SiftUp(buffer, i, count, comparer);
        }

        public void Enqueue (T value) {
            Grow(1);
            _Buffer[_Count - 1] = value;
            SiftDown(_Buffer, 0, _Count - 1, Comparer);
        }

        public T Dequeue () {
            if (_Count <= 0)
                throw new InvalidOperationException("The queue is empty.");

            _Count -= 1;
            T result = _Buffer[0];
            if (_Count > 0) {
                _Buffer[0] = _Buffer[_Count];
                SiftUp(_Buffer, 0, _Count, Comparer);
            }

            return result;
        }
    }
}

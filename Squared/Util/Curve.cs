using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util {
    public struct Pair<T> {
        public T Left, Right;

        public Pair (T both) {
            Left = Right = both;
        }

        public Pair (T left, T right) {
            Left = left;
            Right = right;
        }

        public T[] ToArray () {
            return new T[] { Left, Right };
        }

        public T this[int index] {
            get {
                if (index == 0)
                    return Left;
                else if (index == 1)
                    return Right;
                else
                    throw new IndexOutOfRangeException();
            }
        }
    }

    public class Curve<T> {
        SortedList<float, T> _Items = new SortedList<float, T>();

        public float Start {
            get {
                return _Items.Keys[0];
            }
        }

        public float End {
            get {
                return _Items.Keys[_Items.Count - 1];
            }
        }

        public Pair<int> GetIndexPairAtPosition (float position) {
            var keys = _Items.Keys;
            int count = _Items.Count;
            int low = 0;
            int high = count - 1;
            int index = low;
            int nextIndex = Math.Min(low + 1, high);

            if (position < Start) {
                return new Pair<int>(0);
            } else if (position >= End) {
                return new Pair<int>(count - 1);
            } else if (_Items.ContainsKey(position)) {
                index = _Items.IndexOfKey(position);
                return new Pair<int>(index, Math.Min(index + 1, count - 1));
            }

            while (low <= high) {
                index = (low + high) / 2;
                nextIndex = Math.Min(index + 1, count - 1);
                float key = keys[index];
                if (low == high)
                    break;
                if (key < position) {
                    if ((nextIndex >= count) || (keys[nextIndex] > position)) {
                        break;
                    } else {
                        low = index + 1;
                    }
                } else if (key == position) {
                    break;
                } else {
                    high = index - 1;
                }
            }

            return new Pair<int>(index, nextIndex);
        }

        public float GetPositionAtIndex (int index) {
            return _Items.Keys[index];
        }

        private T GetValueAtPosition (float position) {
            return GetValueAtIndexPair(GetIndexPairAtPosition(position));
        }

        private Pair<T> GetValuePair (Pair<int> indexPair) {
            var values = _Items.Values;
            T left = values[indexPair.Left];
            T right = values[indexPair.Right];
            return new Pair<T>(left, right);
        }

        private T GetValueAtIndexPair (Pair<int> indexPair) {
            var pair = GetValuePair(indexPair);
            return pair.Left;
        }

        public T this[Pair<int> indexPair] {
            get {
                return GetValueAtIndexPair(indexPair);
            }
        }

        public T this[float position] {
            get {
                return GetValueAtPosition(position);
            }
            set {
                _Items[position] = value;
            }
        }
    }
}

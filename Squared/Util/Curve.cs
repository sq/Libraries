using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

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

    public delegate T InterpolatorSource<T> (int index) where T : struct;
    public delegate T Interpolator<T> (InterpolatorSource<T> data, int dataOffset, float positionInWindow) where T : struct;

    public static class Interpolators<T> 
        where T : struct {
        delegate T LinearFn (T a, T b, float c);
        private static LinearFn _Linear = null;

        static Interpolators () {
            Arithmetic.CompileExpression(
                (a, b, c) => a + ((b - a) * c),
                out _Linear
            );
        }

        public static T Null (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
            return data(dataOffset);
        }

        public static T Linear (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
            return _Linear(
                data(dataOffset), 
                data(dataOffset + 1), 
                positionInWindow
            );
        }

        public static Interpolator<T> Default {
            get {
                return Linear;
            }
        }
    }

    public class Curve<T> where T : struct {
        public Interpolator<T> Interpolator;
        SortedList<float, T> _Items = new SortedList<float, T>();

        public Curve () {
            Interpolator = Interpolators<T>.Default;
        }

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

        public T GetValueAtIndex (int index) {
            var values = _Items.Values;
            index = Math.Min(Math.Max(0, index), values.Count - 1);
            return values[index];
        }

        private T GetValueAtPosition (float position) {
            var indexPair = GetIndexPairAtPosition(position);
            var positionPair = GetPositionPair(indexPair);
            float offset = 0.0f;
            if (indexPair.Left != indexPair.Right)
                offset = (position - positionPair.Left) / (positionPair.Right - positionPair.Left);
            return GetValueFromIndexPair(indexPair, offset);
        }

        private Pair<float> GetPositionPair (Pair<int> indexPair) {
            var keys = _Items.Keys;
            return new Pair<float>(keys[indexPair.Left], keys[indexPair.Right]);
        }

        private Pair<T> GetValuePair (Pair<int> indexPair) {
            var values = _Items.Values;
            return new Pair<T>(values[indexPair.Left], values[indexPair.Right]);
        }

        private T GetValueFromIndexPair (Pair<int> indexPair, float offset) {
            return Interpolator(GetValueAtIndex, indexPair.Left, offset);
        }

        public void Clamp (float newStartPosition, float newEndPosition) {
            T newStartValue = GetValueAtPosition(newStartPosition);
            T newEndValue = GetValueAtPosition(newEndPosition);

            float[] keys = _Items.Keys.ToArray();
            foreach (float position in keys) {
                if ((position <= newStartPosition) || (position >= newEndPosition))
                    _Items.Remove(position);
            }

            _Items[newStartPosition] = newStartValue;
            _Items[newEndPosition] = newEndValue;
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

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

    public interface IInterpolator<T> where T : struct {
        int WindowOffset {
            get;
        }

        int WindowSize {
            get;
        }

        T Interpolate (IEnumerator<T> window, float positionInWindow);
    }

    public struct NullInterpolator<T> : IInterpolator<T> where T : struct {
        public int WindowOffset {
            get { return 0; }
        }

        public int WindowSize {
            get { return 1; }
        }

        public T Interpolate (IEnumerator<T> window, float positionInWindow) {
            window.MoveNext();
            T result = window.Current;
            window.Dispose();
            return result;
        }
    }

    public struct LinearInterpolator<T> : IInterpolator<T> where T : struct {
        public int WindowOffset {
            get { return 0; }
        }

        public int WindowSize {
            get { return 2; }
        }

        public T Interpolate (IEnumerator<T> window, float positionInWindow) {
            window.MoveNext();
            T a = window.Current;
            window.MoveNext();
            T b = window.Current;
            window.Dispose();
            // a + ((b - a) * positionInWindow)
            return Arithmetic.Add(
                a, 
                Arithmetic.Multiply(
                    Arithmetic.Subtract(b, a), 
                    positionInWindow
                )
            );
        }
    }

    public class Curve<T> where T : struct {
        public IInterpolator<T> Interpolator;
        SortedList<float, T> _Items = new SortedList<float, T>();

        public Curve () {
            Interpolator = new LinearInterpolator<T>();
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

        private IEnumerator<T> GetWindow (int firstIndex, int lastIndex) {
            var values = _Items.Values;
            int max = values.Count - 1;
            for (int i = firstIndex; i <= lastIndex; i++) {
                int j = Math.Max(Math.Min(i, max), 0);
                yield return values[j];
            }
        }

        private T GetValueFromIndexPair (Pair<int> indexPair, float offset) {
            int first = indexPair.Left + Interpolator.WindowOffset;
            int last = first + Interpolator.WindowSize - 1;
            var window = GetWindow(first, last);
            return Interpolator.Interpolate(window, offset);
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

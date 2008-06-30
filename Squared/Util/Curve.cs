using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Linq.Expressions;

namespace Squared.Util {
    public delegate T InterpolatorSource<T> (int index) where T : struct;
    public delegate T Interpolator<T> (InterpolatorSource<T> data, int dataOffset, float positionInWindow) where T : struct;

    public static class Interpolators<T> 
        where T : struct {

        delegate T LinearFn (T a, T b, float x);
        delegate T CosineFn (T a, T b, float x);
        delegate T CubicPFn (T a, T b, T c, T d);
        delegate T CubicRFn (T a, T b, T c, T d, T p, float x, float x2, float x3);

        private static LinearFn _Linear = null;
        private static CosineFn _Cosine = null;
        private static CubicPFn _CubicP = null;
        private static CubicRFn _CubicR = null;

        static Interpolators () {
            Arithmetic.CompileExpression(
                (a, b, x) => 
                    a + ((b - a) * x),
                out _Linear
            );

            Arithmetic.CompileExpression(
                (a, b, x) =>
                    a + ((b - a) * ((1.0f - Math.Cos(x * Math.PI)) * 0.5f)),
                out _Cosine
            );

            Arithmetic.CompileExpression(
                (a, b, c, d) => 
                    (d - c) - (a - b),
                out _CubicP
            );

            Arithmetic.CompileExpression(
                (a, b, c, d, p, x, x2, x3) =>
                    (p * x3) + ((a - b - p) * x2) + ((c - a) * x) + b,
                out _CubicR
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

        public static T Cosine (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
            return _Cosine(
                data(dataOffset),
                data(dataOffset + 1),
                positionInWindow
            );
        }

        public static T Cubic (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
            T a = data(dataOffset - 1);
            T b = data(dataOffset);
            T c = data(dataOffset + 1);
            T d = data(dataOffset + 2);
            T p = _CubicP(a, b, c, d);
            float x2 = positionInWindow * positionInWindow;
            float x3 = positionInWindow * x2;
            return _CubicR(a, b, c, d, p, positionInWindow, x2, x3);
        }

        public static Interpolator<T> GetByName (string name) {
            Type myType = typeof(Interpolators<T>);
            Type resultType = typeof(Interpolator<T>);
            MethodInfo mi = myType.GetMethod(name);
            MethodInfo defaultMethod = myType.GetMethod("Null");
            return Delegate.CreateDelegate(resultType, mi ?? defaultMethod)
                as Interpolator<T>;
        }

        public static Interpolator<T> Default {
            get {
                return Linear;
            }
        }
    }

    public class Curve<T> where T : struct {
        public Interpolator<T> Interpolator;
        private InterpolatorSource<T> _InterpolatorSource;
        SortedList<float, T> _Items = new SortedList<float, T>();

        public Curve () {
            Interpolator = Interpolators<T>.Default;
            _InterpolatorSource = GetValueAtIndex;
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

        public int GetLowerIndexForPosition (float position) {
            var keys = _Items.Keys;
            int count = _Items.Count;
            int low = 0;
            int high = count - 1;
            int index;
            int nextIndex;

            if (position < Start) {
                return 0;
            } else if (position >= End) {
                return count - 1;
            } else {
                index = _Items.IndexOfKey(position);
                if (index != -1)
                    return index;
                else
                    index = low;
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

            return index;
        }

        public float GetPositionAtIndex (int index) {
            var values = _Items.Values;
            index = Math.Min(Math.Max(0, index), values.Count - 1);
            return _Items.Keys[index];
        }

        public T GetValueAtIndex (int index) {
            var values = _Items.Values;
            index = Math.Min(Math.Max(0, index), values.Count - 1);
            return values[index];
        }

        private T GetValueAtPosition (float position) {
            int index = GetLowerIndexForPosition(position);
            float lowerPosition = GetPositionAtIndex(index);
            float upperPosition = GetPositionAtIndex(index + 1);
            if (lowerPosition < upperPosition) {
                float offset = (position - lowerPosition) / (upperPosition - lowerPosition);

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;
                
                return Interpolator(_InterpolatorSource, index, offset);
            } else {
                return _Items.Values[index];
            }
        }

        public void Clamp (float newStartPosition, float newEndPosition) {
            T newStartValue = GetValueAtPosition(newStartPosition);
            T newEndValue = GetValueAtPosition(newEndPosition);

            var keys = _Items.Keys;
            int i = 0;
            while (i < keys.Count) {
                float position = keys[i];
                if ((position <= newStartPosition) || (position >= newEndPosition)) {
                    _Items.RemoveAt(i);
                } else {
                    i++;
                }
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

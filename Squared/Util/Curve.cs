using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
#if !XBOX
using System.Linq.Expressions;
#endif

namespace Squared.Util {
    public delegate T InterpolatorSource<T> (int index) where T : struct;
    public delegate T Interpolator<T> (InterpolatorSource<T> data, int dataOffset, float positionInWindow) where T : struct;

    internal static class PrimitiveOperators {
        public static float op_Subtraction (float lhs, float rhs) {
            return lhs - rhs;
        }

        public static float op_Addition (float lhs, float rhs) {
            return lhs + rhs;
        }

        public static float op_Multiply (float lhs, float rhs) {
            return lhs * rhs;
        }

        public static double op_Subtraction (double lhs, double rhs) {
            return lhs - rhs;
        }

        public static double op_Addition (double lhs, double rhs) {
            return lhs + rhs;
        }

        public static double op_Multiply (double lhs, float rhs) {
            return lhs * rhs;
        }
    }

    public static class Interpolators<T> 
        where T : struct {

        delegate T LinearFn (T a, T b, float x);
        delegate T CosineFn (T a, T b, float x);
        delegate T CubicPFn (T a, T b, T c, T d);
        delegate T CubicRFn (T a, T b, T c, T d, T p, float x, float x2, float x3);

        delegate T AddFn (T a, T b);
        delegate T SubFn (T a, T b);
        delegate T MulFloatFn (T a, float b);

        private static LinearFn _Linear = null;
        private static CosineFn _Cosine = null;
        private static CubicPFn _CubicP = null;
        private static CubicRFn _CubicR = null;

        static Interpolators () {
            CompileFallbackExpressions();
            CompileNativeExpressions();
        }

        private static U GetOperator<U> (string name, Type[] argumentTypes) 
            where U : class {
            var methodInfo = typeof(T).GetMethod(name, argumentTypes);

            if (methodInfo == null)
                methodInfo = typeof(PrimitiveOperators).GetMethod(name, argumentTypes);

            if (methodInfo == null)
                throw new InvalidOperationException(String.Format("No operator named {0} available for type {1}", name, typeof(T).Name));

            var del = Delegate.CreateDelegate(typeof(U), null, methodInfo);
            return del as U;
        }

        private static void CompileFallbackExpressions () {
            Type t = typeof(T);
            Type t_float = typeof(float);

            var m_sub = GetOperator<SubFn>("op_Subtraction", new Type[] { t, t });
            var m_add = GetOperator<AddFn>("op_Addition", new Type[] { t, t });
            var m_mul_float = GetOperator<MulFloatFn>("op_Multiply", new Type[] { t, t_float });

            _Linear = (a, b, x) => {
                return m_add(a, m_mul_float(m_sub(b, a), x));
            };

            _Cosine = (a, b, x) => {
                var temp = (1.0f - (float)Math.Cos(x * Math.PI)) * 0.5f;
                return m_add(a, m_mul_float(m_sub(b, a), temp));
            };

            _CubicP = (a, b, c, d) => {
                return m_sub(m_sub(d, c), m_sub(a, b));
            };

            _CubicR = (a, b, c, d, p, x, x2, x3) => {
                return m_add(
                    m_add(
                        m_mul_float(p, x3),
                        m_mul_float(
                            m_sub(
                                m_sub(a, b),
                                p
                            ),
                            x2
                        )
                    ),
                    m_add(
                        m_mul_float(
                            m_sub(c, a),
                            x
                        ),
                        b
                    )
                );
            };
        }

        private static void CompileNativeExpressions () {
#if !XBOX
            Arithmetic.CompileExpression(
                (a, b, x) =>
                    a + ((b - a) * x),
                out _Linear
            );

            Arithmetic.CompileExpression(
                (a, b, x) =>
                    a + ((b - a) * ((1.0f - (float)Math.Cos(x * Math.PI)) * 0.5f)),
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
#endif
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
            return Delegate.CreateDelegate(resultType, null, mi ?? defaultMethod)
                as Interpolator<T>;
        }

        public static Interpolator<T> Default {
            get {
                return Linear;
            }
        }
    }

    public class Curve<T> : IEnumerable<Curve<T>.Point>
        where T : struct {
        public Interpolator<T> Interpolator;
        private InterpolatorSource<T> _InterpolatorSource;
        SortedList<float, TItem> _Items = new SortedList<float, TItem>();

        public struct Point {
            public float Position;
            public T Value;
            public Interpolator<T> Interpolator;
        }

        private struct TItem {
            public T Value;
            public Interpolator<T> Interpolator;
        }

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
            return values[index].Value;
        }

        private T GetValueAtPosition (float position) {
            int index = GetLowerIndexForPosition(position);
            float lowerPosition = GetPositionAtIndex(index);
            float upperPosition = GetPositionAtIndex(index + 1);
            Interpolator<T> interpolator = _Items.Values[index].Interpolator ?? Interpolator;
            if (lowerPosition < upperPosition) {
                float offset = (position - lowerPosition) / (upperPosition - lowerPosition);

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;
                
                return interpolator(_InterpolatorSource, index, offset);
            } else {
                return _Items.Values[index].Value;
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

            SetValueAtPosition(newStartPosition, newStartValue);
            SetValueAtPosition(newEndPosition, newEndValue);
        }

        public void SetValueAtPosition (float position, T value, Interpolator<T> interpolator) {
            TItem item = new TItem { Value = value, Interpolator = interpolator };
            _Items[position] = item;
        }

        public void SetValueAtPosition (float position, T value) {
            SetValueAtPosition(position, value, null);
        }

        public T this[float position] {
            get {
                return GetValueAtPosition(position);
            }
            set {
                SetValueAtPosition(position, value);
            }
        }

        public void Add (float position, T value) {
            SetValueAtPosition(position, value);
        }

        public void Add (float position, T value, Interpolator<T> interpolator) {
            SetValueAtPosition(position, value, interpolator);
        }

        public IEnumerator<Curve<T>.Point> GetEnumerator () {
            foreach (var item in _Items)
                yield return new Point {
                    Position = item.Key,
                    Value = item.Value.Value,
                    Interpolator = item.Value.Interpolator
                };
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return this.GetEnumerator();
        }
    }
}

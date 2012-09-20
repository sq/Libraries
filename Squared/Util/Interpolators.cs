using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Squared.Util {
    public delegate T InterpolatorSource<out T> (int index) where T : struct;
    public delegate T Interpolator<T> (InterpolatorSource<T> data, int dataOffset, float positionInWindow) where T : struct;
    public delegate T BoundInterpolatorSource<out T, U> (ref U obj, int index) where T : struct;
    public delegate T BoundInterpolator<T, U> (BoundInterpolatorSource<T, U> data, ref U obj, int dataOffset, float positionInWindow) where T : struct;

    public static class Interpolators<T>
        where T : struct {

        public delegate T LinearFn (T a, T b, float x);
        public delegate T CosineFn (T a, T b, float x);
        public delegate T CubicPFn (T a, T b, T c, T d);
        public delegate T CubicRFn (T a, T b, T c, T d, T p, float x, float x2, float x3);

        private static LinearFn _Linear = null;
        private static CosineFn _Cosine = null;
        private static CubicPFn _CubicP = null;
        private static CubicRFn _CubicR = null;

        static Interpolators () {
            CompileFallbackExpressions();
            CompileNativeExpressions();
        }

        private static void CompileFallbackExpressions () {
            var m_sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
            var m_add = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Add);
            var m_mul_float = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Multiply);

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
#if WINDOWS
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
            if (positionInWindow < 0) {
                var n = Math.Ceiling(Math.Abs(positionInWindow));
                positionInWindow += (float)n;
                dataOffset -= (int)n;
            }

            T a = data(dataOffset - 1);
            T b = data(dataOffset);
            T c = data(dataOffset + 1);
            T d = data(dataOffset + 2);
            T p = _CubicP(a, b, c, d);
            float x2 = positionInWindow * positionInWindow;
            float x3 = positionInWindow * x2;
            return _CubicR(a, b, c, d, p, positionInWindow, x2, x3);
        }

        public static T Null<U> (BoundInterpolatorSource<T, U> data, ref U obj, int dataOffset, float positionInWindow) {
            return data(ref obj, dataOffset);
        }

        public static T Linear<U> (BoundInterpolatorSource<T, U> data, ref U obj, int dataOffset, float positionInWindow) {
            return _Linear(
                data(ref obj, dataOffset),
                data(ref obj, dataOffset + 1),
                positionInWindow
            );
        }

        public static T Cosine<U> (BoundInterpolatorSource<T, U> data, ref U obj, int dataOffset, float positionInWindow) {
            return _Cosine(
                data(ref obj, dataOffset),
                data(ref obj, dataOffset + 1),
                positionInWindow
            );
        }

        public static T Cubic<U> (BoundInterpolatorSource<T, U> data, ref U obj, int dataOffset, float positionInWindow) {
            if (positionInWindow < 0) {
                var n = Math.Ceiling(Math.Abs(positionInWindow));
                positionInWindow += (float)n;
                dataOffset -= (int)n;
            }

            T a = data(ref obj, dataOffset - 1);
            T b = data(ref obj, dataOffset);
            T c = data(ref obj, dataOffset + 1);
            T d = data(ref obj, dataOffset + 2);
            T p = _CubicP(a, b, c, d);
            float x2 = positionInWindow * positionInWindow;
            float x3 = positionInWindow * x2;
            return _CubicR(a, b, c, d, p, positionInWindow, x2, x3);
        }

        public static Interpolator<T> GetByName (string name) {
            var types = new Type[] {
                typeof(InterpolatorSource<T>), typeof(int), typeof(float)
            };
            Type myType = typeof(Interpolators<T>);
            Type resultType = typeof(Interpolator<T>);
            MethodInfo mi = myType.GetMethod(name, types);
            if (mi == null)
                mi = myType.GetMethod("Null", types);
            return Delegate.CreateDelegate(resultType, null, mi) as Interpolator<T>;
        }

        public static Interpolator<T> Default {
            get {
                return Linear;
            }
        }
    }
}

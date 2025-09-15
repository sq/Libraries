﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Squared.Util {
    public delegate ref readonly T InterpolatorSource<T> (int index) where T : struct;
    public delegate T Interpolator<T> (InterpolatorSource<T> data, int dataOffset, float positionInWindow) where T : struct;
    public delegate ref readonly T BoundInterpolatorSource<T, U> (in U obj, int index) where T : struct;
    public delegate T BoundInterpolator<T, U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) where T : struct;

    public static class Interpolators<T>
        where T : struct {

        private static class BoundDefaultCache<U> {
            public static BoundInterpolator<T, U> Linear = Interpolators<T>.Linear<U>;
        }

        public delegate T LinearFn (T a, T b, float x);
        public delegate T CosineFn (T a, T b, float x);
        public delegate T CubicFn (T a, T b, T c, T d, float x);
        public delegate T HermiteFn (T a, T u, T d, T v, float t, float t2, float tSquared, float s, float s2, float sSquared);

        internal static LinearFn _Linear = null;
        internal static CosineFn _Cosine = null;
        internal static CubicFn _Cubic = null;
        internal static HermiteFn _Hermite = null;

        static Interpolators () {
            FindPrecompiledExpressions(typeof(T));
            FindPrecompiledExpressions(typeof(DefaultInterpolators));
            try {
                CompileNativeExpressions();
            } catch {
            }
            try {
                CompileFallbackExpressions();
            } catch {
            }
        }

        private static bool FindPrecompiledExpression<TDelegate> (out TDelegate result, Type type, string name, Type[] signature)
            where TDelegate : Delegate {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var method = type.GetMethod(name, flags, null, signature, null);
            if (method != null) {
                result = (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), method, false);
                if (result == null)
                    System.Diagnostics.Debug.WriteLine($"Failed to bind method '{type.FullName}.{method.Name}' as a delegate of type {typeof(TDelegate).FullName}");
                return result != null;
            } else {
                result = null;
                return false;
            }
        }

        private static void FindPrecompiledExpressions (Type type) {
            var basicSignature = new[] { typeof(T), typeof(T), typeof(float) };
            if (_Linear == null)
                if (!FindPrecompiledExpression(out _Linear, type, "Lerp", basicSignature))
                    FindPrecompiledExpression(out _Linear, type, "Linear", basicSignature);

            if (_Cosine == null)
                FindPrecompiledExpression(out _Cosine, type, "Cosine", basicSignature);

            if (_Cubic == null)
                FindPrecompiledExpression(out _Cubic, type, "Cubic", typeof(CubicFn).GetMethod("Invoke").GetParameters().Select(p => p.ParameterType).ToArray());
        }

        private static void CompileFallbackExpressions () {
            var m_sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract, optional: _Linear != null);
            var m_add = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Add, optional: _Linear != null);
            var m_mul_float = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Multiply, optional: _Linear != null);

            if (_Linear == null)
                _Linear = (a, b, x) => {
                    return m_add(a, m_mul_float(m_sub(b, a), x));
                };

            if (_Cosine == null) {
                if (_Linear != null)
                    _Cosine = (a, b, x) => {
                        var temp = (1.0f - (float)Math.Cos(x * Math.PI)) * 0.5f;
                        return _Linear(a, b, temp);
                    };
                else
                    _Cosine = (a, b, x) => {
                        var temp = (1.0f - (float)Math.Cos(x * Math.PI)) * 0.5f;
                        return m_add(a, m_mul_float(m_sub(b, a), temp));
                    };
            }

            if ((_Cubic == null) && (m_add != null) &&
                (m_mul_float != null) && (m_sub != null)
            )
                _Cubic = (a, b, c, d, x) => {
                    var x2 = x * x;
                    var x3 = x2 * x;
                    var p = m_sub(m_sub(d, c), m_sub(a, b));
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

            if ((_Hermite == null) && (m_add != null) &&
                (m_sub != null) && (m_mul_float != null))
                _Hermite = (a, u, d, v, t, t2, tSquared, s, s2, sSquared) => {
                    return m_sub(
                        m_add(
                            m_add(
                                m_mul_float(a, sSquared * (1 + t2)),
                                m_mul_float(d, tSquared * (1 + s2))
                            ),
                            m_mul_float(u, sSquared * t)
                        ),
                        m_mul_float(v, s * tSquared)
                    );
                };
        }

        private static void CompileExpressionTree<TDelegate> (ParameterExpression[] parameters, Expression body, out TDelegate result)
            where TDelegate : class
        {
            var lambda = Expression.Lambda(typeof(TDelegate), body, parameters);
            result = (TDelegate)(object)lambda.Compile();
        }

        private static void CompileNativeExpressions () {
#if !NODYNAMICMETHOD
            var t = typeof(T);

            ParameterExpression a = Expression.Parameter(t, "a"),
                b = Expression.Parameter(t, "b"),
                c = Expression.Parameter(t, "c"),
                d = Expression.Parameter(t, "d"),
                p = Expression.Parameter(t, "p"),
                x = Expression.Parameter(typeof(float), "x"),
                x2 = Expression.Parameter(typeof(float), "x2");

            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo addOperator = t.GetMethod("op_Addition", flags, null, new[] { t, t }, null),
                subtractOperator = t.GetMethod("op_Subtraction", flags, null, new[] { t, t }, null),
                scaleOperator = t.GetMethod("op_Multiply", flags, null, new[] { t, typeof(float) }, null);

            // If the basic operations we need aren't present, we can't use LINQ to compile the interpolators
            if ((addOperator == null) || (subtractOperator == null) || (scaleOperator == null)) {
                // These will also be missing for primitive types. That's fine, we implemented the key
                //  ones down below in DefaultInterpolators.
                return;
            }

            if (_Linear == null)
                CompileExpressionTree(
                    // a + ((b - a) * x)
                    new[] { a, b, x },
                    Expression.Add(a, Expression.Multiply(Expression.Subtract(b, a), x)),
                    out _Linear
                );

            // FIXME: This is the best we can do
            if (_Cosine == null)
                CompileExpressionTree(
                    /* a + (
                        (b - a) * 
                        (1.0f - (float)Math.Cos(x * Math.PI)) *
                        0.5f
                    ) */
                    new[] { a, b, x },
                    Expression.Add(a,
                        Expression.Multiply(
                            Expression.Subtract(b, a),
                            Expression.Multiply(
                                Expression.Constant(0.5f),
                                Expression.Subtract(
                                    Expression.Constant(1.0f), 
                                    Expression.Convert(
                                        Expression.Call(
                                            ((Func<double, double>)Math.Cos).Method, 
                                            Expression.Multiply(Expression.Convert(x, typeof(double)), Expression.Constant(Math.PI))
                                        ),
                                        typeof(float)
                                    )
                                )
                            )
                        )
                    ),
                    out _Cosine
                );

            if (_Cubic == null) {
                var x3 = Expression.Multiply(x2, x);
                CompileExpressionTree(
                    new[] { a, b, c, d, x },
                    Expression.Block(
                        new [] { p, x2 },
                        Expression.Assign(
                            x2, Expression.Multiply(x, x)
                        ),
                        // (d - c) - (a - b)
                        Expression.Assign(
                            p, 
                            Expression.Subtract(
                                Expression.Subtract(d, c),
                                Expression.Subtract(a, b)
                            )
                        ),
                        /*
                            (p * x3) +
                            ((a - b - p) * x2) + 
                            ((c - a) * x) + 
                            b
                        */
                        Expression.Add(
                            Expression.Add(
                                Expression.Add(
                                    Expression.Multiply(p, x3),
                                    Expression.Multiply(Expression.Subtract(Expression.Subtract(a, b), p), x2)
                                ),
                                Expression.Multiply(Expression.Subtract(c, a), x)
                            ),
                            b
                        )
                    ),
                    out _Cubic
                );
            }
#endif
        }

        public static T Null (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
            return (positionInWindow >= 1)
                ? data(dataOffset + 1)
                : data(dataOffset);
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
            return _Cubic(a, b, c, d, positionInWindow);
        }

        public static T Hermite (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
            if (positionInWindow < 0) {
                var n = Math.Ceiling(Math.Abs(positionInWindow));
                positionInWindow += (float)n;
                dataOffset -= (int)n;
            }

            T a = data(dataOffset);
            T u = data(dataOffset + 1);
            T d = data(dataOffset + 2);
            T v = data(dataOffset + 3);

            var tSquared = positionInWindow * positionInWindow;
            var t2 = positionInWindow * 2;
            var s = 1 - positionInWindow;
            var s2 = s * 2;
            var sSquared = s * s;

            return _Hermite(a, u, d, v, positionInWindow, t2, tSquared, s, s2, sSquared);
        }

        public static T Null<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
            return data(in obj, dataOffset);
        }

        public static T Linear<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
            return _Linear(
                data(in obj, dataOffset),
                data(in obj, dataOffset + 1),
                positionInWindow
            );
        }

        public static T Cosine<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
            return _Cosine(
                data(in obj, dataOffset),
                data(in obj, dataOffset + 1),
                positionInWindow
            );
        }

        private sealed class ExponentialInterpolator {
            public float ExponentIn, ExponentMidpoint, ExponentOut;

            public T Interpolate<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
                var centered = positionInWindow - 0.5f;
                var exponent1 = Arithmetic.Lerp(ExponentIn, ExponentMidpoint, positionInWindow * 2f);
                var exponent2 = Arithmetic.Lerp(ExponentMidpoint, ExponentOut, centered);
                var left =
                    (centered < 0)
                        ? Math.Pow(positionInWindow * 2f, exponent1)
                        : 1;
                var right =
                    (centered >= 0)
                        ? 1 - Math.Pow(1 - (centered * 2f), exponent2)
                        : 0;
                var finalPosition = (left + right) / 2f;
                return _Linear(
                    data(in obj, dataOffset),
                    data(in obj, dataOffset + 1),
                    (float)finalPosition
                );
            }
        }

        public static BoundInterpolator<T, U> Exponential<U> (float exponent = 2) {
            var obj = new ExponentialInterpolator {
                ExponentIn = exponent,
                ExponentMidpoint = exponent,
                ExponentOut = exponent
            };
            return obj.Interpolate;
        } 

        public static BoundInterpolator<T, U> Exponential<U> (float start, float? mid, float? end) {
            var obj = new ExponentialInterpolator {
                ExponentIn = start,
                ExponentMidpoint = mid ?? start,
                ExponentOut = end ?? start
            };
            return obj.Interpolate;
        } 

        public abstract class Easing {
            public abstract float GetT (float t);

            public T Get (InterpolatorSource<T> data, int dataOffset, float positionInWindow) {
                return _Linear(
                    data(dataOffset),
                    data(dataOffset + 1),
                    GetT(positionInWindow)
                );
            }
        }

        public static class Ease {
            internal sealed class EaseInSine : Easing {
                public override float GetT (float t) => 
                    (float)(1 - Math.Cos((t * Math.PI) / 2));
            }

            internal sealed class EaseOutSine : Easing {
                public override float GetT (float t) =>
                    (float)(Math.Sin((t * Math.PI) / 2));
            }

            internal sealed class EaseSine : Easing {
                public override float GetT (float t) => 
                    (float)-(Math.Cos(Math.PI * t) - 1) / 2;
            }

            public static readonly Easing InSine = new EaseInSine(),
                Sine = new EaseSine(),
                OutSine = new EaseOutSine();

            public class InExponential : Easing {
                public float Exponent = 2;

                public override float GetT (float t) => 
                    (float)Math.Pow(t, Exponent);
            }

            public class OutExponential : Easing {
                public float Exponent = 2;

                public override float GetT (float t) =>
                    1f - (float)Math.Pow(1 - t, Exponent);
            }

            public class Exponential : Easing {
                public float Exponent = 2;

                public override float GetT (float t) {
                    var tE = Math.Pow(t, Exponent);
                    return (float)(tE / (tE + Math.Pow(1 - t, Exponent)));
                }
            }
        }

        private sealed class EasedInterpolator<U> {
            internal static Dictionary<Easing, EasedInterpolator<U>> Cache =
                new Dictionary<Easing, EasedInterpolator<U>>(new ReferenceComparer<Easing>());

            public readonly Easing Ease;
            public readonly BoundInterpolator<T, U> Interpolate;

            public EasedInterpolator (Easing ease) {
                Ease = ease;
                Interpolate = _Interpolate;
            }

            private T _Interpolate (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
                var t = Ease.GetT(positionInWindow);
                return _Linear(
                    data(in obj, dataOffset),
                    data(in obj, dataOffset + 1),
                    t
                );
            }
        }

        internal static Dictionary<Easing, Interpolator<T>> EasedCache = 
            new Dictionary<Easing, Interpolator<T>>(new ReferenceComparer<Easing>());

        public static Interpolator<T> Eased (Easing easing) {
            lock (EasedCache) {
                if (!EasedCache.TryGetValue(easing, out var result)) {
                    result = easing.Get;
                    EasedCache[easing] = result;
                }
                return result;
            }
        }

        public static BoundInterpolator<T, U> Eased<U> (Easing easing) {
            var cache = EasedInterpolator<U>.Cache;
            lock (cache) {
                if (!cache.TryGetValue(easing, out var result)) {
                    result = new EasedInterpolator<U>(easing);
                    cache[easing] = result;
                }
                return result.Interpolate;
            }
        }

        // FIXME: This doesn't work but seems like it should
        /*
        public static T Quadratic<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
            // FIXME: Should we always do this? Should it be configurable?
            if (positionInWindow < 0) {
                var n = Math.Ceiling(Math.Abs(positionInWindow));
                positionInWindow += (float)n;
                dataOffset -= (int)n;
            }

            T a = data(in obj, dataOffset - 1),
                b = data(in obj, dataOffset),
                c = data(in obj, dataOffset + 1),
                ab = _Linear(a, b, positionInWindow),
                bc = _Linear(b, c, positionInWindow);
            return _Linear(ab, bc, positionInWindow);
        }
        */

        public static T Cubic<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
            // FIXME: Should we always do this? Should it be configurable?
            if (positionInWindow < 0) {
                var n = Math.Ceiling(Math.Abs(positionInWindow));
                positionInWindow += (float)n;
                dataOffset -= (int)n;
            }

            T a = data(in obj, dataOffset - 1);
            T b = data(in obj, dataOffset);
            T c = data(in obj, dataOffset + 1);
            T d = data(in obj, dataOffset + 2);
            return _Cubic(a, b, c, d, positionInWindow);
        }

        public static T Hermite<U> (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
            if (positionInWindow < 0) {
                var n = Math.Ceiling(Math.Abs(positionInWindow));
                positionInWindow += (float)n;
                dataOffset -= (int)n;
            }

            T a = data(in obj, dataOffset);
            T u = data(in obj, dataOffset + 1);
            T d = data(in obj, dataOffset + 2);
            T v = data(in obj, dataOffset + 3);

            var tSquared = positionInWindow * positionInWindow;
            var t2 = positionInWindow * 2;
            var s = 1 - positionInWindow;
            var s2 = s * 2;
            var sSquared = s * s;

            return _Hermite(a, u, d, v, positionInWindow, t2, tSquared, s, s2, sSquared);
        }

        // FIXME: Thread safety
        private static T[] _TemporaryValues = new T[4];
        private static int _NumTemporaryValues = 0;
        private static InterpolatorSource<T> _TemporarySource;

        private static ref readonly T TemporarySource (int index) {
            return ref _TemporaryValues[Arithmetic.Wrap(index, 0, _NumTemporaryValues - 1)];
        }

        public static T Interpolate (Interpolator<T> interpolator, in T a, in T b, float progress) {
            if ((_TemporaryValues == null) || (_TemporaryValues.Length < 2))
                _TemporaryValues = new T[2];
            if (_TemporarySource == null)
                _TemporarySource = TemporarySource;

            _TemporaryValues[0] = a;
            _TemporaryValues[1] = b;
            _NumTemporaryValues = 2;

            return interpolator(_TemporarySource, 0, progress);
        }

        public static T Interpolate (Interpolator<T> interpolator, T[] values, float progress) {
            var previousValues = _TemporaryValues;
            if (_TemporarySource == null)
                _TemporarySource = TemporarySource;

            try {
                _TemporaryValues = values;
                _NumTemporaryValues = values.Length;
                return interpolator(_TemporarySource, 0, progress);
            } finally {
                _TemporaryValues = previousValues;
            }
        }

        private static readonly Dictionary<string, Interpolator<T>> Cache = 
            new Dictionary<string, Interpolator<T>>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterNamed (string name, Interpolator<T> interpolator) {
            lock (Cache)
                Cache.Add(name, interpolator);
        }

        public static void RegisterNamed<U> (string name, BoundInterpolator<T, U> boundInterpolator) {
            lock (CacheContainer<U>.Cache)
                CacheContainer<U>.Cache.Add(name, boundInterpolator);
        }

        public static Interpolator<T> GetByName (string name) {
            Interpolator<T> result;

            lock (Cache)
            if (Cache.TryGetValue(name, out result))
                return result;

            var types = new Type[] {
                typeof(InterpolatorSource<T>), typeof(int), typeof(float)
            };
            Type myType = typeof(Interpolators<T>);
            Type resultType = typeof(Interpolator<T>);

            var mi = myType.GetMethod(name, BindingFlags.Static | BindingFlags.IgnoreCase | BindingFlags.Public, null, types, null);
            if (mi != null) {
                result = Delegate.CreateDelegate(resultType, null, mi) as Interpolator<T>;
            } else {
                var tname = $"{typeof(Ease).FullName}".Replace("+Ease[[", $"+Ease+{name}[[");
                var possibleType = typeof(Ease).Assembly.GetType(tname, false, true);
                if (possibleType != null)
                    result = Eased((Easing)Activator.CreateInstance(possibleType));
            }

            lock (Cache)
                Cache[name] = result;

            if (result == null)
                throw new ArgumentOutOfRangeException(nameof(name), $"No interpolator named '{name}'");
            return result;
        }

        public static BoundInterpolator<T, U> GetBoundDefault<U>() {
            return BoundDefaultCache<U>.Linear;
        }

        private class BoundUnboundAdapter<U> {
            private class DummySource {
                public U Obj;
                public BoundInterpolatorSource<T, U> Data;
                public readonly InterpolatorSource<T> Get;

                public DummySource () {
                    Get = _Get;
                }

                private ref readonly T _Get (int index) => ref Data(in Obj, index);
            }

            public readonly Interpolator<T> Unbound;
            private ThreadLocal<DummySource> Sources;

            public BoundUnboundAdapter (Interpolator<T> unbound) {
                Unbound = unbound;
                Sources = new ThreadLocal<DummySource>(() => new DummySource());
            }

            public T Interpolate (BoundInterpolatorSource<T, U> data, in U obj, int dataOffset, float positionInWindow) {
                var source = Sources.Value;
                source.Obj = obj;
                source.Data = data;
                return Unbound(source.Get, dataOffset, positionInWindow);
            }
        }

        private static class CacheContainer<U> {
            public static readonly Dictionary<string, BoundInterpolator<T, U>> Cache = 
                new Dictionary<string, BoundInterpolator<T, U>>(StringComparer.OrdinalIgnoreCase);
        }

        public static BoundInterpolator<T, U> GetBoundByName<U> (string name) {
            BoundInterpolator<T, U> result;

            lock (CacheContainer<U>.Cache)
            if (CacheContainer<U>.Cache.TryGetValue(name, out result))
                return result;

            var myType = typeof(Interpolators<T>);
            var resultType = typeof(BoundInterpolator<T, U>);
            var methods = myType.GetMethods();

            MethodInfo mi = null;

            foreach (var m in methods) {
                if (m.Name != name)
                    continue;
                if (!m.ContainsGenericParameters)
                    continue;
                var p = m.GetParameters();
                if (p?.Length != 4)
                    continue;

                mi = m;
                break;
            }

            Interpolator<T> unbound;
            lock (Cache)
                Cache.TryGetValue(name, out unbound);
                    
            if (unbound != null) {
                var t = typeof(BoundUnboundAdapter<>).MakeGenericType(typeof(T), typeof(U));
                var adapter = (BoundUnboundAdapter<U>)Activator.CreateInstance(t, unbound);
                result = adapter.Interpolate;
            } else if (mi != null) {
                var mii = mi.MakeGenericMethod(typeof(U));
                result = Delegate.CreateDelegate(resultType, null, mii) as BoundInterpolator<T, U>;
            } else {
                var tname = $"{typeof(Ease).FullName}".Replace("+Ease[[", $"+Ease+{name}[[");
                var possibleType = typeof(Ease).Assembly.GetType(tname, false, true);
                if (possibleType != null)
                    result = Eased<U>((Easing)Activator.CreateInstance(possibleType));
            }

            lock (CacheContainer<U>.Cache)
                CacheContainer<U>.Cache[name] = result;

            if (result == null)
                throw new ArgumentOutOfRangeException(nameof(name), $"No interpolator named '{name}'");

            return result;
        }

        public static Interpolator<T> Default {
            get {
                return Linear;
            }
        }

        /// <summary>
        /// Dummy method you can call to ensure that everything related to interpolating this type has
        ///  been initialized
        /// </summary>
        public static void Initialize () {
            var temp = Default;
            var temp2 = BoundDefaultCache<Tween<T>>.Linear;
        }
    }

    public static class InterpolatorExtensions {
        public static T Interpolate<T> (this Interpolator<T> interpolator, in T a, in T b, float progress) 
            where T : struct
        {
            return Interpolators<T>.Interpolate(interpolator, a, b, progress);
        }
    }

    internal static class DefaultInterpolators {
        public static float Linear (float a, float b, float t) =>
            a + ((b - a) * t);

        public static float Cosine (float a, float b, float t) =>
            a + ((b - a) * ((1.0f - (float)Math.Cos(t * Math.PI)) * 0.5f));

        public static float Cubic (float a, float b, float c, float d, float x) {
            var p = (d - c) - (a - b);
            var x2 = x * x;
            var x3 = x2 * x;
            return (p * x3) + ((a - b - p) * x2) + ((c - a) * x) + b;
        }

        public static double Linear (double a, double b, float t) =>
            a + ((b - a) * t);

        public static double Cosine (double a, double b, float t) =>
            a + ((b - a) * ((1.0 - Math.Cos(t * Math.PI)) * 0.5));

        public static double Cubic (double a, double b, double c, double d, float x) {
            var p = (d - c) - (a - b);
            var x2 = x * x;
            var x3 = x2 * x;
            return (p * x3) + ((a - b - p) * x2) + ((c - a) * x) + b;
        }
    }
}

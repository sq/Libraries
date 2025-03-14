﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Runtime;

namespace Squared.Util {
    internal static class PrimitiveOperators {
        public static int op_Subtraction (int lhs, int rhs) {
            return lhs - rhs;
        }

        public static int op_Addition (int lhs, int rhs) {
            return lhs + rhs;
        }

        public static int op_Multiply (int lhs, int rhs) {
            return lhs * rhs;
        }

        public static int op_Division (int lhs, int rhs) {
            return lhs / rhs;
        }

        public static int op_Modulus (int lhs, int rhs) {
            return lhs % rhs;
        }

        public static long op_Subtraction (long lhs, long rhs) {
            return lhs - rhs;
        }

        public static long op_Addition (long lhs, long rhs) {
            return lhs + rhs;
        }

        public static long op_Multiply (long lhs, long rhs) {
            return lhs * rhs;
        }

        public static long op_Division (long lhs, long rhs) {
            return lhs / rhs;
        }

        public static long op_Modulus (long lhs, long rhs) {
            return lhs % rhs;
        }

        public static float op_Subtraction (float lhs, float rhs) {
            return lhs - rhs;
        }

        public static float op_Addition (float lhs, float rhs) {
            return lhs + rhs;
        }

        public static float op_Multiply (float lhs, float rhs) {
            return lhs * rhs;
        }

        public static float op_Division (float lhs, float rhs) {
            return lhs / rhs;
        }

        public static float op_Modulus (float lhs, float rhs) {
            return lhs % rhs;
        }

        public static double op_Subtraction (double lhs, double rhs) {
            return lhs - rhs;
        }

        public static double op_Addition (double lhs, double rhs) {
            return lhs + rhs;
        }

        public static double op_Multiply (double lhs, double rhs) {
            return lhs * rhs;
        }

        public static double op_Division (double lhs, double rhs) {
            return lhs / rhs;
        }

        public static double op_Multiply (double lhs, float rhs) {
            return lhs * rhs;
        }

        public static double op_Division (double lhs, float rhs) {
            return lhs / rhs;
        }

        public static double op_Modulus (double lhs, double rhs) {
            return lhs % rhs;
        }
    }

    public static class Arithmetic {
        public static class OperatorCache<T> {
            public static readonly BinaryOperatorMethod<T, T> Add, Subtract, Divide, Multiply;

            static OperatorCache () {
                Add = GetOperator<T, T>(Operators.Add);
                Subtract = GetOperator<T, T>(Operators.Subtract);
                Divide = GetOperator<T, T>(Operators.Divide);
                Multiply = GetOperator<T, T>(Operators.Multiply);
            }
        }

        public enum Operators : int {
            // Binary operators
            Add = 1,
            Subtract = 2,
            Multiply = 3,
            Divide = 4,
            Modulo = 5,
            Equality = 6,
            Inequality = 7,
            GreaterThan = 9,
            LessThan = 10,
            GreaterThanOrEqual = 11,
            LessThanOrEqual = 12,
            // Unary operators
            Negation = 8
        }

        public delegate T UnaryOperatorMethod<T> (T value);
        public delegate T BinaryOperatorMethod<T, in U> (T lhs, U rhs);
        public delegate bool ComparisonMethod<in T, in U> (T lhs, U rhs);

        internal struct OperatorInfo {
            public OpCode OpCode;
            public String MethodName, Sigil;
            public bool IsComparison, IsUnary, NeedsInversion;
        }

        internal static Dictionary<int, OperatorInfo> _OperatorInfo = new Dictionary<int, OperatorInfo> {
            { (int)Operators.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition", Sigil = "+" } },
            { (int)Operators.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction", Sigil = "-" } },
            { (int)Operators.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply", Sigil = "*" } },
            { (int)Operators.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division", Sigil = "/" } },
            { (int)Operators.Modulo, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus", Sigil = "%" } },
            // FIXME: These are not currently completely usable
            { (int)Operators.Equality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Equality", Sigil = "==", IsComparison = true } },
            { (int)Operators.Inequality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Inequality", Sigil = "!=", IsComparison = true, NeedsInversion = true } },
            { (int)Operators.GreaterThan, new OperatorInfo { OpCode = OpCodes.Cgt, MethodName = "op_GreaterThan", Sigil = ">", IsComparison = true } },
            { (int)Operators.LessThan, new OperatorInfo { OpCode = OpCodes.Clt, MethodName = "op_LessThan", Sigil = "<", IsComparison = true } },
            { (int)Operators.GreaterThanOrEqual, new OperatorInfo { OpCode = OpCodes.Clt, MethodName = "op_GreaterThan", Sigil = ">", IsComparison = true, NeedsInversion = true } },
            { (int)Operators.LessThanOrEqual, new OperatorInfo { OpCode = OpCodes.Cgt, MethodName = "op_LessThan", Sigil = "<", IsComparison = true, NeedsInversion = true } },
            { (int)Operators.Negation, new OperatorInfo { OpCode = OpCodes.Neg, MethodName = "op_UnaryNegation", Sigil = "-", IsUnary = true } },
        };

        internal static Dictionary<Type, int> _TypeRanking = new Dictionary<Type, int> {
            { typeof(UInt16), 0 },
            { typeof(Int16), 0 },
            { typeof(UInt32), 1 },
            { typeof(Int32), 1 },
            { typeof(UInt64), 2 },
            { typeof(Int64), 2 },
            { typeof(Single), 3 },
            { typeof(Double), 4 }
        };

        internal static Dictionary<ExpressionType, Operators> _ExpressionTypeToOperator = new Dictionary<ExpressionType, Operators> {
            { ExpressionType.Add, Operators.Add },
            { ExpressionType.Subtract, Operators.Subtract },
            { ExpressionType.Multiply, Operators.Multiply },
            { ExpressionType.Divide, Operators.Divide },
            { ExpressionType.Modulo, Operators.Modulo },
            { ExpressionType.Negate, Operators.Negation },
            { ExpressionType.Equal, Operators.Equality },
            { ExpressionType.NotEqual, Operators.Inequality }
        };

        private struct OperatorKey {
            public Operators Operator;
            public Type T, U;
        }

        private sealed class OperatorKeyComparer : IEqualityComparer<OperatorKey> {
            public bool Equals (OperatorKey x, OperatorKey y) {
                return (x.Operator == y.Operator) &&
                    (x.T == y.T) &&
                    (x.U == y.U);
            }

            public int GetHashCode (OperatorKey obj) {
                return ((int)obj.Operator).GetHashCode() ^ obj.T.GetHashCode() ^ obj.U.GetHashCode();
            }
        }

        private static readonly Dictionary<OperatorKey, Delegate> CachedDelegates = 
            new Dictionary<OperatorKey, Delegate>(new OperatorKeyComparer());

#if !NODYNAMICMETHOD
        private static TDelegate GetOperatorDelegate<TDelegate> (Operators op, Type[] argumentTypes, bool optional = false)
            where TDelegate : class
        {
            var key = new OperatorKey {
                Operator = op,
                T = argumentTypes[0]
            };
            if (argumentTypes.Length > 1)
                key.U = argumentTypes[1];

            Delegate result;
            lock (CachedDelegates)
                if (CachedDelegates.TryGetValue(key, out result))
                    return (TDelegate)(object)result;

            Type delegateType = typeof(TDelegate);
            var name = _OperatorInfo[(int)op].MethodName;
            var methodInfo = typeof(PrimitiveOperators).GetMethod(name, argumentTypes) ??
                argumentTypes[0].GetMethod(name, argumentTypes);
            if ((methodInfo == null) && (argumentTypes.Length > 1))
                methodInfo = argumentTypes[1].GetMethod(name, argumentTypes);

            if (methodInfo != null) {
                try {
                    result = Delegate.CreateDelegate(typeof(TDelegate), methodInfo, false);
                } catch {
                    result = null;
                }
            }

            if (result == null) {
                DynamicMethod dm = new DynamicMethod(
                    name,
                    argumentTypes[0],
                    argumentTypes,
                    true
                );

                ILGenerator ilGenerator = dm.GetILGenerator();
                var exc = GenerateArithmeticIL(ilGenerator, argumentTypes, op);
                if (exc != null) {
                    if (optional)
                        return null;
                    else
                        throw exc;
                }
                result = dm.CreateDelegate(delegateType);
            }

            lock (CachedDelegates)
                CachedDelegates[key] = result;

            return (TDelegate)(object)result;
        }
#else
        private static TDelegate GetOperatorDelegate<TDelegate> (Operators op, Type[] argumentTypes, bool optional = false)
            where TDelegate : class {
            var name = _OperatorInfo[(int)op].MethodName;
            var methodInfo = typeof(PrimitiveOperators).GetMethod(name, argumentTypes) ??
                argumentTypes[0].GetMethod(name, argumentTypes);
            if ((methodInfo == null) && (argumentTypes.Length > 1))
                methodInfo = argumentTypes[1].GetMethod(name, argumentTypes);

            if (methodInfo == null) {
                if (optional)
                    return null;
                throw new InvalidOperationException(String.Format("No operator named {0} available for type {1}", name, argumentTypes[0].Name));
            }

            object del = Delegate.CreateDelegate(typeof(TDelegate), null, methodInfo);
            return (TDelegate)del;
        }
#endif

        public static string GetSigil (Operators op) {
            return _OperatorInfo[(int)op].Sigil;
        }

        private static ThreadLocal<Type[]> UnaryScratchArray = new ThreadLocal<Type[]>(() => new Type[1]);
        private static ThreadLocal<Type[]> BinaryScratchArray = new ThreadLocal<Type[]>(() => new Type[2]);

        public static UnaryOperatorMethod<T> GetOperator<T> (Operators op, bool optional = false) {
            if (!_OperatorInfo[(int)op].IsUnary || _OperatorInfo[(int)op].IsComparison)
                throw new InvalidOperationException("Operator is not unary");

            var usa = UnaryScratchArray.Value;
            usa[0] = typeof(T);
            return GetOperatorDelegate<UnaryOperatorMethod<T>>(op, usa, optional: optional);
        }

        public static BinaryOperatorMethod<T, U> GetOperator<T, U> (Operators op, bool optional = false) {
            if (_OperatorInfo[(int)op].IsUnary || _OperatorInfo[(int)op].IsComparison)
                throw new InvalidOperationException("Operator is not binary");

            var bsa = BinaryScratchArray.Value;
            bsa[0] = typeof(T);
            bsa[1] = typeof(U);
            return GetOperatorDelegate<BinaryOperatorMethod<T, U>>(op, bsa, optional: optional);
        }

        public static ComparisonMethod<T, U> GetComparison<T, U> (Operators op, bool optional = false) {
            if (_OperatorInfo[(int)op].IsUnary || !_OperatorInfo[(int)op].IsComparison)
                throw new InvalidOperationException("Operator is not a comparison");

            var bsa = BinaryScratchArray.Value;
            bsa[0] = typeof(T);
            bsa[1] = typeof(U);
            return GetOperatorDelegate<ComparisonMethod<T, U>>(op, bsa, optional: optional);
        }

        public static object InvokeOperatorSlow (Operators op, object lhs, object rhs) {
            // TODO: Optimize this. It's really for editors so the speed probably doesn't matter much though...
            var gm = typeof(Arithmetic).GetMethod("InvokeOperator2", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(lhs.GetType(), rhs.GetType());
            return gm.Invoke(null, new[] { op, lhs, rhs });
        }

        private static T InvokeOperator2<T, U> (Operators op, T lhs, U rhs) => InvokeOperator(op, lhs, rhs);

        public static T InvokeOperator<T> (Operators op, T value) {
            var method = GetOperator<T>(op);
            T result = method(value);
            return result;
        }

        public static T InvokeOperator<T, U> (Operators op, T lhs, U rhs) {
            var method = GetOperator<T, U>(op);
            T result = method(lhs, rhs);
            return result;
        }

        public static int Clamp (int value, int min, int max) {
            if (value < min)
                return min;
            else if (value > max)
                return max;
            else
                return value;
        }

        public static float Clamp (float value, float min, float max) {
            if (value < min)
                return min;
            else if (value > max)
                return max;
            else
                return value;
        }

        public static double Clamp (double value, double min, double max) {
            if (value < min)
                return min;
            else if (value > max)
                return max;
            else
                return value;
        }

        /// <summary>
        /// Clamps a value to the range (min, max)
        /// </summary>
        public static T Clamp<T> (T value, T min, T max)
            where T : IComparable<T> {
            if (value.CompareTo(max) > 0)
                return max;
            else if (value.CompareTo(min) < 0)
                return min;
            else
                return value;
        }

        /// <summary>
        /// Clamps a value to the range (min, max) then scales it to the range (0, scale)
        /// </summary>
        /// <param name="scale">An optional scale value for integer types (default 1)</param>
        public static T Fraction<T> (T value, T min, T max, T? scale = null)
            where T : struct
        {
            var range = OperatorCache<T>.Subtract(max, min);
            var relative = OperatorCache<T>.Subtract(value, min);
            if (scale != null)
                relative = OperatorCache<T>.Multiply(relative, scale.Value);
            return OperatorCache<T>.Divide(relative, range);
        }

        /// <summary>
        /// Scales a value by (max - min), then divides it by scale and adds it to min
        /// </summary>
        /// <param name="scale">An optional scale value for integer types (default 1)</param>
        public static T FractionToValue<T> (T fraction, T min, T max, T? scale = null)
            where T : struct
        {
            var range = OperatorCache<T>.Subtract(max, min);
            var global = OperatorCache<T>.Multiply(fraction, range);
            if (scale != null)
                global = OperatorCache<T>.Divide(global, scale.Value);
            return OperatorCache<T>.Add(min, global);
        }

        /// <summary>
        /// Clamps a value to the range (0, 1)
        /// </summary>
        public static float Saturate (float value) {
            if (value < 0)
                return 0;
            else if (value > 1)
                return 1;
            else
                return value;
        }

        /// <summary>
        /// Clamps a value to the range (0, 1)
        /// </summary>
        public static float Saturate (float value, float max) {
            if (value < 0)
                return 0;
            else if (value > max)
                return max;
            else
                return value;
        }

        /// <summary>
        /// Clamps a value to the range (0, 1)
        /// </summary>
        public static double Saturate (double value) {
            if (value < 0)
                return 0;
            else if (value > 1)
                return 1;
            else
                return value;
        }

        /// <summary>
        /// Clamps a value to the range (0, 1)
        /// </summary>
        public static double Saturate (double value, double max) {
            if (value < 0)
                return 0;
            else if (value > max)
                return max;
            else
                return value;
        }

        /// <summary>
        /// Wraps a value into the range (min, max)
        /// </summary>
        public static int Wrap (int value, int min, int max) {
            int d = max - min + 1;

            if (max <= min)
                return min;

            if (value < min) {
                return min + ((d - (Math.Abs(min - value) % d)) % d);
            } else if (value > max) {
                return min + (Math.Abs(value - max - 1) % d);
            } else {
                return value;
            }
        }

        /// <summary>
        /// Wraps a value into the range (min, max].
        /// Will never return max.
        /// </summary>
        public static float WrapExclusive (float value, float min, float max) {
            float d = max - min;

            if (max <= min)
                return min;

            if (value < min) {
                return min + ((d - Math.Abs(min - value)) % d);
            } else if (value >= max) {
                return min + (Math.Abs(value - min) % d);
            } else {
                return value;
            }
        }

        /// <summary>
        /// Wraps a value into the range (min, max].
        /// Will never return max.
        /// </summary>
        public static double WrapExclusive (double value, double min, double max) {
            double d = max - min;

            if (max <= min)
                return min;

            if (value < min) {
                return min + ((d - Math.Abs(min - value)) % d);
            } else if (value >= max) {
                return min + (Math.Abs(value - min) % d);
            } else {
                return value;
            }
        }

        public static float WrapInclusive (float value, float min, float max) =>
            (float)WrapInclusive((double)value, (double)min, (double)max);

        /// <summary>
        /// Wraps a value into the range (min, max).
        /// After the first wrap, will never return max, i.e:
        /// WrapInclusive(1, 0, 1) == 1, WrapInclusive(2, 0, 1) == 0
        /// </summary>
        public static double WrapInclusive (double value, double min, double max) {
            if (max <= min)
                return min;

            bool isBefore = value < min;
            double relativeValue = isBefore ? min - value : value - min,
                windowSize = max - min,
                repeatCount = Math.Abs(Math.Truncate(relativeValue / windowSize)),
                repeatBase = repeatCount * windowSize, 
                previousRepeatBase = (repeatCount - 1) * windowSize,
                relativeToRepeatBase = Math.Abs(relativeValue) - repeatBase,
                finalWindowedValue,
                windowStart = double.Epsilon, 
                windowEnd = windowSize - double.Epsilon;

            if (relativeToRepeatBase >= windowStart)
                finalWindowedValue = relativeToRepeatBase;
            else if (repeatCount <= 0)
                finalWindowedValue = min;
            else
                finalWindowedValue = max;

            if (isBefore) {
                var reversed = finalWindowedValue;
                return (min - reversed);
            } else
                return (min + finalWindowedValue);
        }

        public static float Pulse (float value) {
            value = Math.Abs(value % 1.0f);

            if (value > 0.5f)
                return (value - 0.5f) / 0.5f;
            else
                return 1f - (value / 0.5f);
        }

        public static float Pulse (float value, float min, float max) {
            float a = Pulse(value);
            return Lerp(min, max, a);
        }

        public static float PulseExp (float value, float min, float max) {
            float a = Pulse(value);
            return Lerp(min, max, a * a);
        }

        public static float PulseSine (float value, float min, float max) {
            float a = Pulse(value);
            const double multiplier = Math.PI / 2;
            return Lerp(min, max, (float)Math.Sin(a * multiplier));
        }

        /// <summary>
        /// Input range: (0, 2)
        /// Output range: (-max, max)
        /// </summary>
        public static float PulseCyclicExp (float value, float max) {
            var valueCentered = WrapInclusive(value, 0f, 2f);
            if (valueCentered <= 1f) {
                if (valueCentered >= 0.5f) {
                    valueCentered = (0.5f - (valueCentered - 0.5f)) * 2f;
                } else {
                    valueCentered = valueCentered * 2f;
                }
            } else {
                valueCentered -= 1f;
                if (valueCentered >= 0.5f) {
                    valueCentered = (0.5f - (valueCentered - 0.5f)) * -2f;
                } else {
                    valueCentered = valueCentered * -2f;
                }
            }

            var valueExp = (valueCentered * valueCentered) * Math.Sign(valueCentered) * max;
            return valueExp;
        }

        public static double Pulse (double value) {
            value = Math.Abs(value % 1.0f);

            if (value > 0.5f)
                return (value - 0.5f) / 0.5f;
            else
                return 1f - (value / 0.5f);
        }

        public static double Pulse (double value, double min, double max) {
            double a = Pulse(value);
            return Lerp(min, max, a);
        }

        public static double PulseExp (double value, double min, double max) {
            double a = Pulse(value);
            return Lerp(min, max, a * a);
        }

        public static double PulseSine (double value, double min, double max) {
            double a = Pulse(value);
            const double multiplier = Math.PI / 2;
            return Lerp(min, max, Math.Sin(a * multiplier));
        }

        /// <summary>
        /// Input range: (0, 2)
        /// Output range: (-max, max)
        /// </summary>
        public static double PulseCyclicExp (double value, double max) {
            var valueCentered = WrapInclusive(value, 0, 2);
            if (valueCentered <= 1) {
                if (valueCentered >= 0.5) {
                    valueCentered = (0.5 - (valueCentered - 0.5)) * 2;
                } else {
                    valueCentered = valueCentered * 2;
                }
            } else {
                valueCentered -= 1;
                if (valueCentered >= 0.5) {
                    valueCentered = (0.5 - (valueCentered - 0.5)) * -2;
                } else {
                    valueCentered = valueCentered * -2;
                }
            }

            var valueExp = (valueCentered * valueCentered) * Math.Sign(valueCentered) * max;
            return valueExp;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp (int a, int b, float x) {
            if (x <= 0)
                return a;
            else if (x >= 1)
                return b;
            return a + ((b - a) * x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp (float a, float b, float x) {
            if (x <= 0)
                return a;
            else if (x >= 1)
                return b;
            return a + ((b - a) * x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Lerp (double a, double b, double x) {
            if (x <= 0)
                return a;
            else if (x >= 1)
                return b;
            return a + ((b - a) * x);
        }

        public static T Lerp<T> (T a, T b, float x) 
            where T : struct {

            var linear = Interpolators<T>._Linear;
            return linear(a, b, x);
        }

        public static T Lerp<T> (in T a, in T b, float x) 
            where T : struct {

            var linear = Interpolators<T>._Linear;
            return linear(a, b, x);
        }

        public static T Quadratic<T> (in T a, in T b, in T c, float x) 
            where T : struct 
        {
            T ab = Lerp(a, b, x),
                bc = Lerp(b, c, x);
            return Lerp(ab, bc, x);
        }

        public static T Cubic<T> (in T a, in T b, in T c, in T d, float x) 
            where T : struct 
        {
            var cubic = Interpolators<T>._Cubic;
            if (cubic == null)
                throw new Exception($"Cubic interpolator not available for type {typeof(T).FullName}");
            return cubic(a, b, c, d, x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite (float f) {
            if (float.IsInfinity(f))
                return false;
            else if (float.IsNaN(f))
                return false;
            else
                return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite (double d) {
            if (double.IsInfinity(d))
                return false;
            else if (double.IsNaN(d))
                return false;
            else
                return true;
        }
    
        public static int NextPowerOfTwo (int value) {
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(value, 2)));
        }

        public static void MinMax (int a, int b, int c, out int min, out int max) {
            min = Math.Min(a, Math.Min(b, c));
            max = Math.Max(a, Math.Max(b, c));
        }

        public static void MinMax (int a, int b, int c, int d, out int min, out int max) {
            min = Math.Min(a, Math.Min(b, Math.Min(c, d)));
            max = Math.Max(a, Math.Max(b, Math.Max(c, d)));
        }

        public static void MinMax (float a, float b, float c, out float min, out float max) {
            min = Math.Min(a, Math.Min(b, c));
            max = Math.Max(a, Math.Max(b, c));
        }

        public static void MinMax (float a, float b, float c, float d, out float min, out float max) {
            min = Math.Min(a, Math.Min(b, Math.Min(c, d)));
            max = Math.Max(a, Math.Max(b, Math.Max(c, d)));
        }

        public static void MinMax (double a, double b, double c, out double min, out double max) {
            min = Math.Min(a, Math.Min(b, c));
            max = Math.Max(a, Math.Max(b, c));
        }

        public static void MinMax (double a, double b, double c, double d, out double min, out double max) {
            min = Math.Min(a, Math.Min(b, Math.Min(c, d)));
            max = Math.Max(a, Math.Max(b, Math.Max(c, d)));
        }

        #region Code generation functions

        internal static Operators GetOperator (ExpressionType et) {
            Operators op;

            if (!_ExpressionTypeToOperator.TryGetValue(et, out op))
                throw new InvalidOperationException(String.Format("Operator {0} not supported in expressions", et));

            return op;
        }

        internal static OperatorInfo GetOperatorInfo (ExpressionType et) {
            OperatorInfo oi;

            if (!_OperatorInfo.TryGetValue((int)GetOperator(et), out oi))
                throw new InvalidOperationException(String.Format("Operator {0} not supported in expressions", et));

            return oi;
        }

        internal static Exception GenerateArithmeticIL (ILGenerator ilGenerator, Type[] argumentTypes, Operators op) {
            if (argumentTypes.Length >= 1)
                ilGenerator.Emit(OpCodes.Ldarg_0);
            
            if (argumentTypes.Length >= 2)
                ilGenerator.Emit(OpCodes.Ldarg_1);

            var oi = _OperatorInfo[(int)op];
            var result = GenerateOperatorIL(ilGenerator, argumentTypes, oi.MethodName, oi.OpCode, oi.NeedsInversion);

            ilGenerator.Emit(OpCodes.Ret);
            return result;
        }

        internal static Type GetPrimitiveResult (Type lhs, Type rhs) {
            int rankLeft, rankRight;
            if (_TypeRanking.TryGetValue(lhs, out rankLeft) &&
                _TypeRanking.TryGetValue(rhs, out rankRight)) {
                if (rankLeft > rankRight)
                    return lhs;
                else
                    return rhs; // hack
            } else {
                return lhs; // hack
            }
        }

        internal static Exception GenerateOperatorIL (ILGenerator ilGenerator, Type[] argumentTypes, string methodName, OpCode? opCode, bool needsInversion) {
            var lhs = argumentTypes[0];
            Type rhs = lhs;

            if (argumentTypes.Length > 1)
                rhs = argumentTypes[1];

            if ((lhs.IsPrimitive && rhs.IsPrimitive) && opCode.HasValue) {
                ilGenerator.Emit(opCode.Value);
                if (needsInversion)
                    ilGenerator.Emit(OpCodes.Not);
                GetPrimitiveResult(lhs, rhs);
                return null;
            } else {
                MethodInfo operatorMethod = lhs.GetMethod(methodName, new Type[] { lhs, rhs }, null);
                if (operatorMethod != null) {
                    ilGenerator.EmitCall(OpCodes.Call, operatorMethod, null);
                    return null;
                } else {
                    return new InvalidOperationException(
                        String.Format(
                            "GenerateOperatorIL failed for operator {0} with operands {1}, {2}: operation not implemented",
                            methodName, lhs, rhs
                        )
                    );
                }
            }
        }

        #endregion
    }

    public static class FastMath {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorLog2 (int n) {
            // FIXME
            if (n < 0)
                return 0;
            unchecked {
                return CoreCLR.BitOperations.Log2((uint)n);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SignF (float f) {
            unchecked {
                int signBit = (int)((*(uint*)&f) >> 31);
                return (signBit * -2) + 1;
            }
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
        public struct U32F32_X1 {
            [FieldOffset(0)]
            public uint U32;
            [FieldOffset(0)]
            public int I32;
            [FieldOffset(0)]
            public float F32;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 8)]
        public struct U32F32_X2 {
            [FieldOffset(0)]
            public uint U1;
            [FieldOffset(0)]
            public int I1;
            [FieldOffset(0)]
            public float F1;
            [FieldOffset(4)]
            public uint U2;
            [FieldOffset(4)]
            public int I2;
            [FieldOffset(4)]
            public float F2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CompareF (ref U32F32_X1 lhs, ref U32F32_X1 rhs) {
            // Hot magic in action!
            // We pass the union by reference and have the caller initialize it.
            // Otherwise, we'd have to initialize it at entry to this function, which is expensive
            // If it was passed by value it'd be copied per-call, which is pointless

            unchecked {
                var shiftI1 = (lhs.I32 >> 31);

                // Precomputing these shifts once and storing them in locals creates memory
                //  loads/stores so it's better to just compute over and over, it produces
                //  fewer insns and theoretically doesn't hit stack/mem as much
                if (shiftI1 != (rhs.I32 >> 31))
                    return shiftI1 - (rhs.I32 >> 31);

                // Storing these expressions into locals raises overhead, so just write them out bare
                return (int)(lhs.U32 - rhs.U32) * 
                    // We use a bit shift instead of a * 2 because it seems to be faster
                    ((shiftI1 << 1) + 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CompareF (ref U32F32_X2 buf) {
            // Hot magic in action!
            // We pass the union by reference and have the caller initialize it.
            // Otherwise, we'd have to initialize it at entry to this function, which is expensive
            // If it was passed by value it'd be copied per-call, which is pointless

            unchecked {
                var shiftI1 = (buf.I1 >> 31);

                // Precomputing these shifts once and storing them in locals creates memory
                //  loads/stores so it's better to just compute over and over, it produces
                //  fewer insns and theoretically doesn't hit stack/mem as much
                if (shiftI1 != (buf.I2 >> 31))
                    return shiftI1 - (buf.I2 >> 31);

                // Storing these expressions into locals raises overhead, so just write them out bare
                return (int)(buf.U1 - buf.U2) * 
                    // We use a bit shift instead of a * 2 because it seems to be faster
                    ((shiftI1 << 1) + 1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int CompareFSlow (uint u1, uint u2) {
            unchecked {
                int sign1 = (int)(u1 >> 31), sign2 = (int)(u2 >> 31);
                if (sign1 != sign2)
                    return (sign2 - sign1);

                int multiplier = (sign1 * -2) + 1;
                return (int)(u1 - u2) * multiplier;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool EqualsF (float lhs, float rhs) {
            unchecked {
#if !NOSPAN
                return Unsafe.As<float, UInt32>(ref lhs) == Unsafe.As<float, UInt32>(ref rhs);
#else
                return *((UInt32*)&lhs) == *((UInt32*)&rhs);
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int CompareF (float lhs, float rhs) {
#if !NOSPAN
            return CompareF(ref Unsafe.As<float, U32F32_X1>(ref lhs), ref Unsafe.As<float, U32F32_X1>(ref rhs));
#else
            U32F32_X2 u = default;
            u.F1 = lhs;
            u.F2 = rhs;
            return CompareF(ref u);
#endif
        }
    }

    public static class FisherYates {
        public static void Shuffle<T> (
            Random rng,
            ArraySegment<T> values
        ) {
            for (int i = 0, n = values.Count; i <= (n - 2); i += 1) {
                var j = rng.Next(i, n);
                var temp = values.Array[values.Offset + j];
                values.Array[values.Offset + j] = values.Array[values.Offset + i];
                values.Array[values.Offset + i] = temp;
            }
        }

        public static void Shuffle<T> (
            Random rng,
            ref DenseList<T> values
        ) {
            for (int i = 0, n = values.Count; i <= (n - 2); i += 1) {
                var j = rng.Next(i, n);
                var temp = values[j];
                values[j] = values[i];
                values[i] = temp;
            }
        }

        public static void Shuffle<T> (
            Random rng,
            IList<T> values
        ) {
            for (int i = 0, n = values.Count; i <= (n - 2); i += 1) {
                var j = rng.Next(i, n);
                var temp = values[j];
                values[j] = values[i];
                values[i] = temp;
            }
        }

        public static void Shuffle<T> (
            ref CoreCLR.Xoshiro rng,
            ArraySegment<T> values
        ) {
            for (int i = 0, n = values.Count; i <= (n - 2); i += 1) {
                var j = rng.Next(i, n);
                var temp = values.Array[values.Offset + j];
                values.Array[values.Offset + j] = values.Array[values.Offset + i];
                values.Array[values.Offset + i] = temp;
            }
        }

        public static void Shuffle<T> (
            ref CoreCLR.Xoshiro rng,
            ref DenseList<T> values
        ) {
            for (int i = 0, n = values.Count; i <= (n - 2); i += 1) {
                var j = rng.Next(i, n);
                var temp = values[j];
                values[j] = values[i];
                values[i] = temp;
            }
        }

        public static void Shuffle<T> (
            ref CoreCLR.Xoshiro rng,
            IList<T> values
        ) {
            for (int i = 0, n = values.Count; i <= (n - 2); i += 1) {
                var j = rng.Next(i, n);
                var temp = values[j];
                values[j] = values[i];
                values[i] = temp;
            }
        }
    }
}
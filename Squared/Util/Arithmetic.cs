using System;
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
            // Unary operators
            Negation = 8
        }

        public delegate T UnaryOperatorMethod<T> (T value);
        public delegate T BinaryOperatorMethod<T, in U> (T lhs, U rhs);

        internal struct OperatorInfo {
            public OpCode OpCode;
            public String MethodName, Sigil;
            public bool IsComparison, IsUnary;
        }

        internal static Dictionary<int, OperatorInfo> _OperatorInfo = new Dictionary<int, OperatorInfo> {
            { (int)Operators.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition", Sigil = "+" } },
            { (int)Operators.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction", Sigil = "-" } },
            { (int)Operators.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply", Sigil = "*" } },
            { (int)Operators.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division", Sigil = "/" } },
            { (int)Operators.Modulo, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus", Sigil = "%" } },
            { (int)Operators.Equality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Equality", Sigil = "==", IsComparison = true } },
            { (int)Operators.Inequality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Inequality", Sigil = "!=", IsComparison = true } },
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

#if WINDOWS
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
#endif

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
                return obj.GetHashCode();
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

            DynamicMethod dm = new DynamicMethod(
                _OperatorInfo[(int)op].MethodName,
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

            lock (CachedDelegates)
                CachedDelegates[key] = result;

            return (TDelegate)(object)result;
        }
#else
        private static TDelegate GetOperatorDelegate<TDelegate> (Operators op, Type[] argumentTypes, bool optional = false)
            where TDelegate : class {
            var name = _OperatorInfo[op].MethodName;
            var methodInfo = argumentTypes[0].GetMethod(name, argumentTypes);

            if ((methodInfo == null) && (argumentTypes.Length > 1))
                methodInfo = argumentTypes[1].GetMethod(name, argumentTypes);

            if (methodInfo == null)
                methodInfo = typeof(PrimitiveOperators).GetMethod(name, argumentTypes);

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
            if (!_OperatorInfo[(int)op].IsUnary)
                throw new InvalidOperationException("Operator is not unary");

            var usa = UnaryScratchArray.Value;
            usa[0] = typeof(T);
            return GetOperatorDelegate<UnaryOperatorMethod<T>>(op, usa, optional: optional);
        }

        public static BinaryOperatorMethod<T, U> GetOperator<T, U> (Operators op, bool optional = false) {
            if (_OperatorInfo[(int)op].IsUnary)
                throw new InvalidOperationException("Operator is not binary");

            var bsa = BinaryScratchArray.Value;
            bsa[0] = typeof(T);
            bsa[1] = typeof(U);
            return GetOperatorDelegate<BinaryOperatorMethod<T, U>>(op, bsa, optional: optional);
        }

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
        /// Wraps a value into the range (min, max).
        /// After the first wrap, will never return max, i.e:
        /// WrapInclusive(1, 0, 1) == 1, WrapInclusive(2, 0, 1) == 0
        /// </summary>
        public static float WrapInclusive (float value, float min, float max) {
            if (max <= min)
                return min;

            bool isBefore = value < min;
            double relativeValue = isBefore ? min - value : value - min;
            double windowSize = max - min;
            double repeatCount = Math.Abs(Math.Truncate(relativeValue / windowSize));
            double repeatBase = repeatCount * windowSize, previousRepeatBase = (repeatCount - 1) * windowSize;
            double relativeToRepeatBase = Math.Abs(relativeValue) - repeatBase;
            double finalWindowedValue;
            double windowStart = double.Epsilon, windowEnd = windowSize - double.Epsilon;
            if (relativeToRepeatBase >= windowStart)
                finalWindowedValue = relativeToRepeatBase;
            else if (repeatCount <= 0)
                finalWindowedValue = min;
            else
                finalWindowedValue = max;

            if (isBefore) {
                var reversed = finalWindowedValue;
                return (float)(min - reversed);
            } else
                return (float)(min + finalWindowedValue);
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

        private static class LerpSource<T> where T : struct {
            static InterpolatorSource<T> Source;
            static T[] Values;

            static LerpSource () {
                Source = (i) => {
                    if (i >= 1)
                        return ref Values[1];
                    else
                        return ref Values[0];
                };
                Values = new T[2];
            }

            public static InterpolatorSource<T> Get (in T a, in T b) {
                Values[0] = a;
                Values[1] = b;
                return Source;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Lerp (float a, float b, float x) {
            if (x <= 0)
                return a;
            else if (x >= 1)
                return b;
            return a + ((b - a) * x);
        }

        [TargetedPatchingOptOut("")]
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

            return Interpolators<T>.Linear(
                LerpSource<T>.Get(in a, in b),
                0, Saturate(x)
            );
        }

        public static T Lerp<T> (in T a, in T b, float x) 
            where T : struct {

            return Interpolators<T>.Linear(
                LerpSource<T>.Get(in a, in b),
                0, Saturate(x)
            );
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite (float f) {
            if (float.IsInfinity(f))
                return false;
            else if (float.IsNaN(f))
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

#if WINDOWS
        #region CompileExpression<T> overloads

        public static void CompileExpression<T> (Expression<Func<double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double, double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double, double, double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double, double, double, double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T> (Expression<Func<double, double, double, double, double, double, double, double, double>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, double, double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, double, double, double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, double, double, double, double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        public static void CompileExpression<T, Ret> (Expression<Func<double, double, double, double, double, double, double, double, Ret>> expression, out T result)
            where T : class {
            _CompileExpression(expression, out result);
        }

        #endregion

        #region Code generation functions

        internal class EmitState {
            public ILGenerator ILGenerator;
            public Type ReturnType;
            public Type[] ParameterTypes;
            public Dictionary<string, UInt16> ParameterIndices;
        }

        internal static Type EmitExpressionNode (BinaryExpression expr, EmitState es) {
            Type typeLeft = EmitExpression(expr.Left, es);
            Type typeRight = EmitExpression(expr.Right, es);

            var oi = GetOperatorInfo(expr.NodeType);

            GenerateOperatorIL(es.ILGenerator, new [] { typeLeft, typeRight }, oi.MethodName, oi.OpCode);

            if (expr.Conversion != null)
                return EmitExpression(expr.Conversion, es);
            else if (oi.IsComparison)
                return typeof(Boolean);
            else
                return typeLeft;
        }

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

        internal static Type EmitExpressionNode (UnaryExpression expr, EmitState es) {
            Type t = EmitExpression(expr.Operand, es);

            if (expr.NodeType == ExpressionType.Convert) {
                EmitConversion(t, es);
                return t;
            } else {
                var oi = GetOperatorInfo(expr.NodeType);
                es.ILGenerator.Emit(oi.OpCode);
                return t;
            }
        }

        internal static Type EmitExpressionNode (ParameterExpression expr, EmitState es) {
            string paramName = expr.Name;
            UInt16 paramIndex = es.ParameterIndices[paramName];
            Type paramType = es.ParameterTypes[paramIndex];
            switch (paramIndex) {
                case 0:
                    es.ILGenerator.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    es.ILGenerator.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    es.ILGenerator.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    es.ILGenerator.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    es.ILGenerator.Emit(OpCodes.Ldarg, paramIndex);
                    break;
            }
            return paramType;
        }

        internal static Type EmitConversion (Type desiredType, EmitState es) {
            if (desiredType == typeof(Single))
                es.ILGenerator.Emit(OpCodes.Conv_R4);
            else if (desiredType == typeof(Double))
                es.ILGenerator.Emit(OpCodes.Conv_R8);
            else if (desiredType == typeof(Int32))
                es.ILGenerator.Emit(OpCodes.Conv_I4);
            else if (desiredType == typeof(Int64))
                es.ILGenerator.Emit(OpCodes.Conv_I8);
            else
                throw new InvalidOperationException(String.Format("Conversions to type {0} not supported in expressions", desiredType.Name));
            return desiredType;
        }

        internal static Type EmitExpressionNode (MethodCallExpression expr, EmitState es) {
            if (expr.NodeType == ExpressionType.Call) {
                var paramInfo = expr.Method.GetParameters();
                for (int i = 0; i < expr.Arguments.Count; i++) {
                    var arg = expr.Arguments[i];
                    var parInfo = paramInfo[i];
                    Type argType = EmitExpression(arg, es);

                    if (!parInfo.ParameterType.IsAssignableFrom(argType)) {
                        EmitConversion(parInfo.ParameterType, es);
                    }
                }
                
                es.ILGenerator.EmitCall(OpCodes.Call, expr.Method, null);

                return expr.Method.ReturnType;
            } else {
                throw new InvalidOperationException(String.Format("Method calls of type {0} not supported in expressions", expr.NodeType));
            }
        }

        internal static Type EmitExpressionNode (ConstantExpression expr, EmitState es) {
            Type t = expr.Type;
            if (t == typeof(byte))
                es.ILGenerator.Emit(OpCodes.Ldc_I4_S, (byte)expr.Value);
            else if (t == typeof(Int32))
                es.ILGenerator.Emit(OpCodes.Ldc_I4, (Int32)expr.Value);
            else if (t == typeof(Int64))
                es.ILGenerator.Emit(OpCodes.Ldc_I8, (Int64)expr.Value);
            else if (t == typeof(Single))
                es.ILGenerator.Emit(OpCodes.Ldc_R4, (Single)expr.Value);
            else if (t == typeof(Double))
                es.ILGenerator.Emit(OpCodes.Ldc_R8, (Double)expr.Value);
            else
                throw new InvalidOperationException(String.Format("Constants of type {0} not supported in expressions", expr.Type.Name));
            return t;
        }

        internal static Type EmitExpression (Expression expr, EmitState es) {
            if (expr is BinaryExpression)
                return EmitExpressionNode((BinaryExpression)expr, es);
            else if (expr is UnaryExpression)
                return EmitExpressionNode((UnaryExpression)expr, es);
            else if (expr is ParameterExpression)
                return EmitExpressionNode((ParameterExpression)expr, es);
            else if (expr is MethodCallExpression)
                return EmitExpressionNode((MethodCallExpression)expr, es);
            else if (expr is ConstantExpression)
                return EmitExpressionNode((ConstantExpression)expr, es);
            else
                throw new InvalidOperationException(String.Format("Cannot compile expression nodes of type {0}", expr.GetType().Name));
        }

        internal static void _CompileExpression<T> (LambdaExpression expression, out T result)
            where T : class {
            Type t = typeof(T);
            if (!((t.BaseType == typeof(MulticastDelegate)) || (t.BaseType == typeof(Delegate))))
                throw new InvalidOperationException("Cannot compile an expression to a non-delegate type");

            MethodInfo desiredSignature = t.GetMethod("Invoke");
            Type returnType = desiredSignature.ReturnType;
            Type[] parameterTypes = desiredSignature.GetParameters().Select(
                (p) => p.ParameterType
            ).ToArray();
            var parameterIndices = new Dictionary<string, UInt16>();
            for (int i = 0; i < expression.Parameters.Count; i++) {
                parameterIndices[expression.Parameters[i].Name] = (UInt16)i;
            }

            DynamicMethod newMethod = new DynamicMethod(
                String.Format("CompiledExpression<{0}>", t.Name),
                returnType,
                parameterTypes,
                true
            );
            ILGenerator ilGenerator = newMethod.GetILGenerator();

            EmitState es = new EmitState {
                ILGenerator = ilGenerator,
                ReturnType = returnType,
                ParameterTypes = parameterTypes,
                ParameterIndices = parameterIndices
            };

            Type resultType = EmitExpression(expression.Body, es);
            if (!returnType.IsAssignableFrom(resultType)) {
                EmitConversion(returnType, es);
            }            
            ilGenerator.Emit(OpCodes.Ret);

            result = newMethod.CreateDelegate(t) as T;
        }

        internal static Exception GenerateArithmeticIL (ILGenerator ilGenerator, Type[] argumentTypes, Operators op) {
            if (argumentTypes.Length >= 1)
                ilGenerator.Emit(OpCodes.Ldarg_0);
            
            if (argumentTypes.Length >= 2)
                ilGenerator.Emit(OpCodes.Ldarg_1);

            var oi = _OperatorInfo[(int)op];
            var result = GenerateOperatorIL(ilGenerator, argumentTypes, oi.MethodName, oi.OpCode);

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

        internal static Exception GenerateOperatorIL (ILGenerator ilGenerator, Type[] argumentTypes, string methodName, OpCode? opCode) {
            var lhs = argumentTypes[0];
            Type rhs = lhs;

            if (argumentTypes.Length > 1)
                rhs = argumentTypes[1];

            if ((lhs.IsPrimitive && rhs.IsPrimitive) && opCode.HasValue) {
                ilGenerator.Emit(opCode.Value);
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
#endif
    }

    public static class FastMath {
        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorLog2 (int n) {
            // FIXME
            if (n < 0)
                return 0;
            unchecked {
                return CoreCLR.BitOperations.Log2((uint)n);
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SignF (float f) {
            unchecked {
                int signBit = (int)((*(uint*)&f) >> 31);
                return (signBit * -2) + 1;
            }
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
        public struct U32F32 {
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

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int CompareF (ref U32F32 buf) {
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

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool EqualsF (float lhs, float rhs) {
            unchecked {
                return *((UInt32*)&lhs) == *((UInt32*)&rhs);
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int CompareF (float lhs, float rhs) {
            U32F32 u = default;
            u.F1 = lhs;
            u.F2 = rhs;

            return CompareF(ref u);
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
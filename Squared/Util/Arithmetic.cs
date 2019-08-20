using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Squared.Util {
#if WINDOWS
    #region Additional Func overloads
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
    public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
    #endregion
#endif

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

        internal static Dictionary<Operators, OperatorInfo> _OperatorInfo = new Dictionary<Operators, OperatorInfo> {
            { Operators.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition", Sigil = "+" } },
            { Operators.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction", Sigil = "-" } },
            { Operators.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply", Sigil = "*" } },
            { Operators.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division", Sigil = "/" } },
            { Operators.Modulo, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus", Sigil = "%" } },
            { Operators.Equality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Equality", Sigil = "==", IsComparison = true } },
            { Operators.Inequality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Inequality", Sigil = "!=", IsComparison = true } },
            { Operators.Negation, new OperatorInfo { OpCode = OpCodes.Neg, MethodName = "op_UnaryNegation", Sigil = "-", IsUnary = true } },
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

        private static readonly Dictionary<OperatorKey, Delegate> CachedDelegates = new Dictionary<OperatorKey, Delegate>();

#if (WINDOWS || DYNAMICMETHOD) && (!NODYNAMICMETHOD)
        private static TDelegate GetOperatorDelegate<TDelegate> (Operators op, Type[] argumentTypes)
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
                _OperatorInfo[op].MethodName,
                argumentTypes[0],
                argumentTypes,
                true
            );

            ILGenerator ilGenerator = dm.GetILGenerator();
            GenerateArithmeticIL(ilGenerator, argumentTypes, op);
            result = dm.CreateDelegate(delegateType);

            lock (CachedDelegates)
                CachedDelegates[key] = result;

            return (TDelegate)(object)result;
        }
#else
        private static TDelegate GetOperatorDelegate<TDelegate> (Operators op, Type[] argumentTypes)
            where TDelegate : class {
            var name = _OperatorInfo[op].MethodName;
            var methodInfo = argumentTypes[0].GetMethod(name, argumentTypes);

            if ((methodInfo == null) && (argumentTypes.Length > 1))
                methodInfo = argumentTypes[1].GetMethod(name, argumentTypes);

            if (methodInfo == null)
                methodInfo = typeof(PrimitiveOperators).GetMethod(name, argumentTypes);

            if (methodInfo == null)
                throw new InvalidOperationException(String.Format("No operator named {0} available for type {1}", name, argumentTypes[0].Name));

            object del = Delegate.CreateDelegate(typeof(TDelegate), null, methodInfo);
            return (TDelegate)del;
        }
#endif

        public static string GetSigil (Operators op) {
            return _OperatorInfo[op].Sigil;
        }

        public static UnaryOperatorMethod<T> GetOperator<T> (Operators op) {
            if (!_OperatorInfo[op].IsUnary)
                throw new InvalidOperationException("Operator is not unary");

            return GetOperatorDelegate<UnaryOperatorMethod<T>>(op, new[] { typeof(T) });
        }

        public static BinaryOperatorMethod<T, U> GetOperator<T, U> (Operators op) {
            if (_OperatorInfo[op].IsUnary)
                throw new InvalidOperationException("Operator is not binary");

            return GetOperatorDelegate<BinaryOperatorMethod<T, U>>(op, new[] { typeof(T), typeof(U) });
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

        public static T Clamp<T> (T value, T min, T max)
            where T : IComparable<T> {
            if (value.CompareTo(max) > 0)
                return max;
            else if (value.CompareTo(min) < 0)
                return min;
            else
                return value;
        }

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

        public static float Wrap (float value, float min, float max) {
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

        // Input range: 0-2
        // Output range: -max - max
        public static float PulseCyclicExp (float value, float max) {
            var valueCentered = Wrap(value, 0f, 2f);
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
                Source = (i) => (i >= 1) ? Values[1] : Values[0];
                Values = new T[2];
            }

            public static InterpolatorSource<T> Get (ref T a, ref T b) {
                Values[0] = a;
                Values[1] = b;
                return Source;
            }
        }

        public static T Lerp<T> (T a, T b, float x) 
            where T : struct {

            return Interpolators<T>.Linear(
                LerpSource<T>.Get(ref a, ref b),
                0,
                Clamp(x, 0.0f, 1.0f)
            );
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
    
        public static int NextPowerOfTwo (int value) {
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(value, 2)));
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

            GenerateOperatorIL(es.ILGenerator, new [] { typeLeft, typeRight }, GetOperator(expr.NodeType));

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

            if (!_OperatorInfo.TryGetValue(GetOperator(et), out oi))
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

        internal static void GenerateArithmeticIL (ILGenerator ilGenerator, Type[] argumentTypes, Operators op) {
            if (argumentTypes.Length >= 1)
                ilGenerator.Emit(OpCodes.Ldarg_0);
            
            if (argumentTypes.Length >= 2)
                ilGenerator.Emit(OpCodes.Ldarg_1);

            GenerateOperatorIL(ilGenerator, argumentTypes, op);

            ilGenerator.Emit(OpCodes.Ret);
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

        internal static Type GenerateOperatorIL (ILGenerator ilGenerator, Type[] argumentTypes, Operators op) {
            var oi = _OperatorInfo[op];

            var lhs = argumentTypes[0];
            Type rhs = lhs;

            if (argumentTypes.Length > 1)
                rhs = argumentTypes[1];

            if (lhs.IsPrimitive && rhs.IsPrimitive) {
                ilGenerator.Emit(oi.OpCode);
                return GetPrimitiveResult(lhs, rhs);
            } else {
                MethodInfo operatorMethod = lhs.GetMethod(oi.MethodName, new Type[] { lhs, rhs }, null);
                if (operatorMethod != null) {
                    ilGenerator.EmitCall(OpCodes.Call, operatorMethod, null);
                    return operatorMethod.ReturnType;
                } else {
                    throw new InvalidOperationException(
                        String.Format(
                            "GenerateOperatorIL failed for operator {0} with operands {1}, {2}: operation not implemented",
                            op, lhs, rhs
                        )
                    );
                }
            }
        }

        #endregion
#endif
    }

    public static class FastMath {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorLog2 (int n) {
            int result = 0;
            while (n >= 1) {
                result++;
                n /= 2;
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int SignF (float f) {
            unchecked {
                uint u = *(uint*)&f;
                int signBit = (int)(u >> 31);
                int result = (signBit * -2) + 1;
                return result;
            }
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 4)]
        private struct U32F32 {
            [FieldOffset(0)]
            public uint U;
            [FieldOffset(0)]
            public float F;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe int CompareFSlow (uint u1, uint u2) {
            unchecked {
                int sign1 = (int)(u1 >> 31), sign2 = (int)(u2 >> 31);
                if (sign1 != sign2)
                    return (sign2 - sign1);

                int multiplier = (sign1 * -2) + 1;
                var delta = u1 - u2;
                int result = (int)(delta) * multiplier;
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int CompareF (float lhs, float rhs) {
            unchecked {
                // HACK: The union produces a couple fewer memory loads/stores & fewer insns than the uint* approach.
                //  I'm just going to assume that fewer memory ops & fewer insns = better perf 
                /* Union (other insns removed)
                    mov         qword ptr [rsp+28h],rax  
                    vmovss      xmm0,dword ptr [rdx+70h]  
                    vmovss      xmm1,dword ptr [rsi+70h]  
                    vmovss      dword ptr [rsp+28h],xmm0  
                    mov         ecx,dword ptr [rsp+28h]  
                    vmovss      dword ptr [rsp+28h],xmm1  
                    cmp         ecx,dword ptr [rsp+28h]  
                    mov         eax,dword ptr [rsp+28h]  
                    lea         r8d,[r9*2+1]  
                  Ptrs
                    vmovss      xmm0,dword ptr [rdx+70h]  
                    vmovss      dword ptr [rsp+2Ch],xmm0  
                    vmovss      xmm0,dword ptr [rsi+70h]  
                    vmovss      dword ptr [rsp+28h],xmm0  
                    lea         rcx,[rsp+2Ch]  
                    lea         rax,[rsp+28h]  
                    mov         r8d,dword ptr [rcx]  
                    cmp         r8d,dword ptr [rax]  
                    mov         r8d,dword ptr [rcx]  
                    mov         ecx,dword ptr [rax]  
                    lea         eax,[r9*2+1]  
                */
                uint u1;
                U32F32 u = default(U32F32);
                u.F = lhs;
                u1 = u.U;
                u.F = rhs;

                if (u1 == u.U)
                    return 0;

                return CompareFSlow(u1, u.U);
            }
        }
    }
}

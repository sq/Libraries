using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
#if !XBOX
using System.Reflection.Emit;
using System.Linq.Expressions;
#endif

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
        public enum Operators {
            Add,
            Subtract,
            Multiply,
            Divide,
            Modulo,
            Negate,
            Equality
        }

        public delegate T UnaryOperatorMethod<T> (T value);
        public delegate T BinaryOperatorMethod<T, in U> (T lhs, U rhs);

        internal struct OperatorInfo {
            public OpCode OpCode;
            public String MethodName;
            public bool IsComparison, IsUnary;
        }

        internal static Dictionary<Operators, OperatorInfo> _OperatorInfo = new Dictionary<Operators, OperatorInfo> {
            { Operators.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition" } },
            { Operators.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction" } },
            { Operators.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply" } },
            { Operators.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division" } },
            { Operators.Modulo, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus" } },
            { Operators.Negate, new OperatorInfo { OpCode = OpCodes.Neg, MethodName = "op_UnaryNegation", IsUnary = true } },
            { Operators.Equality, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Equality", IsComparison = true } }
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
            { ExpressionType.Negate, Operators.Negate },
            { ExpressionType.Equal, Operators.Equality },
        };
#endif

#if (WINDOWS || DYNAMICMETHOD) && (!NODYNAMICMETHOD)
        private static TDelegate GetOperatorDelegate<TDelegate> (Operators op, Type[] argumentTypes)
            where TDelegate : class
        {
            Type delegateType = typeof(TDelegate);

            DynamicMethod dm = new DynamicMethod(
                _OperatorInfo[op].MethodName,
                argumentTypes[0],
                argumentTypes,
                true
            );

            ILGenerator ilGenerator = dm.GetILGenerator();
            GenerateArithmeticIL(ilGenerator, argumentTypes, op);
            object del = dm.CreateDelegate(delegateType);

            return (TDelegate)del;
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

        public static float Pulse (float value, float min, float max) {
            value = value % 1.0f;
            float a;
            if (value >= 0.5f) {
                a = (value - 0.5f) / 0.5f;
                return Lerp(max, min, a);
            } else {
                a = value / 0.5f;
                return Lerp(min, max, a);
            }
        }

        public static float PulseExp (float value, float min, float max) {
            value = value % 1.0f;
            float a;
            if (value >= 0.5f) {
                a = (value - 0.5f) / 0.5f;
                return Lerp(max, min, a * a);
            } else {
                a = value / 0.5f;
                return Lerp(min, max, a * a);
            }
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
}

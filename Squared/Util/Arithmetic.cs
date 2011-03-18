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
    public static class Arithmetic {
#if !XBOX
        #region Additional Func overloads
        public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
        public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
        public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
        public delegate TResult Func<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8, out TResult> (T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
        #endregion

        public enum Operators {
            Add = ExpressionType.Add,
            Subtract = ExpressionType.Subtract,
            Multiply = ExpressionType.Multiply,
            Divide = ExpressionType.Divide,
            Modulo = ExpressionType.Modulo
        }

        internal struct OperatorInfo {
            public OpCode OpCode;
            public String MethodName;
            public bool IsComparison;
        }

        public delegate T OperatorMethod<T, in U> (T lhs, U rhs);

        #region Internal constants

        internal static Dictionary<ExpressionType, OperatorInfo> _OperatorInfo = new Dictionary<ExpressionType, OperatorInfo> {
            { ExpressionType.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition" } },
            { ExpressionType.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction" } },
            { ExpressionType.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply" } },
            { ExpressionType.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division" } },
            { ExpressionType.Modulo, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus" } },
            { ExpressionType.Negate, new OperatorInfo { OpCode = OpCodes.Neg, MethodName = "op_UnaryNegation" } },
            { ExpressionType.Equal, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Equality", IsComparison = true } }
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

        #endregion

        public static OperatorMethod<T, U> GetOperatorMethod<T, U> (Operators op) {
            Type delegateType = typeof(OperatorMethod<T, U>);
            Type lhsType = typeof(T);
            Type rhsType = typeof(U);

            DynamicMethod dm = new DynamicMethod(
                _OperatorInfo[(ExpressionType)op].MethodName,
                lhsType,
                new Type[] { lhsType, rhsType },
                true
            );
            ILGenerator ilGenerator = dm.GetILGenerator();
            GenerateArithmeticIL(ilGenerator, lhsType, rhsType, op);
            Delegate del = dm.CreateDelegate(delegateType);

            return (OperatorMethod<T, U>)del;
        }

        public static T InvokeOperator<T, U> (Operators op, T lhs, U rhs) {
            var method = GetOperatorMethod<T, U>(op);
            T result = method(lhs, rhs);
            return result;
        }
#endif

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

#if !XBOX
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
            if (!_OperatorInfo.ContainsKey(expr.NodeType))
                throw new InvalidOperationException(String.Format("Binary operator {0} not supported in expressions", expr.NodeType));

            GenerateOperatorIL(es.ILGenerator, typeLeft, typeRight, expr.NodeType);

            if (expr.Conversion != null)
                return EmitExpression(expr.Conversion, es);
            else if (_OperatorInfo[expr.NodeType].IsComparison)
                return typeof(Boolean);
            else
                return typeLeft;
        }

        internal static Type EmitExpressionNode (UnaryExpression expr, EmitState es) {
            Type t = EmitExpression(expr.Operand, es);

            if (expr.NodeType == ExpressionType.Convert) {
                EmitConversion(t, es);
                return t;
            } else {
                if (!_OperatorInfo.ContainsKey(expr.NodeType))
                    throw new InvalidOperationException(String.Format("Unary operator {0} not supported in expressions", expr.NodeType));

                OperatorInfo op = _OperatorInfo[expr.NodeType];
                es.ILGenerator.Emit(op.OpCode);
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

        internal static void GenerateArithmeticIL (ILGenerator ilGenerator, Type lhs, Type rhs, Operators op) {
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);

            GenerateOperatorIL(ilGenerator, lhs, rhs, (ExpressionType)op);

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

        internal static Type GenerateOperatorIL (ILGenerator ilGenerator, Type lhs, Type rhs, ExpressionType op) {
            OperatorInfo opInfo = _OperatorInfo[op];

            if (lhs.IsPrimitive && rhs.IsPrimitive) {
                ilGenerator.Emit(opInfo.OpCode);
                return GetPrimitiveResult(lhs, rhs);
            } else {
                MethodInfo operatorMethod = lhs.GetMethod(opInfo.MethodName, new Type[] { lhs, rhs }, null);
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

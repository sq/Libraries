using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Emit;
using System.Reflection;
using System.Linq.Expressions;

namespace Squared.Util {
    class MethodCache {
        public delegate void MethodGenerator (ref MethodKey mk, ILGenerator ilGenerator);

        public struct MethodKey : IEquatable<MethodKey> {
            public string Name;
            public Type ReturnType;
            public Type[] ParameterTypes;
            public object Extra;

            public bool Equals (MethodKey other) {
                return Name.Equals(other.Name) &&
                    ReturnType.Equals(other.ReturnType) &&
                    Extra.Equals(other.Extra) &&
                    (ParameterTypes.Length == other.ParameterTypes.Length) &&
                    (ParameterTypes.SequenceEqual(other.ParameterTypes));
            }
        }

        struct DelegateKey : IEquatable<DelegateKey> {
            public MethodKey MethodKey;
            public Type DelegateType;

            public DelegateKey (ref MethodKey key, Type delegateType) {
                MethodKey = key;
                DelegateType = delegateType;
            }

            public bool Equals (DelegateKey other) {
                return DelegateType.Equals(other.DelegateType) &&
                    MethodKey.Equals(other.MethodKey);
            }
        }

        private Dictionary<MethodKey, DynamicMethod> _CachedMethods = new Dictionary<MethodKey, DynamicMethod>();
        private Dictionary<DelegateKey, Delegate> _CachedDelegates = new Dictionary<DelegateKey, Delegate>();

        public DynamicMethod GetMethod (ref MethodKey key, MethodGenerator mg) {
            DynamicMethod newMethod;
            lock (_CachedMethods) {
                if (_CachedMethods.TryGetValue(key, out newMethod))
                    return newMethod;
            }

            newMethod = new DynamicMethod(key.Name, key.ReturnType, key.ParameterTypes, true);
            ILGenerator ilGenerator = newMethod.GetILGenerator();
            mg(ref key, ilGenerator);

            lock (_CachedMethods) {
                _CachedMethods[key] = newMethod;
            }
            return newMethod;
        }

        public Delegate GetDelegate (ref MethodKey key, MethodGenerator mg, Type delegateType) {
            Delegate newDelegate;
            DelegateKey dk = new DelegateKey(ref key, delegateType);

            lock (_CachedDelegates) {
                if (_CachedDelegates.TryGetValue(dk, out newDelegate))
                    return newDelegate;
            }

            DynamicMethod method = GetMethod(ref key, mg);
            newDelegate = method.CreateDelegate(delegateType);

            lock (_CachedDelegates) {
                _CachedDelegates[dk] = newDelegate;
            }
            return newDelegate;
        }
    }

    public static class Arithmetic {
        struct OperatorInfo {
            public OpCode OpCode;
            public String MethodName;
        }

        public enum Operators {
            Add = ExpressionType.Add,
            Subtract = ExpressionType.Subtract,
            Multiply = ExpressionType.Multiply,
            Divide = ExpressionType.Divide,
            Modulo = ExpressionType.Modulo
        }

        public delegate T OperatorMethod<T, U> (T lhs, U rhs);

        private static Dictionary<ExpressionType, OperatorInfo> _OperatorInfo = new Dictionary<ExpressionType, OperatorInfo> {
            { ExpressionType.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition" } },
            { ExpressionType.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction" } },
            { ExpressionType.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply" } },
            { ExpressionType.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division" } },
            { ExpressionType.Modulo, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus" } },
            { ExpressionType.Negate, new OperatorInfo { OpCode = OpCodes.Neg, MethodName = "op_UnaryNegation" } },
            { ExpressionType.Equal, new OperatorInfo { OpCode = OpCodes.Ceq, MethodName = "op_Equality" } }
        };

        private static Dictionary<Type, int> _TypeRanking = new Dictionary<Type, int> {
            { typeof(UInt16), 0 },
            { typeof(Int16), 0 },
            { typeof(UInt32), 1 },
            { typeof(Int32), 1 },
            { typeof(UInt64), 2 },
            { typeof(Int64), 2 },
            { typeof(Single), 3 },
            { typeof(Double), 4 }
        };

        private static MethodCache _OperatorWrappers = new MethodCache();

        public static T Clamp<T> (T value, T min, T max)
            where T : IComparable<T> {
            if (value.CompareTo(max) > 0)
                return max;
            else if (value.CompareTo(min) < 0)
                return min;
            else
                return value;
        }

        private static void GenerateArithmeticIL (ILGenerator ilGenerator, Type lhs, Type rhs, Operators op) {
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);

            GenerateOperatorIL(ilGenerator, lhs, rhs, (ExpressionType)op);

            ilGenerator.Emit(OpCodes.Ret);
        }

        private static Type GetPrimitiveResult (Type lhs, Type rhs) {
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

        private static Type GenerateOperatorIL (ILGenerator ilGenerator, Type lhs, Type rhs, ExpressionType op) {
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

        private static void GenerateILForMethod (ref MethodCache.MethodKey mk, ILGenerator ilGenerator) {
            GenerateArithmeticIL(ilGenerator, mk.ReturnType, mk.ParameterTypes[1], (Operators)mk.Extra);
        }

        public static OperatorMethod<T, U> GetOperatorMethod<T, U> (Operators op) {
            Type delegateType = typeof(OperatorMethod<T, U>);
            Type lhsType = typeof(T);
            Type rhsType = typeof(U);

            MethodCache.MethodKey mk = new MethodCache.MethodKey { 
                Name = _OperatorInfo[(ExpressionType)op].MethodName,
                ReturnType = lhsType,
                ParameterTypes = new Type[] { lhsType, rhsType },
                Extra = op
            };

            Delegate del = _OperatorWrappers.GetDelegate(ref mk, GenerateILForMethod, delegateType);

            return (OperatorMethod<T, U>)del;
        }

        public static T InvokeOperator<T, U> (Operators op, T lhs, U rhs) {
            var method = GetOperatorMethod<T, U>(op);
            T result = method(lhs, rhs);
            return result;
        }

        private class EmitState {
            public ILGenerator ILGenerator;
            public Type ReturnType;
            public Type[] ParameterTypes;
            public Dictionary<string, UInt16> ParameterIndices;
        }

        private static Type EmitExpressionNode (BinaryExpression expr, EmitState es) {
            Type typeLeft = EmitExpression(expr.Left, es);
            Type typeRight = EmitExpression(expr.Right, es);
            if (!_OperatorInfo.ContainsKey(expr.NodeType))
                throw new InvalidOperationException(String.Format("Binary operator {0} not supported in expressions", expr.NodeType));

            GenerateOperatorIL(es.ILGenerator, typeLeft, typeRight, expr.NodeType);

            if (expr.Conversion != null)
                return EmitExpression(expr.Conversion, es);
            else
                return typeLeft;
        }

        private static Type EmitExpressionNode (UnaryExpression expr, EmitState es) {
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

        private static Type EmitExpressionNode (ParameterExpression expr, EmitState es) {
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

        private static Type EmitConversion (Type desiredType, EmitState es) {
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

        private static Type EmitExpressionNode (MethodCallExpression expr, EmitState es) {
            throw new InvalidOperationException("Method calls not supported in expressions");
        }

        private static Type EmitExpressionNode (ConstantExpression expr, EmitState es) {
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

        private static Type EmitExpression (Expression expr, EmitState es) {
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

        private static void _CompileExpression<T> (LambdaExpression expression, out T result) 
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
                String.Format("CompiledExpression", t.Name),
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

            EmitExpression(expression.Body, es);
            ilGenerator.Emit(OpCodes.Ret);

            result = newMethod.CreateDelegate(t) as T;
        }

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
    }
}

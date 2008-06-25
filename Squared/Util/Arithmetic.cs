using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Emit;
using System.Reflection;

namespace Squared.Util {

    class MethodCache {
        public delegate void MethodGenerator (ILGenerator ilGenerator);

        public struct MethodKey {
            public string Name;
            public Type ReturnType;
            public Type[] ParameterTypes;
            public MethodGenerator MethodGenerator;

            public void GenerateIL (ILGenerator ilGenerator) {
                MethodGenerator(ilGenerator);
            }
        }

        private Dictionary<MethodKey, DynamicMethod> _CachedMethods = new Dictionary<MethodKey, DynamicMethod>();

        public DynamicMethod GetMethod (MethodKey key) {
            lock (_CachedMethods) {
                if (_CachedMethods.ContainsKey(key))
                    return _CachedMethods[key];
            }

            DynamicMethod newMethod = new DynamicMethod(key.Name, key.ReturnType, key.ParameterTypes, true);
            ILGenerator ilGenerator = newMethod.GetILGenerator();
            key.GenerateIL(ilGenerator);
            lock (_CachedMethods) {
                _CachedMethods[key] = newMethod;
            }
            return newMethod;
        }
    }

    public static class Arithmetic {
        struct OperatorInfo {
            public OpCode OpCode;
            public String MethodName;
        }

        public enum Operators {
            Add,
            Subtract,
            Multiply,
            Divide
        }

        delegate T OperatorMethod<T, U> (T lhs, U rhs);

        private static Dictionary<Operators, OperatorInfo> _OperatorInfo = new Dictionary<Operators, OperatorInfo> {
            { Operators.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition" } },
            { Operators.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction" } },
            { Operators.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply" } },
            { Operators.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division" } }
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

        private static void GenerateOperatorIL (ILGenerator ilGenerator, Type lhs, Type rhs, Operators op) {
            OperatorInfo opInfo = _OperatorInfo[op];
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);
            if (lhs.IsPrimitive) {
                if (!rhs.IsPrimitive) {
                    throw new InvalidOperationException(
                        String.Format(
                            "GenerateOperatorIL failed for operator {0} with operands {1}, {2}: {2} is not a primitive type, but {1} is",
                            op, lhs, rhs
                        )
                    );
                } else {
                    ilGenerator.Emit(opInfo.OpCode);
                }
            } else {
                MethodInfo operatorMethod = lhs.GetMethod(opInfo.MethodName, new Type[] {lhs, rhs}, null);
                ilGenerator.EmitCall(OpCodes.Call, operatorMethod, null);
            }
            ilGenerator.Emit(OpCodes.Ret);
        }

        private static MethodCache.MethodGenerator GetOperatorGenerator (Type lhs, Type rhs, Operators op) {
            return (ilGenerator) => {
                GenerateOperatorIL(ilGenerator, lhs, rhs, op);
            };
        }

        private static OperatorMethod<T, U> GetOperatorMethod<T, U> (Operators op) {
            Type delegateType = typeof(OperatorMethod<T, U>);
            Type lhsType = typeof(T);
            Type rhsType = typeof(U);

            OperatorInfo opInfo = _OperatorInfo[op];
            MethodCache.MethodKey mk = new MethodCache.MethodKey { 
                Name = opInfo.MethodName,
                ReturnType = lhsType,
                ParameterTypes = new Type[] { lhsType, rhsType },
                MethodGenerator = GetOperatorGenerator(lhsType, rhsType, op)
            };

            DynamicMethod dm = _OperatorWrappers.GetMethod(mk);
            Delegate del = dm.CreateDelegate(delegateType);

            return (OperatorMethod<T, U>)del;
        }

        private static T InvokeOperator<T, U> (Operators op, T lhs, U rhs) {
            var method = GetOperatorMethod<T, U>(op);
            T result = method(lhs, rhs);
            return result;
        }

        public static T Add<T, U> (T lhs, U rhs) {
            return InvokeOperator<T, U>(Operators.Add, lhs, rhs);
        }

        public static T Subtract<T, U> (T lhs, U rhs) {
            return InvokeOperator<T, U>(Operators.Subtract, lhs, rhs);
        }

        public static T Multiply<T, U> (T lhs, U rhs) {
            return InvokeOperator<T, U>(Operators.Multiply, lhs, rhs);
        }

        public static T Divide<T, U> (T lhs, U rhs) {
            return InvokeOperator<T, U>(Operators.Divide, lhs, rhs);
        }
    }
}

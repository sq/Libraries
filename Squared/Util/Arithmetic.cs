using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection.Emit;
using System.Reflection;

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
            Add,
            Subtract,
            Multiply,
            Divide,
            Modulus
        }

        public delegate T OperatorMethod<T, U> (T lhs, U rhs);

        private static Dictionary<Operators, OperatorInfo> _OperatorInfo = new Dictionary<Operators, OperatorInfo> {
            { Operators.Add, new OperatorInfo { OpCode = OpCodes.Add, MethodName = "op_Addition" } },
            { Operators.Subtract, new OperatorInfo { OpCode = OpCodes.Sub, MethodName = "op_Subtraction" } },
            { Operators.Multiply, new OperatorInfo { OpCode = OpCodes.Mul, MethodName = "op_Multiply" } },
            { Operators.Divide, new OperatorInfo { OpCode = OpCodes.Div, MethodName = "op_Division" } },
            { Operators.Modulus, new OperatorInfo { OpCode = OpCodes.Rem, MethodName = "op_Modulus" } }
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
            //Console.WriteLine("Constructing method for params {0}, {1}, {2}", lhs, rhs, op);
            OperatorInfo opInfo = _OperatorInfo[op];

            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);

            if (lhs.IsPrimitive && rhs.IsPrimitive) {
                ilGenerator.Emit(opInfo.OpCode);
            } else if (!lhs.IsPrimitive) {
                MethodInfo operatorMethod = lhs.GetMethod(opInfo.MethodName, new Type[] { lhs, rhs }, null);
                if (operatorMethod != null) {
                    ilGenerator.EmitCall(OpCodes.Call, operatorMethod, null);
                } else {
                    throw new InvalidOperationException(
                        String.Format(
                            "GenerateOperatorIL failed for operator {0} with operands {1}, {2}: operation not implemented",
                            op, lhs, rhs
                        )
                    );
                }
            } else {
                throw new InvalidOperationException(
                    String.Format(
                        "GenerateOperatorIL failed for operator {0} with operands {1}, {2}: {2} is not a primitive type, but {1} is",
                        op, lhs, rhs
                    )
                );
            }

            ilGenerator.Emit(OpCodes.Ret);
        }

        private static void GenerateILForMethod (ref MethodCache.MethodKey mk, ILGenerator ilGenerator) {
            GenerateOperatorIL(ilGenerator, mk.ReturnType, mk.ParameterTypes[1], (Operators)mk.Extra);
        }

        public static OperatorMethod<T, U> GetOperatorMethod<T, U> (Operators op) {
            Type delegateType = typeof(OperatorMethod<T, U>);
            Type lhsType = typeof(T);
            Type rhsType = typeof(U);

            MethodCache.MethodKey mk = new MethodCache.MethodKey { 
                Name = _OperatorInfo[op].MethodName,
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
    }
}

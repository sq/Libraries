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

        delegate T OperatorMethod<T> (T lhs, T rhs);

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

        private static void GenerateOperatorIL (ILGenerator ilGenerator, Type t, Operators op) {
            OperatorInfo opInfo = _OperatorInfo[op];
            ilGenerator.Emit(OpCodes.Ldarg_0);
            ilGenerator.Emit(OpCodes.Ldarg_1);
            if (t.IsPrimitive) {
                ilGenerator.Emit(opInfo.OpCode);
            } else {
                MethodInfo operatorMethod = t.GetMethod(opInfo.MethodName, new Type[] {t, t}, null);
                ilGenerator.EmitCall(OpCodes.Call, operatorMethod, null);
            }
            ilGenerator.Emit(OpCodes.Ret);
        }

        private static MethodCache.MethodGenerator GetOperatorGenerator (Type t, Operators op) {
            return (ilGenerator) => {
                GenerateOperatorIL(ilGenerator, t, op);
            };
        }

        private static OperatorMethod<T> GetOperatorMethod<T> (Operators op) 
            where T : struct {
            Type delegateType = typeof(OperatorMethod<T>);
            Type valueType = typeof(T);
            OperatorInfo opInfo = _OperatorInfo[op];
            MethodCache.MethodKey mk = new MethodCache.MethodKey { 
                Name = String.Format("{0}_{1}", valueType, op),
                ReturnType = valueType,
                ParameterTypes = new Type[] { valueType, valueType },
                MethodGenerator = GetOperatorGenerator(valueType, op)
            };
            DynamicMethod dm = _OperatorWrappers.GetMethod(mk);
            Delegate del = dm.CreateDelegate(delegateType);
            Console.WriteLine("Generated delegate {0} for type {1} operator {2}", del, valueType, op);
            return (OperatorMethod<T>)del;
        }

        public static T Add<T> (T lhs, T rhs)
            where T : struct {
            return GetOperatorMethod<T>(Operators.Add)(lhs, rhs);
        }

        public static T Subtract<T> (T lhs, T rhs) 
            where T : struct {
            return GetOperatorMethod<T>(Operators.Subtract)(lhs, rhs);
        }

        public static T Multiply<T> (T lhs, T rhs)
            where T : struct {
            return GetOperatorMethod<T>(Operators.Multiply)(lhs, rhs);
        }

        public static T Divide<T> (T lhs, T rhs)
            where T : struct {
            return GetOperatorMethod<T>(Operators.Divide)(lhs, rhs);
        }
    }
}

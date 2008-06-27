using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Reflection.Emit;
using System.Reflection;

namespace Squared.Util {
    using Elt = Single;

    delegate void VecOp<A, R> (ref A lhs, ref A rhs, out R result);
    delegate void VecOpScalar<A, B, R> (ref A lhs, B rhs, out R result);
    delegate void VecOpUnary<A, R> (ref A lhs, out R result);

    internal class VectorOperations<Vec> 
        where Vec : struct {

        public VecOp<Vec, Vec> Add;
        public VecOp<Vec, Vec> Subtract;
        public VecOp<Vec, Vec> Multiply;
        public VecOp<Vec, Vec> Divide;

        public VecOpScalar<Vec, Elt, Vec> AddScalar;
        public VecOpScalar<Vec, Elt, Vec> SubtractScalar;
        public VecOpScalar<Vec, Elt, Vec> MultiplyScalar;
        public VecOpScalar<Vec, Elt, Vec> DivideScalar;

        internal VectorOperations () {
            Add = CompileOperator(ExpressionType.Add);
            Subtract = CompileOperator(ExpressionType.Subtract);
            Multiply = CompileOperator(ExpressionType.Multiply);
            Divide = CompileOperator(ExpressionType.Divide);

            AddScalar = CompileScalarOperator(ExpressionType.Add);
            SubtractScalar = CompileScalarOperator(ExpressionType.Subtract);
            MultiplyScalar = CompileScalarOperator(ExpressionType.Multiply);
            DivideScalar = CompileScalarOperator(ExpressionType.Divide);
        }

        private static void DumpILToDisk (byte[] il, int count, Type methodRet, Type[] methodArgs, string filename) {
            AssemblyName asmName = new AssemblyName("temp_assembly");
            AssemblyBuilder asm = AppDomain.CurrentDomain.DefineDynamicAssembly(
                asmName,
                AssemblyBuilderAccess.RunAndSave
            );
            ModuleBuilder module = asm.DefineDynamicModule("dumped_il", filename, true);
            TypeBuilder type = module.DefineType("generated_type", TypeAttributes.Public);
            MethodBuilder method = type.DefineMethod("il_method",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.Assembly,
                methodRet,
                methodArgs
            );
            method.CreateMethodBody(il, count);
            type.CreateType();
            asm.Save(filename);
        }

        private static void GetDynamicMethodIL (DynamicMethod dm, out byte[] bytes, out int count) {
            ILGenerator ilg = dm.GetILGenerator();
            bytes = (byte[])ilg.GetType()
                .GetField("m_ILStream", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ilg);
            count = (int)ilg.GetType()
                .GetField("m_length", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ilg);
        }

        private static VecOp<Vec, Vec> CompileOperator (ExpressionType op) {
            Type delegateType = typeof(VecOp<Vec, Vec>);
            Type vectorType = typeof(Vec);
            Type vectorTypeByRef = vectorType.MakeByRefType();
            Type elementType = typeof(Elt);

            FieldInfo[] fields = vectorType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Type[] arguments = new Type[] { vectorTypeByRef, vectorTypeByRef, vectorTypeByRef };

            DynamicMethod dm = new DynamicMethod(
                Arithmetic._OperatorInfo[op].MethodName,
                null,
                arguments,
                true
            );
            ILGenerator ilGenerator = dm.GetILGenerator();

            ilGenerator.Emit(OpCodes.Ldarg_2);
            ilGenerator.Emit(OpCodes.Initobj, vectorType);

            foreach (var field in fields) {
                ilGenerator.Emit(OpCodes.Ldarg_2);

                ilGenerator.Emit(OpCodes.Ldarg_0);
                ilGenerator.Emit(OpCodes.Ldfld, field);

                ilGenerator.Emit(OpCodes.Ldarg_1);
                ilGenerator.Emit(OpCodes.Ldfld, field);

                ilGenerator.Emit(Arithmetic._OperatorInfo[op].OpCode);

                ilGenerator.Emit(OpCodes.Stfld, field);
            }

            ilGenerator.Emit(OpCodes.Ret);

            Delegate del = dm.CreateDelegate(delegateType);

            return (VecOp<Vec, Vec>)del;
        }

        private static VecOpScalar<Vec, Elt, Vec> CompileScalarOperator (ExpressionType op) {
            Type delegateType = typeof(VecOpScalar<Vec, Elt, Vec>);
            Type vectorType = typeof(Vec);
            Type vectorTypeByRef = vectorType.MakeByRefType();
            Type elementType = typeof(Elt);

            FieldInfo[] fields = vectorType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Type[] arguments = new Type[] { vectorTypeByRef, elementType, vectorTypeByRef };

            DynamicMethod dm = new DynamicMethod(
                Arithmetic._OperatorInfo[op].MethodName,
                null,
                arguments,
                true
            );
            ILGenerator ilGenerator = dm.GetILGenerator();

            ilGenerator.Emit(OpCodes.Ldarg_2);
            ilGenerator.Emit(OpCodes.Initobj, vectorType);

            foreach (var field in fields) {
                ilGenerator.Emit(OpCodes.Ldarg_2);

                ilGenerator.Emit(OpCodes.Ldarg_0);
                ilGenerator.Emit(OpCodes.Ldfld, field);

                ilGenerator.Emit(OpCodes.Ldarg_1);

                ilGenerator.Emit(Arithmetic._OperatorInfo[op].OpCode);

                ilGenerator.Emit(OpCodes.Stfld, field);
            }

            ilGenerator.Emit(OpCodes.Ret);

            Delegate del = dm.CreateDelegate(delegateType);

            return (VecOpScalar<Vec, Elt, Vec>)del;
        }
    }

    public struct Vec2 {
        private static VectorOperations<Vec2> _Operators = new VectorOperations<Vec2>();

        public Elt X, Y;

        public Vec2 (Elt x, Elt y) {
            X = x;
            Y = y;
        }

        public static Vec2 operator + (Vec2 lhs, Vec2 rhs) {
            Vec2 result;
            _Operators.Add(ref lhs, ref rhs, out result);
            return result;
        }

        public static Vec2 operator - (Vec2 lhs, Vec2 rhs) {
            Vec2 result;
            _Operators.Subtract(ref lhs, ref rhs, out result);
            return result;
        }

        public static Vec2 operator * (Vec2 lhs, Vec2 rhs) {
            Vec2 result;
            _Operators.Multiply(ref lhs, ref rhs, out result);
            return result;
        }

        public static Vec2 operator / (Vec2 lhs, Vec2 rhs) {
            Vec2 result;
            _Operators.Divide(ref lhs, ref rhs, out result);
            return result;
        }

        public static Vec2 operator + (Vec2 lhs, Elt rhs) {
            Vec2 result;
            _Operators.AddScalar(ref lhs, rhs, out result);
            return result;
        }

        public static Vec2 operator - (Vec2 lhs, Elt rhs) {
            Vec2 result;
            _Operators.SubtractScalar(ref lhs, rhs, out result);
            return result;
        }

        public static Vec2 operator * (Vec2 lhs, Elt rhs) {
            Vec2 result;
            _Operators.MultiplyScalar(ref lhs, rhs, out result);
            return result;
        }

        public static Vec2 operator / (Vec2 lhs, Elt rhs) {
            Vec2 result;
            _Operators.DivideScalar(ref lhs, rhs, out result);
            return result;
        }

        public Elt Magnitude {
            get {
                return (Elt)Math.Sqrt((X * X) + (Y * Y));
            }
        }

        public Elt Cross (Vec2 rhs) {
            return (X * rhs.X) + (Y * rhs.Y);
        }

        public Elt Dot (Vec2 rhs) {
            return (X * rhs.Y) - (Y * rhs.X);
        }

        public override string ToString () {
            return String.Format("({0}, {1})", X, Y);
        }
    }
}

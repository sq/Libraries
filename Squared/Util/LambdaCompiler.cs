using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util.LambdaCompiler {
    public class ClosureVariable : Expression {
        public readonly string Name;
        public readonly object Value;
        private readonly Type  _Type;

        private FieldBuilder FieldBuilder;

        public ClosureVariable (string name, object value, Type type) {
            Name  = name;
            Value = value;
            _Type = type;
        }

        public void DefineField (TypeBuilder builder) {
            FieldBuilder = builder.DefineField(Name, Type, FieldAttributes.Public | FieldAttributes.Static);                
        }

        public void Initialize (Type builtType) {
            var fieldInfo = builtType.GetField(Name);
            fieldInfo.SetValue(null, Value);
        }

        public override Type Type {
            get {
                return _Type;
            }
        }

        public override ExpressionType NodeType {
            get {
                return ExpressionType.Extension;
            }
        }

        public override bool CanReduce {
            get {
                return true;
            }
        }

        public override Expression Reduce () {
            if (FieldBuilder == null)
                throw new InvalidOperationException("Call DefineField first");

            return Expression.Field(null, FieldBuilder);
        }
    }

    public class ClosureVariables : IEnumerable<ClosureVariable> {
        private readonly Dictionary<string, ClosureVariable> Variables = new Dictionary<string, ClosureVariable>();
        private int NextIndex = 1;

        public ClosureVariable Add (string name, object defaultValue, Type type) {
            var result = new ClosureVariable(name, defaultValue, type);
            Variables.Add(name, result);
            return result;
        }

        public ClosureVariable Add (object defaultValue, Type type) {
            var name = "$anonymous" + (NextIndex++);
            return Add(name, defaultValue, type);
        }

        public ClosureVariable Add<T> (string name, T defaultValue) {
            return Add(name, defaultValue, typeof(T));
        }

        public ClosureVariable Add<T> (T defaultValue) {
            var name = "$anonymous" + (NextIndex++);
            return Add(name, defaultValue, typeof(T));
        }

        public ClosureVariable this[string name] {
            get {
                return Variables[name];
            }
        }

        IEnumerator<ClosureVariable> IEnumerable<ClosureVariable>.GetEnumerator () {
            return Variables.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Variables.Values.GetEnumerator();
        }
    }

    public class Compiler {
        readonly AssemblyBuilder    AssemblyBuilder;
        readonly ModuleBuilder      ModuleBuilder;
        readonly DebugInfoGenerator DebugInfoGenerator;

        public Compiler (string assemblyName) {
            AssemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(
                new AssemblyName(assemblyName + Guid.NewGuid().ToString("N")), 
                AssemblyBuilderAccess.Run
            );

            ModuleBuilder = AssemblyBuilder.DefineDynamicModule("Module", true);
            DebugInfoGenerator = DebugInfoGenerator.CreatePdbGenerator();
        }

        public TDelegate CompileLambda<TDelegate> (
            Expression<TDelegate> lambda, ClosureVariables closure
        )
            where TDelegate : class
        {
            var delegateType = typeof(TDelegate);
            var delegateInvokeMethod = delegateType.GetMethod("Invoke", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (delegateInvokeMethod == null)
                throw new InvalidOperationException("Type must be a delegate type");

            var typeBuilder = ModuleBuilder.DefineType(
                "CompiledLambda_" + Guid.NewGuid().ToString("N"),
                TypeAttributes.Public | TypeAttributes.Sealed
            );

            foreach (var v in closure)
                v.DefineField(typeBuilder);

            var methodBuilder = typeBuilder.DefineMethod(
                "Lambda", 
                MethodAttributes.Public | MethodAttributes.Static,
                delegateInvokeMethod.ReturnType,
                (from p in delegateInvokeMethod.GetParameters() select p.ParameterType).ToArray()
            );

            lambda.CompileToMethod(methodBuilder, DebugInfoGenerator);

            var resultType = typeBuilder.CreateType();

            foreach (var v in closure)
                v.Initialize(resultType);

            var resultMethod = typeBuilder.GetMethod("Lambda");
            var result = Delegate.CreateDelegate(delegateType, resultMethod, true) as TDelegate;
            if (result == null)
                throw new Exception("Failed to create delegate");

            return result;
        }
    }
}

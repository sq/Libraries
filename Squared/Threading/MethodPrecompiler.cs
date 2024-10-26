using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class MethodPrecompiler {
        public Action<string> LogPrint;

        internal HashSet<Type> PrecompiledTypes = new HashSet<Type>(ReferenceComparer<Type>.Instance);
        internal HashSet<(Type, Type)> PrecompiledMethodBuilders = new HashSet<(Type, Type)>();

        struct PrecompileMethodWorkItem : IWorkItem {
            public static WorkItemConfiguration Configuration =>
                // Reduce the overhead of processing each item, but also make sure that
                //  the queue won't block things like frame prepares
                new WorkItemConfiguration {
                    ConcurrencyPadding = 2,
                    DefaultStepCount = 3
                };

            public MethodPrecompiler Precompiler;
            public string TypeName, Name;
            public RuntimeMethodHandle MethodHandle;
            public RuntimeTypeHandle[] TypeHandles;

            public void Execute (ThreadGroup group) {
                var lp = Precompiler.LogPrint;
                try {
                    if ((TypeHandles?.Length ?? 0) > 0)
                        RuntimeHelpers.PrepareMethod(MethodHandle, TypeHandles);
                    else
                        RuntimeHelpers.PrepareMethod(MethodHandle);
                    if (lp != null)
                        lp($"compiled {TypeName}::{Name}");
                } catch (Exception exc) {
                    var msg = $"failed to compile {TypeName}::{Name}: {exc}";
                    if (lp != null)
                        lp(msg);
                    else
                        System.Diagnostics.Debug.WriteLine(msg);
                }
            }
        }

        private void EnqueueType (ThreadGroup threadGroup, Type type, List<Type> queue) {
            if (type == null)
                return;
            lock (PrecompiledTypes) {
                if (PrecompiledTypes.Contains(type))
                    return;
            }

            // We don't want to bother precompiling these types because they are very heavy
            //  and most of their methods won't spend time in the PreStub
            if (type.Name.Contains("Awaiter"))
                return;
            else if (type.Name.StartsWith("Task"))
                return;
            else if (type.Name.StartsWith("ValueTask"))
                return;

            if (queue == null) {
                PrecompileMethods(threadGroup, type, null);
            } else {
                if (queue.Contains(type))
                    return;
                queue.Add(type);
            }
        }

        public int PrecompileMethods (ThreadGroup threadGroup, Type type, List<Type> queue) {
            lock (PrecompiledTypes) {
                if (PrecompiledTypes.Contains(type))
                    return 0;
                PrecompiledTypes.Add(type);
            }

            // This will cause methods to fail to compile
            if (type.IsGenericTypeDefinition)
                return 0;

            int result = 0;

            var baseType = type.BaseType;
            // Make sure we catch state machines defined in base types (i.e. CombatScriptContext<> via ActionScriptContext)
            if (baseType.Assembly != typeof(object).Assembly)
                PrecompileMethods(threadGroup, baseType, queue);

            foreach (var nestedType in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) {
                // Precompile any nested types that are async state machines or generators
                //  because they tend to have very large methods that depend on many other
                //  types and methods
                if (
                    typeof(IAsyncStateMachine).IsAssignableFrom(nestedType) ||
                    typeof(IEnumerable).IsAssignableFrom(nestedType) ||
                    typeof(IEnumerator).IsAssignableFrom(nestedType)
                ) {
                    if (nestedType.IsGenericTypeDefinition && type.IsGenericType) {
                        var parentGp = type.GetGenericArguments();
                        if (parentGp.Length == nestedType.GetGenericArguments().Length) {
                            var nestedTypeInstance = nestedType.MakeGenericType(parentGp);
                            EnqueueType(threadGroup, nestedTypeInstance, queue);
                        }
                    } else
                        EnqueueType(threadGroup, nestedType, queue);
                }
            }

            var workQueue = threadGroup.GetQueueForType<PrecompileMethodWorkItem>();

            // The TaskMethodBuilder.Start method takes a generic parameter (the state machine), so our normal
            //  logic will not precompile it. However, when we encounter the actual state machine type we can
            //  recognize that it is a state machine and locate its builder and use those types to identify
            //  the exact Start method we should precompile.
            if (typeof(IAsyncStateMachine).IsAssignableFrom(type)) {
                var builderField = type.GetField("<>t__builder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? 
                    type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(f => f.FieldType.Name.Contains("TaskMethodBuilder"));
                PrecompileMethodBuilder(type, builderField?.FieldType, workQueue);
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                if (method.IsAbstract)
                    continue;
                // PrepareMethod will fail
                else if (method.IsGenericMethod || method.IsGenericMethodDefinition)
                    continue;

                var declaringType = method.DeclaringType;
                // Don't queue Object.Equals and such for precompile a billion times
                if (declaringType.Assembly == typeof(object).Assembly)
                    continue;

                var returnType = method.ReturnType;

                if (declaringType.IsGenericTypeDefinition)
                    continue;

                RuntimeTypeHandle[] genericTypes = null;
                if (declaringType.IsGenericType) {
                    var typeArguments = declaringType.GetGenericArguments();
                    genericTypes = new RuntimeTypeHandle[typeArguments.Length];
                    for (int i = 0; i < typeArguments.Length; i++)
                        genericTypes[i] = typeArguments[i].TypeHandle;
                }

                workQueue.Enqueue(new PrecompileMethodWorkItem {
                    Precompiler = this,
                    TypeName = (LogPrint != null) ? declaringType.FullName : null,
                    Name = (LogPrint != null) ? method.Name : null,
                    MethodHandle = method.MethodHandle,
                    TypeHandles = genericTypes
                });

                var isPossiblyStateMachine = (returnType != null) &&
                    (
                        typeof(Task).IsAssignableFrom(returnType) ||
                        (returnType.Name?.StartsWith("ValueTask") ?? false) ||
                        (returnType.GetCustomAttribute<CompilerGeneratedAttribute>() != null)
                    );

                if (isPossiblyStateMachine)
                    EnqueueType(threadGroup, returnType, queue);

                if (returnType?.Name == "Task`1") {
                    var builderType = typeof(AsyncTaskMethodBuilder<>).MakeGenericType(returnType.GetGenericArguments());
                    EnqueueType(threadGroup, builderType, queue);
                } else if (returnType?.Name == "ValueTask`1") {
                    var builderType = returnType.Assembly.GetType("AsyncValueTaskMethodBuilder`1").MakeGenericType(returnType.GetGenericArguments());
                    EnqueueType(threadGroup, builderType, queue);
                }

                result++;
            }

            if (result > 0)
                threadGroup.NotifyQueuesChanged(result > 4);

            return result;
        }

        private void PrecompileMethodBuilder (
            Type stateMachineType, Type fieldType, 
            WorkQueue<PrecompileMethodWorkItem> workQueue
        ) {
            if (fieldType == null)
                return;

            var tup = (stateMachineType, fieldType);
            lock (PrecompiledMethodBuilders) {
                if (PrecompiledMethodBuilders.Contains(tup))
                    return;
                PrecompiledMethodBuilders.Add(tup);
            }

            if (LogPrint != null)
                LogPrint($"Queued state machine {stateMachineType} start methods for compile");

            var startMethod = fieldType.GetMethod("Start");
            var returnType = fieldType.IsGenericType
                ? fieldType.GetGenericArguments()[0]
                : null;
            workQueue.Enqueue(new PrecompileMethodWorkItem {
                Precompiler = this,
                TypeName = (LogPrint != null) ? fieldType.FullName : null,
                Name = $"Start<{stateMachineType}>",
                MethodHandle = startMethod.MethodHandle,
                TypeHandles = returnType != null
                    ? new[] { returnType.TypeHandle, stateMachineType.TypeHandle }
                    : new[] { stateMachineType.TypeHandle }
            });

            // ValueTaskMethodBuilder's Start method is very simple and delegates to the one for Task, so we want to make
            //  sure that we JIT that one too
            if (fieldType.Name.Contains("ValueTask")) {
                var builderType = returnType != null
                    ? typeof(AsyncTaskMethodBuilder<>).MakeGenericType(returnType)
                    : typeof(AsyncTaskMethodBuilder);
                PrecompileMethodBuilder(stateMachineType, builderType, workQueue);
            }
        }

        public bool WaitForPrecompilingMethods (ThreadGroup threadGroup, int timeoutMs = -1) {
            var workQueue = threadGroup.GetQueueForType<PrecompileMethodWorkItem>();
            return workQueue.WaitUntilDrained(timeoutMs);
        }
    }
}

#define TRACING

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Squared.Util;

// FIXME: This whole file needs unit tests

using tTask = System.Threading.Tasks.Task;
using CallContext = System.Runtime.Remoting.Messaging.CallContext;
using TaskStatus = System.Threading.Tasks.TaskStatus;
using System.Linq.Expressions;
using Squared.Threading;

namespace Squared.Threading.AsyncAwait {
    public class CancellationScope {
        /// <summary>
        /// Set this to false in order to allow cancellation scopes to be used on threads without an active scheduler.
        /// </summary>
        public static bool StrictMode = false;

        public class Registration {
            public readonly CancellationScope    Scope;
            public readonly IWorkItemQueueTarget Scheduler;

            private Action Continuation;

            public Registration (IWorkItemQueueTarget scheduler) {
                Scope = CancellationScope.Current;
                Scheduler = scheduler;

                if ((Scheduler == null) && StrictMode)
                    throw new InvalidOperationException("No implicitly active TaskScheduler on this thread.");
            }

            public Squared.Threading.OnFutureResolved OnComplete (Action continuation) {
                if ((Continuation != null) && (Continuation != continuation))
                    throw new InvalidOperationException("Continuation already registered");

                Continuation = continuation;
                return _OnComplete;
            }

            private void _OnComplete (IFuture f) {
                // FIXME: Is this right?
                if ((Scheduler == null) && !StrictMode)
                    Continuation();
                else
                    Scheduler.QueueWorkItem(Continuation);
            }

            public void ThrowIfCanceled () {
                Scope.ThrowIfCanceled();
            }
        }

        public struct CancellationScopeAwaiter : INotifyCompletion {
            public readonly CancellationScope Scope;

            public CancellationScopeAwaiter (CancellationScope scope)
                : this()
            {
                Scope = scope;
                IsCompleted = false;
            }

            public void OnCompleted (Action continuation) {
                CancellationUtil.UnpackContinuation(continuation, out Scope.Task);

                CancellationScope existingScope;
                if (CancellationScope.TryGet(Scope.Task, out existingScope)) {
                    // HACK: In some cases a cancelled task will get resumed, which starts it over from the beginning.
                    // This hits us and we will get asked to schedule a resume, but we should ignore it so that the task
                    //  stays dead as it should.

#if TRACING
                    Console.WriteLine("Rejecting attempted resurrection of task {0}", existingScope);
#endif
                    return;
                }

                CancellationScope.Set(Scope.Task, Scope);
                IsCompleted = true;

                continuation();
            }

            public bool IsCompleted {
                get;
                private set;
            }

            public CancellationScope GetResult () {
                Scope.ThrowIfCanceled();
                return Scope;
            }
        }


        public static readonly CancellationScope Null = new CancellationScope("<null>");

        private static readonly ConditionalWeakTable<tTask, CancellationScope> ScopeRegistry = new ConditionalWeakTable<tTask, CancellationScope>();
        private static int NextId;

        private static Future<CancellationScope> Reserved = null;

        public static CancellationScope Current {
            get {
                var result = (CancellationScope)CallContext.LogicalGetData("CancellationScope");
                if (result == null)
                    result = Null;
                return result;
            }
        }

        public static void Set (tTask task, CancellationScope scope) {
            ScopeRegistry.Add(task, scope);
        }

        public static bool TryGet (tTask task, out CancellationScope result) {
            return ScopeRegistry.TryGetValue(task, out result);
        }

        public static Future<CancellationScope> Reserve () {
            if (Reserved != null)
                throw new Exception("Scope already reserved");

            Reserved = new Future<CancellationScope>();
            return Reserved;
        }


        public readonly string Description;
        public readonly string FilePath;
        public readonly int    LineNumber;
        public readonly int    Id;

        private UnorderedList<CancellationScope> Children;
        private tTask                            Task;
        private bool                             _IsCanceled;

        public CancellationScope (
            [CallerMemberName]
            string description = null,
            [CallerFilePath]
            string filePath = null,
            [CallerLineNumber]
            int lineNumber = 0
        ) {
            Id = ++NextId;
            Description = description;
            FilePath = filePath;
            LineNumber = lineNumber;
        }

        public CancellationScopeAwaiter GetAwaiter () {
            ThrowIfCanceled();

            // Important to Push here instead of doing so during the OnComplete call.
            // This ensures we're pushed into the right local call context.
            Push();
            return new CancellationScopeAwaiter(this);
        }

        public CancellationScope Parent {
            get;
            private set;
        }

        public bool IsCanceled {
            get {
                if (_IsCanceled)
                    return true;
                else if (Parent != null)
                    return Parent.IsCanceled;
                else
                    return false;
            }
        }

        private void AddChild (CancellationScope child) {
            if (this == Null)
                return;

            if (Children == null)
                Children = new UnorderedList<CancellationScope>();

            Children.Add(child);
        }

        public CancellationScope Push () {
            Parent = Current;
            Parent.AddChild(this);

            CallContext.LogicalSetData("CancellationScope", this);

            return Parent;
        }

        public bool TryCancel () {
            if (this == Null)
                return false;

            if (Children != null) {
                CancellationScope childScope;

                using (var e = Children.GetEnumerator())
                while (e.GetNext(out childScope))
                    childScope.TryCancel();
            }

            var wasCanceled = _IsCanceled;
            _IsCanceled = true;

            if (Task == null)
                return false;

            switch (Task.Status) {
                case TaskStatus.Canceled:
                case TaskStatus.Faulted:
                    return true;

                case TaskStatus.RanToCompletion:
                    return false;
            }

            return true;
        }

        public void ThrowIfCanceled () {
            if (IsCanceled)
                throw new OperationCanceledException(ToString() + " cancelled");
        }

        public override string ToString () {
            return String.Format("<scope #{0} {1}>", Id, Description);
        }
    }

    internal static class CancellationUtil {
        delegate Action TTryGetStateMachineForDebugger (Action continuation);
        delegate tTask  TExtractTaskFromStateMachine   (object stateMachine);

        static readonly TTryGetStateMachineForDebugger TryGetStateMachineForDebugger;
        static readonly Dictionary<Type, TExtractTaskFromStateMachine> ExtractorCache = new Dictionary<Type, TExtractTaskFromStateMachine>();

        static CancellationUtil () {
            var tMethodBuilderCore = System.Type.GetType("System.Runtime.CompilerServices.AsyncMethodBuilderCore", true);
            var mi = tMethodBuilderCore.GetMethod("TryGetStateMachineForDebugger", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            TryGetStateMachineForDebugger = (TTryGetStateMachineForDebugger)Delegate.CreateDelegate(
                typeof(TTryGetStateMachineForDebugger), mi, true
            );
        }

        private static TExtractTaskFromStateMachine CreateExtractor (Type tMachine) {
            var fBuilder = tMachine.GetField("<>t__builder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var tBuilder = fBuilder.FieldType;
            var pTask = tBuilder.GetProperty("Task", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            {
                var pStateMachine = Expression.Parameter(typeof(object), "stateMachine");
                var eCast = Expression.Convert(pStateMachine, tMachine);
                var eBuilder = Expression.MakeMemberAccess(
                    eCast, fBuilder
                );
                var eTask = Expression.MakeMemberAccess(
                    eBuilder, pTask
                );
                var eLambda = Expression.Lambda<TExtractTaskFromStateMachine>(eTask, pStateMachine);
                return eLambda.Compile();
            }
        }

        public static void UnpackContinuation (Action continuation, out tTask task) {
            var stateMachine = TryGetStateMachineForDebugger(continuation);
            if (stateMachine == null)
                throw new Exception("Could not extract state machine from continuation");

            var machine = stateMachine.Target;
            var tMachine = machine.GetType();
            TExtractTaskFromStateMachine extractor;

            lock (ExtractorCache)
            if (!ExtractorCache.TryGetValue(tMachine, out extractor))
                ExtractorCache.Add(tMachine, extractor = CreateExtractor(tMachine));

            task = extractor(machine);
        }
    }
}

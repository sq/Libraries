using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
// FIXME: This whole file needs unit tests

using tTask = System.Threading.Tasks.Task;
using CallContext = System.Runtime.Remoting.Messaging.CallContext;
using Squared.Util;

namespace Squared.Task {
    public class CancellationScope {
        public class Registration {
            public readonly CancellationScope Scope;
            public readonly TaskScheduler     Scheduler;

            private Action Continuation;

            public Registration () {
                Scope = CancellationScope.Current;
                Scheduler = TaskScheduler.Current;

                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active TaskScheduler on this thread.");
            }

            public Squared.Task.OnComplete OnComplete (Action continuation) {
                if ((Continuation != null) && (Continuation != continuation))
                    throw new InvalidOperationException("Continuation already registered");

                Continuation = continuation;
                return _OnComplete;
            }

            private void _OnComplete (IFuture f) {
                Scheduler.QueueWorkItem(Continuation);
            }

            public void ThrowIfCanceled () {
                Scope.ThrowIfCanceled();
            }
        }

        public struct Awaiter : INotifyCompletion {
            public readonly CancellationScope Scope;

            public Awaiter (CancellationScope scope)
                : this()
            {
                Scope = scope;
                IsCompleted = false;
            }

            public void OnCompleted (Action continuation) {
                tTask task;
                IAsyncStateMachine stateMachine;

                CancellationUtil.UnpackContinuation(continuation, out task, out stateMachine);

                CancellationScope existingScope;
                if (CancellationScope.TryGet(task, out existingScope)) {
                    // HACK: In some cases a cancelled task will get resumed, which starts it over from the beginning.
                    // This hits us and we will get asked to schedule a resume, but we should ignore it so that the task
                    //  stays dead as it should.
                    Console.WriteLine("Rejecting attempted resurrection of task {0}", existingScope);
                    return;
                }

                CancellationScope.Set(task, Scope);
                IsCompleted = true;

                Scope.SetStateMachine(stateMachine);

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


        public readonly string Description;
        public readonly string FilePath;
        public readonly int    LineNumber;
        public readonly int    Id;

        private IAsyncStateMachine _StateMachine;
        private bool               _IsCanceled;

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

        private void SetStateMachine (IAsyncStateMachine stateMachine) {
            if (this == Null)
                throw new InvalidOperationException("Cannot set a state machine on the null scope");

            if ((_StateMachine != null) && (_StateMachine != stateMachine))
                throw new InvalidOperationException("Already have a state machine for this scope");

            _StateMachine = stateMachine;
        }

        public Awaiter GetAwaiter () {
            ThrowIfCanceled();

            // Important to Push here instead of doing so during the OnComplete call.
            // This ensures we're pushed into the right local call context.
            Push();
            return new Awaiter(this);
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

        public CancellationScope Push () {
            Parent = Current;
            CallContext.LogicalSetData("CancellationScope", this);

            return Parent;
        }

        public bool TryCancel () {
            if (this == Null)
                return false;

            if (_StateMachine == null)
                return false;

            var wasCanceled = _IsCanceled;
            _IsCanceled = true;

            if (!wasCanceled)
                _StateMachine.MoveNext();

            return true;
        }

        public void ThrowIfCanceled () {
            if (IsCanceled)
                throw new OperationCanceledException("This scope was cancelled");
        }

        private tTask ThrowIfCanceled (tTask task) {
            ThrowIfCanceled();

            return task;
        }

        public override string ToString () {
            if (Description != null)
                return String.Format("Scope #{0} '{1}'", Id, Description);
            else
                return String.Format("Scope #{0}", Id);
        }
    }

    internal static class CancellationUtil {
        public static void UnpackContinuation (Action continuation, out tTask task, out IAsyncStateMachine stateMachine) {
            // FIXME: Optimize this
            var continuationWrapper = continuation.Target;
            var tCw = continuationWrapper.GetType();

            var fInvokeAction = tCw.GetField("m_invokeAction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var invokeAction = (Delegate)fInvokeAction.GetValue(continuationWrapper);
            var methodBuilder = invokeAction.Target;
            var tMb = methodBuilder.GetType();
            var fInnerTask = tMb.GetField("innerTask", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            task = (System.Threading.Tasks.Task)fInnerTask.GetValue(methodBuilder);

            var fContinuation = tCw.GetField("m_continuation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var innerContinuation = (Delegate)fContinuation.GetValue(continuationWrapper);
            var moveNextRunner = innerContinuation.Target;
            var tRunner = moveNextRunner.GetType();
            var fStateMachine = tRunner.GetField("m_stateMachine", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            stateMachine = (IAsyncStateMachine)fStateMachine.GetValue(moveNextRunner);
        }
    }
}

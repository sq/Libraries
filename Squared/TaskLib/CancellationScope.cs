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
                CancellationUtil.UnpackContinuation(continuation, out Scope.Task, out Scope.Continuation);

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

        private UnorderedList<CancellationScope> Children;
        private tTask                            Task;
        private Action                           Continuation;
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

            if (Task.IsCanceled)
                return true;
            else if (Task.IsCompleted)
                return false;

            if (Continuation == null)
                return false;

            if (!wasCanceled) {
#if TRACING
                Console.WriteLine("Advancing {0} to cancel it", Description);
#endif
                Continuation();
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
        public static void UnpackContinuation (Action continuation, out tTask task, out Action resume) {
            // FIXME: Optimize this
            var continuationWrapper = continuation.Target;
            var tCw = continuationWrapper.GetType();

            var fInvokeAction = tCw.GetField("m_invokeAction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var invokeAction = (Delegate)fInvokeAction.GetValue(continuationWrapper);
            var methodBuilder = invokeAction.Target;
            var tMb = methodBuilder.GetType();
            var fInnerTask = tMb.GetField("innerTask", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            task = (System.Threading.Tasks.Task)fInnerTask.GetValue(methodBuilder);

            /*
            var fContinuation = tCw.GetField("m_continuation", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var innerContinuation = (Delegate)fContinuation.GetValue(continuationWrapper);
            var moveNextRunner = innerContinuation.Target;
            var tRunner = moveNextRunner.GetType();
            var fStateMachine = tRunner.GetField("m_stateMachine", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            stateMachine = (IAsyncStateMachine)fStateMachine.GetValue(moveNextRunner);
             */

            resume = continuation;
        }
    }
}

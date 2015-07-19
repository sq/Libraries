using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
// FIXME: This whole file needs unit tests

using tTask = System.Threading.Tasks.Task;

namespace Squared.Task {
    public static class FutureAwaitExtensionMethods {
        public struct FutureAwaiter<TResult> : INotifyCompletion {
            public readonly TaskScheduler Scheduler;
            public readonly Future<TResult> Future;

            public FutureAwaiter (Future<TResult> future) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(TaskCancellation.ConditionalResume(Scheduler, continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public TResult GetResult () {
                TaskCancellation.ThrowIfCancelling();

                return Future.Result;
            }
        }

        public struct IFutureAwaiter : INotifyCompletion {
            public readonly TaskScheduler Scheduler;
            public readonly IFuture Future;

            public IFutureAwaiter (IFuture future) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(TaskCancellation.ConditionalResume(Scheduler, continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
                TaskCancellation.ThrowIfCancelling();

                return Future.Result;
            }
        }

        public struct VoidFutureAwaiter : INotifyCompletion {
            public readonly TaskScheduler Scheduler;
            public readonly Future<NoneType> Future;

            public VoidFutureAwaiter (Future<NoneType> future) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(TaskCancellation.ConditionalResume(Scheduler, continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public void GetResult () {
                TaskCancellation.ThrowIfCancelling();

                var @void = Future.Result;
                return;
            }
        }

        public struct ISchedulableAwaiter : INotifyCompletion {
            public readonly TaskScheduler Scheduler;
            public readonly ISchedulable  Schedulable;
            public readonly IFuture       Future;

            public ISchedulableAwaiter (ISchedulable schedulable) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Schedulable = schedulable;
                Future = Scheduler.Start(schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(TaskCancellation.ConditionalResume(Scheduler, continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
                TaskCancellation.ThrowIfCancelling();

                return Future.Result;
            }
        }

        public struct ISchedulableAwaiter<T> : INotifyCompletion {
            public readonly TaskScheduler   Scheduler;
            public readonly ISchedulable<T> Schedulable;
            public readonly Future<T>       Future;

            public ISchedulableAwaiter (ISchedulable<T> schedulable) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Schedulable = schedulable;
                Future = new Future<T>();
                Scheduler.Start(Future, schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(TaskCancellation.ConditionalResume(Scheduler, continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public T GetResult () {
                TaskCancellation.ThrowIfCancelling();

                return Future.Result;
            }
        }

        public struct SequenceAwaiter<T> : INotifyCompletion {
            public readonly TaskScheduler Scheduler;
            public readonly Future<T>[]   Futures;
            public readonly IFuture       Ready;

            public SequenceAwaiter (ISchedulable<T>[] schedulables) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Futures = new Future<T>[schedulables.Length];
                if (Futures.Length == 0) {
                    Ready = new SignalFuture(true);
                    return;
                }

                for (var i = 0; i < Futures.Length; i++)
                    Futures[i] = Scheduler.Start(schedulables[i]);

                Ready = Future.WaitForAll(Futures);
            }

            public SequenceAwaiter (Future<T>[] futures) {
                Scheduler = TaskScheduler.Current;
                if (Scheduler == null)
                    throw new InvalidOperationException("No implicitly active scheduler. Use 'await Scheduler.Start(x)'.");

                Futures = futures;
                if (Futures.Length == 0) {
                    Ready = new SignalFuture(true);
                    return;
                }

                Ready = Future.WaitForAll(futures);
            }

            public void OnCompleted (Action continuation) {
                Ready.RegisterOnComplete(TaskCancellation.ConditionalResume(Scheduler, continuation));
            }

            public bool IsCompleted {
                get {
                    return Ready.Completed;
                }
            }

            public T[] GetResult () {
                TaskCancellation.ThrowIfCancelling();

                var result = new T[Futures.Length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = Futures[i].Result;

                return result;
            }
        }

        public static FutureAwaiter<T> GetAwaiter<T> (this Future<T> future) {
            return new FutureAwaiter<T>(future);
        }

        public static IFutureAwaiter GetAwaiter (this IFuture future) {
            return new IFutureAwaiter(future);
        }

        public static VoidFutureAwaiter GetAwaiter (this Future<NoneType> future) {
            return new VoidFutureAwaiter(future);
        }

        public static ISchedulableAwaiter GetAwaiter (this ISchedulable schedulable) {
            return new ISchedulableAwaiter(schedulable);
        }

        public static ISchedulableAwaiter<T> GetAwaiter<T> (this ISchedulable<T> schedulable) {
            return new ISchedulableAwaiter<T>(schedulable);
        }

        public static ISchedulableAwaiter GetAwaiter (this IEnumerable<IFuture> futures) {
            var wfa = new WaitForAll(futures.ToArray());
            return new ISchedulableAwaiter(wfa);
        }

        public static ISchedulableAwaiter GetAwaiter (this IEnumerable<ISchedulable> schedulables) {
            var wfa = new WaitForAll(schedulables.ToArray());
            return new ISchedulableAwaiter(wfa);
        }

        public static SequenceAwaiter<T> GetAwaiter<T> (this IEnumerable<Future<T>> futures) {
            return new SequenceAwaiter<T>(futures.ToArray());
        }

        public static SequenceAwaiter<T> GetAwaiter<T> (this IEnumerable<ISchedulable<T>> schedulables) {
            return new SequenceAwaiter<T>(schedulables.ToArray());
        }

        public static IFutureAwaiter GetAwaiter (this IEnumerator<object> task) {
            var scheduler = TaskScheduler.Current;
            if (scheduler == null)
                throw new InvalidOperationException("No active TaskScheduler");

            var f = scheduler.Start(task, TaskExecutionPolicy.RunWhileFutureLives);
            return new IFutureAwaiter(f);
        }

        public static SignalFuture GetFuture (this tTask task) {
            var result = new SignalFuture();
            BindFuture(task, result);
            return result;
        }

        public static Future<T> GetFuture<T> (this System.Threading.Tasks.Task<T> task) {
            var result = new Future<T>();
            BindFuture(task, result);
            return result;
        }

        public static void BindFuture (this tTask task, IFuture future) {
            task.GetAwaiter().OnCompleted(() => {
                if (task.Exception != null)
                    future.SetResult(null, task.Exception);
                else
                    future.Complete();
            });
            future.RegisterOnDispose((_) => {
                TaskCancellation.TryCancel(task);
            });
        }

        public static void BindFuture<T> (this System.Threading.Tasks.Task<T> task, Future<T> future) {
            task.GetAwaiter().OnCompleted(() => {
                if (task.Exception != null)
                    future.SetResult(default(T), task.Exception);
                else
                    future.SetResult(task.Result, task.Exception);
            });
            future.RegisterOnDispose((_) => {
                TaskCancellation.TryCancel(task);
            });
        }
    }

    public struct ExternalCancellation : INotifyCompletion {
        public static readonly ExternalCancellation Allow = new ExternalCancellation();
        private System.Threading.Tasks.Task Caller;

        public ExternalCancellation GetAwaiter () {
            return this;
        }

        public void OnCompleted (Action continuation) {
            Caller = TaskCancellation.RegisterCancellationTarget(continuation);

            // HACK: In some cases a cancelled task will get resumed, which starts it over from the beginning.
            // This hits us and we will get asked to schedule a resume, but we should ignore it so that the task
            //  stays dead as it should.
            if (Caller == null)
                return;

            continuation();
        }

        public bool IsCompleted {
            get {
                return (Caller != null);
            }
        }

        public System.Threading.Tasks.Task GetResult () {
            TaskCancellation.ThrowIfCancelling(Caller);

            return Caller;
        }
    }

    public static class TaskCancellation {
        private static object Token = new object();
        private static readonly ConditionalWeakTable<System.Threading.Tasks.Task, Action> CancellerRegistry = new ConditionalWeakTable<tTask, Action>();
        private static readonly ConditionalWeakTable<System.Threading.Tasks.Task, object> CancelledRegistry = new ConditionalWeakTable<tTask, object>();
        private static readonly Stack<bool> CancellationStack = new Stack<bool>();

        static TaskCancellation () {
            CancellationStack.Push(false);
        }

        public static System.Threading.Tasks.Task GetTaskFromContinuation (Action continuation) {
            // FIXME: Optimize this
            var continuationWrapper = continuation.Target;
            var tCw = continuationWrapper.GetType();
            var fInvokeAction = tCw.GetField("m_invokeAction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var invokeAction = (Delegate)fInvokeAction.GetValue(continuationWrapper);
            var methodBuilder = invokeAction.Target;
            var tMb = methodBuilder.GetType();
            var fInnerTask = tMb.GetField("innerTask", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var innerTask = (System.Threading.Tasks.Task)fInnerTask.GetValue(methodBuilder);
            return innerTask;
        }

        public static System.Threading.Tasks.Task RegisterCancellationTarget (Action resumeContinuation) {
            var innerTask = GetTaskFromContinuation(resumeContinuation);

            object token;
            if (CancelledRegistry.TryGetValue(innerTask, out token))
                return null;

            lock (CancellerRegistry)
                CancellerRegistry.Add(innerTask, resumeContinuation);

            return innerTask;
        }

        public static bool Cancelling {
            get {
                return CancellationStack.Peek();
            }
        }

        public static Squared.Task.OnComplete ConditionalResume (TaskScheduler scheduler, Action resumeContinuation) {
            return (future) => {
                // HACK: If the task was cancelled while we were waiting, invoking the resume
                //  continuation will end up executing the task again from the beginning... WTF
                // The AllowExternalCancellation will drop the resume on the floor, though.
                /*
                var t = GetTaskFromContinuation(resumeContinuation);
                if (t.IsCanceled)
                    return;
                 */

                scheduler.QueueWorkItem(resumeContinuation);
            };
        }

        public static void ThrowIfCancelling (tTask task = null) {
            if (Cancelling)
                throw new OperationCanceledException("Externally cancelled");

            if (task != null) {
                object token;
                if (CancelledRegistry.TryGetValue(task, out token)) {
                    Console.WriteLine("Is task {0} cancelled? yes", task);
                    throw new OperationCanceledException("Externally cancelled");
                } else {
                    Console.WriteLine("Is task {0} cancelled? no", task);
                }
            }
        }

        public static bool TryCancel (tTask task) {
            if (task.IsCanceled)
                return true;
            else if (task.IsCompleted)
                return false;

            CancelledRegistry.Add(task, Token);

            Action canceller;
            lock (CancellerRegistry)
            if (!CancellerRegistry.TryGetValue(task, out canceller))
                return false;

            try {
                Console.WriteLine("Task {0} Cancelled", task);
                CancellationStack.Push(true);
                canceller();
                return true;
            } finally {
                CancellationStack.Pop();
                lock (CancellerRegistry)
                    CancellerRegistry.Remove(task);
            }
        }
    }
}

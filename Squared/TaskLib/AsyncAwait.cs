using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Squared.Util;
using Squared.Util.Event;
// FIXME: This whole file needs unit tests

using tTask = System.Threading.Tasks.Task;
using CallContext = System.Runtime.Remoting.Messaging.CallContext;
using IEventInfo = Squared.Util.Event.IEventInfo;
using Squared.Threading;
using Squared.Threading.AsyncAwait;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Squared.Task {
    public static class TaskAwaitExtensionMethods {
        public struct ISchedulableAwaiter : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly ISchedulable  Schedulable;
            public readonly IFuture       Future;

            public ISchedulableAwaiter (ISchedulable schedulable) {
                Registration = new CancellationScope.Registration(TaskScheduler.Current);
                Schedulable = schedulable;
                var ts = (TaskScheduler)Registration.Scheduler;
                Future = ts.Start(schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnCompleteWithUserData(continuation), Registration);
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
                CancellationScope.Current?.ThrowIfCanceled();

                return Future.Result;
            }
        }

        public struct ISchedulableAwaiter<T> : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly ISchedulable<T> Schedulable;
            public readonly Future<T>       Future;

            public ISchedulableAwaiter (ISchedulable<T> schedulable) {
                Registration = new CancellationScope.Registration(TaskScheduler.Current);
                Schedulable = schedulable;
                Future = new Future<T>();
                var ts = (TaskScheduler)Registration.Scheduler;
                ts.Start(Future, schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnCompleteWithUserData(continuation), Registration);
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public T GetResult () {
                CancellationScope.Current?.ThrowIfCanceled();

                return Future.Result;
            }
        }

        public struct SequenceAwaiter<T> : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly Future<T>[]   Futures;
            public readonly IFuture       Ready;

            public SequenceAwaiter (ISchedulable<T>[] schedulables) {
                Registration = new CancellationScope.Registration(TaskScheduler.Current);

                Futures = new Future<T>[schedulables.Length];
                if (Futures.Length == 0) {
                    Ready = SignalFuture.Signaled;
                    return;
                }

                var ts = (TaskScheduler)Registration.Scheduler;
                for (var i = 0; i < Futures.Length; i++)
                    Futures[i] = ts.Start(schedulables[i]);

                Ready = Future.WaitForAll(Futures);
            }

            public SequenceAwaiter (Future<T>[] futures) {
                Registration = new CancellationScope.Registration(TaskScheduler.Current);

                Futures = futures;
                if (Futures.Length == 0) {
                    Ready = SignalFuture.Signaled;
                    return;
                }

                Ready = Future.WaitForAll(futures);
            }

            public void OnCompleted (Action continuation) {
                // FIXME: Use UserData
                Ready.RegisterOnComplete(Registration.OnCompleteWithUserData(continuation), Registration);
            }

            public bool IsCompleted {
                get {
                    return Ready.Completed;
                }
            }

            public T[] GetResult () {
                CancellationScope.Current?.ThrowIfCanceled();

                var result = new T[Futures.Length];
                for (int i = 0; i < result.Length; i++)
                    result[i] = Futures[i].Result;

                return result;
            }
        }

        public struct QueueToSchedulerTaskAwaiter : INotifyCompletion {
            private TaskAwaiter Awaiter;
            private TaskScheduler Scheduler;
            private bool ForNextStep;

            static MethodInfo QueueMethod = typeof(TaskScheduler).GetMethod("QueueWorkItem", new [] { typeof(Action) }),
                QueueNextStepMethod = typeof(TaskScheduler).GetMethod("QueueWorkItemForNextStep", new [] { typeof(Action) });

            public QueueToSchedulerTaskAwaiter (tTask task, TaskScheduler scheduler, bool forNextStep) {
                Awaiter = task.GetAwaiter();
                Scheduler = scheduler;
                ForNextStep = forNextStep;
            }

            public void OnCompleted (Action continuation) {
                QueueToSchedulerWhenComplete(Scheduler, Awaiter, continuation, ForNextStep, Awaiter.IsCompleted);
            }

            public bool IsCompleted => Awaiter.IsCompleted;
            public void GetResult () => Awaiter.GetResult();

            public QueueToSchedulerTaskAwaiter GetAwaiter () => this;
        }

        public struct QueueToSchedulerTaskAwaiter<T> : INotifyCompletion {
            private TaskAwaiter<T> Awaiter;
            private TaskScheduler Scheduler;
            private bool ForNextStep;

            static MethodInfo QueueMethod = typeof(TaskScheduler).GetMethod("QueueWorkItem", new [] { typeof(Action) }),
                QueueNextStepMethod = typeof(TaskScheduler).GetMethod("QueueWorkItemForNextStep", new [] { typeof(Action) });

            public QueueToSchedulerTaskAwaiter (Task<T> task, TaskScheduler scheduler, bool forNextStep) {
                Awaiter = task.GetAwaiter();
                Scheduler = scheduler;
                ForNextStep = forNextStep;
            }

            public void OnCompleted (Action continuation) {
                QueueToSchedulerWhenComplete(Scheduler, Awaiter, continuation, ForNextStep, Awaiter.IsCompleted);
            }

            public bool IsCompleted => Awaiter.IsCompleted;
            public T GetResult () => Awaiter.GetResult();

            public QueueToSchedulerTaskAwaiter<T> GetAwaiter () => this;
        }

        public struct QueueToSchedulerAwaitable : INotifyCompletion {
            private TaskScheduler Scheduler;
            private bool ForNextStep;

            public QueueToSchedulerAwaitable (TaskScheduler scheduler, bool forNextStep) {
                Scheduler = scheduler;
                ForNextStep = forNextStep;
            }

            public void OnCompleted (Action continuation) {
                if (ForNextStep)
                    Scheduler.QueueWorkItemForNextStep(continuation);
                else
                    Scheduler.QueueWorkItem(continuation);
            }

            public bool IsCompleted => false;
            public void GetResult () { }

            public QueueToSchedulerAwaitable GetAwaiter () => this;
        }

        private class QueueToSchedulerThunk {
            public TaskScheduler Scheduler;
            public bool ForNextStep;
            public Action Continuation;

            public void OnCompleted () {
                if (ForNextStep)
                    Scheduler.QueueWorkItemForNextStep(Continuation);
                else
                    Scheduler.QueueWorkItem(Continuation);
            }
        }

        public static void QueueToSchedulerWhenComplete<TAwaiter> (TaskScheduler scheduler, in TAwaiter awaiter, Action continuation, bool forNextStep, bool completeSynchronously)
            where TAwaiter : INotifyCompletion
        {
            if (completeSynchronously) {
                if (forNextStep)
                    scheduler.QueueWorkItemForNextStep(continuation);
                else
                    scheduler.QueueWorkItem(continuation);
            } else {
                var thunk = new QueueToSchedulerThunk {
                    Scheduler = scheduler,
                    ForNextStep = forNextStep,
                    Continuation = continuation
                };
                awaiter.OnCompleted(thunk.OnCompleted);
            }
        }

        public static FutureAwaitExtensionMethods.IFutureAwaiter GetAwaiter (this IEnumerator<object> task) {
            var scheduler = TaskScheduler.Current;
            if (scheduler == null)
                throw new InvalidOperationException("No active TaskScheduler");

            var f = scheduler.Start(task, TaskExecutionPolicy.RunWhileFutureLives);
            return f.GetAwaiter();
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

        public static QueueToSchedulerTaskAwaiter QueueToScheduler (this tTask task, TaskScheduler scheduler = null, bool forNextStep = false) {
            return new QueueToSchedulerTaskAwaiter(task, scheduler ?? TaskScheduler.Current, forNextStep);
        }

        public static QueueToSchedulerTaskAwaiter<T> QueueToScheduler<T> (this Task<T> task, TaskScheduler scheduler = null, bool forNextStep = false) {
            return new QueueToSchedulerTaskAwaiter<T>(task, scheduler ?? TaskScheduler.Current, forNextStep);
        }

        public static QueueToSchedulerAwaitable Queue (this TaskScheduler scheduler, bool forNextStep = false) {
            return new QueueToSchedulerAwaitable(scheduler, forNextStep);
        }
    }
}

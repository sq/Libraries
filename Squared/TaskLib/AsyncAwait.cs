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
    public static class FutureAwaitExtensionMethods {
        public struct FutureAwaiter<TResult> : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly Future<TResult> Future;

            public FutureAwaiter (Future<TResult> future) {
                Registration = new CancellationScope.Registration();
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnComplete(continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public TResult GetResult () {
                Registration.ThrowIfCanceled();

                return Future.Result;
            }
        }

        public struct IFutureAwaiter : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly IFuture Future;

            public IFutureAwaiter (IFuture future) {
                Registration = new CancellationScope.Registration();
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnComplete(continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
                CancellationScope.Current.ThrowIfCanceled();

                return Future.Result;
            }
        }

        public struct VoidFutureAwaiter : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly Future<NoneType> Future;

            public VoidFutureAwaiter (Future<NoneType> future) {
                Registration = new CancellationScope.Registration();
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnComplete(continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public void GetResult () {
                CancellationScope.Current.ThrowIfCanceled();

                var @void = Future.Result;
                return;
            }
        }

        public struct ISchedulableAwaiter : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly ISchedulable  Schedulable;
            public readonly IFuture       Future;

            public ISchedulableAwaiter (ISchedulable schedulable) {
                Registration = new CancellationScope.Registration();
                Schedulable = schedulable;
                Future = Registration.Scheduler.Start(schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnComplete(continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
                CancellationScope.Current.ThrowIfCanceled();

                return Future.Result;
            }
        }

        public struct ISchedulableAwaiter<T> : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly ISchedulable<T> Schedulable;
            public readonly Future<T>       Future;

            public ISchedulableAwaiter (ISchedulable<T> schedulable) {
                Registration = new CancellationScope.Registration();
                Schedulable = schedulable;
                Future = new Future<T>();
                Registration.Scheduler.Start(Future, schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete(Registration.OnComplete(continuation));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public T GetResult () {
                CancellationScope.Current.ThrowIfCanceled();

                return Future.Result;
            }
        }

        public struct SequenceAwaiter<T> : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly Future<T>[]   Futures;
            public readonly IFuture       Ready;

            public SequenceAwaiter (ISchedulable<T>[] schedulables) {
                Registration = new CancellationScope.Registration();

                Futures = new Future<T>[schedulables.Length];
                if (Futures.Length == 0) {
                    Ready = new SignalFuture(true);
                    return;
                }

                for (var i = 0; i < Futures.Length; i++)
                    Futures[i] = Registration.Scheduler.Start(schedulables[i]);

                Ready = Future.WaitForAll(Futures);
            }

            public SequenceAwaiter (Future<T>[] futures) {
                Registration = new CancellationScope.Registration();

                Futures = futures;
                if (Futures.Length == 0) {
                    Ready = new SignalFuture(true);
                    return;
                }

                Ready = Future.WaitForAll(futures);
            }

            public void OnCompleted (Action continuation) {
                Ready.RegisterOnComplete(Registration.OnComplete(continuation));
            }

            public bool IsCompleted {
                get {
                    return Ready.Completed;
                }
            }

            public T[] GetResult () {
                CancellationScope.Current.ThrowIfCanceled();

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
                task.TryCancelScope();
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
                task.TryCancelScope();
            });
        }

        public static bool TryCancelScope (this tTask task) {
            if (task.IsCanceled)
                return true;
            else if (task.IsCompleted)
                return false;

            CancellationScope scope;
            if (CancellationScope.TryGet(task, out scope))
                return scope.TryCancel();

            return false;
        }
    }
}

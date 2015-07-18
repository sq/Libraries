using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Squared.Task {
    public static class FutureAwaitExtensionMethods {
        public struct FutureAwaiter<TResult> : INotifyCompletion {
            public readonly Future<TResult> Future;

            public FutureAwaiter (Future<TResult> future) {
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete((_) => continuation());
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public TResult GetResult () {
                return Future.Result;
            }
        }

        public struct IFutureAwaiter : INotifyCompletion {
            public readonly IFuture Future;

            public IFutureAwaiter (IFuture future) {
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete((_) => continuation());
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
                return Future.Result;
            }
        }

        public struct VoidFutureAwaiter : INotifyCompletion {
            public readonly Future<NoneType> Future;

            public VoidFutureAwaiter (Future<NoneType> future) {
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                Future.RegisterOnComplete((_) => continuation());
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public void GetResult () {
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
                Future.RegisterOnComplete((_) => continuation());
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public object GetResult () {
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
                Future.RegisterOnComplete((_) => continuation());
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public T GetResult () {
                return Future.Result;
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
    }
}

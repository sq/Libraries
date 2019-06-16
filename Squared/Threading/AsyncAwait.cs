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
using EventInfo = Squared.Util.Event.EventInfo;
using Squared.Threading;
using Squared.Threading.AsyncAwait;
using System.Runtime.ExceptionServices;

namespace Squared.Threading {
    public static class FutureAwaitExtensionMethods {
        public struct IFutureWithDisposedValue {
            public IFuture Future;
            public object  DisposedValue;

            public IFutureAwaiter GetAwaiter () {
                return new IFutureAwaiter(Future, DisposedValue, true);
            }
        }

        public struct FutureWithDisposedValue<TResult> {
            public Future<TResult> Future;
            public TResult         DisposedValue;

            public FutureAwaiter<TResult> GetAwaiter () {
                return new FutureAwaiter<TResult>(Future, DisposedValue, true);
            }
        }

        public struct NonThrowingIFuture {
            public IFuture Future;

            public IFutureAwaiter GetAwaiter () {
                return new IFutureAwaiter(Future, false);
            }
        }

        public struct NonThrowingFuture<TResult> {
            public Future<TResult> Future;

            public FutureAwaiter<TResult> GetAwaiter () {
                return new FutureAwaiter<TResult>(Future, false);
            }
        }

        public struct FutureAwaiter<TResult> : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly Future<TResult> Future;
            public readonly bool HasDisposedValue;
            public readonly TResult DisposedValue;
            public readonly bool ThrowOnError;

            public FutureAwaiter (Future<TResult> future, bool throwOnError) {
                Registration = new CancellationScope.Registration(WorkItemQueueTarget.Current);
                Future = future;
                HasDisposedValue = false;
                DisposedValue = default(TResult);
                ThrowOnError = throwOnError;
            }

            public FutureAwaiter (Future<TResult> future, TResult disposedValue, bool throwOnError) {
                Registration = new CancellationScope.Registration(WorkItemQueueTarget.Current);
                Future = future;
                HasDisposedValue = true;
                DisposedValue = disposedValue;
                ThrowOnError = throwOnError;
            }

            public void OnCompleted (Action continuation) {
                var oc = Registration.OnComplete(continuation);
                Future.RegisterOnComplete(oc);
                Future.RegisterOnDispose((f) => oc(f));
            }

            public bool IsCompleted {
                get {
                    return Future.Disposed || Future.Completed;
                }
            }

            public TResult GetResult () {
                Registration.ThrowIfCanceled();

                if (Future.Disposed && HasDisposedValue)
                    return DisposedValue;

                if ((ThrowOnError == false) && Future.Failed)
                    return default(TResult);

                return Future.Result2;
            }
        }

        public struct IFutureAwaiter : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly IFuture Future;
            public readonly bool HasDisposedValue;
            public readonly object DisposedValue;
            public readonly bool ThrowOnError;

            public IFutureAwaiter (IFuture future, bool throwOnError) {
                Registration = new CancellationScope.Registration(WorkItemQueueTarget.Current);
                Future = future;
                HasDisposedValue = false;
                DisposedValue = null;
                ThrowOnError = throwOnError;
            }

            public IFutureAwaiter (IFuture future, object disposedValue, bool throwOnError) {
                Registration = new CancellationScope.Registration(WorkItemQueueTarget.Current);
                Future = future;
                HasDisposedValue = true;
                DisposedValue = disposedValue;
                ThrowOnError = throwOnError;
            }

            public void OnCompleted (Action continuation) {
                var oc = Registration.OnComplete(continuation);
                Future.RegisterOnComplete(oc);
                Future.RegisterOnDispose((f) => oc(f));
            }

            public bool IsCompleted {
                get {
                    return Future.Disposed || Future.Completed;
                }
            }

            public object GetResult () {
                Registration.ThrowIfCanceled();

                if (Future.Disposed && HasDisposedValue)
                    return DisposedValue;

                if ((ThrowOnError == false) && Future.Failed)
                    return null;

                return Future.Result2;
            }
        }

        public struct VoidFutureAwaiter : INotifyCompletion {
            public readonly CancellationScope.Registration Registration;
            public readonly Future<NoneType> Future;

            public VoidFutureAwaiter (Future<NoneType> future) {
                Registration = new CancellationScope.Registration(WorkItemQueueTarget.Current);
                Future = future;
            }

            public void OnCompleted (Action continuation) {
                var oc = Registration.OnComplete(continuation);
                Future.RegisterOnComplete(oc);
                Future.RegisterOnDispose((f) => oc(f));
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public void GetResult () {
                CancellationScope.Current.ThrowIfCanceled();

                if (!Future.Disposed) {
                    var @void = Future.Result;
                }

                return;
            }
        }

        public abstract class WaitableEventAwaiterBase<T> : INotifyCompletion, IDisposable {
            bool              _IsCompleted;
            Action            Continuation;
            EventSubscription Subscription;
            T                 Result;
            Future<T>         _Future;

            protected abstract bool TryExtractResult (EventInfo e, out T result);
            protected abstract EventSubscription Subscribe (EventSubscriber subscriber);

            protected void EventHandler (EventInfo e) {
                if (_IsCompleted)
                    return;

                // Events that don't match are silently rejected.
                // FIXME: Is this actually horrible
                if (!TryExtractResult(e, out Result))
                    return;

                e.Consume();
                _IsCompleted = true;
                Dispose();

                if (Continuation != null)
                    Continuation();

                if (_Future != null)
                    _Future.SetResult(Result, null);
            }

            public IFuture Future {
                get {
                    if (Continuation != null)
                        throw new InvalidOperationException("A continuation was already registered");

                    if (_Future == null) {
                        _Future = new Future<T>();
                        _Future.RegisterOnDispose(OnFutureDisposed);
                        Subscription = Subscribe(EventHandler);
                    }

                    return _Future;
                }
            }

            private void OnFutureDisposed (IFuture future) {
                Dispose();
            }

            public void OnCompleted (Action continuation) {
                if (_Future != null)
                    throw new InvalidOperationException("A future was already created");
                if (Continuation != null)
                    throw new InvalidOperationException("A continuation was already registered");

                Continuation = continuation;
                Subscription = Subscribe(EventHandler);
            }

            public void Dispose () {
                Subscription.Dispose();
            }

            public bool IsCompleted {
                get {
                    return _IsCompleted;
                }
            }

            public T GetResult () {
                if (!_IsCompleted)
                    throw new InvalidOperationException("No event received");

                return Result;
            }
        }

        public struct WaitableEvent {
            public class Awaiter : WaitableEventAwaiterBase<EventInfo> {
                readonly WaitableEvent Parent;

                public Awaiter (ref WaitableEvent parent) {
                    Parent = parent;
                }

                protected override bool TryExtractResult (EventInfo e, out EventInfo result) {
                    result = e;
                    return true;
                }

                protected override EventSubscription Subscribe (EventSubscriber subscriber) {
                    return Parent.EventBus.Subscribe(Parent.Source, Parent.Type, subscriber);
                }
            }

            private readonly EventBus EventBus;
            private readonly object Source;
            private readonly string Type;

            public WaitableEvent (EventBus eventBus, object source, string type) {
                EventBus = eventBus;
                Source = source;
                Type = type;
            }

            public WaitableEvent.Awaiter GetAwaiter () {
                return new Awaiter(ref this);
            }
        }

        public struct WaitableEvent<T> {
            public class Awaiter : WaitableEventAwaiterBase<T> {
                readonly WaitableEvent<T> Parent;

                public Awaiter (ref WaitableEvent<T> parent) {
                    Parent = parent;
                }

                protected override bool TryExtractResult (EventInfo e, out T result) {
                    if (e.Arguments is T) {
                        result = (T)e.Arguments;
                        return true;
                    }

                    result = default(T);
                    return false;
                }

                protected override EventSubscription Subscribe (EventSubscriber subscriber) {
                    return Parent.EventBus.Subscribe(Parent.Source, Parent.Type, subscriber);
                }
            }

            private readonly EventBus EventBus;
            private readonly object Source;
            private readonly string Type;

            public WaitableEvent (EventBus eventBus, object source, string type) {
                EventBus = eventBus;
                Source = source;
                Type = type;
            }

            public WaitableEvent<T>.Awaiter GetAwaiter () {
                return new Awaiter(ref this);
            }
        }

        public static FutureAwaiter<T> GetAwaiter<T> (this Future<T> future) {
            return new FutureAwaiter<T>(future, true);
        }

        public static IFutureAwaiter GetAwaiter (this IFuture future) {
            return new IFutureAwaiter(future, true);
        }

        public static VoidFutureAwaiter GetAwaiter (this Future<NoneType> future) {
            return new VoidFutureAwaiter(future);
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
                // FIXME: ExceptionDispatchInfo?
                if (task.IsFaulted) {
                    future.Fail(task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception);
                } else
                    future.Complete();
            });
            future.RegisterOnDispose((_) => {
                task.TryCancelScope();
            });
        }

        public static void BindFuture<T> (this System.Threading.Tasks.Task<T> task, Future<T> future) {
            task.GetAwaiter().OnCompleted(() => {
                future.SetResult2(task);
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

        public static WaitableEvent Event (this EventBus eventBus, object source = null, string type = null) {
            return new WaitableEvent(eventBus, source ?? EventBus.AnySource, type ?? EventBus.AnyType);
        }

        public static WaitableEvent<T> Event<T> (this EventBus eventBus, object source = null, string type = null) {
            return new WaitableEvent<T>(eventBus, source ?? EventBus.AnySource, type ?? EventBus.AnyType);
        }

        public static FutureWithDisposedValue<TResult> ValueWhenDisposed<TResult> (this Future<TResult> future, TResult value) {
            return new FutureWithDisposedValue<TResult> {
                Future = future,
                DisposedValue = value
            };
        }

        public static NonThrowingFuture<TResult> DoNotThrow<TResult> (this Future<TResult> future) {
            return new NonThrowingFuture<TResult> {
                Future = future
            };
        }

        public static IFutureWithDisposedValue ValueWhenDisposed (this IFuture future, object value) {
            return new IFutureWithDisposedValue {
                Future = future,
                DisposedValue = value
            };
        }

        public static NonThrowingIFuture DoNotThrow (this IFuture future) {
            return new NonThrowingIFuture {
                Future = future
            };
        }
    }
}

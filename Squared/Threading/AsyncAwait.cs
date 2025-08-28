﻿using System;
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
using IEventInfo = Squared.Util.Event.IEventInfo;
using Squared.Threading;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace Squared.Threading {
    // FIXME: We should implement ICriticalNotifyCompletion and do ExecutionContext flowing for the normal variant.

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

        public struct FutureAwaiter<TResult> : ICriticalNotifyCompletion, INotifyCompletion {
            public readonly Future<TResult> Future;
            public readonly bool HasDisposedValue;
            public readonly TResult DisposedValue;
            public readonly bool ThrowOnError;

            public FutureAwaiter (Future<TResult> future, bool throwOnError) {
                if (future == null)
                    throw new ArgumentNullException(nameof(future));
                Future = future;
                HasDisposedValue = false;
                DisposedValue = default(TResult);
                ThrowOnError = throwOnError;
            }

            public FutureAwaiter (Future<TResult> future, TResult disposedValue, bool throwOnError) {
                if (future == null)
                    throw new ArgumentNullException(nameof(future));
                Future = future;
                HasDisposedValue = true;
                DisposedValue = disposedValue;
                ThrowOnError = throwOnError;
            }

            public void UnsafeOnCompleted (Action continuation) => OnCompleted(continuation);

            public void OnCompleted (Action continuation) {
                ((IFuture)Future).RegisterOnResolved(continuation);
            }

            public bool IsCompleted {
                get {
                    return Future.Disposed || Future.Completed;
                }
            }

            public TResult GetResult () {
                if (Future.Disposed && HasDisposedValue)
                    return DisposedValue;

                if ((ThrowOnError == false) && Future.Failed)
                    return default(TResult);

                return Future.Result2;
            }
        }

        public struct IFutureAwaiter : ICriticalNotifyCompletion, INotifyCompletion {
            public readonly IFuture Future;
            public readonly bool HasDisposedValue;
            public readonly object DisposedValue;
            public readonly bool ThrowOnError;

            public IFutureAwaiter (IFuture future, bool throwOnError) {
                if (future == null)
                    throw new ArgumentNullException(nameof(future));
                Future = future;
                HasDisposedValue = false;
                DisposedValue = null;
                ThrowOnError = throwOnError;
            }

            public IFutureAwaiter (IFuture future, object disposedValue, bool throwOnError) {
                if (future == null)
                    throw new ArgumentNullException(nameof(future));
                Future = future;
                HasDisposedValue = true;
                DisposedValue = disposedValue;
                ThrowOnError = throwOnError;
            }

            public void UnsafeOnCompleted (Action continuation) => OnCompleted(continuation);

            public void OnCompleted (Action continuation) {
                Future.RegisterOnResolved(continuation);
            }

            public bool IsCompleted {
                get {
                    return Future.Disposed || Future.Completed;
                }
            }

            public object GetResult () {
                if (Future.Disposed && HasDisposedValue)
                    return DisposedValue;

                if ((ThrowOnError == false) && Future.Failed)
                    return null;

                return Future.Result2;
            }
        }

        public struct VoidFutureAwaiter : ICriticalNotifyCompletion, INotifyCompletion {
            public readonly Future<NoneType> Future;

            public VoidFutureAwaiter (Future<NoneType> future) {
                if (future == null)
                    throw new ArgumentNullException(nameof(future));
                Future = future;
            }

            public void UnsafeOnCompleted (Action continuation) => OnCompleted(continuation);

            public void OnCompleted (Action continuation) {
                ((IFuture)Future).RegisterOnResolved(continuation);
            }

            public bool IsCompleted {
                get {
                    return Future.Completed;
                }
            }

            public void GetResult () {
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

            protected abstract bool TryExtractResult (IEventInfo e, out T result);
            protected abstract EventSubscription Subscribe (EventSubscriber subscriber);

            protected void EventHandler (IEventInfo e) {
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
            public sealed class Awaiter : WaitableEventAwaiterBase<IEventInfo> {
                readonly WaitableEvent Parent;

                public Awaiter (ref WaitableEvent parent) {
                    Parent = parent;
                }

                protected override bool TryExtractResult (IEventInfo e, out IEventInfo result) {
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
            public sealed class Awaiter : WaitableEventAwaiterBase<T> {
                readonly WaitableEvent<T> Parent;

                public Awaiter (ref WaitableEvent<T> parent) {
                    Parent = parent;
                }

                protected override bool TryExtractResult (IEventInfo e, out T result) {
                    var eit = e as IEventInfo<T>;
                    if (eit != null) {
                        result = eit.Arguments;
                    } else if (e.Arguments is T) {
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
            if (task.IsCompleted && !task.IsFaulted)
                return SignalFuture.Signaled;
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
            if (task.IsCompleted && !task.IsFaulted) {
                future.Complete();
                return;
            }

            // FIXME: ConfigureAwait(false)?
            task.GetAwaiter().OnCompleted(() => {
                // FIXME: ExceptionDispatchInfo?
                if (task.IsFaulted)
                    future.Fail(task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception);
                else
                    future.Complete();
            });
        }

        public static void BindFuture<T> (this System.Threading.Tasks.Task<T> task, Future<T> future) {
            if (task.IsCompleted) {
                future.SetResultFrom(task);
                return;
            }

            // FIXME: ConfigureAwait(false)?
            task.GetAwaiter().OnCompleted(() => {
                future.SetResultFrom(task);
            });
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

        private class AsTaskThunk {
            public readonly TaskCompletionSource<NoneType> CompletionSource = new TaskCompletionSource<NoneType>();

            public Task Task => CompletionSource.Task;

            internal void OnResolved (IFuture future) {
                if (future.Failed)
                    CompletionSource.SetException(future.Error);
                else if (future.Disposed)
                    CompletionSource.SetCanceled();
                else
                    CompletionSource.SetResult(NoneType.None);
            }
        }

        public static Task AsTask (this IFuture future) {
            var thunk = new AsTaskThunk();
            future.RegisterOnResolved(thunk.OnResolved);
            return thunk.Task;
        }

        private class AsTaskThunk<T> {
            public readonly TaskCompletionSource<T> CompletionSource = new TaskCompletionSource<T>();

            public Task<T> Task => CompletionSource.Task;

            internal void OnResolved (Future<T> future) {
                if (future.Failed)
                    CompletionSource.SetException(future.Error);
                else if (future.Disposed)
                    CompletionSource.SetCanceled();
                else
                    CompletionSource.SetResult(future.Result);
            }
        }

        public static Task<T> AsTask<T> (this Future<T> future) {
            var thunk = new AsTaskThunk<T>();
            future.RegisterOnResolved2(thunk.OnResolved);
            return thunk.Task;
        }
    }
}

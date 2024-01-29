using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Squared.Util;
using Squared.Util.Bind;

namespace Squared.Threading {
    public delegate void OnFutureResolved (IFuture future);
    public delegate void OnFutureResolved<T> (Future<T> future);
    public delegate void OnFutureResolvedWithData (IFuture future, object userData);
    public delegate void OnFutureResolvedWithData<T> (Future<T> future, object userData);

    public class FutureException : Exception {
        public FutureException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    public class FutureAlreadyHasResultException : InvalidOperationException {
        public readonly IFuture Future;

        public FutureAlreadyHasResultException (IFuture future)
            : base("Future already has a result") {
            Future = future;
        }
    }

    public class FutureHasNoResultException : InvalidOperationException {
        public readonly IFuture Future;

        public FutureHasNoResultException (IFuture future)
            : base("Future does not yet have a result") {
            Future = future;
        }
    }

    public class FutureDisposedException : InvalidOperationException {
        public readonly IFuture Future;

        public FutureDisposedException (IFuture future)
            : base("Future is disposed") {
            Future = future;
        }
    }

    public class FutureHandlerException : Exception {
        public readonly IFuture Future;
        public readonly Delegate Handler;

        public FutureHandlerException (IFuture future, Delegate handler, Exception innerException) 
            : base("One of the Future's handlers threw an uncaught exception", innerException) {
            Future = future;
            Handler = handler;
        }

        public FutureHandlerException (IFuture future, Delegate handler, string message) 
            : base(message) {
            Future = future;
            Handler = handler;
        }
    }

    internal static class FutureHelpers {
        public static readonly WaitCallback RunInThreadHelper;

        static FutureHelpers () {
            RunInThreadHelper = _RunInThreadHelper;
        }

        internal static void _RunInThreadHelper (object state) {
            var thunk = (RunInThreadThunk)state;
            try {
                thunk.Invoke();
            } catch (System.Reflection.TargetInvocationException ex) {
                thunk.Fail(ex.InnerException);
            } catch (Exception ex) {
                thunk.Fail(ex);
            }
        }
    }

    internal abstract class RunInThreadThunk {
        public abstract void Fail (Exception e);
        public abstract void Invoke ();
    }

    internal sealed class ActionRunInThreadThunk : RunInThreadThunk {
        public readonly SignalFuture Future = new SignalFuture();
        public Action WorkItem;

        public override void Invoke () {
            WorkItem();
            Future.Complete();
        }

        public override void Fail (Exception ex) {
            Future.Fail(ex);
        }
    }

    internal sealed class FuncRunInThreadThunk<T> : RunInThreadThunk {
        public readonly Future<T> Future = new Future<T>();
        public Func<T> WorkItem;

        public override void Invoke () {
            Future.Complete(WorkItem());
        }

        public override void Fail (Exception ex) {
            Future.Fail(ex);
        }
    }

    internal sealed class DynamicRunInThreadThunk : RunInThreadThunk {
        public readonly IFuture Future = new Future<object>();
        public object[] Arguments;
        public Delegate WorkItem;

        public override void Invoke () {
            Future.Complete(WorkItem.DynamicInvoke(Arguments));
        }

        public override void Fail (Exception ex) {
            Future.Fail(ex);
        }
    }

    public interface IFuture : IDisposable {
        bool Failed { get; }
        bool Resolved { get; }
        bool Completed { get; }
        bool CompletedSuccessfully { get; }
        bool Disposed { get; }
        object Result {
            get;
        }
        object Result2 {
            get;
        }
        Exception Error {
            get;
        }
        Type ResultType {
            get;
        }

        bool GetResult (out object result, out Exception error);
        void SetResult (object result, Exception error);
        void SetResult2 (object result, ExceptionDispatchInfo errorInfo);
        void RegisterHandlers (Action completeHandler, Action disposeHandler);
        void RegisterHandlers (OnFutureResolved completeHandler, OnFutureResolved disposeHandler);
        void RegisterHandlers (OnFutureResolvedWithData completeHandler, OnFutureResolvedWithData disposeHandler, object userData);
        void RegisterOnResolved (Action handler);
        void RegisterOnResolved (OnFutureResolvedWithData handler, object userData);
        void RegisterOnComplete (Action handler);
        void RegisterOnComplete (OnFutureResolvedWithData handler, object userData);
        void RegisterOnDispose (Action handler);
        void RegisterOnDispose (OnFutureResolvedWithData handler, object userData);
        void RegisterOnResolved (OnFutureResolved handler);
        void RegisterOnComplete (OnFutureResolved handler);
        void RegisterOnDispose (OnFutureResolved handler);
        void RegisterOnErrorCheck (Action handler);
        bool CopyFrom (IFuture source);
    }

    public sealed class NoneType {
        private NoneType () {
        }

        public static readonly NoneType None = new NoneType();
    }

    public sealed class SignalFuture : Future<NoneType> {
        public static readonly SignalFuture Signaled = new SignalFuture(true);

        public SignalFuture ()
            : base() {
        }

        public SignalFuture (bool signaled) 
            : base() {

            if (signaled)
                base.SetResult(NoneType.None, null);
        }
    }

    public static class Future {
        public enum State : int {
            Empty = 0,
            // Unknown = 1,
            CompletedWithValue = 2,
            CompletedWithError = 3,
            Disposed = 4,
            // Disposing = 5;
        }

        public static Future<T> New<T> () {
            return new Future<T>();
        }

        public static Future<T> New<T> (T value) {
            return new Future<T>(value);
        }

        public static Future<IFuture> WaitForFirst (IEnumerable<IFuture> futures) {
            return new WaitForFirstThunk(futures).Composite;
        }

        public static Future<IFuture> WaitForFirst (params IFuture[] futures) {
            return new WaitForFirstThunk(futures).Composite;
        }

        public static IFuture WaitForAll (IEnumerable<IFuture> futures) {
            return new WaitForAllThunk(futures).Composite;
        }

        public static IFuture WaitForAll (params IFuture[] futures) {
            return new WaitForAllThunk(futures).Composite;
        }

        public static Future<U> RunInThread<U> (Func<U> workItem) {
            var thunk = new FuncRunInThreadThunk<U> {
                WorkItem = workItem,
            };
            ThreadPool.QueueUserWorkItem(FutureHelpers.RunInThreadHelper, thunk);
            return thunk.Future;
        }

        public static SignalFuture RunInThread (Action workItem) {
            var thunk = new ActionRunInThreadThunk {
                WorkItem = workItem,
            };
            ThreadPool.QueueUserWorkItem(FutureHelpers.RunInThreadHelper, thunk);
            return thunk.Future;
        }

        public static IFuture RunInThread (Delegate workItem, params object[] arguments) {
            var thunk = new DynamicRunInThreadThunk {
                WorkItem = workItem,
                Arguments = arguments
            };
            ThreadPool.QueueUserWorkItem(FutureHelpers.RunInThreadHelper, thunk);
            return thunk.Future;
        }

        private sealed class WaitForFirstThunk {
            public Future<IFuture> Composite = new Future<IFuture>();
            public bool Completed;

            public WaitForFirstThunk (IEnumerable<IFuture> futures) {
                OnFutureResolved handler = null;

                if (
                    ((futures as System.Collections.IList)?.Count == 0) ||
                    ((futures as Array)?.Length == 0)
                ) {
                    // HACK
                    Composite.Complete(null);
                    return;
                }

                foreach (var f in futures) {
                    if (f == null)
                        continue;
                    if (handler == null)
                        handler = OnResolved;
                    f.RegisterOnResolved(handler);
                    if (Completed)
                        break;
                }
            }
            
            public void OnResolved (IFuture f) {
                lock (this) {
                    if (Completed)
                        return;

                    Completed = true;
                }

                Composite.Complete(f);
            }
        }

        private sealed class WaitForAllThunk {
            public SignalFuture Composite = new SignalFuture();
            private bool Initializing = true;
            public int TargetCount;
            public int DisposeCount, CompleteCount;

            public WaitForAllThunk (IEnumerable<IFuture> futures) {
                if (
                    ((futures as System.Collections.IList)?.Count == 0) ||
                    ((futures as Array)?.Length == 0)
                ) {
                    // HACK
                    Composite.Complete();
                    return;
                }

                OnFutureResolved handler = null;
                foreach (var f in futures) {
                    if (f == null)
                        continue;
                    if (handler == null)
                        handler = OnResolved;

                    // NOTE: Duplicates will work correctly because we will increment the target count
                    //  twice and then register the handler twice, so the handler will run twice.
                    TargetCount++;
                    f.RegisterOnResolved(handler);
                }

                bool signal;
                lock (this) {
                    Initializing = false;
                    signal = (DisposeCount + CompleteCount) == TargetCount;
                }

                if (signal && !Composite.Completed && !Composite.Disposed) {
                    if (CompleteCount == 0)
                        Composite.Dispose();
                    else
                        Composite.Complete();
                }
            }
            
            public void OnResolved (IFuture f) {
                bool signal = false;
                lock (this) {
                    if (f.Disposed)
                        DisposeCount++;
                    else
                        CompleteCount++;

                    if (Initializing)
                        return;

                    signal = (DisposeCount + CompleteCount) == TargetCount;
                }

                if (signal && !Composite.Completed && !Composite.Disposed) {
                    if (CompleteCount == 0)
                        Composite.Dispose();
                    else
                        Composite.Complete();
                }
            }
        }

        private sealed class WaitForSingleEventThunk {
            public readonly Squared.Util.Bind.BoundMember<EventHandler> Event;
            public readonly SignalFuture Future = new SignalFuture();
            private readonly EventHandler Handler;

            public WaitForSingleEventThunk (Squared.Util.Bind.BoundMember<EventHandler> evt) {
                Event = evt;
                Handler = OnEvent;

                Event.Add(Handler);
                // FIXME: Use UserData
                Future.RegisterOnDispose((Threading.OnFutureResolved)this.OnDispose);
            }

            public void OnEvent (object sender, EventArgs args) {
                OnDispose(Future);
                Future.Complete();
            }

            public void OnDispose (IFuture future) {
                Event.Remove(Handler);
            }
        }

        private sealed class WaitForSingleEventThunk<TEventArgs>
            where TEventArgs : System.EventArgs {

            public readonly Squared.Util.Bind.BoundMember<EventHandler<TEventArgs>> Event;
            public readonly Future<TEventArgs> Future = new Future<TEventArgs>();
            private readonly EventHandler<TEventArgs> Handler;

            public WaitForSingleEventThunk (Squared.Util.Bind.BoundMember<EventHandler<TEventArgs>> evt) {
                Event = evt;
                Handler = OnEvent;

                Event.Add(Handler);
                // FIXME: Use UserData
                Future.RegisterOnDispose((Threading.OnFutureResolved)this.OnDispose);
            }

            public void OnEvent (object sender, TEventArgs args) {
                OnDispose(Future);
                Future.SetResult(args, null);
            }

            public void OnDispose (IFuture future) {
                Event.Remove(Handler);
            }
        }

        public static Future<TEventArgs> WaitForSingleEvent<TEventArgs>
            (Squared.Util.Bind.BoundMember<EventHandler<TEventArgs>> evt)
            where TEventArgs : System.EventArgs 
        {
            return (new WaitForSingleEventThunk<TEventArgs>(evt)).Future;
        }

        public static SignalFuture WaitForSingleEvent(Squared.Util.Bind.BoundMember<EventHandler> evt) {
            return (new WaitForSingleEventThunk(evt)).Future;
        }

        public static Future<TEventArgs> WaitForSingleEvent<TEventArgs>
            (Expression<Action<EventHandler<TEventArgs>>> evt)
            where TEventArgs : System.EventArgs {

            var bm = Squared.Util.Bind.BoundMember.New(evt);
            return WaitForSingleEvent(bm);
        }

        public static SignalFuture WaitForSingleEvent (Expression<Action<EventHandler>> evt) {
            var bm = Squared.Util.Bind.BoundMember.New(evt);
            return WaitForSingleEvent(bm);
        }
    }

    public class Future<T> : IDisposable, IFuture {
        private enum HandlerType : byte {
            Resolved,
            Completed,
            Disposed
        }

        private readonly struct Handler {
            public readonly HandlerType Type;
            public readonly Delegate Delegate;
            public readonly object UserData;

            public Handler (HandlerType type, Delegate handler, object userData) {
                Type = type;
                Delegate = handler;
                UserData = userData;
            }

            public void Invoke (Future<T> future) {
                try {
                    if (Delegate is OnFutureResolved<T> typed) {
                        typed(future);
                    } else if (Delegate is OnFutureResolved untyped) {
                        untyped(future);
                    } else if (Delegate is OnFutureResolvedWithData<T> withData) {
                        withData(future, UserData);
                    } else if (Delegate is OnFutureResolvedWithData withDataUntyped) {
                        withDataUntyped(future, UserData);
                    } else if (Delegate is Action action) {
                        action();
                    } else {
                        throw new FutureHandlerException(future, Delegate, "Invalid future handler");
                    }
                } catch (Exception exc) {
                    throw new FutureHandlerException(future, Delegate, exc);
                }
            }
        }

        private static readonly int ProcessorCount;

        private const int State_Empty = (int)Future.State.Empty;
        private const int State_Indeterminate = 1;
        private const int State_CompletedWithValue = (int)Future.State.CompletedWithValue;
        private const int State_CompletedWithError = (int)Future.State.CompletedWithError;
        private const int State_Disposed = (int)Future.State.Disposed;
        private const int State_Disposing = 5;

        private volatile int _State = State_Empty;
        private DenseList<Handler> _Handlers;
        private object _Error = null;
        private Action _OnErrorChecked = null;
        private T _Result = default(T);

        public override string ToString () {
            int state = _State;
            var result = _Result;
            var error = InternalError;
            string stateText = "??";
            switch (state) {
                case State_Empty:
                    stateText = "Empty";
                    break;
                case State_Indeterminate:
                    stateText = "Indeterminate";
                    break;
                case State_CompletedWithValue:
                    stateText = "CompletedWithValue";
                    break;
                case State_CompletedWithError:
                    stateText = "CompletedWithError";
                    break;
                case State_Disposed:
                case State_Disposing:
                    stateText = "Disposed";
                    break;
            }
            return String.Format("<Future<{3}> ({0}) r={1} e={2}>", stateText, result, error, typeof(T).Name);
        }

        /// <summary>
        /// Creates a future that is not yet completed
        /// </summary>
        public Future () {
        }

        /// <summary>
        /// Creates a future that is already completed with a value
        /// </summary>
        public Future (T value) {
            this.SetResult(value, null);
        }

        static Future () {
            ProcessorCount = Environment.ProcessorCount;
        }

        private static void SpinWait (int iterationCount) {
            if ((iterationCount < 5) && (ProcessorCount > 1)) {
                Thread.SpinWait(7 * iterationCount);
            } else if (iterationCount < 7) {
                Thread.Sleep(0);
            } else {
                Thread.Sleep(1);
            }
        }

        private void InvokeHandlers (HandlerType type) {
            var c = _Handlers.Count;
            if (c <= 0)
                return;

            try {
                for (int i = 0; i < c; i++) {
                    ref var handler = ref _Handlers.Item(i);
                    if ((handler.Type == type) || (handler.Type == HandlerType.Resolved))
                        handler.Invoke(this);
                }
            } finally {
                _Handlers.Clear();
            }
        }

        void OnErrorCheck () {
            var ec = Interlocked.Exchange(ref _OnErrorChecked, null);
            if (ec != null)
                ec();
        }

        /// <summary>
        /// Registers a handler that will be invoked when the error status of this future is checked.
        /// You can use this to identify whether a future has been abandoned.
        /// </summary>
        public void RegisterOnErrorCheck (Action handler) {
            Action newHandler;
            int iterations = 1;

            while (true) {
                var oldHandler = _OnErrorChecked;
                if (oldHandler != null) {
                    newHandler = () => {
                        oldHandler();
                        handler();
                    };
                } else {
                    newHandler = handler;
                }

                if (Interlocked.CompareExchange(ref _OnErrorChecked, newHandler, oldHandler) == oldHandler)
                    break;

                SpinWait(iterations);
                iterations += 1;
            }
        }

        private void ClearIndeterminate (int newState) {
            if (Interlocked.CompareExchange(ref _State, newState, State_Indeterminate) != State_Indeterminate)
                throw new ThreadStateException("Future state was not indeterminate");
        }

        private int TrySetIndeterminate () {
            int state;
            int iterations = 1;
            while (true) {
                state = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if (state != State_Indeterminate)
                    break;

                SpinWait(iterations++);
            }
            return state;
        }

        /// <returns>Whether the future is already complete and handlers were run</returns>
        private bool RegisterHandler_Impl (Delegate handler, HandlerType type, object userData = null) {
            if (handler == null)
                return false;

            var item = new Handler(type, handler, userData);

            int oldState = TrySetIndeterminate();
                
            if (oldState == State_Empty) {
                _Handlers.Add(item);
                ClearIndeterminate(State_Empty);
                return false;
            }

            var runCompletedHandler = (oldState == State_CompletedWithValue) ||
                (oldState == State_CompletedWithError);
            var runDisposedHandler = (oldState == State_Disposed) ||
                (oldState == State_Disposing);

            if (!runCompletedHandler && !runDisposedHandler)
                throw new ThreadStateException("Failed to register future handler due to a thread state error");

            switch (type) {
                case HandlerType.Completed:
                    if (runCompletedHandler) {
                        item.Invoke(this);
                        return true;
                    }
                    break;
                case HandlerType.Disposed:
                    if (runDisposedHandler) {
                        item.Invoke(this);
                        return true;
                    }
                    break;
                case HandlerType.Resolved:
                    if (runCompletedHandler || runDisposedHandler) {
                        item.Invoke(this);
                        return true;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }

            return false;
        }

        /// <summary>
        /// Registers a pair of handlers to be notified upon future completion and disposal, respectively.
        /// </summary>
        public void RegisterHandlers (Action onComplete, Action onDispose) {
            if (onComplete == onDispose) {
                RegisterHandler_Impl(onComplete, HandlerType.Resolved);
                return;
            }

            // FIXME: Set state to indeterminate once instead of twice
            if (!RegisterHandler_Impl(onComplete, HandlerType.Completed))
                RegisterHandler_Impl(onDispose, HandlerType.Disposed);
        }

        /// <summary>
        /// Registers a pair of handlers to be notified upon future completion and disposal, respectively.
        /// </summary>
        public void RegisterHandlers (OnFutureResolved<T> onComplete, OnFutureResolved onDispose) {
            // FIXME: Set state to indeterminate once instead of twice
            if (!RegisterHandler_Impl(onComplete, HandlerType.Completed))
                RegisterHandler_Impl(onDispose, HandlerType.Disposed);
        }

        /// <summary>
        /// Registers a pair of handlers to be notified upon future completion and disposal, respectively.
        /// </summary>
        void IFuture.RegisterHandlers (OnFutureResolved onComplete, OnFutureResolved onDispose) {
            if (onComplete == onDispose) {
                RegisterHandler_Impl(onComplete, HandlerType.Resolved);
                return;
            }

            // FIXME: Set state to indeterminate once instead of twice
            if (!RegisterHandler_Impl(onComplete, HandlerType.Completed))
                RegisterHandler_Impl(onDispose, HandlerType.Disposed);
        }

        /// <summary>
        /// Registers a pair of handlers to be notified upon future completion and disposal, respectively.
        /// </summary>
        void IFuture.RegisterHandlers (OnFutureResolvedWithData onComplete, OnFutureResolvedWithData onDispose, object userData) {
            if (onComplete == onDispose) {
                RegisterHandler_Impl(onComplete, HandlerType.Resolved, userData);
                return;
            }

            // FIXME: Set state to indeterminate once instead of twice
            if (!RegisterHandler_Impl(onComplete, HandlerType.Completed, userData))
                RegisterHandler_Impl(onDispose, HandlerType.Disposed, userData);
        }

        void IFuture.RegisterOnResolved (OnFutureResolved handler) {
            RegisterHandler_Impl(handler, HandlerType.Resolved);
        }

        void IFuture.RegisterOnResolved (Action handler) {
            RegisterHandler_Impl(handler, HandlerType.Resolved);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion or disposal.
        /// </summary>
        public void RegisterOnResolved (OnFutureResolved handler) {
            RegisterHandler_Impl(handler, HandlerType.Resolved);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion or disposal.
        /// </summary>
        public void RegisterOnResolved2 (OnFutureResolved<T> handler) {
            RegisterHandler_Impl(handler, HandlerType.Resolved);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion or disposal.
        /// </summary>
        public void RegisterOnResolved (OnFutureResolvedWithData handler, object userData) {
            RegisterHandler_Impl(handler, HandlerType.Resolved, userData);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion or disposal.
        /// </summary>
        public void RegisterOnResolved2 (OnFutureResolvedWithData<T> handler, object userData) {
            RegisterHandler_Impl(handler, HandlerType.Resolved, userData);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion. If the future is disposed, this will not run.
        /// </summary>
        public void RegisterOnComplete (Action handler) {
            RegisterHandler_Impl(handler, HandlerType.Completed);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion. If the future is disposed, this will not run.
        /// </summary>
        public void RegisterOnComplete (OnFutureResolved handler) {
            RegisterHandler_Impl(handler, HandlerType.Completed);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion. If the future is disposed, this will not run.
        /// </summary>
        public void RegisterOnComplete2 (OnFutureResolved<T> handler) {
            RegisterHandler_Impl(handler, HandlerType.Completed);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion. If the future is disposed, this will not run.
        /// </summary>
        public void RegisterOnComplete (OnFutureResolvedWithData handler, object userData) {
            RegisterHandler_Impl(handler, HandlerType.Completed, userData);
        }

        /// <summary>
        /// Registers a handler to be notified upon future completion. If the future is disposed, this will not run.
        /// </summary>
        public void RegisterOnComplete (OnFutureResolvedWithData<T> handler, object userData) {
            RegisterHandler_Impl(handler, HandlerType.Completed, userData);
        }

        /// <summary>
        /// Registers a handlers to be notified upon future disposal. If the future is completed, this will not run.
        /// </summary>
        public void RegisterOnDispose (Action handler) {
            RegisterHandler_Impl(handler, HandlerType.Disposed);
        }

        /// <summary>
        /// Registers a handlers to be notified upon future disposal. If the future is completed, this will not run.
        /// </summary>
        public void RegisterOnDispose (OnFutureResolved handler) {
            RegisterHandler_Impl(handler, HandlerType.Disposed);
        }

        /// <summary>
        /// Registers a handlers to be notified upon future disposal. If the future is completed, this will not run.
        /// </summary>
        public void RegisterOnDispose (OnFutureResolvedWithData<T> handler, object userData) {
            RegisterHandler_Impl(handler, HandlerType.Disposed, userData);
        }

        /// <summary>
        /// Registers a handlers to be notified upon future disposal. If the future is completed, this will not run.
        /// </summary>
        public void RegisterOnDispose (OnFutureResolvedWithData handler, object userData) {
            RegisterHandler_Impl(handler, HandlerType.Disposed, userData);
        }
        
        /// <summary>
        /// Returns true if the future has been disposed or is in the process of being disposed.
        /// </summary>
        public bool Disposed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                int state = _State;
                return (state == State_Disposed) || (state == State_Disposing);
            }
        }

        /// <summary>
        /// Returns true if the future has been completed or disposed.
        /// Note that if the future is currently being completed, this may return false.
        /// </summary>
        public bool Resolved {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                int state = _State;
                return (state != State_Indeterminate) && (state != State_Empty);
            }
        }

        /// <summary>
        /// Returns true if the future has been completed.
        /// Note that if the future is currently being completed, this may return false.
        /// </summary>
        public bool Completed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                int state = _State;
                return (state == State_CompletedWithValue) || (state == State_CompletedWithError);
            }
        }

        /// <summary>
        /// Returns true if the future has been completed with a result instead of an error.
        /// Note that if the future is currently being completed, this may return false.
        /// </summary>
        public bool CompletedSuccessfully {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                OnErrorCheck();
                return _State == State_CompletedWithValue;
            }
        }

        /// <summary>
        /// Returns true if the future has been completed but was completed with an error.
        /// Note that if the future is currently being completed, this may return false.
        /// </summary>
        public bool Failed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                OnErrorCheck();
                return _State == State_CompletedWithError;
            }
        }

        Type IFuture.ResultType {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return typeof(T);
            }
        }

        object IFuture.Result {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Result;
            }
        }

        object IFuture.Result2 {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Result2;
            }
        }

        private Exception InternalError {
            get {
                return (_Error is ExceptionDispatchInfo edi) 
                    ? edi.SourceException 
                    : (Exception)_Error;
            }
        }

        public Future.State State {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                int state = _State;
                if (state == State_Disposing)
                    return Future.State.Disposed;
                // FIXME: Is this right?
                else if (state == State_Indeterminate)
                    return Future.State.Empty;
                return (Future.State)state;
            }
        }

        /// <summary>
        /// Returns the exception that caused the future to fail, if it has failed.
        /// </summary>
        public Exception Error {
            get {
                int state = _State;
                if (state == State_CompletedWithValue) {
                    return null;
                } else if (state == State_CompletedWithError) {
                    OnErrorCheck();
                    return InternalError;
                } else if ((state == State_Disposed) || (state == State_Disposing)) {
                    return null;
                } else
                    throw new FutureHasNoResultException(this);
            }
        }

        /// <summary>
        /// Returns the future's result if it contains one. 
        /// If it failed, a FutureException will be thrown, with the original exception as its InnerException.
        /// </summary>
        public ref readonly T Result {
            get {
                int state = _State;
                if (state == State_CompletedWithValue) {
                    return ref _Result;
                } else if (state == State_CompletedWithError) {
                    OnErrorCheck();
                    throw new FutureException("Future's result was an error", InternalError);
                } else if ((state == State_Disposed) || (state == State_Disposing)) {
                    throw new FutureDisposedException(this);
                } else
                    throw new FutureHasNoResultException(this);
            }
        }

        /// <summary>
        /// Returns the future's result if it contains one.
        /// If it failed, the exception responsible will be rethrown with its stack preserved (if possible).
        /// </summary>
        public ref readonly T Result2 {
            get {
                int state = _State;
                if (state == State_CompletedWithValue) {
                    return ref _Result;
                } else if (state == State_CompletedWithError) {
                    OnErrorCheck();
                    if (_Error is ExceptionDispatchInfo edi) {
                        edi.Throw();
                        throw new FutureException("Failed to rethrow exception from dispatch info", edi.SourceException);
                    } else
                        throw new FutureException("Future's result was an error", (Exception)_Error);
                } else if ((state == State_Disposed) || (state == State_Disposing)) {
                    throw new FutureDisposedException(this);
                } else
                    throw new FutureHasNoResultException(this);
            }
        }

        bool IFuture.CopyFrom (IFuture source) {
            var tSelf = source as Future<T>;
            if (tSelf != null)
                return CopyFrom(tSelf);
            else {
                object r;
                Exception e;
                if (source.GetResult(out r, out e)) {
                    if (r == null)
                        SetResult(default(T), e);
                    else
                        SetResult((T)r, e);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Attempts to the current state of the source future to this future.
        /// May fail for various reasons (for example, this future is already completed).
        /// </summary>
        /// <returns>true if the copy was successful.</returns>
        public bool CopyFrom (Future<T> source) {
            var state = source._State;

            // FIXME: Wait if it's indeterminate state
            if ((state == State_Indeterminate) || (state == State_Empty))
                return false;

            if (!SetResultPrologue())
                return false;

            _Result = source._Result;
            _Error = source._Error;

            SetResultEpilogue(state);
            return true;
        }

        void IFuture.SetResult (object result, Exception error) {
            if ((error != null) && (result != null)) {
                throw new FutureException("Cannot complete a future with both a result and an error.", error);
            }

            if (result == null)
                SetResult(default(T), error);
            else {
                T value;
                try {
                    value = (T)result;
                } catch (Exception exc) {
                    value = default(T);
                    error = new FutureException("Could not convert result to correct type", exc);
                }
                SetResult(value, error);
            }
        }

        void IFuture.SetResult2 (object result, ExceptionDispatchInfo errorInfo) {
            if ((errorInfo != null) && (result != null)) {
                throw new FutureException("Cannot complete a future with both a result and an error.", errorInfo.SourceException);
            }

            if (result == null)
                SetResult2(default(T), errorInfo);
            else
                SetResult2((T)result, errorInfo);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SetResultPrologue (bool throwOnFailure = true) {
            int iterations = 1;

            while (true) {
                int oldState = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if ((oldState == State_Disposed) || (oldState == State_Disposing)) {
                    return false;
                } else if ((oldState == State_CompletedWithValue) || (oldState == State_CompletedWithError)) {
                    if (throwOnFailure)
                        throw new FutureAlreadyHasResultException(this);
                    else
                        return false;
                } else if (oldState == State_Empty)
                    break;

                SpinWait(iterations++);
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetResultEpilogue (int newState) {
            if (Interlocked.Exchange(ref _State, newState) != State_Indeterminate)
                throw new ThreadStateException("Future state was not indeterminate");

            if ((newState == State_CompletedWithValue) || (newState == State_CompletedWithError))
                InvokeHandlers(HandlerType.Completed);
        }

        /// <summary>
        /// Completes the future. If the provided task failed, its exception will be stored into this future.
        /// </summary>
        public void SetResultFrom (System.Threading.Tasks.Task task) {
            if (!task.IsCompleted)
                throw new InvalidOperationException("Task not completed");

            if (!SetResultPrologue())
                return;

            int newState = task.IsFaulted ? State_CompletedWithError : State_CompletedWithValue;
            if (newState == State_CompletedWithError) {
                _Error = ExceptionDispatchInfo.Capture(task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception);
            } else {
                _Error = null;
            }

            SetResultEpilogue(newState);
        }

        /// <summary>
        /// Sets the result of this future to the result of a completed task.
        /// If the task failed, the exception will be stored into this future.
        /// </summary>
        public void SetResultFrom (System.Threading.Tasks.Task<T> task) {
            if (!task.IsCompleted)
                throw new InvalidOperationException("Task not completed");

            if (!SetResultPrologue())
                return;

            int newState = task.IsFaulted ? State_CompletedWithError : State_CompletedWithValue;
            _Error = null;
            if (newState == State_CompletedWithError) {
                // FIXME: Assign result here?
                _Error = ExceptionDispatchInfo.Capture(task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception);
            } else {
                _Result = task.Result;
                _Error = null;
            }

            SetResultEpilogue(newState);
        }

        /// <summary>
        /// Sets the result of this future along with information on the responsible exception (if any).
        /// This information will be used to rethrow with stack preserved, as necessary.
        /// </summary>
        public void SetResult2 (in T result, ExceptionDispatchInfo errorInfo) {
            if (!SetResultPrologue())
                return;

            int newState = (errorInfo != null) ? State_CompletedWithError : State_CompletedWithValue;
            _Result = result;
            _Error = errorInfo;

            SetResultEpilogue(newState);
        }

        /// <summary>
        /// Sets the result of this future along with the responsible exception (if any).
        /// The exception will be wrapped instead of being rethrown.
        /// </summary>
        public void SetResult (in T result, Exception error) {
            if (!SetResultPrologue())
                return;

            int newState = (error != null) ? State_CompletedWithError : State_CompletedWithValue;
            _Result = result;
            _Error = error;

            SetResultEpilogue(newState);
        }

        /// <summary>
        /// Sets the result of this future along with the responsible exception (if any).
        /// The exception will be wrapped instead of being rethrown.
        /// If the future already has a result or has been disposed, this method will return false.
        /// </summary>
        public bool TrySetResult (in T result, Exception error) {
            if (!SetResultPrologue())
                return false;

            int newState = (error != null) ? State_CompletedWithError : State_CompletedWithValue;
            _Result = result;
            _Error = error;

            SetResultEpilogue(newState);
            return true;
        }

        /// <summary>
        /// Disposes the future and invokes any OnDispose handlers.
        /// No OnComplete handlers will be invoked.
        /// </summary>
        public void Dispose () {
            int iterations = 1;

            while (true) {
                int oldState = Interlocked.CompareExchange(ref _State, State_Disposing, State_Empty);

                if ((oldState == State_Disposed) || 
                    (oldState == State_Disposing) ||
                    (oldState == State_CompletedWithValue) || 
                    (oldState == State_CompletedWithError)
                ) {
                    return;
                } else if (oldState == State_Empty)
                    break;

                SpinWait(iterations++);
            }

            // FIXME: Possible leak / failure to invoke, but probably fine?
            InvokeHandlers(HandlerType.Disposed);

            if (Interlocked.Exchange(ref _State, State_Disposed) != State_Disposing)
                throw new ThreadStateException("Future state was not disposing");
        }

        bool IFuture.GetResult (out object result, out Exception error) {
            T temp;
            bool retval = this.GetResult(out temp, out error);

            if (error != null)
                result = null;
            else
                result = temp;

            return retval;
        }

        /// <summary>
        /// Attempts to retrieve the future's result.
        /// Will return false if the future completed with an error, was disposed, or is not completed.
        /// </summary>
        public bool GetResult (out T result) {
            if (_State == State_CompletedWithValue) {
                Thread.MemoryBarrier();
                if (_State == State_CompletedWithValue) {
                    result = _Result;
                    return true;
                }
            }

            result = default(T);
            return false;
        }

        /// <summary>
        /// Attempts to retrieve the future's result.
        /// Will return false if the future was disposed or is not completed.
        /// </summary>
        public bool GetResult (out T result, out Exception error) {
            int state = _State;

            if (state == State_CompletedWithValue) {
                Thread.MemoryBarrier();
                if (_State == State_CompletedWithValue) {
                    result = _Result;
                    error = null;
                    return true;
                }
            }
            
            if (state == State_CompletedWithError) {
                Thread.MemoryBarrier();
                if (_State == State_CompletedWithError) {
                    OnErrorCheck();
                    result = default(T);
                    error = InternalError;
                    return true;
                }
            }

            result = default(T);
            error = null;
            return false;
        }
    }
    
    public static class FutureExtensionMethods {
        private static OnFutureResolvedWithData BindHandler = _BindHandler;

        private static void _BindHandler (IFuture source, object _receiver) {
            var receiver = (IFuture)_receiver;
            source.GetResult(out object result, out Exception error);
            // FIXME: SetResult2?
            receiver.SetResult(result, error);
        }

        /// <summary>
        /// Causes this future to become completed when the specified future is completed.
        /// </summary>
        public static void Bind (this IFuture receiver, IFuture source) {
            source.RegisterOnResolved(BindHandler, receiver);
        }

        /// <summary>
        /// Causes the result of this future to be stored into the specified member when it is completed.
        /// </summary>
        public static IFuture Bind<T> (this IFuture source, Expression<Func<T>> receiver) {
            var member = BoundMember.New(receiver);

            // FIXME: Use UserData version?
            source.RegisterOnComplete((_) => {
                Exception error;
                object result;
                if (source.GetResult(out result, out error))
                    ((IBoundMember)member).Value = result;
            });

            return source;
        }

        /// <summary>
        /// Causes the result of this future to be stored into the specified member when it is completed.
        /// </summary>
        public static Future<T> Bind<T> (this Future<T> source, Expression<Func<T>> receiver) {
            var member = BoundMember.New(receiver);

            source.RegisterOnComplete((f) => {
                T result;
                if (source.GetResult(out result))
                    member.Value = result;
            });
            
            return source;
        }

        public static void AssertSucceeded (this IFuture future) {
            if (future.Failed)
                throw new FutureException("Operation was expected to succeed", future.Error);
        }

        public static void Complete (this IFuture future) {
            future.SetResult(null, null);
        }

        public static void Complete (this IFuture future, object result) {
            future.SetResult(result, null);
        }

        public static void Complete<T> (this Future<T> future, T result) {
            future.SetResult(result, null);
        }

        public static void Fail (this IFuture future, Exception error) {
            // future.SetResult(null, error);
            future.SetResult2(null, ExceptionDispatchInfo.Capture(error));
        }

        public static void Fail<T> (this Future<T> future, Exception error) {
            future.SetResult2(default(T), ExceptionDispatchInfo.Capture(error));
        }

        public static bool CheckForFailure (this IFuture future, params Type[] failureTypes) {
            object result;
            Exception error;
            if (future.GetResult(out result, out error)) {
                if (error != null) {
                    foreach (Type type in failureTypes)
                        if (type.IsInstanceOfType(error))
                            return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Creates a ManualResetEventSlim that will become set when this future is completed.
        /// </summary>
        public static ManualResetEventSlim GetCompletionEvent (this IFuture future) {
            var evt = new ManualResetEventSlim(false);
            // FIXME: Use userData
            future.RegisterOnComplete(evt.Set);
            return evt;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Linq.Expressions;
using Squared.Util.Bind;

namespace Squared.Threading {
    public delegate void OnComplete(IFuture future);
    public delegate void OnDispose(IFuture future);

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

    internal class ActionRunInThreadThunk : RunInThreadThunk {
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

    internal class FuncRunInThreadThunk<T> : RunInThreadThunk {
        public readonly Future<T> Future = new Future<T>();
        public Func<T> WorkItem;

        public override void Invoke () {
            Future.Complete(WorkItem());
        }

        public override void Fail (Exception ex) {
            Future.Fail(ex);
        }
    }

    internal class DynamicRunInThreadThunk : RunInThreadThunk {
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
        bool Failed {
            get;
        }
        bool Completed {
            get;
        }
        bool Disposed {
            get;
        }
        object Result {
            get;
        }
        Exception Error {
            get;
        }

        bool GetResult (out object result, out Exception error);
        void SetResult (object result, Exception error);
        void RegisterOnComplete (OnComplete handler);
        void RegisterOnDispose (OnDispose handler);
        void RegisterOnErrorCheck (Action handler);
    }

    public class NoneType {
        private NoneType () {
        }

        public static readonly NoneType None = new NoneType();
    }

    public class SignalFuture : Future<NoneType> {
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
        public static Future<T> New<T> () {
            return new Future<T>();
        }

        public static Future<T> New<T> (T value) {
            return new Future<T>(value);
        }

        public static Future<IFuture> WaitForFirst (IEnumerable<IFuture> futures) {
            return WaitForFirst(futures.ToArray());
        }

        public static Future<IFuture> WaitForFirst (params IFuture[] futures) {
            return WaitForX(futures, futures.Length);
        }

        public static IFuture WaitForAll (IEnumerable<IFuture> futures) {
            return WaitForAll(futures.ToArray());
        }

        public static IFuture WaitForAll (params IFuture[] futures) {
            return WaitForX(futures, 1);
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

        private class WaitHandler {
            public Future<IFuture> Composite;
            public readonly List<IFuture> State = new List<IFuture>();
            public int Trigger;
            private bool Disposing = false;

            public void OnCompositeDispose (IFuture f) {
                if (Disposing)
                    return;

                lock (State)
                try {
                    Disposing = true;

                    foreach (var future in State)
                        future.Dispose();

                    State.Clear();
                } finally {
                    Disposing = false;
                }
            }

            public void OnComplete (IFuture f) {
                bool completed = false;
                lock (State) {
                    if (State.Count == Trigger) {
                        completed = true;
                        State.Clear();
                    } else {
                        State.Remove(f);
                    }
                }

                if (completed)
                    Composite.Complete(f);
            }

            public void OnDispose (IFuture f) {
                bool completed = false;
                lock (State) {
                    if (State.Count == Trigger) {
                        completed = true;
                        State.Clear();
                    } else {
                        State.Remove(f);
                    }
                }

                if (completed)
                    Composite.Dispose();
            }
        }

        private static Future<IFuture> WaitForX (IEnumerable<IFuture> futures, int x) {
            if (futures == null)
                throw new ArgumentException("Must specify at least one future to wait on", "futures");

            var f = new Future<IFuture>();
            var h = new WaitHandler();

            h.Composite = f;
            h.State.AddRange(futures);
            h.Trigger = x;

            OnComplete oc = h.OnComplete;
            OnDispose od = h.OnDispose;

            if (h.State.Count == 0)
                throw new ArgumentException("Must specify at least one future to wait on", "futures");

            f.RegisterOnDispose(h.OnCompositeDispose);

            foreach (IFuture _ in futures) {
                _.RegisterOnComplete(oc);
                _.RegisterOnDispose(od);
            }

            return f;
        }

        private class WaitForSingleEventThunk {
            public readonly Squared.Util.Bind.BoundMember<EventHandler> Event;
            public readonly SignalFuture Future = new SignalFuture();
            private readonly EventHandler Handler;

            public WaitForSingleEventThunk (Squared.Util.Bind.BoundMember<EventHandler> evt) {
                Event = evt;
                Handler = OnEvent;

                Event.Add(Handler);
                Future.RegisterOnDispose(OnDispose);
            }

            public void OnEvent (object sender, EventArgs args) {
                OnDispose(Future);
                Future.Complete();
            }

            public void OnDispose (IFuture future) {
                Event.Remove(Handler);
            }
        }

        private class WaitForSingleEventThunk<TEventArgs>
            where TEventArgs : System.EventArgs {

            public readonly Squared.Util.Bind.BoundMember<EventHandler<TEventArgs>> Event;
            public readonly Future<TEventArgs> Future = new Future<TEventArgs>();
            private readonly EventHandler<TEventArgs> Handler;

            public WaitForSingleEventThunk (Squared.Util.Bind.BoundMember<EventHandler<TEventArgs>> evt) {
                Event = evt;
                Handler = OnEvent;

                Event.Add(Handler);
                Future.RegisterOnDispose(OnDispose);
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
        public static readonly Future<T> Default;

        private static readonly int ProcessorCount;        

        private const int State_Empty = 0;
        private const int State_Indeterminate = 1;
        private const int State_CompletedWithValue = 2;
        private const int State_CompletedWithError = 3;
        private const int State_Disposed = 4;

        private int _State = State_Empty;
        private OnComplete _OnComplete = null;
        private OnDispose _OnDispose = null;
        private Exception _Error = null;
        private Action _OnErrorChecked = null;
        private T _Result = default(T);

        public override string ToString () {
            int state = _State;
            var result = _Result;
            var error = _Error;
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
                    stateText = "Disposed";
                    break;
            }
            return String.Format("<Future<{3}> ({0}) r={1} e={2}>", stateText, result, error, typeof(T).Name);
        }

        public Future () {
        }

        public Future (T value) {
            this.SetResult(value, null);
        }

        static Future () {
            ProcessorCount = Environment.ProcessorCount;
            Default = new Future<T>(default(T));
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

        private void InvokeOnDispose (OnDispose handler) {
            if (handler == null)
                return;

            try {
                handler(this);
            } catch (Exception ex) {
                throw new FutureHandlerException(this, handler, ex);
            }
        }

        private void InvokeOnComplete (OnComplete handler) {
            if (handler == null)
                return;

            try {
                handler(this);
            } catch (Exception ex) {
                throw new FutureHandlerException(this, handler, ex);
            }
        }

        void OnErrorCheck () {
            var ec = _OnErrorChecked;
            if (ec != null) {
                ec = Interlocked.Exchange(ref _OnErrorChecked, null);
                if (ec != null)
                    ec();
            }
        }

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

        public void RegisterOnComplete (OnComplete handler) {
            OnComplete newOnComplete;
            int iterations = 1;
            int state;

            while (true) {
                state = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if (state != State_Indeterminate)
                    break;

                SpinWait(iterations++);
            }

            if (state == State_Empty) {
                var oldOnComplete = _OnComplete;
                if (oldOnComplete != null) {
                    newOnComplete = (f) => {
                        oldOnComplete(f);
                        handler(f);
                    };
                } else {
                    newOnComplete = handler;
                }

                _OnComplete = newOnComplete;

                if (Interlocked.CompareExchange(ref _State, State_Empty, State_Indeterminate) != State_Indeterminate)
                    throw new ThreadStateException();
            } else if (state == State_CompletedWithValue) {
                InvokeOnComplete(handler);
            } else if (state == State_CompletedWithError) {
                InvokeOnComplete(handler);
            }
        }

        public void RegisterOnDispose (OnDispose handler) {
            OnDispose newOnDispose;
            int iterations = 1;
            int state;

            while (true) {
                state = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if (state != State_Indeterminate)
                    break;

                SpinWait(iterations++);
            }

            if (state == State_Empty) {
                var oldOnDispose = _OnDispose;
                if (oldOnDispose != null) {
                    newOnDispose = (f) => {
                        oldOnDispose(f);
                        handler(f);
                    };
                } else {
                    newOnDispose = handler;
                }

                _OnDispose = newOnDispose;

                if (Interlocked.CompareExchange(ref _State, State_Empty, State_Indeterminate) != State_Indeterminate)
                    throw new ThreadStateException();
            } else if (state == State_Disposed) {
                InvokeOnDispose(handler);
            }
        }

        public bool Disposed {
            get {
                return _State == State_Disposed;
            }
        }

        public bool Completed {
            get {
                int state = _State;
                return (state == State_CompletedWithValue) || (state == State_CompletedWithError);
            }
        }

        public bool Failed {
            get {
                OnErrorCheck();
                return _State == State_CompletedWithError;
            }
        }

        object IFuture.Result {
            get {
                return this.Result;
            }
        }

        public Exception Error {
            get {
                int state = _State;
                if (state == State_CompletedWithValue) {
                    return null;
                } else if (state == State_CompletedWithError) {
                    OnErrorCheck();
                    return _Error;
                } else
                    throw new FutureHasNoResultException(this);
            }
        }

        public T Result {
            get {
                int state = _State;
                if (state == State_CompletedWithValue) {
                    return _Result;
                } else if (state == State_CompletedWithError) {
                    OnErrorCheck();
                    throw new FutureException("Future's result was an error", (Exception)_Error);
                } else
                    throw new FutureHasNoResultException(this);
            }
        }

        void IFuture.SetResult (object result, Exception error) {
            if ((error != null) && (result != null)) {
                throw new FutureException("Cannot complete a future with both a result and an error.", error);
            }

            if (result == null)
                SetResult(default(T), error);
            else
                SetResult((T)result, error);
        }

        public void SetResult (T result, Exception error) {
            int iterations = 1;

            while (true) {
                int oldState = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if (oldState == State_Disposed) {
                    return;
                } else if ((oldState == State_CompletedWithValue) || (oldState == State_CompletedWithError))
                    throw new FutureAlreadyHasResultException(this);
                else if (oldState == State_Empty)
                    break;

                SpinWait(iterations++);
            }

            int newState = (error != null) ? State_CompletedWithError : State_CompletedWithValue;
            _Result = result;
            _Error = error;
            OnComplete handler = _OnComplete;
            _OnDispose = null;
            _OnComplete = null;

            if (Interlocked.Exchange(ref _State, newState) != State_Indeterminate)
                throw new ThreadStateException();

            if (newState == State_CompletedWithValue)
                InvokeOnComplete(handler);
            else if (newState == State_CompletedWithError)
                InvokeOnComplete(handler);
        }

        public void Dispose () {
            int iterations = 1;

            while (true) {
                int oldState = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if ((oldState == State_Disposed) || (oldState == State_CompletedWithValue) || (oldState == State_CompletedWithError)) {
                    return;
                } else if (oldState == State_Empty)
                    break;

                SpinWait(iterations++);
            }

            OnDispose handler = _OnDispose;
            _OnDispose = null;
            _OnComplete = null;

            if (Interlocked.Exchange(ref _State, State_Disposed) != State_Indeterminate)
                throw new ThreadStateException();

            InvokeOnDispose(handler);
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
                    error = _Error;
                    return true;
                }
            }

            result = default(T);
            error = null;
            return false;
        }
    }
    
    public static class FutureExtensionMethods {
        /// <summary>
        /// Causes this future to become completed when the specified future is completed.
        /// </summary>
        public static void Bind (this IFuture future, IFuture target) {
            OnComplete handler = (f) => {
                object result;
                Exception error;
                f.GetResult(out result, out error);
                future.SetResult(result, error);
            };
            target.RegisterOnComplete(handler);
        }

        public static IFuture Bind<T> (this IFuture future, Expression<Func<T>> target) {
            var member = BoundMember.New(target);

            future.RegisterOnComplete((_) => {
                Exception error;
                object result;
                if (future.GetResult(out result, out error))
                    ((IBoundMember)member).Value = result;
            });

            return future;
        }

        public static Future<T> Bind<T> (this Future<T> future, Expression<Func<T>> target) {
            var member = BoundMember.New(target);

            future.RegisterOnComplete((_) => {
                T result;
                if (future.GetResult(out result))
                    member.Value = result;
            });
            
            return future;
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
            future.SetResult(null, error);
        }

        public static void Fail<T> (this Future<T> future, Exception error) {
            future.SetResult(default(T), error);
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
            ManualResetEventSlim evt = new ManualResetEventSlim(false);
            OnComplete handler = (f) => evt.Set();
            future.RegisterOnComplete(handler);
            return evt;
        }
    }
}

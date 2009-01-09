#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;

namespace Squared.Task {
    public delegate void OnComplete(Future future, object value, Exception error);
    public delegate void OnDispose(Future future);

    [Serializable]
    public class FutureException : Exception {
        public FutureException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    [Serializable]
    public class FutureAlreadyHasResultException : InvalidOperationException {
        public FutureAlreadyHasResultException ()
            : base("Future already has a result") {
        }
    }

    [Serializable]
    public class FutureHasNoResultException : InvalidOperationException {
        public FutureHasNoResultException ()
            : base("Future does not yet have a result") {
        }
    }

    [Serializable]
    public class FutureDisposedException : InvalidOperationException {
        public FutureDisposedException ()
            : base("Future is disposed") {
        }
    }

    [Serializable]
    public class FutureHandlerException : Exception {
        Delegate Handler;

        public FutureHandlerException (Delegate handler, Exception innerException) 
            : base("One of the Future's handlers threw an uncaught exception", innerException) {
            Handler = handler;
        }
    }

    internal static class FutureHelpers {
        public static WaitCallback RunInThreadHelper;

        static FutureHelpers () {
            RunInThreadHelper = _RunInThreadHelper;
        }

        internal static void _RunInThreadHelper (object state) {
            var thunk = (RunInThreadThunk)state;
            try {
                var result = thunk.Invoke();
                thunk.Future.Complete(result);
            } catch (System.Reflection.TargetInvocationException ex) {
                thunk.Future.Fail(ex.InnerException);
            } catch (Exception ex) {
                thunk.Future.Fail(ex);
            }
        }
    }

    internal abstract class RunInThreadThunk {
        public Future Future = new Future();

        public abstract object Invoke ();
    }

    internal class ActionRunInThreadThunk : RunInThreadThunk {
        public Action WorkItem;

        public override object Invoke () {
            WorkItem();
            return null;
        }
    }

    internal class FuncRunInThreadThunk : RunInThreadThunk {
        public Func<object> WorkItem;

        public override object Invoke () {
            return WorkItem();
        }
    }

    internal class DynamicRunInThreadThunk : RunInThreadThunk {
        public object[] Arguments;
        public Delegate WorkItem;

        public override object Invoke () {
#if XBOX
            return WorkItem.Method.Invoke(WorkItem.Target, Arguments);
#else
            return WorkItem.DynamicInvoke(Arguments);
#endif
        }
    }

    public class Future : IDisposable {
        private const int State_Empty = 0;
        private const int State_Indeterminate = 1;
        private const int State_CompletedWithValue = 2;
        private const int State_CompletedWithError = 3;
        private const int State_Disposed = 4;

        private volatile int _State = State_Empty;
        private volatile object _Result = null;
        private volatile OnComplete _OnComplete = null;
        private volatile OnDispose _OnDispose = null;

        public override string ToString () {
            int state = _State;
            var result = _Result;
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
            return String.Format("<Future ({0}) r={1}>", stateText, result);
        }

        public Future () {
        }

        public Future (object value) {
            this.SetResult(value, null);
        }

        private static void SpinWait (int iterationCount) {
#if !XBOX
            if ((iterationCount < 4) && (Environment.ProcessorCount > 1)) {
                Thread.SpinWait(5 * iterationCount);
            } else if (iterationCount < 7) {
#else
            if (iterationCount < 3) {
#endif
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
                throw new FutureHandlerException(handler, ex);
            }
        }

        private void InvokeOnComplete (OnComplete handler, object result, Exception error) {
            if (handler == null)
                return;

            try {
                handler(this, result, error);
            } catch (Exception ex) {
                throw new FutureHandlerException(handler, ex);
            }
        }

        public void RegisterOnComplete (OnComplete handler) {
            OnComplete newOnComplete;
            int iterations = 1;
            int state = 0;

            while (true) {
                state = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if (state != State_Indeterminate)
                    break;

                SpinWait(iterations++);
            }

            if (state == State_Empty) {
                var oldOnComplete = _OnComplete;
                if (oldOnComplete != null) {
                    newOnComplete = (f, r, e) => {
                        oldOnComplete(f, r, e);
                        handler(f, r, e);
                    };
                } else {
                    newOnComplete = handler;
                }

                _OnComplete = newOnComplete;

                if (Interlocked.CompareExchange(ref _State, State_Empty, State_Indeterminate) != State_Indeterminate)
                    throw new ThreadStateException();
            } else if (state == State_CompletedWithValue) {
                InvokeOnComplete(handler, _Result, null);
            } else if (state == State_CompletedWithError) {
                InvokeOnComplete(handler, null, (Exception)_Result);
            }
        }

        public void RegisterOnDispose (OnDispose handler) {
            OnDispose newOnDispose;
            int iterations = 1;
            int state = 0;

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
                return _State == State_CompletedWithError;
            }
        }

        public object Result {
            get {
                int state = _State;
                if (state == State_CompletedWithValue) {
                    return _Result;
                } else if (state == State_CompletedWithError) {
                    throw new FutureException("Future's result was an error", (Exception)_Result);
                } else
                    throw new FutureHasNoResultException();
            }
        }

        public void SetResult (object result, Exception error) {
            if ((result != null) && (error != null)) {
                throw new FutureException("Cannot complete a future with both a result and an error.", error);
            }

            int iterations = 1;

            while (true) {
                int oldState = Interlocked.CompareExchange(ref _State, State_Indeterminate, State_Empty);

                if (oldState == State_Disposed) {
                    return;
                } else if ((oldState == State_CompletedWithValue) || (oldState == State_CompletedWithError))
                    throw new FutureAlreadyHasResultException();
                else if (oldState == State_Empty)
                    break;

                SpinWait(iterations++);
            }

            int newState = (error != null) ? State_CompletedWithError : State_CompletedWithValue;
            _Result = error ?? result;
            OnComplete handler = _OnComplete;
            _OnDispose = null;
            _OnComplete = null;

            if (Interlocked.Exchange(ref _State, newState) != State_Indeterminate)
                throw new ThreadStateException();

            if (newState == State_CompletedWithValue)
                InvokeOnComplete(handler, result, null);
            else if (newState == State_CompletedWithError)
                InvokeOnComplete(handler, null, error);
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

        public bool GetResult (out object result, out Exception error) {
            int state = _State;

            if (state == State_CompletedWithValue) {
                result = _Result;
                error = null;
                return true;
            } else if (state == State_CompletedWithError) {
                result = null;
                error = (Exception)_Result;
                return true;
            }

            result = null;
            error = null;
            return false;
        }

        public static Future WaitForFirst (IEnumerable<Future> futures) {
            return WaitForFirst(futures.ToArray());
        }

        public static Future WaitForFirst (params Future[] futures) {
            return WaitForX(futures, futures.Length);
        }

        public static Future WaitForAll (IEnumerable<Future> futures) {
            return WaitForAll(futures.ToArray());
        }

        public static Future WaitForAll (params Future[] futures) {
            return WaitForX(futures, 1);
        }

        public static Future RunInThread (Func<object> workItem) {
            var thunk = new FuncRunInThreadThunk {
                WorkItem = workItem,
            };
            ThreadPool.QueueUserWorkItem(FutureHelpers.RunInThreadHelper, thunk);
            return thunk.Future;
        }

        public static Future RunInThread (Action workItem) {
            var thunk = new ActionRunInThreadThunk {
                WorkItem = workItem,
            };
            ThreadPool.QueueUserWorkItem(FutureHelpers.RunInThreadHelper, thunk);
            return thunk.Future;
        }

        public static Future RunInThread (Delegate workItem, params object[] arguments) {
            var thunk = new DynamicRunInThreadThunk {
                WorkItem = workItem,
                Arguments = arguments
            };
            ThreadPool.QueueUserWorkItem(FutureHelpers.RunInThreadHelper, thunk);
            return thunk.Future;
        }

        private class WaitHandler {
            public Future Composite;
            public List<Future> State = new List<Future>();
            public int Trigger;

            public void OnComplete (Future f, object r, Exception e) {
                bool completed = false;
                lock (State) {
                    if (State.Count == Trigger) {
                        completed = true;
                        State.Clear();
                    } else {
                        State.Remove(f);
                    }
                }

                if (completed) {
                    Composite.Complete(f);
                }
            }
        }

        private static Future WaitForX (Future[] futures, int x) {
            if ((futures == null) || (futures.Length == 0))
                throw new ArgumentException("Must specify at least one future to wait on", "futures");

            Future f = new Future();
            var h = new WaitHandler();
            h.Composite = f;
            h.State.AddRange(futures);
            h.Trigger = x;
            OnComplete handler = h.OnComplete;

            foreach (Future _ in futures)
                _.RegisterOnComplete(handler);

            return f;
        }
    }
    
    public static class FutureExtensionMethods {
        /// <summary>
        /// Causes this future to become completed when the specified future is completed.
        /// </summary>
        public static void Bind (this Future future, Future target) {
            OnComplete handler = (f, result, error) => {
                future.SetResult(result, error);
            };
            target.RegisterOnComplete(handler);
        }

        public static void Complete (this Future future) {
            future.SetResult(null, null);
        }

        public static void Complete (this Future future, object result) {
            future.SetResult(result, null);
        }

        public static void Fail (this Future future, Exception error) {
            future.SetResult(null, error);
        }

        public static bool CheckForFailure (this Future future, params Type[] failureTypes) {
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
        /// Creates a ManualResetEvent that will become set when this future is completed.
        /// </summary>
        public static ManualResetEvent GetCompletionEvent (this Future future) {
            ManualResetEvent evt = new ManualResetEvent(false);
            OnComplete handler = (f, result, error) => {
                evt.Set();
            };
            future.RegisterOnComplete(handler);
            return evt;
        }

        public static WaitWithTimeout WaitWithTimeout (this Future future, double timeout) {
            return new WaitWithTimeout(future, timeout);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace Squared.Task {
    public delegate void OnComplete(object value, Exception error);
    public delegate void OnDispose();

    public class FutureException : Exception {
        public FutureException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    public class FutureAlreadyHasResultException : InvalidOperationException {
        public FutureAlreadyHasResultException ()
            : base("Future already has a result") {
        }
    }

    public class FutureHasNoResultException : InvalidOperationException {
        public FutureHasNoResultException ()
            : base("Future does not yet have a result") {
        }
    }

    public class FutureDisposedException : InvalidOperationException {
        public FutureDisposedException ()
            : base("Future is disposed") {
        }
    }

    public class CannotUnregisterHandlerException : InvalidOperationException {
        public CannotUnregisterHandlerException ()
            : base("Future handlers cannot be unregistered once a future is completed or disposed") {
        }
    }

    public class Future : IDisposable {
        private bool _Completed = false;
        private bool _Disposed = false;
        private object _Value;
        private Exception _Error;
        private Queue<OnComplete> _OnCompletes = new Queue<OnComplete>();
        private Queue<OnDispose> _OnDisposes = new Queue<OnDispose>();

        public override string ToString () {
            lock (this)
                return String.Format("<Future ({0}) r={1},{2}>", _Completed ? "completed" : (_Disposed ? "disposed" : ""), _Value, _Error);
        }

        public Future () {
        }

        public Future (object value) {
            this.Complete(value);
        }

        public Future (Exception error) {
            this.Fail(error);
        }

        private void InvokeOnDisposes () {
            while (_OnDisposes.Count != 0) {
                OnDispose item = _OnDisposes.Dequeue();
                Monitor.Exit(this);
                try {
                    item();
                } finally {
                    Monitor.Enter(this);
                }
            }
        }

        private void InvokeOnCompletes (object result, Exception error) {
            while (_OnCompletes.Count != 0) {
                OnComplete item = _OnCompletes.Dequeue();
                Monitor.Exit(this);
                try {
                    item(result, error);
                } finally {
                    Monitor.Enter(this);
                }
            }
        }

        public void RegisterOnComplete (OnComplete handler) {
            lock (this) {
                if (_Disposed)
                    return;

                if (!_Completed) {
                    _OnCompletes.Enqueue(handler);
                    return;
                }
            }
            handler(_Value, _Error);
        }

        public void RegisterOnDispose (OnDispose handler) {
            lock (this) {
                if (_Completed)
                    return;

                if (!_Disposed) {
                    _OnDisposes.Enqueue(handler);
                    return;
                }
            }
            handler();
        }

        public bool Disposed {
            get {
                lock (this)
                    return _Disposed;
            }
        }

        public bool Completed {
            get {
                lock (this)
                    return _Completed;
            }
        }

        public bool Failed {
            get {
                lock (this) {
                    if (_Completed)
                        return (_Error != null);
                    else
                        return false;
                }
            }
        }

        public object Result {
            get {
                lock (this) {
                    if (_Completed) {
                        if (_Error != null)
                            throw new FutureException("Future's result was an error", _Error);
                        else
                            return _Value;
                    } else {
                        throw new FutureHasNoResultException();
                    }
                }
            }
        }

        public void SetResult (object result, Exception error) {
            lock (this) {
                if (_Disposed)
                    throw new FutureDisposedException();
                else if (_Completed)
                    throw new FutureAlreadyHasResultException();
                else {
                    _Value = result;
                    _Error = error;
                    _Completed = true;
                    InvokeOnCompletes(result, error);
                }

                _OnCompletes.Clear();
                _OnDisposes.Clear();
            }
        }

        public void Dispose () {
            lock (this) {
                if (_Disposed)
                    return;
                else if (_Completed)
                    return;
                else {
                    _Disposed = true;
                    InvokeOnDisposes();
                }

                _OnCompletes.Clear();
                _OnDisposes.Clear();
            }
        }

        public bool GetResult (out object result, out Exception error) {
            lock (this) {
                if (_Completed) {
                    result = _Value;
                    error = _Error;
                    return true;
                } else {
                    result = null;
                    error = null;
                    return false;
                }
            }
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

        private static Future WaitForX (Future[] futures, int x) {
            if ((futures == null) || (futures.Length == 0))
                throw new ArgumentException("Must specify at least one future to wait on", "futures");

            Future f = new Future();

            var state = new List<Future>();
            state.AddRange(futures);

            foreach (Future _ in futures) {
                Future item = _;
                OnComplete handler = (r, e) => {
                    bool completed = false;
                    lock (state) {
                        if (state.Count == x) {
                            completed = true;
                            state.Clear();
                        } else {
                            state.Remove(item);
                        }
                    }

                    if (completed) {
                        f.Complete(item);
                    }
                };
                _.RegisterOnComplete(handler);
            }

            return f;
        }
    }
    
    public static class FutureExtensionMethods {
        public static void Bind (this Future future, Future target) {
            OnComplete handler = (result, error) => {
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

        public static ManualResetEvent GetCompletionEvent (this Future future) {
            ManualResetEvent evt = new ManualResetEvent(false);
            OnComplete handler = (result, error) => {
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

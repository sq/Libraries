using System;
using System.Collections.Generic;
using System.Threading;

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

    public class Future : IDisposable {
        private bool _Completed = false;
        private bool _Disposed = false;
        private object _Value;
        private Exception _Error;
        private OnComplete _OnComplete;
        private OnDispose _OnDispose;

        public override string ToString () {
            lock (this)
                return String.Format("<Future {0}(r={1} e={2})", _Completed ? "completed" : (_Disposed ? "disposed" : ""), _Value, _Error);
        }

        public Future () {
        }

        public Future (object value) {
            this.Complete(value);
        }

        public Future (Exception error) {
            this.Fail(error);
        }

        private void InvokeOnDisposes (OnDispose evt) {
            if (evt != null)
                evt();
        }

        private void InvokeOnCompletes (OnComplete evt, object result, Exception error) {
            if (evt != null)
                evt(result, error);
        }

        public void RegisterOnComplete (OnComplete handler) {
            lock (this) {
                if (_Disposed)
                    return;

                if (!_Completed) {
                    _OnComplete += handler;
                    return;
                }
            }
            handler(_Value, _Error);
        }

        public void RegisterOnDisposed (OnDispose handler) {
            lock (this) {
                if (_Completed)
                    return;

                if (!_Disposed) {
                    _OnDispose += handler;
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
            OnComplete evt;
            lock (this) {
                if (_Disposed)
                    throw new FutureDisposedException();
                else if (_Completed)
                    throw new FutureAlreadyHasResultException();
                else {
                    _Value = result;
                    _Error = error;
                    _Completed = true;
                    evt = _OnComplete;
                }
            }
            InvokeOnCompletes(evt, result, error);
        }

        public void Dispose () {
            OnDispose evt;
            lock (this) {
                if (_Disposed)
                    return;
                else if (_Completed)
                    return;
                else {
                    _Disposed = true;
                    evt = _OnDispose;
                }
            }
            InvokeOnDisposes(evt);

            _OnComplete = null;
            _OnDispose = null;
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

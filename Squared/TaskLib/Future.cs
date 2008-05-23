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
        private int _CompletionState = 0;
        private volatile bool _Completed = false;
        private volatile bool _Disposed = false;
        private volatile object _Value;
        private volatile Exception _Error;
        private OnComplete _OnComplete;
        private OnDispose _OnDispose;

        public Future () {
        }

        public Future (object value) {
            this.Complete(value);
        }

        public Future (Exception error) {
            this.Fail(error);
        }

        private void InvokeOnDisposes () {
            OnDispose evt = Interlocked.Exchange(ref _OnDispose, null);

            if (evt != null)
                evt();
        }

        private void InvokeOnCompletes (object result, Exception error) {
            OnComplete evt = Interlocked.Exchange(ref _OnComplete, null);

            if (evt != null)
                evt(result, error);
        }

        public void RegisterOnComplete (OnComplete handler) {
            if (_Disposed)
                return;

            if (_Completed) {
                handler(_Value, _Error);
            } else {
                while (true) {
                    OnComplete prev = _OnComplete;
                    OnComplete result = prev;
                    result += handler;
                    if (Interlocked.CompareExchange<OnComplete>(ref _OnComplete, result, prev) == prev)
                        break;
                }
            }
        }

        public void RegisterOnDisposed (OnDispose handler) {
            if (_Completed)
                return;

            if (_Disposed) {
                handler();
            } else {
                while (true) {
                    OnDispose prev = _OnDispose;
                    OnDispose result = prev;
                    result += handler;
                    if (Interlocked.CompareExchange<OnDispose>(ref _OnDispose, result, prev) == prev)
                        break;
                }
            }
        }

        public bool Disposed {
            get {
                return _Disposed;
            }
        }

        public bool Completed {
            get {
                return _Completed;
            }
        }

        public bool Failed {
            get {
                if (_Completed)
                    return (_Error != null);
                else
                    return false;
            }
        }

        public object Result {
            get {
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

        public void SetResult (object result, Exception error) {
            int newState = Interlocked.Increment(ref _CompletionState);
            if (newState != 1) {
                if (_Disposed)
                    throw new FutureDisposedException();
                else
                    throw new FutureAlreadyHasResultException();
            } else {
                _Value = result;
                _Error = error;
                _Completed = true;
                InvokeOnCompletes(result, error);
            }
        }

        public void Dispose () {
            int newState = Interlocked.Increment(ref _CompletionState);
            if (newState != 1)
                return;

            _Disposed = true;
            InvokeOnDisposes();

            _OnComplete = null;
            _OnDispose = null;
        }

        public bool GetResult (out object result, out Exception error) {
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
                foreach (Type type in failureTypes)
                    if (type.IsInstanceOfType(error))
                        return true;
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

using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Task {
    public delegate void OnComplete(object value, Exception error);

    public class FutureException : Exception {
        public FutureException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    public class Future {
        private int _CompletionState = 0;
        private volatile bool _Completed = false;
        private volatile object _Value;
        private volatile Exception _Error;
        private List<OnComplete> _OnCompletes = new List<OnComplete>();

        public Future () {
        }

        public Future (object value) {
            this.Complete(value);
        }

        public Future (Exception error) {
            this.Fail(error);
        }

        private void InvokeOnCompletes (object result, Exception error) {
            Monitor.Enter(_OnCompletes);
            OnComplete[] onCompletes = _OnCompletes.ToArray();
            _OnCompletes.Clear();
            Monitor.Exit(_OnCompletes);

            foreach (OnComplete oc in onCompletes)
                oc(result, error);
        }

        public void RegisterOnComplete (OnComplete handler) {
            if (_Completed) {
                handler(_Value, _Error);
            } else {
                Monitor.Enter(_OnCompletes);
                _OnCompletes.Add(handler);
                Monitor.Exit(_OnCompletes);
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
                    throw new InvalidOperationException("Future has no result");
                }
            }
        }

        public void SetResult (object result, Exception error) {
            int newState = Interlocked.Increment(ref _CompletionState);
            if (newState != 1) {
                throw new InvalidOperationException("Future already has a result");
            } else {
                _Value = result;
                _Error = error;
                _Completed = true;
                InvokeOnCompletes(result, error);
            }
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

        public static bool CheckForFailure (this Future future, Type failureType) {
            object result;
            Exception error;
            if (future.GetResult(out result, out error))
                return failureType.IsInstanceOfType(error);
            else
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

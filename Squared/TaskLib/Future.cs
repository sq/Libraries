using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Task {
    public delegate void OnComplete(object value, Exception error);

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
            foreach (OnComplete oc in _OnCompletes)
                oc(result, error);
            _OnCompletes.Clear();
        }

        public void RegisterOnComplete (OnComplete handler) {
            if (_Completed) {
                handler(_Value, _Error);
            } else {
                _OnCompletes.Add(handler);
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
                        throw _Error;
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

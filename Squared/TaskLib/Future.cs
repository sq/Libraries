using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Task {
    public delegate void OnComplete(object value, Exception error);

    struct FutureResult {
        public object Value;
        public Exception Exception;

        public FutureResult(object value, Exception exception) {
            Value = value;
            Exception = exception;
        }
    }

    public class Future {
        private object _Lock = new object();
        private FutureResult? _Result;
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
            _OnCompletes.ForEach((f) => { f(result, error); });
        }

        public void RegisterOnComplete (OnComplete handler) {
            lock (_Lock) {
                if (Completed) {
                    if (Failed)
                        handler(null, _Result.Value.Exception);
                    else
                        handler(_Result.Value.Value, null);
                } else {
                    _OnCompletes.Add(handler);
                }
            }
        }

        public bool Completed {
            get {
                lock (_Lock) {
                    return _Result.HasValue;
                }
            }
        }

        public bool Failed {
            get {
                lock (_Lock) {
                    if (_Result.HasValue)
                        return _Result.Value.Exception != null;
                    else
                        return false;
                }
            }
        }

        public object Result {
            get {
                lock (_Lock) {
                    if (!_Result.HasValue)
                        throw new InvalidOperationException("Future has no result");
                    else if (_Result.Value.Exception != null)
                        throw _Result.Value.Exception;
                    else
                        return _Result.Value.Value;
                }
            }
        }

        public void SetResult (object result, Exception error) {
            lock (_Lock) {
                if (Completed)
                    throw new InvalidOperationException("Future already has a result");
                _Result = new FutureResult(result, error);
            }
            InvokeOnCompletes(result, error);
        }

        public bool GetResult (out object result, out Exception error) {
            lock (_Lock) {
                if (!_Result.HasValue) {
                    result = null;
                    error = null;
                    return false;
                }

                result = _Result.Value.Value;
                error = _Result.Value.Exception;
                return true;
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

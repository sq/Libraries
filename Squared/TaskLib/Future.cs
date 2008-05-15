using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Task {
    public delegate void OnComplete(object value, Exception error);

    public struct FutureResult {
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
                    if (_Result.Value.Exception != null)
                        throw _Result.Value.Exception;
                    else
                        return _Result.Value.Value;
                }
            }
        }

        public void Complete () {
            Complete(null);
        }

        public void Complete (object result) {
            lock (_Lock) {
                if (Completed)
                    throw new InvalidOperationException("Already completed");
                _Result = new FutureResult(result, null);
            }
            InvokeOnCompletes(result, null);
        }

        public void Fail (Exception error) {
            lock (_Lock) {
                if (Completed)
                    throw new InvalidOperationException("Already completed");
                _Result = new FutureResult(null, error);
            }
            InvokeOnCompletes(null, error);
        }

        void BindOnComplete (object result, Exception error) {
            if (error != null)
                Fail(error);
            else
                Complete(result);
        }

        public void Bind (Future target) {
            target.RegisterOnComplete(this.BindOnComplete);
        }

        public ManualResetEvent GetCompletionEvent () {
            lock (_Lock) {
                ManualResetEvent evt = new ManualResetEvent(this.Completed);
                OnComplete handler = (result, error) => {
                    evt.Set();
                };
                RegisterOnComplete(handler);
                return evt;
            }
        }
    }
}

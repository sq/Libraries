using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task {
    public class BlockingQueue<T> {
        private object _Lock = new object();
        private Queue<Future<T>> _WaitingFutures = new Queue<Future<T>>();
        private Queue<T> _Queue = new Queue<T>();

        public int Count {
            get {
                lock (_Lock) {
                    return _Queue.Count - _WaitingFutures.Count;
                }
            }
        }

        public int DequeueMultiple (IList<T> output, int maximum) {
            for (int i = 0; i < maximum; i++) {
                lock (_Lock) {
                    if (_Queue.Count == 0)
                        return i;

                    output.Add(_Queue.Dequeue());
                }
            }

            return maximum;
        }

        public T[] DequeueMultiple (int maximum) {
            var result = new List<T>(maximum);
            DequeueMultiple(result, maximum);
            return result.ToArray();
        }

        public T[] DequeueAll () {
            lock (_Lock) {
                T[] result = new T[_Queue.Count];
                _Queue.CopyTo(result, 0);
                _Queue.Clear();
                return result;
            }
        }

        public Future<T> Dequeue () {
            var f = new Future<T>();
            lock (_Lock) {
                if (_Queue.Count > 0)
                    f.Complete(_Queue.Dequeue());
                else
                    _WaitingFutures.Enqueue(f);
            }
            return f;
        }

        public void Enqueue (T value) {
            Future<T> wf = null;

            while (true)
            lock (_Lock) {
                if (_WaitingFutures.Count > 0) {
                    wf = _WaitingFutures.Dequeue();

                    try {
                        wf.Complete(value);
                        if (wf.Completed)
                            return;
                    } catch (FutureDisposedException) {
                    }
                } else {
                    _Queue.Enqueue(value);
                    return;
                }
            }
        }

        public void EnqueueMultiple (IEnumerable<T> values) {
            foreach (var value in values)
                Enqueue(value);
        }
    }
}

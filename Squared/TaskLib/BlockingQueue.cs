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

            lock (_Lock) {
                if (_WaitingFutures.Count > 0) {
                    wf = _WaitingFutures.Dequeue();
                } else {
                    _Queue.Enqueue(value);
                    return;
                }
            }

            wf.Complete(value);
        }

        public void EnqueueMultiple (T[] values) {
            foreach (var value in values)
                Enqueue(value);
        }
    }
}

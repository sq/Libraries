using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task {
    public class BlockingQueue<T> {
        private object _Lock = new object();
        private Queue<Future> _WaitingFutures = new Queue<Future>();
        private Queue<T> _Queue = new Queue<T>();

        public int Count {
            get {
                lock (_Lock) {
                    return _Queue.Count - _WaitingFutures.Count;
                }
            }
        }

        public Future Dequeue () {
            Future f = new Future();
            lock (_Lock) {
                if (_Queue.Count > 0)
                    f.Complete(_Queue.Dequeue());
                else
                    _WaitingFutures.Enqueue(f);
            }
            return f;
        }

        public void Enqueue (T value) {
            lock (_Lock) {
                if (_WaitingFutures.Count > 0)
                    _WaitingFutures.Dequeue().Complete(value);
                else
                    _Queue.Enqueue(value);
            }
        }
    }
}

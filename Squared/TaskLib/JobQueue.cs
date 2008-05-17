using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Squared.Task {
    internal class JobQueue {
        private Queue<Action> _Queue = new Queue<Action>();
        private AutoResetEvent _NewJobEvent = new AutoResetEvent(false);
        private int _NumEventWaiters = 0;

        public void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);
            if (_NumEventWaiters > 0)
                _NewJobEvent.Set();
        }

        public void Step () {
            while (_Queue.Count > 0)
                _Queue.Dequeue()();
        }

        public void WaitForWorkItems () {
            Interlocked.Increment(ref _NumEventWaiters);
            while (_Queue.Count == 0) {
                _NewJobEvent.WaitOne(100, true);
            }
            Interlocked.Decrement(ref _NumEventWaiters);
        }

        public int Count {
            get {
                return _Queue.Count;
            }
        }
    }
}

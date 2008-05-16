using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Squared.Task {
    internal class JobQueue {
        private Queue<Action> _Queue = new Queue<Action>();
        private AutoResetEvent _NewJobEvent = new AutoResetEvent(false);

        public void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);
            _NewJobEvent.Set();
        }

        public void Step () {
            Action item = null;
            while (true) {
                try {
                    item = _Queue.Dequeue();
                } catch (InvalidOperationException) {
                    break;
                }
                item();
            }
        }

        public void WaitForWorkItems () {
            if (_Queue.Count == 0)
                _NewJobEvent.WaitOne();
        }

        public int Count {
            get {
                return _Queue.Count;
            }
        }
    }
}

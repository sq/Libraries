using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task {
    internal class JobQueue {
        public event Action OnNewWorkItem;

        private Queue<Action> _Queue = new Queue<Action>();
        private ManualResetEvent _NewWorkItemEvent = null;
        private bool _ThreadSafe = false;

        private void SetWorkItemEvent () {
            _NewWorkItemEvent.Set();
        }

        public bool ThreadSafe {
            get {
                return _ThreadSafe;
            }
            set {
                if (_ThreadSafe == value)
                    return;

                _ThreadSafe = value;
                if (value) {
                    _NewWorkItemEvent = new ManualResetEvent(false);
                    OnNewWorkItem += SetWorkItemEvent;
                } else {
                    OnNewWorkItem -= SetWorkItemEvent;
                    _NewWorkItemEvent = null;
                }
            }
        }

        public void QueueWorkItem (Action item) {
            if (_ThreadSafe)
                Monitor.Enter(_Queue);
            try {
                _Queue.Enqueue(item);
            } finally {
                if (_ThreadSafe)
                    Monitor.Exit(_Queue);
            }
            if (OnNewWorkItem != null)
                OnNewWorkItem();
        }

        public void Step () {
            Action item = null;
            while (true) {
                if (_ThreadSafe)
                    Monitor.Enter(_Queue);
                try {
                    if (_Queue.Count == 0)
                        return;
                    item = _Queue.Dequeue();
                } finally {
                    if (_ThreadSafe)
                        Monitor.Exit(_Queue);
                }
                if (item != null)
                    item();
            }
        }

        public bool WaitForWorkItems (double timeout) {
            if (!_ThreadSafe)
                throw new InvalidOperationException("WaitForWorkItems is invalid in non-thread-safe mode");

            Monitor.Enter(_Queue);
            try {
                if (_Queue.Count != 0)
                    return true;
                else
                    _NewWorkItemEvent.Reset();
            } finally {
                Monitor.Exit(_Queue);
            }
            if (timeout > 0) {
                bool result = _NewWorkItemEvent.WaitOne(TimeSpan.FromSeconds(timeout), true);
                return result;
            } else {
                _NewWorkItemEvent.WaitOne();
                return true;
            }
        }

        public int Count {
            get {
                return _Queue.Count;
            }
        }
    }
}

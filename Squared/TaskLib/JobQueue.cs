using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Squared.Task {
    internal class JobQueue {
        public event Action OnNewWorkItem;

        private Queue<Action> _Queue = new Queue<Action>();
        private AutoResetEvent _NewWorkItemEvent = null;
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
                    _NewWorkItemEvent = new AutoResetEvent(false);
                    OnNewWorkItem += SetWorkItemEvent;
                } else {
                    OnNewWorkItem -= SetWorkItemEvent;
                    _NewWorkItemEvent = null;
                }
            }
        }

        public void QueueWorkItem (Action item) {
            try {
                if (_ThreadSafe)
                    Monitor.Enter(_Queue);
                _Queue.Enqueue(item);
            } finally {
                if (_ThreadSafe)
                    Monitor.Exit(_Queue);
            }
            if (OnNewWorkItem != null)
                OnNewWorkItem();
        }

        public void Step () {
            Action item;
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
                item();
            }
        }

        public void WaitForWorkItems () {
            if (!_ThreadSafe)
                throw new InvalidOperationException("You cannot WaitForWorkItems unless in ThreadSafe mode");

            if (_ThreadSafe)
                Monitor.Enter(_Queue);
            try {
                if (_Queue.Count != 0)
                    return;
            } finally {
                if (_ThreadSafe)
                    Monitor.Exit(_Queue);
            }

            _NewWorkItemEvent.WaitOne();
        }

        public int Count {
            get {
                return _Queue.Count;
            }
        }
    }
}

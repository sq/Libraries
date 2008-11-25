using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Security;
using Squared.Util;

namespace Squared.Task {
    public interface IJobQueue : IDisposable {
        void QueueWorkItem (Action item);
        void Step ();
        void WaitForFuture (Future future);
        bool WaitForWorkItems (double timeout);
        int Count { get; }
    }

    public static partial class JobQueue {
        [Obsolete("This method is deprecated. Use JobQueue.ThreadSafe instead.", true)]
        public static IJobQueue SingleThreaded () {
            return ThreadSafe();
        }

        public static IJobQueue ThreadSafe () {
            return new ThreadSafeJobQueue();
        }
    }

    public class JobQueueBase : IJobQueue {
        protected AtomicQueue<Action> _Queue = new AtomicQueue<Action>();

        public virtual void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);
        }

        public void Step () {
            Action item = null;
            do {
                if (_Queue.Dequeue(out item))
                    item();
            } while (item != null);
        }

        public void WaitForFuture (Future future) {
            Action item = null;
            while (!future.Completed) {
                if (_Queue.Dequeue(out item))
                    item();
            }
        }

        public virtual bool WaitForWorkItems (double timeout) {
            return (_Queue.GetCount() > 0);
        }

        public int Count {
            get {
                return _Queue.GetCount();
            }
        }

        public virtual void Dispose () {
        }
    }

    public class ThreadSafeJobQueue : JobQueueBase {
        public const double DefaultWaitTimeout = 1.0;

        private volatile int _WaiterCount = 0;

        public override void QueueWorkItem (Action item) {
            base.QueueWorkItem(item);

            if (_WaiterCount > 0)
                lock (_Queue)
                    Monitor.PulseAll(_Queue);
        }

        public override bool WaitForWorkItems (double timeout) {
            if (_Queue.GetCount() > 0) {
                return true;
            } else {
                Interlocked.Increment(ref _WaiterCount);

                if (timeout <= 0)
                    timeout = DefaultWaitTimeout;

                lock (_Queue) {
                    try {
                        if (_Queue.GetCount() > 0)
                            return true;
                        else
                            return Monitor.Wait(_Queue, TimeSpan.FromSeconds(timeout), true);
                    } finally {
                        Interlocked.Decrement(ref _WaiterCount);
                    }
                }
            }
        }

        public override void Dispose () {
            base.Dispose();
        }
    }
}

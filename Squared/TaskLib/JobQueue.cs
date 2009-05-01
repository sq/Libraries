#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

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
        bool WaitForFuture (IFuture future);
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

    public sealed class ThreadSafeJobQueue : IJobQueue {
        public const double DefaultWaitTimeout = 1.0;

        private AutoResetEvent _WaiterSignal = new AutoResetEvent(false);
        private volatile int _WaiterCount = 0;

        private AtomicQueue<Action> _Queue = new AtomicQueue<Action>();

        public void Step () {
            Action item = null;
            do {
                if (_Queue.Dequeue(out item))
                    item();
            } while (item != null);
        }

        public bool WaitForFuture (IFuture future) {
            Action item = null;
            while (!future.Completed) {
                if (_Queue.Dequeue(out item))
                    item();
                else {
                    Thread.Sleep(0);
                    return false;
                }
            }

            return true;
        }

        public int Count {
            get {
                return _Queue.Count;
            }
        }

        public void Dispose () {
        }

        public void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);

            if (_WaiterCount > 0)
                _WaiterSignal.Set();
        }

        public bool WaitForWorkItems (double timeout) {
            if (_Queue.Count > 0) {
                return true;
            } else {
                Interlocked.Increment(ref _WaiterCount);

                if (timeout <= 0)
                    timeout = DefaultWaitTimeout;

                int timeoutMs = (int)Math.Ceiling(TimeSpan.FromSeconds(timeout).TotalMilliseconds);

                try {
                    if (_Queue.Count > 0)
                        return true;
                    else
                        return _WaiterSignal.WaitOne(timeoutMs, false);
                } finally {
                    Interlocked.Decrement(ref _WaiterCount);
                }
            }
        }
    }
}

#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Security;
using Squared.Util;

namespace Squared.Task {
    public class InfiniteStepException : Exception {
        public InfiniteStepException (long duration) 
            : base(String.Format("Stepped for {0:###.0} seconds without reaching the end of the job queue", TimeSpan.FromTicks(duration).TotalSeconds)) {
        }
    }

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

        public static Func<IJobQueue> ThreadSafe (TimeSpan maxStepDuration) {
            long stepDurationTicks = maxStepDuration.Ticks;
            return () => new ThreadSafeJobQueue(stepDurationTicks);
        }
    }

    public sealed class ThreadSafeJobQueue : IJobQueue {
        public const double DefaultWaitTimeout = 1.0;
        public readonly long MaxStepDuration = 0;

        private AutoResetEvent _WaiterSignal = new AutoResetEvent(false);
        private volatile int _WaiterCount = 0;

        private AtomicQueue<Action> _Queue = new AtomicQueue<Action>();

        public ThreadSafeJobQueue ()
            : this(0) {
        }

        public ThreadSafeJobQueue (long maxStepDuration) {
            MaxStepDuration = maxStepDuration;
        }

        public void Step () {
            long stepStarted = 0;
            if (MaxStepDuration > 0)
                stepStarted = Time.Ticks;

            int i = 0;
            Action item = null;
            do {
                if (_Queue.Dequeue(out item)) {
                    item();
                    i++;

                    if ((MaxStepDuration > 0) && ((i % 100) == 0)) {
                        if ((Time.Ticks - stepStarted) > MaxStepDuration)
                            throw new InfiniteStepException(MaxStepDuration);
                    }
                }
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

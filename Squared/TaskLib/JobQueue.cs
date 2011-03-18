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

    /// <summary>
    /// Invoked to notify the owner of the job queue that its maximum step duration has been exceeded.
    /// The default behavior when no handler has been provided for a thread safe job queue is to throw (because by default there is no maximum step duration for a thread safe job queue).
    /// The default behavior when no handler has been provided for a windows message job queue is to abort the step.
    /// </summary>
    /// <returns>Returns true to continue the step operation. Returns false to abort the step.</returns>
    public delegate bool MaxStepDurationExceededHandler (long elapsedTicks);

    public sealed class ThreadSafeJobQueue : IJobQueue {
        public const int StepDurationCheckInterval = 1000;
        public static readonly long? DefaultMaxStepDuration = null;

        public readonly long? MaxStepDuration;
        public event MaxStepDurationExceededHandler MaxStepDurationExceeded;

        public const double DefaultWaitTimeout = 1.0;

        private bool _Disposed = false;

        private AutoResetEvent _WaiterSignal = new AutoResetEvent(false);
        private int _WaiterCount = 0;

        private AtomicQueue<Action> _Queue = new AtomicQueue<Action>();

        public ThreadSafeJobQueue ()
            : this(DefaultMaxStepDuration) {
        }

        public ThreadSafeJobQueue (long? maxStepDuration) {
            MaxStepDuration = maxStepDuration;
        }

        public void Step () {
            long stepStarted = 0;
            if (MaxStepDuration.HasValue)
                stepStarted = Time.Ticks;

            int i = 0;
            Action item;
            do {
                if (_Queue.Dequeue(out item)) {
                    item();
                    i++;

                    if ((MaxStepDuration.HasValue) && ((i % StepDurationCheckInterval) == 0)) {
                        var elapsedTicks = (Time.Ticks - stepStarted);
                        if (elapsedTicks > MaxStepDuration.Value)
                            if (!OnMaxStepDurationExceeded(elapsedTicks))
                                return;
                    }
                }
            } while (item != null);
        }

        private bool OnMaxStepDurationExceeded (long elapsedTicks) {
            if (MaxStepDurationExceeded != null)
                return MaxStepDurationExceeded(elapsedTicks);
            else
                throw new InfiniteStepException(elapsedTicks);
        }

        public bool WaitForFuture (IFuture future) {
            Action item;
            while (!future.Completed) {
                if (_Disposed)
                    throw new ObjectDisposedException("ThreadSafeJobQueue");

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

        public bool IsDisposed {
            get {
                return _Disposed;
            }
        }

        public void Dispose () {
            _Disposed = true;
            Thread.MemoryBarrier();
            _WaiterSignal.Set();
        }

        public void QueueWorkItem (Action item) {
            if (_Disposed)
                throw new ObjectDisposedException("ThreadSafeJobQueue");

            _Queue.Enqueue(item);

            Thread.MemoryBarrier();
            if (_WaiterCount > 0) {
                Thread.MemoryBarrier();
                _WaiterSignal.Set();
            }
        }

        public bool WaitForWorkItems (double timeout) {
            if (_Disposed)
                return false;

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
                    else {
#if XBOX
                        var result = _WaiterSignal.WaitOne(timeoutMs);
#else
                        var result =  _WaiterSignal.WaitOne(timeoutMs, false);
#endif
                        if (_Disposed)
                            return false;
                        else
                            return result;
                    }
                } finally {
                    Interlocked.Decrement(ref _WaiterCount);
                }
            }
        }
    }
}

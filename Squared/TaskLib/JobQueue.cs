using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Security;
using Squared.Util;
using System.Collections.Concurrent;
using Squared.Threading;
using Squared.Threading.CoreCLR;

namespace Squared.Task {
    public class InfiniteStepException : Exception {
        public InfiniteStepException (long duration) 
            : base(String.Format("Stepped for {0:###.0} seconds without reaching the end of the job queue", TimeSpan.FromTicks(duration).TotalSeconds)) {
        }
    }

    public interface IJobQueue : IDisposable, IWorkItemQueueTarget {
        /// <summary>Adds a work item to the end of the job queue for the next step.</summary>
        void QueueWorkItemForNextStep (Action item);
        /// <summary>Adds a work item to the end of the job queue for the next step.</summary>
        void QueueWorkItemForNextStep (WorkItemQueueEntry entry);
        /// <summary>Pumps the job queue, processing all the work items it contains.</summary>
        void Step ();
        /// <summary>Pumps the job queue until it is out of work items or the future is completed, whichever comes first.</summary>
        /// <param name="future">The future to wait for.</param>
        /// <returns>True if the future was completed before pumping stopped, false otherwise.</returns>
        bool WaitForFuture (IFuture future);
        /// <summary>Waits until a work item is added to the queue or the timeout elapses, whichever comes first.</summary>
        /// <param name="timeout">The amount of time to wait (in seconds)</param>
        /// <returns>True if a work item was added to the queue before the timeout, false otherwise.</returns>
        bool WaitForWorkItems (double timeout);

        bool IsEmpty { get; }
        bool NextStepIsEmpty { get; }

        /// <summary>
        /// If true, it is safe to call Step, WaitForFuture and WaitForWorkItems on the current thread.
        /// If false, you must not use those operations on this thread.
        /// </summary>
        bool CanPumpOnThisThread { get; }

        event UnhandledExceptionEventHandler UnhandledException;
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
        public event UnhandledExceptionEventHandler UnhandledException;

        public const double DefaultWaitTimeout = 1.0;

        private bool _Disposed = false;

        private readonly System.Threading.ManualResetEventSlim _WaiterSignal = new ManualResetEventSlim(false);
        private int _WaiterCount = 0;

        private readonly LowAllocConcurrentQueue<WorkItemQueueEntry> _Queue = new LowAllocConcurrentQueue<WorkItemQueueEntry>();
        private readonly LowAllocConcurrentQueue<WorkItemQueueEntry> _NextStepQueue = new LowAllocConcurrentQueue<WorkItemQueueEntry>();

        public ThreadGroup ThreadGroup;

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
            WorkItemQueueEntry item;
            do {
                if (_Queue.TryDequeue(out item)) {
                    try {
                        item.Invoke();
                    } catch (Exception exc) {
                        if (UnhandledException != null)
                            UnhandledException(this, new UnhandledExceptionEventArgs(exc, false));
                        else
                            throw;
                    }

                    i++;

                    if ((MaxStepDuration.HasValue) && ((i % StepDurationCheckInterval) == 0)) {
                        var elapsedTicks = (Time.Ticks - stepStarted);
                        if (elapsedTicks > MaxStepDuration.Value)
                            if (!OnMaxStepDurationExceeded(elapsedTicks))
                                return;
                    }
                }
            } while (item.Action != null);

            while (_NextStepQueue.TryDequeue(out item))
                _Queue.Enqueue(item);
        }

        private bool OnMaxStepDurationExceeded (long elapsedTicks) {
            if (MaxStepDurationExceeded != null)
                return MaxStepDurationExceeded(elapsedTicks);
            else
                throw new InfiniteStepException(elapsedTicks);
        }

        public bool WaitForFuture (IFuture future) {
            WorkItemQueueEntry item;
            while (!future.Completed) {
                if (_Disposed)
                    throw new ObjectDisposedException("ThreadSafeJobQueue");
                if (future.Disposed)
                    throw new FutureDisposedException(future);

                if (_Queue.TryDequeue(out item))
                    item.Invoke();
                else if (_NextStepQueue.TryDequeue(out item))
                    item.Invoke();
                else {
                    if (ThreadGroup?.StepMainThread() != true)
                        Thread.Sleep(0);
                    return false;
                }
            }

            return true;
        }

        public bool IsEmpty => _Queue.IsEmpty;
        public bool NextStepIsEmpty => _NextStepQueue.IsEmpty;

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

            _Queue.Enqueue(new WorkItemQueueEntry { Action = item });
            _WaiterSignal.Set();
        }

        public void QueueWorkItem (WorkItemQueueEntry entry) {
            if (_Disposed)
                throw new ObjectDisposedException("ThreadSafeJobQueue");

            _Queue.Enqueue(entry);
            _WaiterSignal.Set();
        }

        public void QueueWorkItemForNextStep (Action item) {
            if (_Disposed)
                throw new ObjectDisposedException("ThreadSafeJobQueue");

            _NextStepQueue.Enqueue(new WorkItemQueueEntry { Action = item });
            _WaiterSignal.Set();
        }

        public void QueueWorkItemForNextStep (WorkItemQueueEntry entry) {
            if (_Disposed)
                throw new ObjectDisposedException("ThreadSafeJobQueue");

            _NextStepQueue.Enqueue(entry);
            _WaiterSignal.Set();
        }

        public bool CanPumpOnThisThread {
            get {
                return true;
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

                int timeoutMs = (int)Math.Ceiling(timeout * 1000);

                try {
                    if (_Queue.Count > 0)
                        return true;
                    else {
                        var result = _WaiterSignal.Wait(timeoutMs);

                        if (_Disposed) {
                            return false;
                        } else {
                            _WaiterSignal.Reset();
                            return result;
                        }
                    }
                } finally {
                    Interlocked.Decrement(ref _WaiterCount);
                }
            }
        }
    }
}

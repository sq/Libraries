using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    /// <summary>
    /// Responds to one or more work items being processed by the current thread.
    /// </summary>
    public delegate void WorkQueueDrainListener (int itemsRunByThisThread, bool moreWorkRemains);

    public interface IWorkItemQueueTarget {
        /// <summary>Adds a work item to the end of the job queue for the current step.</summary>
        void QueueWorkItem (Action item);
    }

    public static class WorkItemQueueTarget {
        private static IWorkItemQueueTarget Default = null;

        public static IWorkItemQueueTarget Current {
            get {
                var result = (IWorkItemQueueTarget)CallContext.LogicalGetData("TaskScheduler");
                return result ?? Default;
            }
            set {
                CallContext.LogicalSetData("WorkItemQueueTarget", value);
            }
        }

        public static void SetDefaultIfNone (IWorkItemQueueTarget @default) {
            Interlocked.CompareExchange(ref Default, @default, null);
        }
    }

    public interface IWorkQueue {
        /// <param name="exhausted">Is set to true if the Step operation caused the queue to become empty.</param>
        /// <returns>The number of work items handled.</returns>
        int Step (out bool exhausted, int? maximumCount = null);
        /// <summary>
        /// Registers a listener to be invoked any time a group of work items is processed by a thread.
        /// </summary>
        void RegisterDrainListener (WorkQueueDrainListener listener);
    }

    // This job must be run on the main thread
    public interface IMainThreadWorkItem : IWorkItem {
    }

    public interface IWorkItem {
        void Execute ();
    }

    public delegate void OnWorkItemComplete<T> (ref T item)
        where T : IWorkItem;

    internal struct InternalWorkItem<T>
        where T : IWorkItem
    {
        public WorkQueue<T>          Queue;
        public OnWorkItemComplete<T> OnComplete;
        public T                     Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal InternalWorkItem (WorkQueue<T> queue, ref T data, OnWorkItemComplete<T> onComplete) {
            Queue = queue;
            Data = data;
            OnComplete = onComplete;
        }
    }

    public class WorkQueue<T> : IWorkQueue
        where T : IWorkItem 
    {
        public static class Configuration {
            public static int? MaxStepCount = null;

            // For debugging
            public static bool BlockEnqueuesWhileDraining = false;

            /// <summary>
            /// Configures the number of steps taken each time this queue is visited by a worker thread.
            /// Low values increase the overhead of individual work items.
            /// High values reduce the overhead of work items but increase the odds that all worker threads can get bogged down
            ///  by a single queue.
            /// </summary>
            public static int DefaultStepCount = 64;
        }

        // For debugging
        internal bool IsMainThreadQueue = false;

        private volatile int HasAnyListeners = 0;
        private readonly List<WorkQueueDrainListener> DrainListeners = new List<WorkQueueDrainListener>();
        private readonly ReaderWriterLockSlim QueueLock = new ReaderWriterLockSlim();
        private readonly Queue<InternalWorkItem<T>> Queue = new Queue<InternalWorkItem<T>>();
        private int InFlightTasks = 0;

        private ExceptionDispatchInfo UnhandledException;

        private readonly ManualResetEvent DrainComplete = new ManualResetEvent(false);
        private volatile int NumWaitingForDrain = 0;

        private SignalFuture _DrainCompleteFuture = null;

        private readonly bool IsMainThreadWorkItem;

        public WorkQueue () {
            IsMainThreadWorkItem = typeof(IMainThreadWorkItem).IsAssignableFrom(typeof(T));
        }

        public void RegisterDrainListener (WorkQueueDrainListener listener) {
            if (listener == null)
                throw new ArgumentNullException("listener");

            // FIXME: This will deadlock if we are currently draining... don't do that

            HasAnyListeners = 1;
            lock (DrainListeners)
                DrainListeners.Add(listener);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ManageDrain () {
            if (Configuration.BlockEnqueuesWhileDraining && (NumWaitingForDrain > 0))
                throw new Exception("Draining");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete = null) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            try {
                QueueLock.EnterWriteLock();
                ManageDrain();
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            } finally {
                QueueLock.ExitWriteLock();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            try {
                QueueLock.EnterWriteLock();
                ManageDrain();
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            } finally {
                QueueLock.ExitWriteLock();
            }
        }

        public void EnqueueMany (ArraySegment<T> data) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            try {
                QueueLock.EnterWriteLock();
                ManageDrain();

                var wi = new InternalWorkItem<T>();
                wi.Queue = this;
                wi.OnComplete = null;
                for (var i = 0; i < data.Count; i++) {
                    wi.Data = data.Array[data.Offset + i];
                    Queue.Enqueue(wi);
                }
            } finally {
                QueueLock.ExitWriteLock();
            }
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            int result = 0, count = 0;
            InternalWorkItem<T> item = default(InternalWorkItem<T>);
            int actualMaximumCount = Math.Min(
                maximumCount ?? Configuration.DefaultStepCount, 
                Configuration.MaxStepCount ?? Configuration.DefaultStepCount
            );

            bool running = true, signalDrained = false, inReadLock = false;
            do {
                bool isInFlight = false, inWriteLock = false;

                try {
                    // FIXME: Find a way to not acquire this every step
                    if (!inReadLock) {
                        QueueLock.EnterUpgradeableReadLock();
                        inReadLock = true;
                    }

                    running = ((count = Queue.Count) > 0) &&
                        (result < actualMaximumCount);

                    if (running) {
                        isInFlight = true;
                        InFlightTasks++;

                        QueueLock.EnterWriteLock();
                        inWriteLock = true;

                        item = Queue.Dequeue();

                        QueueLock.ExitWriteLock();
                        inWriteLock = false;
                    }

                    if (inReadLock) {
                        QueueLock.ExitUpgradeableReadLock();
                        inReadLock = false;
                    }

                    if (running) {
                        item.Data.Execute();
                        if (item.OnComplete != null)
                            item.OnComplete(ref item.Data);

                        result++;
                    }
                } catch (Exception exc) {
                    UnhandledException = ExceptionDispatchInfo.Capture(exc);
                    signalDrained = true;
                    break;
                } finally {
                    if (inWriteLock)
                        QueueLock.ExitWriteLock();

                    if (running) {
                        if (!inReadLock) {
                            QueueLock.EnterUpgradeableReadLock();
                            inReadLock = true;
                        }

                        if (isInFlight)
                            InFlightTasks--;

                        if ((Queue.Count == 0) && (InFlightTasks <= 0))
                            signalDrained = true;

                        if (inReadLock) {
                            QueueLock.ExitUpgradeableReadLock();
                            inReadLock = false;
                        }
                    }
                }
            } while (running);

            if (!inReadLock) {
                QueueLock.EnterUpgradeableReadLock();
                inReadLock = true;
            }

            exhausted = Queue.Count == 0;

            if (inReadLock)
                QueueLock.ExitUpgradeableReadLock();

            if (signalDrained)
                SignalDrained();

            if ((result > 0) && (HasAnyListeners != 0)) {
                lock (DrainListeners)
                    foreach (var listener in DrainListeners)
                        listener(result, !exhausted);
            }

            return result;
        }

        private void SignalDrained () {
            var f = Interlocked.Exchange(ref _DrainCompleteFuture, null);
            DrainComplete.Set();
            if (f != null)
                f.SetResult2(NoneType.None, UnhandledException);
        }

        public void AssertEmpty () {
            bool isEmpty = true;

            if (InFlightTasks > 0)
                isEmpty = false;

            QueueLock.EnterReadLock();
            if (Queue.Count > 0)
                isEmpty = false;

            if (InFlightTasks > 0)
                isEmpty = false;
            QueueLock.ExitReadLock();

            if (!isEmpty)
                throw new Exception("Queue is not fully drained");
        }

        public SignalFuture DrainedSignal {
            get {
                QueueLock.EnterReadLock();
                if (Queue.Count == 0) {
                    QueueLock.ExitReadLock();
                    return SignalFuture.Signaled;
                } else
                    QueueLock.ExitReadLock();

                var f = _DrainCompleteFuture;
                if (f == null) {
                    f = new SignalFuture(false);
                    var originalValue = Interlocked.CompareExchange(ref _DrainCompleteFuture, f, null);
                    if (originalValue != null)
                        f = originalValue;
                }
                return f;
            }
        }

        public async Task WaitUntilDrainedAsync (int timeoutMs = -1) {
            var resultCount = Interlocked.Increment(ref NumWaitingForDrain);

            bool doWait = false;
            QueueLock.EnterReadLock();
            doWait = (Queue.Count > 0) || (InFlightTasks > 0);
            QueueLock.ExitReadLock();

            bool waitSuccessful;
            if (doWait) {
                var fWait = new Future<bool>();
                ThreadPool.RegisterWaitForSingleObject(
                    DrainComplete, (_, timedOut) => { fWait.SetResult(!timedOut, null); }, 
                    null, timeoutMs, true
                );
                waitSuccessful = await fWait;
            } else
                waitSuccessful = true;

            var result = Interlocked.Decrement(ref NumWaitingForDrain);
            if ((result == 0) && waitSuccessful)
                DrainComplete.Reset();

            var uhe = Interlocked.Exchange(ref UnhandledException, null);
            if (uhe != null)
                uhe.Throw();
        }

        public void WaitUntilDrained (int timeoutMs = -1) {
            var resultCount = Interlocked.Increment(ref NumWaitingForDrain);

            bool doWait = false;
            QueueLock.EnterReadLock();
            doWait = (Queue.Count > 0) || (InFlightTasks > 0);
            QueueLock.ExitReadLock();

            bool waitSuccessful;
            if (doWait)
                waitSuccessful = DrainComplete.WaitOne(timeoutMs);
            else
                waitSuccessful = true;

            var result = Interlocked.Decrement(ref NumWaitingForDrain);
            if ((result == 0) && waitSuccessful)
                DrainComplete.Reset();

            var uhe = Interlocked.Exchange(ref UnhandledException, null);
            if (uhe != null)
                uhe.Throw();
        }
    }
}

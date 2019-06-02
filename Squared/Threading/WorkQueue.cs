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

namespace Squared.Threading {
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

            lock (Queue) {
                ManageDrain();
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            lock (Queue) {
                ManageDrain();
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            }
        }

        public void EnqueueMany (ArraySegment<T> data) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            lock (Queue) {
                ManageDrain();

                var wi = new InternalWorkItem<T>();
                wi.Queue = this;
                wi.OnComplete = null;
                for (var i = 0; i < data.Count; i++) {
                    wi.Data = data.Array[data.Offset + i];
                    Queue.Enqueue(wi);
                }
            }
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            int result = 0, count = 0;
            InternalWorkItem<T> item = default(InternalWorkItem<T>);
            int actualMaximumCount = Math.Min(
                maximumCount ?? Configuration.DefaultStepCount, 
                Configuration.MaxStepCount ?? Configuration.DefaultStepCount
            );

            bool running = true;
            do {
                var isInFlight = false;

                try {
                    lock (Queue) {
                        running = ((count = Queue.Count) > 0) &&
                            (result < actualMaximumCount);

                        if (running) {
                            isInFlight = true;
                            InFlightTasks++;

                            item = Queue.Dequeue();
                        }
                    }

                    if (running) {
                        item.Data.Execute();
                        if (item.OnComplete != null)
                            item.OnComplete(ref item.Data);

                        result++;
                    }
                } catch (Exception exc) {
                    UnhandledException = ExceptionDispatchInfo.Capture(exc);
                    SignalDrained();
                    break;
                } finally {
                    if (running) {
                        lock (Queue) {
                            if (isInFlight)
                                InFlightTasks--;

                            if ((Queue.Count == 0) && (InFlightTasks <= 0))
                                SignalDrained();
                        }
                    }
                }
            } while (running);

            lock (Queue)
                exhausted = Queue.Count == 0;

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

            lock (Queue) {
                if (Queue.Count > 0)
                    isEmpty = false;

                if (InFlightTasks > 0)
                    isEmpty = false;
            }

            if (!isEmpty)
                throw new Exception("Queue is not fully drained");
        }

        public SignalFuture DrainedSignal {
            get {
                lock (Queue)
                    if (Queue.Count == 0)
                        return SignalFuture.Signaled;

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

        public void WaitUntilDrained (int timeoutMs = -1) {
            var resultCount = Interlocked.Increment(ref NumWaitingForDrain);

            bool doWait = false;
            lock (Queue)
                doWait = (Queue.Count > 0) || (InFlightTasks > 0);

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

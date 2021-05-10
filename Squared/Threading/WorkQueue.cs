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
        bool IsEmpty { get; }
        void AssertEmpty ();
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

        internal class SubQueue {
            public readonly WorkQueue<T> Owner;
            public volatile int NumWaitingForDrain = 0;
            public readonly AutoResetEvent DrainedSignal = new AutoResetEvent(false);
            public readonly object ItemsLock = new object();
            public readonly Queue<InternalWorkItem<T>> Items = new Queue<InternalWorkItem<T>>();

            public SubQueue (WorkQueue<T> owner) {
                Owner = owner;
            }

            public void NotifyDrained () {
                // FIXME: Should we do this first? Assumption is that in a very bad case, the future's
                //  complete handler might begin waiting
                DrainedSignal.Set();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AssertCanEnqueue () {
                if (Configuration.BlockEnqueuesWhileDraining && (NumWaitingForDrain > 0))
                    throw new Exception("Draining");
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add (ref InternalWorkItem<T> item) {
                AssertCanEnqueue();
                lock (ItemsLock)
                    Items.Enqueue(item);
            }
        }

        // For debugging
        internal bool IsMainThreadQueue = false;

        private volatile int HasAnyListeners = 0;
        private volatile int InFlightTasks = 0;
        private readonly List<WorkQueueDrainListener> DrainListeners = new List<WorkQueueDrainListener>();
        private SubQueue CurrentSubQueue;

        private ExceptionDispatchInfo UnhandledException;
        private readonly bool IsMainThreadWorkItem;

        public readonly ThreadGroup Owner;

        public WorkQueue (ThreadGroup owner) {
            Owner = owner;
            CurrentSubQueue = new SubQueue(this);
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

        private void NotifyChanged () {
            Owner.ConsiderNewThread(false);
            Owner.WakeAllThreads();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete = null, bool notifyChanged = true) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            var sq = CurrentSubQueue;
            var wi = new InternalWorkItem<T>(this, ref data, onComplete);
            sq.Add(ref wi);
            if (notifyChanged)
                NotifyChanged();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null, bool notifyChanged = true) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            var sq = CurrentSubQueue;
            var wi = new InternalWorkItem<T>(this, ref data, onComplete);
            sq.Add(ref wi);
            if (notifyChanged)
                NotifyChanged();
        }

        public void EnqueueMany (ArraySegment<T> data, bool notifyChanged = true) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            var sq = CurrentSubQueue;
            lock (sq.ItemsLock) {
                sq.AssertCanEnqueue();

                var wi = new InternalWorkItem<T>();
                wi.Queue = this;
                wi.OnComplete = null;
                for (var i = 0; i < data.Count; i++) {
                    wi.Data = data.Array[data.Offset + i];
                    sq.Items.Enqueue(wi);
                }
            }

            if (notifyChanged)
                NotifyChanged();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubQueue GetNextQueue () {
            return CurrentSubQueue;
        }

        private void StepQueue (SubQueue sq, ref int result, out bool exhausted, int actualMaximumCount) {
            InternalWorkItem<T> item = default(InternalWorkItem<T>);
            int count = 0, numProcessed = 0;
            bool running = true, inLock = false, signalDrained = false;

            do {
                bool isInFlight = false;

                try {
                    // FIXME: Find a way to not acquire this every step
                    if (!inLock) {
                        Monitor.Enter(sq.ItemsLock);
                        inLock = true;
                    }

                    running = ((count = sq.Items.Count) > 0) &&
                        (actualMaximumCount > 0);

                    if (running) {
                        isInFlight = true;
                        Interlocked.Increment(ref InFlightTasks);

                        item = sq.Items.Dequeue();
                        if (sq.Items.Count == 0)
                            signalDrained = true;
                    } else if (count == 0) {
                        signalDrained = true;
                    }

                    if (inLock) {
                        Monitor.Exit(sq.ItemsLock);
                        inLock = false;
                    }

                    try {
                        if (running) {
                            numProcessed++;
                            item.Data.Execute();
                            if (item.OnComplete != null)
                                item.OnComplete(ref item.Data);

                            result++;
                        }
                    } finally {
                        if (isInFlight)
                            Interlocked.Decrement(ref InFlightTasks);
                    }
                } catch (Exception exc) {
                    UnhandledException = ExceptionDispatchInfo.Capture(exc);
                    signalDrained = true;
                    break;
                } finally {
                    exhausted = sq.Items.Count <= 0;
                    if (inLock) {
                        Monitor.Exit(sq.ItemsLock);
                        inLock = false;
                    }
                }
            } while (running);

            actualMaximumCount -= numProcessed;

            if (inLock)
                Monitor.Exit(sq.ItemsLock);

            if (signalDrained)
                sq.NotifyDrained();
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            int result = 0;
            int actualMaximumCount = Math.Min(
                maximumCount ?? Configuration.DefaultStepCount, 
                Configuration.MaxStepCount ?? Configuration.DefaultStepCount
            );
            exhausted = true;

            var sq = GetNextQueue();
            if (sq != null)
                StepQueue(sq, ref result, out exhausted, actualMaximumCount);

            if ((result > 0) && (HasAnyListeners != 0)) {
                lock (DrainListeners)
                    foreach (var listener in DrainListeners)
                        listener(result, !exhausted);
            }

            return result;
        }

        public bool IsEmpty {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var q = CurrentSubQueue;
                lock (q.ItemsLock)
                    return (q.Items.Count == 0) && (InFlightTasks <= 0);
            }
        }

        public void AssertEmpty () {
            if (IsEmpty)
                return;
#if DEBUG
            throw new Exception("Queue is not fully drained");
#else
            Console.Error.WriteLine("WorkQueue of type {0} was not fully drained", typeof(T).FullName);
#endif
        }

        public bool WaitUntilDrained (int timeoutMs = -1) {
            if (IsEmpty)
                return true;

            var sq = CurrentSubQueue;
            var resultCount = Interlocked.Increment(ref sq.NumWaitingForDrain);
            long endWhen =
                timeoutMs >= 0
                    ? Time.Ticks + (Time.MillisecondInTicks * timeoutMs)
                    : long.MaxValue - 1;

            bool doWait = false;
            try {
                do {
                    lock (sq.ItemsLock)
                        doWait = (sq.Items.Count > 0) || (InFlightTasks > 0);
                    if (doWait) {
                        var now = Time.Ticks;
                        if (now > endWhen)
                            break;
                        var maxWait = (timeoutMs <= 0)
                            ? timeoutMs
                            : (int)(Math.Max(0, endWhen - now) / Time.MillisecondInTicks);
                        NotifyChanged();
                        if (!sq.DrainedSignal.WaitOne(maxWait))
                            ;
                        ;
                    } else {
                        AssertEmpty();
                        return true;
                    }
                } while (true);
            } finally {
                Interlocked.Decrement(ref sq.NumWaitingForDrain);

                var uhe = Interlocked.Exchange(ref UnhandledException, null);
                if (uhe != null)
                    uhe.Throw();
            }

            return false;
        }
    }
}

// #define INSTRUMENT_FAST_PATH

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    public struct WorkItemQueueEntry {
        private static ThreadLocal<object[]> ScratchArray = 
            new ThreadLocal<object[]>(() => new object[1]);

        public Delegate Action;
        public object Arg1;

        public void Invoke () {
            if (Action is Action a)
                a();
            else if (Action is Action<object> ao)
                ao(Arg1);
            else if (Action is SendOrPostCallback sopc)
                sopc(Arg1);
            else {
                var sa = ScratchArray.Value;
                sa[0] = Arg1;
                // FIXME: Is this correct when re-entrant? It should be
                Action.DynamicInvoke(sa);
            }
        }
    }

    public interface IWorkItemQueueTarget {
        /// <summary>Adds a work item to the end of the job queue for the current step.</summary>
        void QueueWorkItem (Action item);
        /// <summary>Adds a work item to the end of the job queue for the current step.</summary>
        void QueueWorkItem (WorkItemQueueEntry entry);
    }

    public class WorkQueueException : Exception {
        public IWorkQueue Queue;

        public WorkQueueException (IWorkQueue queue, string message)
            : base (message) {
            Queue = queue;
        }
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
        void AssertEmpty ();
        bool IsEmpty { get; }
        int Priority { get; }
    }

    public class WorkItemConfiguration {
        /// <summary>
        /// Work items with higher priority values have their queues stepped before queues
        ///  with lower priority
        /// </summary>
        public int Priority = 0;

        public int? MaxStepCount = null;

        /// <summary>
        /// Configures the number of steps taken each time this queue is visited by a worker thread.
        /// Low values increase the overhead of individual work items.
        /// High values reduce the overhead of work items but increase the odds that all worker threads can get bogged down
        ///  by a single queue.
        /// </summary>
        public int DefaultStepCount = 64;

        public int StochasticNotifyInterval = 32;

        /// <summary>
        /// Configures the maximum number of items that can be processed at once. If a single work
        ///  item is likely to block a thread for a long period of time, you should make this value small.
        /// </summary>
        public int? MaxConcurrency;
        /// <summary>
        /// Configures the maximum number of items that can be processed at once. The total number of 
        ///  threads is reduced by this much to compute a limit.
        /// The default is 1 unless the thread group has been configured with a different default.
        /// </summary>
        public int? ConcurrencyPadding = null;
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
#if DEBUG
        public bool                  Valid;
#endif
        public WorkQueue<T>          Queue;
        public OnWorkItemComplete<T> OnComplete;
        public T                     Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal InternalWorkItem (WorkQueue<T> queue, ref T data, OnWorkItemComplete<T> onComplete) {
            Queue = queue;
            Data = data;
            OnComplete = onComplete;
#if DEBUG
            Valid = true;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Execute (ref InternalWorkItem<T> item) {
#if DEBUG
            if (!item.Valid)
                throw new WorkQueueException(item.Queue, "Invalid work item");
#endif
            item.Data.Execute();
            if (item.OnComplete != null)
                item.OnComplete(ref item.Data);
        }
    }

    public enum WorkQueueNotifyMode : int {
        Never = 0,
        Always = 1,
        Stochastically = 2
    }

    public class WorkQueue<T> : IWorkQueue
        where T : IWorkItem 
    {
        const int DefaultBufferSize = 512;

        // For debugging
        public static bool BlockEnqueuesWhileDraining = false;

        // For debugging
        internal bool IsMainThreadQueue = false;

        private volatile int ItemsQueued = 0, ItemsProcessed = 0;
        private volatile int HasAnyListeners = 0;
        private readonly List<WorkQueueDrainListener> DrainListeners = new List<WorkQueueDrainListener>();
        private volatile int NumWaitingForDrain = 0;
        private readonly ManualResetEventSlim DrainedSignal = new ManualResetEventSlim(false);

        private readonly object ItemsLock = new object();
        private InternalWorkItem<T>[] _Items;
        private int _Head, _Tail, _Count;
        private int _Semilock;

        private volatile int _NumProcessing = 0;
        private ExceptionDispatchInfo UnhandledException;
        private readonly bool IsMainThreadWorkItem;

        public readonly WorkItemConfiguration Configuration;
        public readonly ThreadGroup Owner;

        public int ItemsInFlight => _NumProcessing;

        public WorkQueue (ThreadGroup owner) {
            Owner = owner;
            IsMainThreadWorkItem = typeof(IMainThreadWorkItem).IsAssignableFrom(typeof(T));
            Configuration = (WorkItemConfiguration)typeof(T).GetProperty("Configuration", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null)
                ?? new WorkItemConfiguration();
            _Items = new InternalWorkItem<T>[DefaultBufferSize];
        }

        private void NotifyDrained () {
            // FIXME: Should we do this first? Assumption is that in a very bad case, the future's
            //  complete handler might begin waiting
            DrainedSignal.Set();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AssertCanEnqueue () {
            if (BlockEnqueuesWhileDraining && (NumWaitingForDrain > 0))
                throw new WorkQueueException(this, "Cannot enqueue items while the queue is draining");
        }

        const int Semilock_Open = 0;
        const int Semilock_Reading = 1;
        const int Semilock_Dequeuing = 2;
        const int Semilock_Adding = 3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AddInternal (ref InternalWorkItem<T> item) {
            AssertCanEnqueue();
            var result = Interlocked.Increment(ref ItemsQueued);
            lock (ItemsLock) {
                // Any existing read operations are irrelevant, we're inside the lock so we own the semilock state
                Volatile.Write(ref _Semilock, Semilock_Adding);
                EnsureCapacityLocked(_Count + 1);
                _Items[_Tail] = item;
                AdvanceLocked(ref _Tail);
                _Count++;
                Interlocked.CompareExchange(ref _Semilock, Semilock_Open, Semilock_Adding);
            }
            return result;
        }

#if INSTRUMENT_FAST_PATH
        private static int EarlyOutCount = 0;
        private static int SlowOutCount = 0;
#endif

        private bool TryDequeue (out InternalWorkItem<T> item, out bool empty) {
            // Attempt to transition the semilock into read mode, and as long as it wasn't in add mode,
            if (Interlocked.CompareExchange(ref _Semilock, Semilock_Reading, Semilock_Open) != 3) {
                // Determine whether we can early-out this dequeue operation because we successfully entered
                //  the semilock either during an enqueue or when it wasn't held at all
                // FIXME: Is it safe to do this check during a dequeue? I think so
                empty = Volatile.Read(ref _Count) <= 0;
                // We may have entered this block without acquiring the semilock in open mode, in which case
                //  whoever has it (in dequeue mode) will release it when they're done
                Interlocked.CompareExchange(ref _Semilock, Semilock_Open, Semilock_Reading);
                if (empty) {
                    item = default(InternalWorkItem<T>);
#if INSTRUMENT_FAST_PATH
                    Interlocked.Increment(ref EarlyOutCount);
#endif
                    return false;
                }
            }

            lock (ItemsLock) {
                // We've successfully acquired the state lock, so we own the semilock state now
                Volatile.Write(ref _Semilock, Semilock_Dequeuing);
                if (_Count <= 0) {
                    empty = true;
                    item = default(InternalWorkItem<T>);
#if INSTRUMENT_FAST_PATH
                    Interlocked.Increment(ref SlowOutCount);
#endif
                    Volatile.Write(ref _Semilock, Semilock_Open);
                    return false;
                }

                item = _Items[_Head];
                _Items[_Head] = default(InternalWorkItem<T>);
                AdvanceLocked(ref _Head);
                _Count--;
                empty = _Count <= 0;
                Volatile.Write(ref _Semilock, Semilock_Open);
                return true;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacityLocked (int capacity) {
            if (_Items.Length < capacity)
                GrowLocked(capacity);
        }

        private void GrowLocked (int capacity) {
            var newSize = UnorderedList<T>.PickGrowthSize(_Items.Length, capacity);
            var newItems = new InternalWorkItem<T>[newSize];
            if (_Count > 0) {
                if (_Head < _Tail) {
                    Array.Copy(_Items, _Head, newItems, 0, _Count);
                } else {
                    Array.Copy(_Items, _Head, newItems, 0, _Items.Length - _Head);
                    Array.Copy(_Items, 0, newItems, _Items.Length - _Head, _Tail);
                }
            }
            _Items = newItems;
            _Head = 0;
            _Tail = (_Count == capacity) ? 0 : _Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceLocked (ref int index) {
            var temp = index + 1;
            if (temp >= _Items.Length)
                temp = 0;
            index = temp;
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
        private void NotifyChanged () {
            Owner.NotifyQueuesChanged();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyChanged (WorkQueueNotifyMode mode, int newCount) {
            switch (mode) {
                case WorkQueueNotifyMode.Never:
                    return;
                default:
                case WorkQueueNotifyMode.Always:
                    Owner.NotifyQueuesChanged();
                    return;
                case WorkQueueNotifyMode.Stochastically:
                    if ((newCount % Configuration.StochasticNotifyInterval) == 0)
                        Owner.NotifyQueuesChanged();
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, bool notifyChanged) {
            Enqueue(ref data, null, notifyChanged ? WorkQueueNotifyMode.Always : WorkQueueNotifyMode.Never);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, bool notifyChanged) {
            Enqueue(ref data, null, notifyChanged ? WorkQueueNotifyMode.Always : WorkQueueNotifyMode.Never);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete, bool notifyChanged) {
            Enqueue(ref data, onComplete, notifyChanged ? WorkQueueNotifyMode.Always : WorkQueueNotifyMode.Never);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete, bool notifyChanged) {
            Enqueue(ref data, onComplete, notifyChanged ? WorkQueueNotifyMode.Always : WorkQueueNotifyMode.Never);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete = null, WorkQueueNotifyMode notifyChanged = WorkQueueNotifyMode.Always) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            var wi = new InternalWorkItem<T>(this, ref data, onComplete);
            var newCount = AddInternal(ref wi);
            NotifyChanged(notifyChanged, newCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null, WorkQueueNotifyMode notifyChanged = WorkQueueNotifyMode.Always) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            var wi = new InternalWorkItem<T>(this, ref data, onComplete);
            var newCount = AddInternal(ref wi);
            NotifyChanged(notifyChanged, newCount);
        }

        public void EnqueueMany (ArraySegment<T> data, bool notifyChanged = true) {
#if DEBUG
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");
#endif

            lock (ItemsLock) {
                AssertCanEnqueue();
                Interlocked.Add(ref ItemsQueued, data.Count);

                var wi = new InternalWorkItem<T>();
                wi.Queue = this;
                wi.OnComplete = null;
                for (var i = 0; i < data.Count; i++) {
                    wi.Data = data.Array[data.Offset + i];
                    AddInternal(ref wi);
                }
            }

            if (notifyChanged)
                NotifyChanged();
        }

        private void StepInternal (out int result, out bool exhausted, int actualMaximumCount) {
            // We eat an extra lock acquisition this way, but it skips a lot of extra work
            // FIXME: Optimize this out since in profiles it eats like 2% of CPU, probably not worth it anymore
            if (IsEmpty) {
                result = 0;
                exhausted = true;
                return;
            }

            InternalWorkItem<T> item = default(InternalWorkItem<T>);
            int numProcessed = 0;
            bool running = true, signalDrained = false;

            var padded = Owner.Count - (Configuration.ConcurrencyPadding ?? Owner.DefaultConcurrencyPadding);
            var lesser = Math.Min(Configuration.MaxConcurrency ?? 9999, padded);
            var maxConcurrency = Math.Max(lesser, 1);

            exhausted = false;
            result = 0;
            // TODO: Move this into the loop so we do it at the start of processing the first item?
            if (Interlocked.Increment(ref _NumProcessing) > maxConcurrency) {
                Interlocked.Decrement(ref _NumProcessing);
                return;
            }

            do {
                try {
                    bool empty = false;
                    running = (actualMaximumCount > 0) &&
                        (numProcessed < actualMaximumCount) &&
                        TryDequeue(out item, out empty);

                    if (empty) {
                        signalDrained = true;
                        exhausted = true;
                    }

                    if (running) {
                        try {
                            numProcessed++;
                            InternalWorkItem<T>.Execute(ref item);
                            result++;
                        } finally {
                            Interlocked.Increment(ref ItemsProcessed);
                        }
                    }
                } catch (Exception exc) {
                    UnhandledException = ExceptionDispatchInfo.Capture(exc);
                    signalDrained = true;
                    break;
                }
            } while (running);

            actualMaximumCount -= numProcessed;
            Interlocked.Decrement(ref _NumProcessing);

            if (signalDrained)
                NotifyDrained();
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            int actualMaximumCount = Math.Min(
                maximumCount ?? Configuration.DefaultStepCount, 
                Configuration.MaxStepCount ?? Configuration.DefaultStepCount
            );

            StepInternal(out int result, out exhausted, actualMaximumCount);

            if ((result > 0) && (HasAnyListeners != 0)) {
                lock (DrainListeners)
                    foreach (var listener in DrainListeners)
                        listener(result, !exhausted);
            }

            return result;
        }

        int IWorkQueue.Priority => Configuration.Priority;

        public bool IsEmpty {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                lock (ItemsLock)
                    return (_Count <= 0) && (ItemsProcessed >= ItemsQueued);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AssertEmpty () {
            if (IsEmpty)
                return;
#if DEBUG
            throw new WorkQueueException(this, "Queue is not fully drained");
#else
            Console.Error.WriteLine("WorkQueue of type {0} was not fully drained", typeof(T).FullName);
#endif
        }

        public bool WaitUntilDrained (int timeoutMs = -1) {
            if (IsEmpty)
                return true;

            var resultCount = Interlocked.Increment(ref NumWaitingForDrain);
            long endWhen =
                timeoutMs >= 0
                    ? Time.Ticks + (Time.MillisecondInTicks * timeoutMs)
                    : long.MaxValue - 1;

            bool doWait = false;
            var waterMark = ItemsQueued;
            try {
                do {
                    lock (ItemsLock)
                        doWait = (_Count > 0) || (ItemsProcessed < ItemsQueued);
                    if (doWait) {
                        var now = Time.Ticks;
                        if (now > endWhen)
                            break;
                        var maxWait = (timeoutMs <= 0)
                            ? timeoutMs
                            : (int)(Math.Max(0, endWhen - now) / Time.MillisecondInTicks);
                        NotifyChanged();
                        if (!DrainedSignal.Wait(maxWait))
                            ;
                        else
                            ;
                    } else {
#if DEBUG
                        if (!IsEmpty)
                            throw new WorkQueueException(this, "Queue is not empty");
                        Thread.Yield();
                        if (!IsEmpty)
                            throw new WorkQueueException(this, "Queue is not empty");
                        if (ItemsProcessed < waterMark)
                            throw new WorkQueueException(this, "AssertDrained returned before reaching watermark");
#endif
                        return (ItemsProcessed >= waterMark);
                    }
                } while (true);
            } finally {
                Interlocked.Decrement(ref NumWaitingForDrain);

                var uhe = Interlocked.Exchange(ref UnhandledException, null);
                if (uhe != null)
                    uhe.Throw();
            }

            return false;
        }
    }
}

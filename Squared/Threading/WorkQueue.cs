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
using Squared.Threading.CoreCLR;
using Squared.Util;

namespace Squared.Threading {
    /// <summary>
    /// Responds to one or more work items being processed by the current thread.
    /// </summary>
    public delegate void WorkQueueDrainListener (int itemsRunByThisThread, bool moreWorkRemains);

    public interface IDynamicInvokeHelper {
        void Invoke (object d, object arg1);
    }

    static class DynamicInvokeHelper {
        private static ThreadLocal<Dictionary<Type, IDynamicInvokeHelper>> Caches = 
            new ThreadLocal<Dictionary<Type, IDynamicInvokeHelper>>(() => new Dictionary<Type, IDynamicInvokeHelper>());

        public static IDynamicInvokeHelper Get (Type tDelegate) {
            var cache = Caches.Value;
            if (!cache.TryGetValue(tDelegate, out IDynamicInvokeHelper result)) {
                var mInvoke = tDelegate.GetMethod("Invoke");
                if (mInvoke == null)
                    throw new Exception($"Failed to bind {tDelegate}.Invoke");
                var p = mInvoke.GetParameters();
                if (p.Length != 1)
                    throw new Exception($"Expected {tDelegate}.Invoke to accept 1 parameter but it accepts {p.Length}");
                var tHelper = typeof(DynamicInvokeHelper<,>).MakeGenericType(tDelegate, p[0].ParameterType);
                result = (IDynamicInvokeHelper)Activator.CreateInstance(tHelper, mInvoke);
                cache[tDelegate] = result;
            }
            return result;
        }
    }

    sealed class DynamicInvokeHelper<TDelegate, TArg0> : IDynamicInvokeHelper {
        Action<TDelegate, TArg0> _Invoke;

        public DynamicInvokeHelper (MethodInfo mInvoke) {
            _Invoke = (Action<TDelegate, TArg0>)Delegate.CreateDelegate(typeof(Action<TDelegate, TArg0>), mInvoke);
        }

        public void Invoke (object d, object arg0) {
            _Invoke((TDelegate)d, (TArg0)arg0);
        }
    }

    public struct WorkItemQueueEntry {
        private static class TypeHelper<T> {
            public static IDynamicInvokeHelper Helper = DynamicInvokeHelper.Get(typeof(T));
        }

        public Delegate Action;
        public object Arg1;
        public IDynamicInvokeHelper InvokeHelper;

        public static WorkItemQueueEntry New<TDelegate, TArg1> (TDelegate action, TArg1 arg1)
            where TDelegate : class
        {
            return new WorkItemQueueEntry {
                Action = (Delegate)(object)action,
                Arg1 = arg1,
                InvokeHelper = TypeHelper<TDelegate>.Helper
            };
        }

        public void Invoke () {
            if (Action is Action a)
                a();
            else if (Action is Action<object> ao)
                ao(Arg1);
            else if (Action is SendOrPostCallback sopc)
                sopc(Arg1);
            else {
                var helper = InvokeHelper ?? DynamicInvokeHelper.Get(Action.GetType());
                helper.Invoke(Action, Arg1);
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
        bool IsDrained { get; }
        int Priority { get; }
    }

    internal static class WorkItemConfigurationForType<T>
        where T : IWorkItem
    {
        public static readonly WorkItemConfiguration Configuration;

        static WorkItemConfigurationForType () {
            Configuration = (WorkItemConfiguration)typeof(T).GetProperty("Configuration", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null)
                ?? new WorkItemConfiguration();
            if (typeof(T).GetField("Configuration", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) != null)
                throw new Exception($"{typeof(T).FullName}.Configuration must be a property not a field");
            if (typeof(IMainThreadWorkItem).IsAssignableFrom(typeof(T)))
                Configuration.AllowMainThread = Configuration.MainThreadOnly = true;
        }
    }

    public sealed class WorkItemConfiguration {
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

        /// <summary>
        /// If set, all work items will be queued to the main thread.
        /// </summary>
        public bool MainThreadOnly = false;
        /// <summary>
        /// If false, attempts to queue these items to the main thread will fail.
        /// </summary>
        public bool AllowMainThread = true;
    }

    // This job must be run on the main thread
    public interface IMainThreadWorkItem : IWorkItem {
    }

    public interface IWorkItem {
        void Execute (ThreadGroup group);
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
        public static void Execute (ThreadGroup owner, ref InternalWorkItem<T> item) {
#if DEBUG
            if (!item.Valid)
                throw new WorkQueueException(item.Queue, "Invalid work item");
#endif
            item.Data.Execute(owner);

            if (item.OnComplete != null)
                item.OnComplete(ref item.Data);
        }
    }

    public enum WorkQueueNotifyMode : int {
        Never = 0,
        Always = 1
    }

    public sealed class WorkQueue<T> : IWorkQueue
        where T : IWorkItem
    {
        // For debugging
        public static bool BlockEnqueuesWhileDraining = false;

        // For debugging
        internal bool IsMainThreadQueue = false;

        /// <summary>
        /// This counter increases the moment an item is queued, and decreases any time an item finishes
        ///  being processed (successfully or unsuccessfully).
        /// Contrast with _Count, which is the number of items waiting to be dequeued.
        /// If (_Count <= 0) && (ItemsWaitingForProcessing > 0) then one or more workers are currently
        ///  processing items but the queue contains no additional work.
        /// </summary>
        private volatile int ItemsWaitingForProcessing = 0;
        private volatile int HasAnyListeners = 0;
        private readonly List<WorkQueueDrainListener> DrainListeners = new List<WorkQueueDrainListener>();
        private volatile int NumWaitingForDrain = 0;
        private readonly ManualResetEventSlim FinishedProcessingSignal = new ManualResetEventSlim(false);
        private object SignalLock = new object();

        private LowAllocConcurrentQueue<InternalWorkItem<T>> _Items;

        private volatile int _NumProcessing = 0;
        private ExceptionDispatchInfo UnhandledException;
        private readonly bool IsMainThreadWorkItem;

        public readonly WorkItemConfiguration Configuration;
        public readonly ThreadGroup Owner;

        public int ItemsInFlight => _NumProcessing;

        public WorkQueue (ThreadGroup owner) {
            Owner = owner;
            Configuration = WorkItemConfigurationForType<T>.Configuration;
            _Items = new LowAllocConcurrentQueue<InternalWorkItem<T>>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AssertCanEnqueue () {
            if (IsMainThreadWorkItem && !IsMainThreadQueue)
                throw new InvalidOperationException("This work item must be queued on the main thread");

            if (BlockEnqueuesWhileDraining && (NumWaitingForDrain > 0))
                throw new WorkQueueException(this, "Cannot enqueue items while the queue is draining");
        }

        const int Semilock_Open = 0;
        const int Semilock_Reading = 1;
        const int Semilock_Dequeuing = 2;
        const int Semilock_Adding = 3;

        /// <summary>
        /// Caller is responsible for AssertCanEnqueue and updating ItemsWaitingForProcessing!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddInternal (ref InternalWorkItem<T> item) {
            _Items.Enqueue(ref item);
        }

#if INSTRUMENT_FAST_PATH
        private static int EarlyOutCount = 0;
        private static int SlowOutCount = 0;
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryDequeue (ref InternalWorkItem<T> item) {
            return _Items.TryDequeue(out item);
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
        private void NotifyChanged (bool wakeAll = true) {
            Owner.NotifyQueuesChanged(wakeAll);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NotifyChanged (WorkQueueNotifyMode mode, bool wakeAll = true) {
            switch (mode) {
                case WorkQueueNotifyMode.Never:
                    return;
                default:
                case WorkQueueNotifyMode.Always:
                    Owner.NotifyQueuesChanged(wakeAll);
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
            AssertCanEnqueue();
            if (Interlocked.Increment(ref ItemsWaitingForProcessing) <= 2)
                lock (SignalLock)
                    FinishedProcessingSignal.Reset();
            var wi = new InternalWorkItem<T>(this, ref data, onComplete);
            AddInternal(ref wi);
            NotifyChanged(notifyChanged, wakeAll: false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null, WorkQueueNotifyMode notifyChanged = WorkQueueNotifyMode.Always) {
            AssertCanEnqueue();
            if (Interlocked.Increment(ref ItemsWaitingForProcessing) <= 2)
                lock (SignalLock)
                    FinishedProcessingSignal.Reset();
            var wi = new InternalWorkItem<T>(this, ref data, onComplete);
            AddInternal(ref wi);
            NotifyChanged(notifyChanged, wakeAll: false);
        }

        public void EnqueueMany (ArraySegment<T> data, bool notifyChanged = true) {
            AssertCanEnqueue();
            lock (SignalLock) {
                Interlocked.Add(ref ItemsWaitingForProcessing, data.Count);
                FinishedProcessingSignal.Reset();
            }
            var wi = new InternalWorkItem<T>();
            wi.Queue = this;
            wi.OnComplete = null;

            for (var i = 0; i < data.Count; i++) {
                wi.Data = data.Array[data.Offset + i];
                AddInternal(ref wi);
            }

            if (notifyChanged)
                NotifyChanged();
        }

        private void StepInternal (out int result, out bool exhausted, int actualMaximumCount) {
            // This skips a lot of unnecessary work, though it is kind of a race condition
            if (!IsWorkPossiblyWaiting) {
                result = 0;
                exhausted = true;
                return;
            }

            InternalWorkItem<T> item = default(InternalWorkItem<T>);
            int numProcessed = 0;
            bool running = true, wasLast = false;

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
                    running = (actualMaximumCount > 0) &&
                        (numProcessed < actualMaximumCount);

                    if (running) {
                        var dequeued = TryDequeue(ref item);
                        if (!dequeued)
                            exhausted = true;
                        running = running && dequeued;
                    }

                    if (running) {
                        try {
                            numProcessed++;
                            InternalWorkItem<T>.Execute(Owner, ref item);
                            result++;
                        } finally {
                            wasLast = Interlocked.Decrement(ref ItemsWaitingForProcessing) <= 0;
                        }
                    }
                } catch (Exception exc) {
                    UnhandledException = ExceptionDispatchInfo.Capture(exc);
                    break;
                }
            } while (running);

            actualMaximumCount -= numProcessed;
            Interlocked.Decrement(ref _NumProcessing);
            // The following would be ideal but I think it could produce a hang in some cases
            /*
            // This is a race, but that's probably OK since anyone waiting for the queue to drain will verify and spin if
            //  we lose the race
            var signalDone = wasLast && (Volatile.Read(ref ItemsQueued) <= processedCounter);
            */

            if (wasLast) {
                lock (SignalLock)
                    if (_Items.IsEmpty)
                        FinishedProcessingSignal.Set();
            }
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            var config = Configuration;
            int actualMaximumCount = Math.Min(
                maximumCount ?? config.DefaultStepCount, 
                config.MaxStepCount ?? config.DefaultStepCount
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

        public bool IsDrained {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _Items.IsEmpty && (ItemsWaitingForProcessing <= 0);
            }
        }

        private bool IsWorkPossiblyWaiting {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ItemsWaitingForProcessing > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AssertEmpty () {
            if (IsDrained)
                return;
#if DEBUG
            throw new WorkQueueException(this, "Queue is not fully drained");
#else
            Console.Error.WriteLine("WorkQueue of type {0} was not fully drained", typeof(T).FullName);
#endif
        }

        public bool WaitUntilDrained (int timeoutMs = -1) {
            if (IsDrained)
                return true;

            var resultCount = Interlocked.Increment(ref NumWaitingForDrain);
            const int actualMaxWaitBecauseSomethingAboutTheseThreadingPrimitivesIsBroken = 1000;
            long endWhen =
                timeoutMs >= 0
                    ? Time.Ticks + (Time.MillisecondInTicks * timeoutMs)
                    : long.MaxValue - 1;

            bool doWait = false, hasWaited = false;
            try {
                do {
                    // Read these first and read the queued count last so that if we lose a race
                    //  we will lose it in the 'there's extra work' fashion instead of 'we're done
                    //  but not really' fashion
                    doWait = (ItemsWaitingForProcessing > 0) || !_Items.IsEmpty;
                    var now = Time.Ticks;
                    if (doWait) {
                        if (now > endWhen)
                            break;

                        hasWaited = true;
                        var maxWait = (timeoutMs <= 0)
                            ? actualMaxWaitBecauseSomethingAboutTheseThreadingPrimitivesIsBroken
                            : (int)(Math.Max(0, endWhen - now) / Time.MillisecondInTicks);
                        NotifyChanged();

                        // Note that we may still spin after getting this signal because threading is hard
                        if (!FinishedProcessingSignal.Wait(maxWait)) {
                            if (ItemsWaitingForProcessing <= 0)
                                System.Diagnostics.Debug.WriteLine($"WARNING: FinishedProcessingSignal wait timed out even though work is done for {typeof(T)}");
                        }
                    } else {
                        // FIXME: This is a race condition!
#if DEBUG
                        if (ItemsWaitingForProcessing > 0)
                            Thread.Yield();
                        if ((ItemsWaitingForProcessing > 0) && !hasWaited)
                            throw new WorkQueueException(this, "AssertDrained returned without waiting even though work was left");
                        if ((ItemsWaitingForProcessing > 0) && (now < endWhen))
                            continue;
#endif
                        return (ItemsWaitingForProcessing <= 0);
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

        public override string ToString () {
            var result = $"WorkQueue<{typeof(T).Name}> {ItemsWaitingForProcessing} waiting {ItemsInFlight} in-flight";
#if INSTRUMENT_FAST_PATH
            result += $"early out x{EarlyOutCount / (double)(EarlyOutCount + SlowOutCount)}";
#endif
            return result;
        }
    }
}

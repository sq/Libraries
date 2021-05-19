using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;
using System.Linq;

namespace Squared.Threading {
    public class ThreadGroup : IDisposable {
        internal class PriorityOrderedQueueList {
            internal class ListForPriority : UnorderedList<IWorkQueue> {
                public readonly int Priority;

                public ListForPriority (int priority)
                    : base () {
                    Priority = priority;
                }

                public IWorkQueue this [int index] => DangerousGetItem(index);
            }

            internal Dictionary<int, ListForPriority> ListsByPriority =
                new Dictionary<int, ListForPriority>();
            internal UnorderedList<IWorkQueue[]> Items = 
                new UnorderedList<IWorkQueue[]>();

            public PriorityOrderedQueueList () {
            }

            public void FillFrom (Dictionary<Type, IWorkQueue> queues) {
                Clear();
                foreach (var q in queues.Values)
                    Add(q, false);
                UpdateItems();
            }

            public void UpdateItems () {
                var items = (from kvp in ListsByPriority orderby kvp.Key descending select kvp.Value);
                Items.Clear();
                foreach (var l in items) {
                    var buf = new IWorkQueue[l.Count];
                    l.CopyTo(buf, 0, l.Count);
                    Items.Add(buf);
                }
            }

            public void Add (IWorkQueue queue, bool autoUpdate) {
                if (!ListsByPriority.TryGetValue(queue.Priority, out ListForPriority lfp))
                    ListsByPriority[queue.Priority] = lfp = new ListForPriority(queue.Priority);
                lfp.Add(queue);

                if (autoUpdate)
                    UpdateItems();
            }

            public void Clear () {
                foreach (var v in ListsByPriority.Values)
                    v.Clear();

                Items.Clear();
            }
        }

        /// <summary>
        /// By default, the number of threads actively processing a given work item type will be
        ///  limited to the number of threads minus this amount. This ensures that a given work item
        ///  will not saturate all threads and prevent other work from being processed.
        /// In some cases you want to be able to saturate threads with one work item type, so you can
        ///  change the default instead of setting it per-work-item by changing this value.
        /// </summary>
        public int DefaultConcurrencyPadding = 1;

        /// <summary>
        /// Thread groups will create no more than this many threads, no matter how many processors
        ///  the machine has and no matter how many threads were requested. You must set this
        ///  before creating any thread groups.
        /// This threshold is placed in effect because having a large number of threads contending
        ///  to pull items off of work queues creates enough overhead to reduce the overall efficiency
        ///  of the worker threads (and make queueing new work items more expensive).
        /// </summary>
        public static int MaximumThreadCount = 12;

        /// <summary>
        /// If set to a value above 0, the amount of time spent stepping on the main thread
        ///  in one invocation will be limited to this duration.
        /// Note that under normal circumstances, the main thread will be stepped twice (BeforeDraw and EndDraw)
        /// </summary>
        public float?   MainThreadStepLengthLimitMs = null;

        public readonly ITimeProvider TimeProvider;
        public readonly bool CreateBackgroundThreads;
        public readonly int ThreadCount;
        public readonly ApartmentState COMThreadingModel;

        public bool IsDisposed { get; private set; }
        
        // A lock-free dictionary for looking up queues by work item type
        private readonly Dictionary<Type, IWorkQueue> Queues = 
            new Dictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IWorkQueue> MainThreadQueues = 
            new Dictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());
        private readonly UnorderedList<IWorkQueue> MainThreadQueueList = 
            new UnorderedList<IWorkQueue>();

        // This list is immutable and will be swapped out using CompareExchange
        //  when the set of queues changes, so that workers don't need to lock it
        internal volatile PriorityOrderedQueueList QueuesForWorkers = 
            new PriorityOrderedQueueList();

        public readonly GroupThread[] Threads;

        public string Name;

        /// <param name="threadCount">The desired number of threads to create. If not specified, the default is based on the number of processors available.</param>
        /// <param name="createBackgroundThreads">Whether to create background threads for processing work. Non-background threads will block the program from exiting until their work is complete.</param>
        /// <param name="timeProvider">The time provider used to measure elapsed times during work item processing.</param>
        /// <param name="comThreadingModel">If you don't already know what this is, you probably don't care about it.</param>
        /// <param name="name">A name used to identify the threads owned by this group for debugging purposes.</param>
        public ThreadGroup (
            int? threadCount = null,
            bool createBackgroundThreads = false,
            ITimeProvider timeProvider = null,
            ApartmentState comThreadingModel = ApartmentState.Unknown,
            string name = null
        ) {
            Name = name;
            ThreadCount = Math.Min(threadCount.GetValueOrDefault(Environment.ProcessorCount), MaximumThreadCount);
            CreateBackgroundThreads = createBackgroundThreads;
            TimeProvider = timeProvider ?? Time.DefaultTimeProvider;
            COMThreadingModel = comThreadingModel;

            Threads = new GroupThread[ThreadCount];
            for (int i = 0; i < Threads.Length; i++)
                Threads[i] = new GroupThread(this, i);
        }

        private void NewQueueCreated () {
            bool waitLonger = false;
            while (true) {
                var prev = Volatile.Read(ref QueuesForWorkers);
                var result = new PriorityOrderedQueueList();
                lock (Queues)
                    result.FillFrom(Queues);
                // Prevent a race condition where somehow two queues are created rapidly and
                //  one of the NewQueueCreated calls loses the race and overwrites the longer list
                //  with a shorter one
                if (Interlocked.CompareExchange(ref QueuesForWorkers, result, prev) != prev) {
                    if (waitLonger)
                        Thread.Sleep(1);
                    else {
                        Thread.Yield();
                        waitLonger = true;
                    }
                    continue;
                } else {
                    break; 
                }
            }
        }

        /// <summary>
        /// Call this method to advance all the main thread work item queues.
        /// This is necessary to ensure main-thread-only jobs advance.
        /// </summary>
        /// <returns>Whether all queues have been exhausted.</returns>
        public bool StepMainThread (float? currentElapsedMs = null) {
            bool allExhausted = true;
            int totalSteps = 0;
            float stepLengthLimitMs = (MainThreadStepLengthLimitMs ?? 999999) - (currentElapsedMs ?? 0);

            // FIXME: This will deadlock if you create a new queue while it's stepping the main thread
            var started = Time.Ticks;
            lock (MainThreadQueueList)
            foreach (var q in MainThreadQueueList) {
                bool exhausted;
                // We want to run one queue item at a time to try and drain all the main thread queues evenly
                totalSteps += q.Step(out exhausted, 1);
                if (!exhausted)
                    allExhausted = false;

                var elapsedTicks = (Time.Ticks - started) / Time.MillisecondInTicks;
                if (
                    elapsedTicks > stepLengthLimitMs
                )
                    return false;
            }

            return allExhausted;
        }

        public bool TryStepMainThreadUntilDrained () {
            float stepLengthLimitMs = MainThreadStepLengthLimitMs ?? 999999;
            var started = Time.Ticks;

            while (true) {
                var elapsedMs = (Time.Ticks - started) / Time.MillisecondInTicks;
                if (elapsedMs >= stepLengthLimitMs)
                    return false;

                bool ok = !StepMainThread(elapsedMs);
                if (!ok)
                    break;

                Thread.Yield();
            }

            return true;
        }

        public int Count {
            get {
                return Threads.Length;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue<T> (T item, OnWorkItemComplete<T> onComplete = null)
            where T : IWorkItem
        {
            var queue = GetQueueForType<T>();
            queue.Enqueue(ref item, onComplete);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue<T> (ref T item, OnWorkItemComplete<T> onComplete = null)
            where T : IWorkItem
        {
            var queue = GetQueueForType<T>();
            queue.Enqueue(ref item, onComplete);
        }

        /// <summary>
        /// You can use this to request a work queue for a given type of work item, then queue
        ///  multiple items cheaply. If you queue items directly, it's your responsibility to call
        ///  ThreadGroup.NotifyQueuesChanged to ensure that a sufficient number of threads are ready
        ///  to perform work.
        /// </summary>
        /// <param name="forMainThread">Pass true if you wish to queue a work item to run on the main thread. Will be set automatically for main-thread-only work items.</param>
        public WorkQueue<T> GetQueueForType<T> (bool forMainThread = false)
            where T : IWorkItem 
        {
            var type = typeof(T);
            bool resultIsNew;
            IWorkQueue existing;
            WorkQueue<T> result;

            var queues = Queues;
            var isMainThreadOnly = typeof(IMainThreadWorkItem).IsAssignableFrom(type) || forMainThread;

            // If the job must be run on the main thread, add to the main thread queue
            // Note that you must manually pump this queue yourself.
            if (isMainThreadOnly)
                queues = MainThreadQueues;

            lock (queues) {
                if (!queues.TryGetValue(type, out existing)) {
                    result = CreateQueueForType<T>(isMainThreadOnly);
                    queues.Add(type, result);
                    resultIsNew = true;
                } else {
                    result = (WorkQueue<T>)existing;
                    resultIsNew = false;
                }
            }

            if (isMainThreadOnly) {
                lock (MainThreadQueueList)
                    MainThreadQueueList.Add(result);
            }

            if (resultIsNew) {
                NewQueueCreated();
                NotifyQueuesChanged();
            }

            return result;
        }

        private WorkQueue<T> CreateQueueForType<T> (bool isMainThreadOnly)
            where T : IWorkItem
        {
            return new WorkQueue<T>(this) {
                IsMainThreadQueue = isMainThreadOnly
            };
        }

        /// <summary>
        /// Notifies the scheduler that new items have been added to the queues.
        /// This ensures that any sleeping worker threads wake up, and that
        ///  new threads are created if necessary
        /// </summary>
        public void NotifyQueuesChanged () {
            WakeAllThreads();
        }

        private void WakeAllThreads () {
            foreach (var thread in Threads)
                thread?.Wake();
        }

        public void Dispose () {
            Dispose(false);
        }

        void Dispose (bool finalizing) {
            if (IsDisposed)
                return;

            IsDisposed = true;

            for (int i = 0; i < Threads.Length; i++) {
                var thread = Interlocked.Exchange(ref Threads[i], null);
                thread?.Dispose();
            }

            if (!finalizing)
                GC.SuppressFinalize(this);
        }

        ~ThreadGroup () {
            Dispose(true);
        }
    }

    public static class ThreadGroupExtensions {
        private class ActionWorkItem : IWorkItem {
            public Action Action;
            public SignalFuture Future;

            public void Execute () {
                try {
                    Action();
                    Future.Complete();
                } catch (Exception exc) {
                    Future.SetResult2(NoneType.None, ExceptionDispatchInfo.Capture(exc));
                }
            }
        }

        private class FuncWorkItem<T> : IWorkItem {
            public Func<T> Func;
            public Future<T> Future;

            public void Execute () {
                try {
                    var result = Func();
                    Future.SetResult(result, null);
                } catch (Exception exc) {
                    Future.SetResult2(default(T), ExceptionDispatchInfo.Capture(exc));
                }
            }
        }

        public static SignalFuture Invoke (this ThreadGroup group, Action action) {
            var workItem = new ActionWorkItem {
                Action = action,
                Future = new SignalFuture()
            };
            group.Enqueue(ref workItem);
            return workItem.Future;
        }

        public static Future<T> Invoke<T> (this ThreadGroup group, Func<T> func) {
            var workItem = new FuncWorkItem<T> {
                Func = func,
                Future = new Future<T>()
            };
            group.Enqueue(ref workItem);
            return workItem.Future;
        }
    }
}

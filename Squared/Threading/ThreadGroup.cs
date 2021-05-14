using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class ThreadGroup : IDisposable {
        public static int MaximumThreadCount = 12;

        public bool IsDisposed { get; private set; }

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
        
        // A lock-free dictionary for looking up queues by work item type
        private readonly Dictionary<Type, IWorkQueue> Queues = 
            new Dictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, IWorkQueue> MainThreadQueues = 
            new Dictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());
        private readonly UnorderedList<IWorkQueue> MainThreadQueueList = 
            new UnorderedList<IWorkQueue>();

        // We keep separate iterable copies of the queue list for thread spawning purposes
        // Each thread has its own so that it is unlikely to not experience lock contention
        internal readonly ThreadLocal<UnorderedList<IWorkQueue>> QueueLists;

        public readonly GroupThread[] Threads;

        public string Name;

        public ThreadGroup (
            int? threadCount = null,
            bool createBackgroundThreads = false,
            ITimeProvider timeProvider = null,
            ApartmentState comThreadingModel = ApartmentState.Unknown,
            string name = null
        ) {
            Name = name;
            ThreadCount = Math.Min(threadCount.GetValueOrDefault(Environment.ProcessorCount + 1), MaximumThreadCount);
            CreateBackgroundThreads = createBackgroundThreads;
            TimeProvider = timeProvider ?? Time.DefaultTimeProvider;
            COMThreadingModel = comThreadingModel;
            QueueLists = new ThreadLocal<UnorderedList<IWorkQueue>>(CreateNewQueue, true);

            Threads = new GroupThread[ThreadCount];
            for (int i = 0; i < Threads.Length; i++)
                Threads[i] = new GroupThread(this, i);
        }

        private UnorderedList<IWorkQueue> CreateNewQueue () {
            var result = new UnorderedList<IWorkQueue>();
            lock (Queues)
                foreach (var item in Queues.Values)
                    result.Add(item);
            return result;
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
                foreach (var value in QueueLists.Values)
                    lock (value)
                        value.Add(result);

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
            foreach (var thread in Threads)
                thread?.Wake();
        }

        internal void WakeAllThreads () {
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

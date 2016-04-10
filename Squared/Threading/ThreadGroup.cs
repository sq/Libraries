using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class ThreadGroup : IDisposable {
        public bool IsDisposed { get; private set; }

        public readonly int MinimumThreadCount;
        public readonly int MaximumThreadCount;
        
        // A lock-free dictionary for looking up queues by work item type
        private readonly ConcurrentDictionary<Type, IWorkQueue> Queues = 
            new ConcurrentDictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());

        // We keep a separate iterable copy of the queue list for thread spawning purposes
        private readonly List<IWorkQueue> QueueList = new List<IWorkQueue>();

        private readonly List<GroupThread> Threads = new List<GroupThread>();

        private int IdleThreadCount = 0;
        private int BusyStatesObserved = 0;

        private const int NewThreadBusyThreshold = 4;

        public ThreadGroup (
            int? minimumThreads = null,
            int? maximumThreads = null
        ) {
            MaximumThreadCount = maximumThreads.GetValueOrDefault(Environment.ProcessorCount + 1);
            MinimumThreadCount = Math.Min(minimumThreads.GetValueOrDefault(1), MaximumThreadCount);

            lock (Threads)
            while ((Count < MinimumThreadCount) && (Count < MaximumThreadCount))
                SpawnThread();
        }

        public int Count {
            get {
                lock (Threads)
                    return Threads.Count;
            }
        }

        public void Enqueue<T> (T item, OnWorkItemComplete<T> onComplete = null)
            where T : IWorkItem
        {
            var queue = GetQueueForType<T>();
            queue.Enqueue(ref item, onComplete);
            ConsiderNewThread();
        }

        public void Enqueue<T> (ref T item, OnWorkItemComplete<T> onComplete = null)
            where T : IWorkItem
        {
            var queue = GetQueueForType<T>();
            queue.Enqueue(ref item, onComplete);
            ConsiderNewThread();
        }

        /// <summary>
        /// You can use this to request a work queue for a given type of work item, then queue
        ///  multiple items cheaply. If you queue items directly, it's your responsibility to call
        ///  ThreadGroup.ConsiderNewThread to ensure that new thread(s) are created to perform work.
        /// </summary>
        public WorkQueue<T> GetQueueForType<T> ()
            where T : IWorkItem 
        {
            var type = typeof(T);
            IWorkQueue existing;
            WorkQueue<T> result;

            if (!Queues.TryGetValue(type, out existing)) {
                result = CreateQueueForType<T>();
                
                // We lost a race to create the new work queue
                if (!Queues.TryAdd(type, result)) {
                    result = (WorkQueue<T>)Queues[type];
                } else {
                    lock (QueueList)
                        QueueList.Add(result);

                    // FIXME: Can this deadlock somehow?
                    lock (Threads)
                    foreach (var thread in Threads)
                        thread.RegisterQueue(result);
                }
            } else {
                result = (WorkQueue<T>)existing;
            }

            return result;
        }

        private WorkQueue<T> CreateQueueForType<T> ()
            where T : IWorkItem
        {
            return new WorkQueue<T>();
        }

        /// <summary>
        /// Checks to see whether an additional worker thread is needed, and if so, creates one.
        /// If you've just enqueued many work items it's advised to invoke this once with assumeBusy: true.
        /// </summary>
        /// <param name="assumeBusy">If true, it is assumed that many tasks are waiting.</param>
        /// <returns>true if a thread was created.</returns>
        public bool ConsiderNewThread (bool assumeBusy = false) {
            lock (Threads) {
                if (Threads.Count >= MaximumThreadCount)
                    return false;

                if (IdleThreadCount <= 0) {
                    BusyStatesObserved++;

                    if (assumeBusy || (BusyStatesObserved > NewThreadBusyThreshold)) {
                        BusyStatesObserved = 0;
                        SpawnThread();
                        return true;
                    }
                }

                return false;
            }
        }

        public void ForciblySpawnThread () {
            lock (Threads)
                SpawnThread();
        }

        internal void RegisterQueuesForNewThread (GroupThread newThread) {
            lock (QueueList)
            foreach (var queue in QueueList)
                newThread.RegisterQueue(queue);
        }

        private GroupThread SpawnThread () {
            var thread = new GroupThread(this);
            Threads.Add(thread);
            return thread;
        }

        public void Dispose () {
            Dispose(false);
        }

        void Dispose (bool finalizing) {
            if (IsDisposed)
                return;

            IsDisposed = true;

            lock (Threads) {
                foreach (var thread in Threads) {
                    thread.Dispose();
                }
                Threads.Clear();
            }

            if (!finalizing)
                GC.SuppressFinalize(this);
        }

        ~ThreadGroup () {
            Dispose(true);
        }
    }
}

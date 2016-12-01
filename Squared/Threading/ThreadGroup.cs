using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class ThreadGroup : IDisposable {
        public bool IsDisposed { get; private set; }

        public event Action<double> NewThreadCreated;

        /// <summary>
        /// If all worker threads are busy for this long (in milliseconds),
        ///  a new thread will be spawned if possible
        /// </summary>
        public float    NewThreadBusyThresholdMs = 5;

        public readonly ITimeProvider TimeProvider;
        public readonly bool CreateBackgroundThreads;
        public readonly int MinimumThreadCount;
        public readonly int MaximumThreadCount;
        public readonly ApartmentState COMThreadingModel;
        
        // A lock-free dictionary for looking up queues by work item type
        private readonly ConcurrentDictionary<Type, IWorkQueue> Queues = 
            new ConcurrentDictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());

        // We keep a separate iterable copy of the queue list for thread spawning purposes
        private readonly List<IWorkQueue> QueueList = new List<IWorkQueue>();

        private int CurrentThreadCount;
        private readonly UnorderedList<GroupThread> Threads = new UnorderedList<GroupThread>(256);

        private long LastTimeThreadWasIdle = long.MaxValue;
        private bool CanMakeNewThreads, HasNoThreads;

        public ThreadGroup (
            int? minimumThreads = null,
            int? maximumThreads = null,
            bool createBackgroundThreads = false,
            ITimeProvider timeProvider = null,
            ApartmentState comThreadingModel = ApartmentState.Unknown
        ) {
            MaximumThreadCount = maximumThreads.GetValueOrDefault(Environment.ProcessorCount + 1);
            MinimumThreadCount = Math.Min(minimumThreads.GetValueOrDefault(1), MaximumThreadCount);
            CreateBackgroundThreads = createBackgroundThreads;
            TimeProvider = timeProvider ?? Time.DefaultTimeProvider;
            COMThreadingModel = comThreadingModel;

            lock (Threads)
            while ((Count < MinimumThreadCount) && (Count < MaximumThreadCount))
                SpawnThread(true);

            HasNoThreads = (Count == 0);
            CanMakeNewThreads = (Count < MaximumThreadCount);
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
            NotifyQueuesChanged();
        }

        public void Enqueue<T> (ref T item, OnWorkItemComplete<T> onComplete = null)
            where T : IWorkItem
        {
            var queue = GetQueueForType<T>();
            queue.Enqueue(ref item, onComplete);
            NotifyQueuesChanged();
        }

        internal void ThreadBecameIdle () {
            Interlocked.Exchange(ref LastTimeThreadWasIdle, TimeProvider.Ticks);
        }

        internal void ThreadBeganWorking () {
            Interlocked.Exchange(ref LastTimeThreadWasIdle, TimeProvider.Ticks);
        }

        /// <summary>
        /// You can use this to request a work queue for a given type of work item, then queue
        ///  multiple items cheaply. If you queue items directly, it's your responsibility to call
        ///  ThreadGroup.NotifyQueuesChanged to ensure that a sufficient number of threads are ready
        ///  to perform work.
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
        /// Notifies the scheduler that new items have been added to the queues.
        /// This ensures that any sleeping worker threads wake up, and that
        ///  new threads are created if necessary
        /// </summary>
        public void NotifyQueuesChanged (bool assumeBusy = false) {
            ConsiderNewThread(assumeBusy);

            // Skip locking 'Threads' here since it's expensive.
            // This means we might fail to wake a brand new thread,
            //  but that's fine.
            var threads = Threads.GetBuffer();
            for (int i = 0; i < CurrentThreadCount; i++) {
                var thread = threads[i];

                if (thread != null)
                    thread.WakeEvent.Set();
            }
        }

        /// <summary>
        /// Checks to see whether an additional worker thread is needed, and if so, creates one.
        /// If you've just enqueued many work items it's advised to invoke this once with assumeBusy: true.
        /// </summary>
        /// <param name="assumeBusy">If true, it is assumed that many tasks are waiting.</param>
        /// <returns>true if a thread was created.</returns>
        private bool ConsiderNewThread (bool assumeBusy = false) {
            if (!CanMakeNewThreads)
                return false;

            var timeSinceLastIdle = TimeProvider.Ticks - Interlocked.Read(ref LastTimeThreadWasIdle);
            var timeSinceLastIdleMs = TimeSpan.FromTicks(timeSinceLastIdle).TotalMilliseconds;

            if (HasNoThreads || (timeSinceLastIdleMs >= NewThreadBusyThresholdMs)) {
                lock (Threads) {
                    SpawnThread(false);

                    if (NewThreadCreated != null)
                        NewThreadCreated(timeSinceLastIdleMs);

                    return true;
                }
            }

            return false;
        }

        public void ForciblySpawnThread () {
            lock (Threads)
                SpawnThread(true);
        }

        internal void RegisterQueuesForNewThread (GroupThread newThread) {
            lock (QueueList)
            foreach (var queue in QueueList)
                newThread.RegisterQueue(queue);
        }

        private void SpawnThread (bool force) {
            // Just in case thread state gets out of sync...
            if ((Threads.Count >= MaximumThreadCount) && !force)
                return;

            Interlocked.Exchange(ref LastTimeThreadWasIdle, TimeProvider.Ticks);
            var thread = new GroupThread(this);
            Threads.Add(thread);

            Thread.MemoryBarrier();

            HasNoThreads = false;
            CanMakeNewThreads = Threads.Count < MaximumThreadCount;
            CurrentThreadCount = Threads.Count;

            Thread.MemoryBarrier();
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

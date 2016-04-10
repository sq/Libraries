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

        private readonly ConcurrentDictionary<Type, IWorkQueue> Queues = 
            new ConcurrentDictionary<Type, IWorkQueue>(new ReferenceComparer<Type>());
        private readonly List<GroupThread> Threads = new List<GroupThread>();

        public ThreadGroup (
            int? minimumThreads = null,
            int? maximumThreads = null
        ) {
            MaximumThreadCount = maximumThreads.GetValueOrDefault(Environment.ProcessorCount + 1);
            MinimumThreadCount = Math.Min(minimumThreads.GetValueOrDefault(1), MaximumThreadCount);

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
        }

        public void Enqueue<T> (ref T item, OnWorkItemComplete<T> onComplete = null)
            where T : IWorkItem
        {
            var queue = GetQueueForType<T>();
            queue.Enqueue(ref item, onComplete);
        }

        public WorkQueue<T> GetQueueForType<T> ()
            where T : IWorkItem 
        {
            var type = typeof(T);
            IWorkQueue existing;
            WorkQueue<T> result;

            if (!Queues.TryGetValue(type, out existing)) {
                result = CreateQueueForType<T>();
                
                // We lost a race to create the new work queue
                if (!Queues.TryAdd(type, result))
                    result = (WorkQueue<T>)Queues[type];
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

        public void ForciblySpawnThread () {
            SpawnThread();
        }

        private GroupThread SpawnThread () {
            var thread = new GroupThread(this);
            lock (Threads)
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

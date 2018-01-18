using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Threading {
    public interface IWorkQueue {
        /// <param name="exhausted">Is set to true if the Step operation caused the queue to become empty.</param>
        /// <returns>The number of work items handled.</returns>
        int Step (out bool exhausted, int? maximumCount = null);
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
        /// <summary>
        /// Configures the number of steps taken each time this queue is visited by a worker thread.
        /// Low values increase the overhead of individual work items.
        /// High values reduce the overhead of work items but increase the odds that all worker threads can get bogged down
        ///  by a single queue.
        /// </summary>
        public int DefaultStepCount = 64;

        private readonly Queue<InternalWorkItem<T>> Queue = new Queue<InternalWorkItem<T>>();
        private int InFlightTasks = 0;

        private readonly ManualResetEventSlim DrainComplete = new ManualResetEventSlim(false);
        private volatile int NumWaitingForDrain = 0;

        public WorkQueue () {
        }

        private void ManageDrain () {
            if (NumWaitingForDrain > 0)
                throw new Exception("Draining");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete = null) {
            lock (Queue) {
                ManageDrain();
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null) {
            lock (Queue) {
                ManageDrain();
                Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnqueueMany (ArraySegment<T> data) {
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
            int actualMaximumCount = maximumCount.GetValueOrDefault(DefaultStepCount);
            InternalWorkItem<T> item = default(InternalWorkItem<T>);

            bool running = true;
            do {
                lock (Queue) {
                    running = ((count = Queue.Count) > 0) &&
                        (result < actualMaximumCount);

                    if (running) {
                        InFlightTasks++;

                        item = Queue.Dequeue();
                    }
                }

                if (running) {
                    item.Data.Execute();
                    if (item.OnComplete != null)
                        item.OnComplete(ref item.Data);

                    result++;

                    lock (Queue) {
                        InFlightTasks--;
                        if ((Queue.Count == 0) && (InFlightTasks <= 0))
                            DrainComplete.Set();
                    }
                }
            } while (running);

            lock (Queue)
                exhausted = Queue.Count == 0;

            return result;
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

        public void WaitUntilDrained () {
            var resultCount = Interlocked.Increment(ref NumWaitingForDrain);

            bool doWait = false;
            lock (Queue)
                doWait = (Queue.Count > 0) || (InFlightTasks > 0);

            if (doWait)
                DrainComplete.Wait();

            var result = Interlocked.Decrement(ref NumWaitingForDrain);
            if (result == 0)
                DrainComplete.Reset();
        }
    }
}

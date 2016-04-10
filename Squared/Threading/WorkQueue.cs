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
        public readonly WorkQueue<T>          Queue;
        public readonly OnWorkItemComplete<T> OnComplete;
        public          T                     Data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal InternalWorkItem (WorkQueue<T> queue, ref T data, OnWorkItemComplete<T> onComplete) {
            Queue = queue;
            Data = data;
            OnComplete = onComplete;
        }
        
        // TODO: Add cheap blocking wait primitive
    }

    public class WorkQueue<T> : IWorkQueue
        where T : IWorkItem 
    {
        public struct Marker {
            private readonly WorkQueue<T> Queue;
            public readonly long Executed, Enqueued;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Marker (WorkQueue<T> queue) {
                Queue = queue;
                Executed = Interlocked.Read(ref Queue.ItemsExecuted);
                Enqueued = Interlocked.Read(ref Queue.ItemsEnqueued);
            }

            /// <summary>
            /// Waits until all items enqueued at the marking point have been executed
            /// </summary>
            public void Wait () {
                while (true) {
                    lock (Queue.Token) {
                        var executed = Interlocked.Read(ref Queue.ItemsExecuted);
                        if (executed >= Enqueued)
                            return;

                        Monitor.Wait(Queue.Token);
                    }
                }
            }
        }

        /// <summary>
        /// Configures the number of steps taken each time this queue is visited by a worker thread.
        /// Low values increase the overhead of individual work items.
        /// High values reduce the overhead of work items but increase the odds that all worker threads can get bogged down
        ///  by a single queue.
        /// </summary>
        public int DefaultStepCount = 128;

        private readonly object Token = new object();

        private readonly ConcurrentQueue<InternalWorkItem<T>> Queue = 
            new ConcurrentQueue<InternalWorkItem<T>>();

        private long ItemsEnqueued;
        private long ItemsExecuted;

        public WorkQueue () {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (T data, OnWorkItemComplete<T> onComplete = null) {
            Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            Interlocked.Increment(ref ItemsEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue (ref T data, OnWorkItemComplete<T> onComplete = null) {
            Queue.Enqueue(new InternalWorkItem<T>(this, ref data, onComplete));
            Interlocked.Increment(ref ItemsEnqueued);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Marker Mark () {
            return new Marker(this);
        }

        public int Step (out bool exhausted, int? maximumCount = null) {
            InternalWorkItem<T> item;
            int result = 0;
            int actualMaximumCount = maximumCount.GetValueOrDefault(DefaultStepCount);

            while (
                (result < actualMaximumCount) &&
                Queue.TryDequeue(out item)
            ) {
                item.Data.Execute();
                if (item.OnComplete != null)
                    item.OnComplete(ref item.Data);

                result++;
            }

            if (result > 0)
                Interlocked.Add(ref ItemsExecuted, result);

            if (Monitor.TryEnter(Token, 10)) {
                Monitor.PulseAll(Token);
                Monitor.Exit(Token);
            } else
                throw new ThreadStateException("Failed to acquire the worker queue lock after 10 milliseconds");

            exhausted = (result > 0) && Queue.IsEmpty;

            return result;
        }

        public void WaitUntilDrained () {
            var done = false;

            while (!done) {
                lock (Token) {
                    done = 
                        (Interlocked.Read(ref ItemsExecuted) >= Interlocked.Read(ref ItemsEnqueued)) &&
                        Queue.IsEmpty;

                    if (!done)                        
                        Monitor.Wait(Token);
                }

                ;
            }
        }
    }
}

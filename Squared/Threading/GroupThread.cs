using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class GroupThread : IDisposable {
        public  readonly ThreadGroup          Owner;
        public  readonly Thread               Thread;
        public  readonly ThreadIdleManager    IdleManager = new ThreadIdleManager();

#if DEBUG
        // HACK: Set the delay to EXTREMELY LONG so that we will get nasty pauses
        //  in debug builds if we hit a latent bug, so that it will be obvious
        //  they are there and we can debug them
        public static int IdleWaitDurationMs = 10000;
#else
        // HACK: Set a long enough time to avoid churning the thread scheduler
        //  (this will improve efficiency on battery-powered devices, etc)
        // But not TOO long in order to avoid completely collapsing in the event
        //  that a latent bug in our signaling manifests itself
        public static int IdleWaitDurationMs = 500;
#endif
        private int NextQueueIndex;

        public bool IsDisposed { get; private set; }

        internal GroupThread (ThreadGroup owner, int nextQueueIndex) {
            Owner = owner;
            NextQueueIndex = nextQueueIndex;
            Thread = new Thread(ThreadMain);
            Thread.Name = string.Format("ThreadGroup {0} {1} worker #{2}", owner.GetHashCode(), owner.Name, owner.Count);
            Thread.IsBackground = owner.CreateBackgroundThreads;
            if (owner.COMThreadingModel != ApartmentState.Unknown)
                Thread.SetApartmentState(owner.COMThreadingModel);
            // HACK: Ensure the thread-local list has been allocated before we return
            //  since our caller has the threads lock held.
            var queueList = owner.QueueLists.Value;
            Thread.Start(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Wake () {
            IdleManager.Wake();
        }

        private static void ThreadMain (object _self) {
            var weakSelf = ThreadMainSetup(ref _self, out UnorderedList<IWorkQueue> queueList);

            bool running = true;
            while (running) {
                // HACK: We retain a strong reference to our GroupThread while we're running,
                //  and if our owner GroupThread has been collected, we abort
                var idleManager = ThreadMainStep(weakSelf, queueList, ref running);
                if (idleManager != null)
                    idleManager.Wait(IdleWaitDurationMs);
            }
        }

        private static WeakReference<GroupThread> ThreadMainSetup (
            ref object _self, out UnorderedList<IWorkQueue> queueList
        ) {
            var self = (GroupThread)_self;
            var weakSelf = new WeakReference<GroupThread>(self);
            queueList = self.Owner.QueueLists.Value;
            return weakSelf;
        }

        private static ThreadIdleManager ThreadMainStep (WeakReference<GroupThread> weakSelf, UnorderedList<IWorkQueue> queueList, ref bool running) {
            // We hold the strong reference at method scope so we can be sure it doesn't get held too long
            GroupThread strongSelf = null;

            // If our owner object has been collected or disposed, abort
            if (!weakSelf.TryGetTarget(out strongSelf) || strongSelf.IsDisposed) {
                running = false;
                return null;
            }

            if (strongSelf.IdleManager.BeginRunning() && strongSelf.PerformWork(queueList))
                return strongSelf.IdleManager;
            else
                return null;
        }

        private bool PerformWork (UnorderedList<IWorkQueue> queueList) {
            int queueCount;
            IWorkQueue[] queues;
            lock (queueList) {
                queueCount = queueList.Count;
                // Since we only ever add new queues, the worst outcome of a race here
                //  is that we miss the new queue(s) for this step
                queues = queueList.GetBufferArray();
            }

            bool moreWorkRemains = false;
            var nqi = NextQueueIndex++;
            for (int i = 0; i < queueCount; i++) {
                if (IsDisposed)
                    return false;

                // We round-robin select a queue from our pool every tick and then step it
                IWorkQueue queue = null;
                int queueIndex = (i + nqi) % queueCount;
                queue = queues[queueIndex];

                if (queue != null) {
                    bool exhausted;
                    int processedItemCount = queue.Step(out exhausted);

                    // HACK: If we processed at least one item in this queue, but more items remain,
                    //  make sure the caller knows not to go to sleep.
                    if (processedItemCount > 0)
                        moreWorkRemains |= !exhausted;
                }
            }

            return !moreWorkRemains;
        }

        public void Dispose () {
            IsDisposed = true;
            Wake();
        }
    }
}

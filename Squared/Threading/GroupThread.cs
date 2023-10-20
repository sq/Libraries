using System;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public sealed class GroupThread : IDisposable {
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

        public string Name { get; private set; }
        public bool IsDisposed { get; private set; }

        internal GroupThread (ThreadGroup owner, int index) {
            Owner = owner;
            NextQueueIndex = index;
            Thread = new Thread(ThreadMain);
            Name = string.Format($"{owner.Name} worker #{index} [ThreadGroup {owner.GetHashCode():X8}]");
            Thread.Name = Name;
            Thread.IsBackground = owner.CreateBackgroundThreads;
            if (owner.COMThreadingModel != ApartmentState.Unknown)
                Thread.SetApartmentState(owner.COMThreadingModel);
            Thread.Start(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Wake () {
            IdleManager.Wake();
        }

        private static void ThreadMain (object _self) {
            var weakSelf = ThreadMainSetup(ref _self);

            bool running = true;
            while (running) {
                // HACK: We retain a strong reference to our GroupThread while we're running,
                //  and if our owner GroupThread has been collected, we abort
                var idleManager = ThreadMainStep(weakSelf, ref running);
                if (idleManager != null)
                    idleManager.Wait(IdleWaitDurationMs);
            }
        }

        private static WeakReference<GroupThread> ThreadMainSetup (
            ref object _self
        ) {
            var self = (GroupThread)_self;
            var weakSelf = new WeakReference<GroupThread>(self);
            Profiling.Superluminal.SetCurrentThreadName(self.Name);
            return weakSelf;
        }

        private static ThreadIdleManager ThreadMainStep (
            WeakReference<GroupThread> weakSelf, ref bool running
        ) {
            // We hold the strong reference at method scope so we can be sure it doesn't get held too long
            GroupThread strongSelf = null;

            // If our owner object has been collected or disposed, abort
            if (!weakSelf.TryGetTarget(out strongSelf) || strongSelf.IsDisposed) {
                running = false;
                return null;
            }

            if (strongSelf.IdleManager.BeginRunning() && strongSelf.PerformWork())
                return strongSelf.IdleManager;
            else
                return null;
        }

        private bool PerformWork () {
            var queues = Owner.QueuesForWorkers;

            var gsc = Owner.SynchronizationContext;
            var sc = SynchronizationContext.Current;
            bool moreWorkRemains = false;
            try {
                if (gsc != null)
                    SynchronizationContext.SetSynchronizationContext(gsc);
                var nqi = NextQueueIndex++;
                for (int pi = 0, pc = queues.Items.Count; pi < pc; pi++) {
                    if (IsDisposed)
                        return false;

                    var pq = queues.Items.DangerousGetItem(pi);
                    var processedAnyAtThisLevel = false;

                    for (int li = 0, lc = pq.Length; li < lc; li++) {
                        var index = (li + nqi) % lc;
                        var queue = pq[index];
                        if (queue == null)
                            continue;

                        int processedItemCount = queue.Step(out bool exhausted);
                        if (processedItemCount > 0) {
                            moreWorkRemains |= !exhausted;
                            processedAnyAtThisLevel = true;
                        }
                    }

                    // If we processed any work items at this priority level,
                    //  bail out instead of continuing to a lower priority level.
                    // This is intended to ensure that high-priority items complete
                    //  first, while allowing lower priority items to run if the
                    //  high priority queue(s) are at their max concurrency level.
                    if (processedAnyAtThisLevel)
                        break;
                }
            } finally {
                if (gsc != null)
                    SynchronizationContext.SetSynchronizationContext(sc);
            }

            return !moreWorkRemains;
        }

        public void Dispose () {
            IsDisposed = true;
            Wake();
        }
    }
}

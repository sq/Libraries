using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class GroupThread : IDisposable {
        public readonly ThreadGroup      Owner;
        public readonly Thread           Thread;
        public readonly object           WakeSignal;

        private readonly UnorderedList<IWorkQueue> Queues = new UnorderedList<IWorkQueue>();

#if DEBUG
        // For troubleshooting
        public static int IdleWaitDurationMs = 10000;
#else
        public static int IdleWaitDurationMs = 100;
#endif
        private int NextQueueIndex;

        public bool IsDisposed { get; private set; }

        internal GroupThread (ThreadGroup owner, int nextQueueIndex) {
            Owner = owner;
            NextQueueIndex = nextQueueIndex;
            WakeSignal = owner.WakeSignal;
            Thread = new Thread(ThreadMain);
            Thread.Name = string.Format("ThreadGroup {0} {1} worker #{2}", owner.GetHashCode(), owner.Name, owner.Count);
            Thread.IsBackground = owner.CreateBackgroundThreads;
            if (owner.COMThreadingModel != ApartmentState.Unknown)
                Thread.SetApartmentState(owner.COMThreadingModel);
            owner.RegisterQueuesForNewThread(this);
            Thread.Start(this);
        }

        internal void RegisterQueue (IWorkQueue queue) {
            lock (Queues)
                Queues.Add(queue);
        }

        private static void ThreadMain (object _self) {
            ManualResetEventSlim wakeEvent;
            var weakSelf = ThreadMainSetup(ref _self, out object wakeSignal);

            // On thread termination we release our event.
            // If we did this in Dispose there'd be no clean way to deal with this.
            while (true) {
                bool moreWorkRemains;
                // HACK: We retain a strong reference to our GroupThread while we're running,
                //  and if our owner GroupThread has been collected, we abort
                if (!ThreadMainStep(weakSelf, out moreWorkRemains))
                    break;
                // The strong reference is released here so we can wait to be woken up

                if (!moreWorkRemains) {
                    // We only wait if no work remains
                    lock (wakeSignal)
                        Monitor.Wait(wakeSignal, IdleWaitDurationMs);
                }
            }
        }

        private static WeakReference<GroupThread> ThreadMainSetup (
            ref object _self, out object wakeSignal
        ) {
            var self = (GroupThread)_self;
            var weakSelf = new WeakReference<GroupThread>(self);
            wakeSignal = self.WakeSignal;
            return weakSelf;
        }

        private static bool ThreadMainStep (WeakReference<GroupThread> weakSelf, out bool moreWorkRemains) {
            // We hold the strong reference at method scope so we can be sure it doesn't get held too long
            GroupThread strongSelf = null;
            moreWorkRemains = false;

            // If our owner object has been collected, abort
            if (!weakSelf.TryGetTarget(out strongSelf))
                return false;

            // If our owner object has been disposed, abort
            if (strongSelf.IsDisposed)
                return false;

            int queueCount;
            IWorkQueue[] queues;
            lock (strongSelf.Queues) {
                queueCount = strongSelf.Queues.Count;
                queues = strongSelf.Queues.GetBufferArray();
            }

            strongSelf.Owner.ThreadBeganWorking();

            var nqi = strongSelf.NextQueueIndex++;
            for (int i = 0; i < queueCount; i++) {
                if (strongSelf.IsDisposed)
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

            strongSelf.Owner.ThreadBecameIdle();

            GC.KeepAlive(strongSelf);
            return true;
        }

        public void Dispose () {
            IsDisposed = true;
            var taken = false;
            Monitor.TryEnter(WakeSignal, 1, ref taken);
            if (taken) {
                Monitor.PulseAll(WakeSignal);
                Monitor.Exit(WakeSignal);
            }
            // HACK: This shouldn't be necessary, but without this tests hang
            if (false && Thread.IsBackground)
                Thread.Abort();
        }
    }
}

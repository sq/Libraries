using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading {
    public class GroupThread : IDisposable {
        public readonly ThreadGroup          Owner;
        public readonly Thread               Thread;
        public readonly ManualResetEventSlim WakeEvent;

        private readonly List<IWorkQueue> Queues = new List<IWorkQueue>();

        public bool IsDisposed { get; private set; }

        public GroupThread (ThreadGroup owner) {
            Owner = owner;
            WakeEvent = new ManualResetEventSlim(true);
            Thread = new Thread(ThreadMain);
            Thread.Name = "Squared.Threading worker thread";
            owner.RegisterQueuesForNewThread(this);
            Thread.Start(this);
        }

        internal void RegisterQueue (IWorkQueue queue) {
            lock (Queues)
                Queues.Add(queue);
        }

        private static void ThreadMain (object _self) {
            ManualResetEventSlim wakeEvent;
            var weakSelf = ThreadMainSetup(ref _self, out wakeEvent);

            int queueIndex = 0;

            // On thread termination we release our event.
            // If we did this in Dispose there'd be no clean way to deal with this.
            using (wakeEvent)
            while (true) {
                // HACK: We retain a strong reference to our GroupThread while we're running,
                //  and if our owner GroupThread has been collected, we abort
                if (!ThreadMainStep(weakSelf, ref queueIndex))
                    break;

                // The strong reference is released here so we can wait to be woken up
                wakeEvent.Wait();
            }
        }

        private static WeakReference<GroupThread> ThreadMainSetup (
            ref object _self, out ManualResetEventSlim wakeEvent
        ) {
            var self = (GroupThread)_self;
            var weakSelf = new WeakReference<GroupThread>(self);
            wakeEvent = self.WakeEvent;
            return weakSelf;
        }

        private static bool ThreadMainStep (WeakReference<GroupThread> weakSelf, ref int queueIndex) {
            // We hold the strong reference at method scope so we can be sure it doesn't get held too long
            GroupThread strongSelf = null;

            // If our owner object has been collected, abort
            if (!weakSelf.TryGetTarget(out strongSelf))
                return false;

            // If our owner object has been disposed, abort
            if (strongSelf.IsDisposed)
                return false;

            IWorkQueue queue = null;
            lock (strongSelf.Queues) {
                // We round-robin select a queue from our pool every tick and then step it
                var queueCount = strongSelf.Queues.Count;

                if (queueCount > 0) {
                    if (queueIndex < queueCount)
                        queue = strongSelf.Queues[queueIndex];

                    queueIndex = (queueIndex + 1) % queueCount;
                }
            }

            if (queue != null)
                queue.Step();

            GC.KeepAlive(strongSelf);
            return true;
        }

        public void Dispose () {
            IsDisposed = true;
            WakeEvent.Set();
        }
    }
}

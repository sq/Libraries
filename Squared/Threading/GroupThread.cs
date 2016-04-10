using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Threading {
    public class GroupThread : IDisposable {
        public readonly ThreadGroup          Owner;
        public readonly Thread               Thread;
        public readonly ManualResetEventSlim WakeEvent;

        public bool IsDisposed { get; private set; }

        public GroupThread (ThreadGroup owner) {
            Owner = owner;
            WakeEvent = new ManualResetEventSlim(true);
            Thread = new Thread(ThreadMain);
            Thread.Name = "Squared.Threading worker thread";
            Thread.Start(this);
        }

        private static void ThreadMain (object _self) {
            ManualResetEventSlim wakeEvent;
            var weakSelf = ThreadMainSetup(ref _self, out wakeEvent);

            // On thread termination we release our event.
            // If we did this in Dispose there'd be no clean way to deal with this.
            using (wakeEvent)
            while (true) {
                // HACK: We retain a strong reference to our GroupThread while we're running,
                //  and if our owner GroupThread has been collected, we abort
                if (!ThreadMainStep(weakSelf))
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

        private static bool ThreadMainStep (WeakReference<GroupThread> weakSelf) {
            // We hold the strong reference at method scope so we can be sure it doesn't get held too long
            GroupThread strongSelf = null;

            // If our owner object has been collected, abort
            if (!weakSelf.TryGetTarget(out strongSelf))
                return false;

            // If our owner object has been disposed, abort
            if (strongSelf.IsDisposed)
                return false;

            GC.KeepAlive(strongSelf);
            return true;
        }

        public void Dispose () {
            IsDisposed = true;
            WakeEvent.Set();
        }
    }
}

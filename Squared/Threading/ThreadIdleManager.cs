using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Threading {
    /// <summary>
    /// Manages the sleep/wake state of a single worker thread, and tracks whether the thread
    ///  has received another wake request since the last time it woke up.
    /// </summary>
    public sealed class ThreadIdleManager : IDisposable {
        const int RunState_Sleeping = 0,
            RunState_WakeRequested = 1,
            RunState_Running = 2,
            RunState_Disposed = 3;

        private volatile int RequestState = 0;
        private volatile int RunState = RunState_Sleeping;
        private ManualResetEventSlim Event = new ManualResetEventSlim(true, 1);

        /// <returns>true if the thread is allowed to begin running, false if it has been disposed</returns>
        public bool BeginRunning () {
            // FIXME: Is this right?
            var previousState = Interlocked.Exchange(ref RunState, RunState_Running);
            if (previousState == RunState_Disposed)
                Volatile.Write(ref RunState, RunState_Disposed);
            if (previousState != RunState_Running)
                Event.Reset();
            return (previousState != RunState_Disposed);
        }

        /// <summary>
        /// Wakes any sleeping threads and sets a flag indicating that any running threads should not sleep
        ///  until at least one of them runs again once
        /// </summary>
        public void Wake () {
            var wasWorkAlreadyRequested = Interlocked.Exchange(ref RequestState, 1) == 1;
            var oldRunState = Interlocked.CompareExchange(ref RunState, RunState_WakeRequested, RunState_Sleeping);
            var needsToSignal = oldRunState == RunState_Sleeping;
            if (wasWorkAlreadyRequested && !needsToSignal)
                return;
            if (oldRunState == RunState_Disposed)
                return;

            if (needsToSignal)
                // FIXME: Yield here somehow
                Event.Set();
        }

        /// <summary>
        /// Unless a wake has already been requested since the last time this thread began running,
        ///  sleeps until a wake is requested or the specified timeout expires.
        /// </summary>
        /// <returns>true if a wake was requested, false if the timeout expired</returns>
        public bool Wait (int timeoutMs = -1) {
            var oldState = Interlocked.CompareExchange(ref RunState, RunState_Sleeping, RunState_Running);
            if (oldState != RunState_Running)
                return true;

            if (Interlocked.Exchange(ref RequestState, 0) != 0)
                return true;

            var result = Event.Wait(timeoutMs);
            Interlocked.CompareExchange(ref RunState, RunState_Running, RunState_Sleeping);
            return result;
        }

        public void Dispose () {
            Volatile.Write(ref RunState, RunState_Disposed);
            Event.Set();
        }
    }
}

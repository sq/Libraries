using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Threading {
    public class ThreadIdleManager : IDisposable {
        const int State_Sleeping = 0,
            State_WakeRequested = 1,
            State_Awake = 2,
            State_Running = 3,
            State_Disposed = 4;

        private volatile int State = State_WakeRequested;
        private ManualResetEventSlim Event = new ManualResetEventSlim(true, 1);

        /// <returns>true if the thread is allowed to begin running, false if it has been disposed</returns>
        public bool BeginRunning () {
            // FIXME: Is this right?
            Event.Reset();
            var previousState = Interlocked.Exchange(ref State, State_Running);
            return (previousState != State_Disposed);
        }

        /// <returns>true if the thread is allowed to stop running, false if it has been asked to wake up again</returns>
        public bool StopRunning () {
            return Interlocked.CompareExchange(ref State, State_Awake, State_Running) == State_Running;
        }

        /// <summary>
        /// Wakes any sleeping threads and sets a flag indicating that any running threads should not sleep
        ///  until at least one of them runs again once
        /// </summary>
        public void Wake () {
            var oldState = Interlocked.Exchange(ref State, State_WakeRequested);
            if (oldState == State_Sleeping) {
                Thread.Yield();
                if (Volatile.Read(ref State) != State_Running)
                    Event.Set();
            }
        }

        /// <summary>
        /// Unless a wake has already been requested since the last time this thread began running,
        ///  sleeps until a wake is requested or the specified timeout expires.
        /// </summary>
        /// <returns>true if a wake was requested, false if the timeout expired</returns>
        public bool Wait (int timeoutMs = -1) {
            if (Interlocked.CompareExchange(ref State, State_Sleeping, State_Running) != State_Running)
                return true;

            Thread.Yield();
            if (Volatile.Read(ref State) != State_Sleeping)
                return true;

            var result = Event.Wait(timeoutMs);
            Interlocked.CompareExchange(ref State, State_Awake, State_Sleeping);
            return result;
        }

        public void Dispose () {
            Volatile.Write(ref State, State_Disposed);
        }
    }
}

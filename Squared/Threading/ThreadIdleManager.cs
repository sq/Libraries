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
            State_Running = 2,
            State_Disposed = 3;

        private volatile int State = State_WakeRequested;
        private ManualResetEventSlim Event = new ManualResetEventSlim(true, 1);

        /// <returns>true if the thread is allowed to begin running, false if it has been disposed</returns>
        public bool BeginRunning () {
            // FIXME: Is this right?
            Event.Reset();
            var previousState = Interlocked.Exchange(ref State, State_Running);
            if (previousState == State_Disposed)
                Volatile.Write(ref State, State_Disposed);
            return (previousState != State_Disposed);
        }

        /// <summary>
        /// Wakes any sleeping threads and sets a flag indicating that any running threads should not sleep
        ///  until at least one of them runs again once
        /// </summary>
        public void Wake () {
            var oldState = Interlocked.Exchange(ref State, State_WakeRequested);
            if (oldState == State_Disposed) {
                Volatile.Write(ref State, State_Disposed);
                return;
            }
            if (oldState == State_Sleeping) {
                Thread.Yield();
                // FIXME: It might be faster to check for State_Sleeping here, but I think that may be a race?
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
            var oldState = Interlocked.CompareExchange(ref State, State_Sleeping, State_Running);
            if (oldState != State_Running)
                return true;

            Thread.Yield();
            var newState = Volatile.Read(ref State);
            if (newState != State_Sleeping) {
                if (newState != State_Disposed)
                    Volatile.Write(ref State, State_Running);
                return true;
            }

            var result = Event.Wait(timeoutMs);
            Interlocked.CompareExchange(ref State, State_Running, State_Sleeping);
            return result;
        }

        public void Dispose () {
            Volatile.Write(ref State, State_Disposed);
        }
    }
}

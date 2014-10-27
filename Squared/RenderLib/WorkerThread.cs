#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Security.Permissions;
using System.Runtime.InteropServices;

namespace Squared.Render.Internal {
    public class WorkerThread : IDisposable {
        public readonly Thread Thread;
        public readonly Action<WorkerThread> Function;

        public volatile object Tag = null;

        private volatile int _PendingWork = 0;
        private volatile int _ThreadRunning = 0;
        private volatile int _ThreadWaiting = 0;
        private volatile Exception _PendingError = null;

        private readonly AutoResetEvent _WakeSignal = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _CompletedSignal = new ManualResetEventSlim(true);
        private readonly ManualResetEventSlim _StartedSignal = new ManualResetEventSlim(false);

        private volatile bool _Disposed = false;

        public WorkerThread (Action<WorkerThread> function) {
            Function = function;

            Thread = new Thread(WorkerFn);
            Thread.IsBackground = true;
#if SDL2
            // :trollface: -flibit
            if (function.Method.Name.Equals("ThreadedDraw"))
            {
                return;
            }
            else
            {
                System.Console.WriteLine("WorkerThread used with " + function.Method.Name);
                throw new Exception("This is used for something?! -flibit");
            }
#else
            Thread.Start();
#endif
        }

        public void RequestWork () {
            if (_ThreadRunning == 0)
                _StartedSignal.Wait();

            WaitForPendingWork();

            _PendingWork = 1;

            _CompletedSignal.Reset();
            _WakeSignal.Set();
        }

        public void WaitForPendingWork () {
            Exception pe = Interlocked.Exchange(ref _PendingError, null);
            if (pe != null)
                throw pe;

            _CompletedSignal.Wait();
        }

        private void WorkerFn () {
            while (!_Disposed) {
                if (Interlocked.Exchange(ref _ThreadRunning, 1) == 0)
                    _StartedSignal.Set();

                Interlocked.Increment(ref _ThreadWaiting);
                _WakeSignal.WaitOne();
                Interlocked.Decrement(ref _ThreadWaiting);
                if (_Disposed)
                    break;

                if (_PendingWork > 0) {
                    try {
                        Function(this);
                    } catch (Exception ex) {
                        if (Debugger.IsAttached)
                            Debugger.Break();

                        _PendingError = new Exception("An error occurred in a worker thread", ex);
                    }

                    Interlocked.Decrement(ref _PendingWork);
                }

                _CompletedSignal.Set();
            }
        }

        public bool IsWorkPending {
            get {
                return (_ThreadWaiting == 0) || (_PendingWork != 0);
            }
        }

        public void Dispose () {
            _Disposed = true;
            _WakeSignal.Set();
#if SDL2
            // :trollface part 2 electric nothreadsaboo: -flibit
            if (Thread.IsAlive)
#endif
            Thread.Join();
            _CompletedSignal.Set();
        }
    }
}

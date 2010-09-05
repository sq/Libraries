#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace Squared.Render.Internal {
    public class WorkerThread : IDisposable {
        public readonly Thread Thread;
        public readonly Action<WorkerThread> Function;
        public readonly int ProcessorAffinity;

        public volatile object Tag = null;

        private volatile int _PendingWork = 0;
        private volatile int _ThreadRunning = 0;
        private volatile int _ThreadWaiting = 0;
        private volatile Exception _PendingError = null;

#if !XBOX
        private object _WakeLock = new object();
        private object _CompletedLock = new object();
        private object _StartedLock = new object();
#else
        private AutoResetEvent _WakeSignal = new AutoResetEvent(false);
        private AutoResetEvent _CompletedSignal = new AutoResetEvent(false);
        private AutoResetEvent _StartedSignal = new AutoResetEvent(false);
#endif

        public WorkerThread (Action<WorkerThread> function, int processorAffinity) {
            ProcessorAffinity = processorAffinity;
            Function = function;

            Thread = new Thread(WorkerFn);
            Thread.IsBackground = true;
            Thread.Start();
        }

        public void RequestWork () {
            if (_ThreadWaiting == 0)
                WaitForPendingWork();

            Interlocked.Increment(ref _PendingWork);

#if !XBOX
            if (_ThreadRunning == 0)
                lock (_StartedLock)
                    if (_ThreadRunning == 0)
                        Monitor.Wait(_StartedLock);
#else
            if (_ThreadRunning == 0)
                _StartedSignal.WaitOne();
#endif

#if !XBOX
            lock (_WakeLock)
                Monitor.Pulse(_WakeLock);
#else
            _WakeSignal.Set();
#endif
        }

        public void WaitForPendingWork () {
            Exception pe = Interlocked.Exchange(ref _PendingError, null);
            if (pe != null)
                throw pe;

            while ((_ThreadWaiting == 0) || (_PendingWork != 0)) {
#if !XBOX
                lock (_CompletedLock) {
                    if ((_ThreadWaiting == 0) || (_PendingWork != 0))
                        Monitor.Wait(_CompletedLock);
                }
#else
                _CompletedSignal.WaitOne();
#endif
            }
        }

        private void WorkerFn () {
#if XBOX
            Thread.CurrentThread.SetProcessorAffinity(ProcessorAffinity);        
#endif

            Interlocked.Increment(ref _ThreadWaiting);
#if !XBOX
            lock (_WakeLock)
            while (true) {
                if (Interlocked.Exchange(ref _ThreadRunning, 1) == 0)
                    lock (_StartedLock)
                        Monitor.PulseAll(_StartedLock);
                
                Monitor.Wait(_WakeLock);
                Interlocked.Decrement(ref _ThreadWaiting);
#else
            while (true) {
                if (Interlocked.Exchange(ref _ThreadRunning, 1) == 0)
                    _StartedSignal.Set();

                _WakeSignal.WaitOne();
                _WakeSignal.Reset();
                Interlocked.Decrement(ref _ThreadWaiting);
#endif

                while (_PendingWork > 0) {
                    try {
                        Function(this);
                    } catch (Exception ex) {
                        if (Debugger.IsAttached)
                            Debugger.Break();

                        _PendingError = new Exception("An error occurred in a worker thread", ex);
                    }

                    Interlocked.Decrement(ref _PendingWork);
                }

                Interlocked.Increment(ref _ThreadWaiting);
#if !XBOX
                lock (_CompletedLock)
                    Monitor.PulseAll(_CompletedLock);
#else
                _CompletedSignal.Set();
#endif
            }
        }

        public void Dispose () {
            Thread.Abort();
#if !XBOX
            lock (_WakeLock)
                Monitor.PulseAll(_WakeLock);
#else
            _WakeSignal.Set();
#endif

            Thread.Join();

#if !XBOX
            lock (_CompletedLock)
                Monitor.PulseAll(_CompletedLock);
#else
            _CompletedSignal.Set();
#endif
        }
    }
}

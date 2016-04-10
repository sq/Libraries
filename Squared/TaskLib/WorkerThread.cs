using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task.Internal {
    public delegate void WorkerThreadFunc<in T> (T workItems, ManualResetEventSlim newWorkItemEvent);

    public class WorkerThread<Container> : IDisposable
        where Container : new() {
        private WorkerThreadFunc<Container> _ThreadFunc;
        private Thread _Thread = null;
        private ManualResetEventSlim _WakeEvent = new ManualResetEventSlim(false);
        private Container _WorkItems = new Container();
        private ThreadPriority? _Priority;
        private bool _IsDisposed = false;
        private string _ThreadName;

        public WorkerThread (WorkerThreadFunc<Container> threadFunc, ThreadPriority? priority = null, string threadName = null) {
            _ThreadFunc = threadFunc;

            const int maxNameLength = 48;
            var tfn = _ThreadFunc.GetType().ToString();
            if (tfn.Length > maxNameLength)
                tfn = tfn.Substring(tfn.Length - maxNameLength, maxNameLength);

            _ThreadName = threadName ?? String.Format("{0} {1:X8}", tfn, this.GetHashCode());

            _Priority = priority;
        }

        public Container WorkItems {
            get {
                return _WorkItems;
            }
        }

        public Thread Thread {
            get {
                return _Thread;
            }
        }

        public void Wake () {
            if (_IsDisposed)
                return;

            if (_WakeEvent != null)
                _WakeEvent.Set();

            if (_Thread == null) {
                var newThread = new Thread(() => {
                    try {
                        var wi = _WorkItems;
                        var we = _WakeEvent;

                        // If either of these fields are null, we've probably been disposed
                        if ((wi != null) && (we != null))
                            _ThreadFunc(wi, we);
                    } catch (ThreadInterruptedException) {
                    } catch (ThreadAbortException) {                        
                    }

                    var me = Interlocked.Exchange(ref _Thread, null);
                    if (me == Thread.CurrentThread)
                        OnThreadTerminated(Thread.CurrentThread);

                });

                if (_Priority.HasValue)
                    newThread.Priority = _Priority.Value;

                newThread.IsBackground = true;
                newThread.Name = _ThreadName;

                if (Interlocked.CompareExchange(ref _Thread, newThread, null) == null)
                    newThread.Start();
            }
        }

        private void OnThreadTerminated (Thread theThread) {
            if (_IsDisposed)
                return;

            var we = _WakeEvent;
            if ((we != null) && we.Wait(1))
                Wake();
        }

        public void Dispose () {
            if (_IsDisposed)
                return;

            _IsDisposed = true;
            Thread.MemoryBarrier();

            var wakeEvent = Interlocked.Exchange(ref _WakeEvent, null);

            var thread = Interlocked.Exchange(ref _Thread, null);
            if (thread != null) {
                thread.Interrupt();
            }

            if (wakeEvent != null)
                wakeEvent.Set();
        }
    }
}

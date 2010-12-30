using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

#if XBOX
namespace System.Threading {
    public enum ThreadPriority {
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with any other
        //     priority.
        Lowest = 0,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with Normal priority
        //     and before those with Lowest priority.
        BelowNormal = 1,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with AboveNormal
        //     priority and before those with BelowNormal priority. Threads have Normal
        //     priority by default.
        Normal = 2,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled after threads with Highest priority
        //     and before those with Normal priority.
        AboveNormal = 3,
        //
        // Summary:
        //     The System.Threading.Thread can be scheduled before threads with any other
        //     priority.
        Highest = 4,
    }
}
#endif

namespace Squared.Task {
    delegate void WorkerThreadFunc<T> (T workItems, ManualResetEvent newWorkItemEvent);

    internal class WorkerThread<Container> : IDisposable
        where Container : new() {
        private WorkerThreadFunc<Container> _ThreadFunc;
        private Thread _Thread = null;
        private ManualResetEvent _WakeEvent = new ManualResetEvent(false);
        private Container _WorkItems = new Container();
        private ThreadPriority _Priority;

        public WorkerThread (WorkerThreadFunc<Container> threadFunc, ThreadPriority priority) {
            _ThreadFunc = threadFunc;
            _Priority = priority;
        }

        public Container WorkItems {
            get {
                return _WorkItems;
            }
        }

        public void Wake () {
            if (_WakeEvent != null)
                _WakeEvent.Set();

            if (_Thread == null) {
                _Thread = new Thread(() => {
                    try {
                        _ThreadFunc(_WorkItems, _WakeEvent);
#if !XBOX
                    } catch (ThreadInterruptedException) {
#endif
                    } catch (ThreadAbortException) {                        
                    }
                });
#if !XBOX
                _Thread.Priority = _Priority;
#endif
                _Thread.IsBackground = true;
                _Thread.Name = String.Format("{0}_{1}", _ThreadFunc.Method.Name, this.GetHashCode());
                _Thread.Start();
            }
        }

        public void Dispose () {
            if (_Thread != null) {
#if !XBOX
                _Thread.Interrupt();
#endif
                _Thread.Join(10);
                _Thread.Abort();
                _Thread = null;
            }

            _WakeEvent = null;
        }
    }
}

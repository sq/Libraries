using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

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
            _WakeEvent.Set();

            if (_Thread == null) {
                _Thread = new Thread(() => {
                    try {
                        _ThreadFunc(_WorkItems, _WakeEvent);
                    } catch (ThreadInterruptedException) {
                    }
                });
                _Thread.Priority = _Priority;
                _Thread.IsBackground = true;
                _Thread.Name = String.Format("WorkerThread{0}", this.GetHashCode());
                _Thread.Start();
            }
        }

        public void Dispose () {
            if (_Thread != null) {
                _Thread.Interrupt();
                _Thread.Abort();
                _Thread = null;
            }

            _WakeEvent = null;
        }
    }
}

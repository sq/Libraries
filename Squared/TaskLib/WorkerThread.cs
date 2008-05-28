using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task {
    delegate void WorkerThreadFunc<T> (List<T> workItems, ManualResetEvent newWorkItemEvent);

    internal class WorkerThread<T> : IDisposable {
        private WorkerThreadFunc<T> _ThreadFunc;
        private Thread _Thread = null;
        private ManualResetEvent _WakeEvent = new ManualResetEvent(false);
        private List<T> _WorkItems = new List<T>();
        private ThreadPriority _Priority;

        public WorkerThread (WorkerThreadFunc<T> threadFunc, ThreadPriority priority) {
            _ThreadFunc = threadFunc;
            _Priority = priority;
        }

        public void DequeueWorkItem (T item) {
            lock (_WorkItems) {
                _WorkItems.Remove(item);
                _WakeEvent.Set();
            }
        }

        public void QueueWorkItem (T item) {
            lock (_WorkItems) {
                _WorkItems.Add(item);
                _WakeEvent.Set();
            }

            if (_Thread == null) {
                _Thread = new Thread(() => {
                    try {
                        _ThreadFunc(_WorkItems, _WakeEvent);
                    } catch (ThreadInterruptedException) {
                    }
                });
                _Thread.Priority = _Priority;
                _Thread.IsBackground = true;
                _Thread.Name = String.Format("WorkerThread<{0}>", typeof(T).Name);
                _Thread.Start();
            }
        }

        public void Dispose () {
            lock (_WorkItems) {
                _WorkItems.Clear();
            }

            if (_Thread != null) {
                _Thread.Interrupt();
                _Thread.Abort();
                _Thread = null;
            }

            _WakeEvent = null;
        }
    }
}

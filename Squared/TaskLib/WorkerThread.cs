using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task {
    delegate void WorkerThreadFunc<T> (List<T> workItems, AutoResetEvent newWorkItemEvent);

    internal class WorkerThread<T> : IDisposable {
        private WorkerThreadFunc<T> _ThreadFunc;
        private Thread _Thread = null;
        private AutoResetEvent _WakeEvent = new AutoResetEvent(false);
        private List<T> _WorkItems = new List<T>();
        private ThreadPriority _Priority;

        public WorkerThread (WorkerThreadFunc<T> threadFunc, ThreadPriority priority) {
            _ThreadFunc = threadFunc;
            _Priority = priority;
        }

        public void QueueWorkItem (T item) {
            lock (_WorkItems) {
                _WorkItems.Add(item);
                _WakeEvent.Set();
            }

            if (_Thread == null) {
                _Thread = new Thread(() => {
                    _ThreadFunc(_WorkItems, _WakeEvent);
                });
                _Thread.Priority = _Priority;
                _Thread.IsBackground = true;
                _Thread.Name = String.Format("WorkerThread<{0}>", typeof(T).Name);
                _Thread.Start();
            }
        }

        public void Dispose () {
            if (_Thread != null) {
                _Thread.Abort();
                _Thread = null;
            }

            lock (_WorkItems) {
                _WorkItems.Clear();
            }
        }
    }
}

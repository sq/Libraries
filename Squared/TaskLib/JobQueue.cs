using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Security;

namespace Squared.Task {
    public interface IJobQueue : IDisposable {
        void QueueWorkItem (Action item);
        void Clear ();
        void Step ();
        void WaitForFuture (Future future);
        bool WaitForWorkItems (double timeout);
        int Count { get; }
    }

    public static partial class JobQueue {
        /// <summary>
        /// Please don't use this unless you're really sure you need it! Things like Sleep don't work right if you use it.
        /// </summary>
        /// <returns></returns>
        [Obsolete("Single-threaded job queues are not recommended for use. Use a multi-threaded job queue instead.", false)]
        public static IJobQueue SingleThreaded () {
            return new SingleThreadedJobQueue();
        }

        public static IJobQueue MultiThreaded () {
            return new MultiThreadedJobQueue();
        }
    }

    public class SingleThreadedJobQueue : IJobQueue {
        protected Queue<Action> _Queue = new Queue<Action>();

        public virtual void QueueWorkItem (Action item) {
            _Queue.Enqueue(item);
        }

        public void Step () {
            Action item = null;
            do {
                item = GetNextWorkItem();
                if (item != null)
                    item();
            } while (item != null);
        }

        public void WaitForFuture (Future future) {
            Action item = null;
            while (!future.Completed) {
                item = GetNextWorkItem();
                if (item != null)
                    item();
            }
        }

        protected virtual Action GetNextWorkItem () {
            if (_Queue.Count == 0)
                return null;

            return _Queue.Dequeue();
        }

        public virtual bool WaitForWorkItems (double timeout) {
            return (Count > 0);
        }

        public virtual void Clear () {
            _Queue.Clear();
        }

        public virtual int Count {
            get {
                int count = _Queue.Count;
                return count;
            }
        }

        public virtual void Dispose () {
            Clear();
        }
    }

    public class MultiThreadedJobQueue : SingleThreadedJobQueue {
        private ManualResetEvent _NewWorkItemEvent = new ManualResetEvent(false);

        public override void QueueWorkItem (Action item) {
            lock (_Queue)
                base.QueueWorkItem(item);

            _NewWorkItemEvent.Set();
        }

        protected override Action GetNextWorkItem() {
            lock (_Queue)
                return base.GetNextWorkItem();
        }

        public override bool WaitForWorkItems (double timeout) {
            lock (_Queue) {
                if (_Queue.Count != 0)
                    return true;
                else
                    _NewWorkItemEvent.Reset();
            }

            if (timeout > 0) {
                bool result = _NewWorkItemEvent.WaitOne((int)Math.Floor(timeout * 1000), true);
                return result;
            } else {
                _NewWorkItemEvent.WaitOne();
                return true;
            }
        }

        public override void Clear () {
            lock (_Queue)
                base.Clear();
        }

        public override int Count {
            get {
                lock (_Queue)
                    return base.Count;
            }
        }

        public override void Dispose () {
            base.Dispose();
        }
    }
}

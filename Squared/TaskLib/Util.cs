using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Squared.Task {
    public struct WaitForNextStep : ISchedulable {
        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.AddStepListener(future.Complete);
        }
    }

    public class WaitWithTimeout : ISchedulable {
        Future _Future, _TaskFuture, _SleepFuture;
        double _Timeout;

        public WaitWithTimeout (Future future, double timeout) {
            _Future = future;
            _Timeout = timeout;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            _SleepFuture = scheduler.Start(new Sleep(_Timeout));
            _TaskFuture = scheduler.Start(new WaitForFirst(_Future, _SleepFuture));
            _TaskFuture.RegisterOnComplete((result, error) => {
                if (result == _SleepFuture)
                    future.Fail(new TimeoutException("WaitWithTimeout timed out."));
                else
                    future.Complete();
            });
        }
    }

    public class TaskRunner : ISchedulable {
        IEnumerator<object> _Task;
        TaskExecutionPolicy _ExecutionPolicy;
        Future _Future;

        public TaskRunner (IEnumerator<object> task) 
            : this(task, TaskExecutionPolicy.RunWhileFutureLives) {
        }

        public TaskRunner (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
            _Task = task;
            _Future = null;
            _ExecutionPolicy = executionPolicy;
        }

        void Completed (object result, Exception error) {
            this._Future.Complete(result);
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            this._Future = scheduler.Start(_Task, _ExecutionPolicy);
            future.Bind(this._Future);
        }

        public object Result {
            get {
                return _Future.Result;
            }
        }
    }

    public struct SleepUntil : ISchedulable {
        long _EndWhen;

        public SleepUntil (long when) {
            _EndWhen = when;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueSleep(_EndWhen, future);
        }
    }

    public struct Sleep : ISchedulable {
        long _EndWhen;

        public Sleep (double duration) {
            _EndWhen = DateTime.Now.AddSeconds(duration).Ticks;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueSleep(_EndWhen, future);
        }
    }

    public struct WaitForWaitHandle : ISchedulable {
        WaitHandle _Handle;

        public WaitForWaitHandle (WaitHandle handle) {
            _Handle = handle;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueWait(_Handle, future);
        }
    }

    public class WaitForFirst : ISchedulable {
        protected List<Future> _Futures = new List<Future>();
        Future _CompositeFuture;

        public WaitForFirst (IEnumerable<Future> futures) {
            _Futures.AddRange(futures);
        }

        public WaitForFirst (params Future[] futures) {
            _Futures.AddRange(futures);
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            _CompositeFuture = future;
            foreach (Future _ in _Futures.ToArray()) {
                Future f = _;
                f.RegisterOnComplete((result, error) => {
                    lock (this._CompositeFuture) {
                        if (!this._CompositeFuture.Completed) {
                            this._Futures.Clear();
                            this._CompositeFuture.Complete(f);
                        }
                    }
                });
            }
        }

    }

    public class WaitForAll : ISchedulable {
        protected List<Future> _Futures = new List<Future>();
        Future _CompositeFuture;

        public WaitForAll () {
        }

        public WaitForAll (IEnumerable<Future> futures) {
            _Futures.AddRange(futures);
        }

        public WaitForAll (params Future[] futures) {
            _Futures.AddRange(futures);
        }

        protected virtual void OnSchedule (TaskScheduler scheduler) {
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            _CompositeFuture = future;
            OnSchedule(scheduler);
            foreach (Future _ in _Futures.ToArray()) {
                Future f = _;
                f.RegisterOnComplete((result, error) => {
                    int count;
                    lock (this._Futures) {
                        this._Futures.Remove(f);
                        count = this._Futures.Count;
                    }
                    if (count == 0)
                        this._CompositeFuture.Complete();
                });
            }
        }
    }

    public class WaitForWaitHandles : WaitForAll {
        IEnumerable<WaitHandle> _Handles;

        public WaitForWaitHandles (IEnumerable<WaitHandle> handles)
            : base() {
            _Handles = handles;
        }

        public WaitForWaitHandles (params WaitHandle[] handles)
            : base() {
            _Handles = handles;
        }

        protected override void OnSchedule (TaskScheduler scheduler) {
            foreach (WaitHandle handle in _Handles) {
                var f = new Future();
                scheduler.QueueWait(handle, f);
                _Futures.Add(f);
            }

            base.OnSchedule(scheduler);
        }
    }
}

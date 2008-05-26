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
            _TaskFuture = Future.WaitForFirst(_Future, _SleepFuture);
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

    public class WaitForWaitHandles : ISchedulable {
        IEnumerable<WaitHandle> _Handles;

        public WaitForWaitHandles (IEnumerable<WaitHandle> handles) {
            _Handles = handles;
        }

        public WaitForWaitHandles (params WaitHandle[] handles) {
            _Handles = handles;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            List<Future> futures = new List<Future>();
            foreach (WaitHandle handle in _Handles) {
                var f = new Future();
                scheduler.QueueWait(handle, f);
                futures.Add(f);
            }

            future.Bind(Future.WaitForAll(futures));
        }
    }
}

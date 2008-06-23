using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
    /// <summary>
    /// Schedules your task to continue execution at the end of the current step.
    /// </summary>
    public struct Yield : ISchedulable {
        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            future.Complete();
        }
    }

    /// <summary>
    /// Schedules your task to continue execution at the beginning of the next step.
    /// </summary>
    public struct WaitForNextStep : ISchedulable {
        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.AddStepListener(future.Complete);
        }
    }

    /// <summary>
    /// Completes when the specified future completes, or when (timeout) seconds have elapsed, whichever comes first.
    /// </summary>
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
            _TaskFuture.RegisterOnComplete((f, result, error) => {
                if (result == _SleepFuture)
                    future.Fail(new TimeoutException("WaitWithTimeout timed out."));
                else
                    future.Complete();
            });
        }
    }

    /// <summary>
    /// Starts a task and stores the resulting future.
    /// </summary>
    public class Start : ISchedulable {
        IEnumerator<object> _Task;
        TaskExecutionPolicy _ExecutionPolicy;
        Future _Future;

        public Start (IEnumerator<object> task) 
            : this(task, TaskExecutionPolicy.RunWhileFutureLives) {
        }

        public Start (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
            _Task = task;
            _ExecutionPolicy = executionPolicy;
            _Future = null;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            _Future = scheduler.Start(_Task, _ExecutionPolicy);
            future.Complete(_Future);
        }

        public Future Future {
            get {
                return _Future;
            }
        }
    }

    /// <summary>
    /// Schedules a task to run to completion.
    /// </summary>
    public class RunToCompletion : ISchedulable {
        IEnumerator<object> _Task;
        TaskExecutionPolicy _ExecutionPolicy;
        Future _Future;

        public RunToCompletion (IEnumerator<object> task) 
            : this(task, TaskExecutionPolicy.RunWhileFutureLives) {
        }

        public RunToCompletion (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
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

    /// <summary>
    /// Waits until a specified time (in ticks).
    /// </summary>
    public struct SleepUntil : ISchedulable {
        long _EndWhen;

        public SleepUntil (long when) {
            _EndWhen = when;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueSleep(_EndWhen, future);
        }
    }

    /// <summary>
    /// Waits for a specified amount of time (in seconds).
    /// </summary>
    public struct Sleep : ISchedulable {
        long _EndWhen;

        public Sleep (double duration) {
            _EndWhen = Time.Ticks + TimeSpan.FromSeconds(duration).Ticks;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueSleep(_EndWhen, future);
        }
    }

    /// <summary>
    /// Waits for a WaitHandle to become set.
    /// </summary>
    public struct WaitForWaitHandle : ISchedulable {
        WaitHandle _Handle;

        public WaitForWaitHandle (WaitHandle handle) {
            _Handle = handle;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueWait(_Handle, future);
        }
    }

    /// <summary>
    /// Waits for multiple WaitHandles to all become set.
    /// </summary>
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

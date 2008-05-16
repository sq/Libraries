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

    public abstract class WaiterBase : ISchedulable {
        Future _TaskFuture;

        protected abstract IEnumerator<object> WaiterTask ();

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            _TaskFuture = scheduler.Start(WaiterTask());
            future.Bind(_TaskFuture);
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

    public class WaitForFirst : WaiterBase {
        IEnumerable<Future> _Futures;

        public WaitForFirst (IEnumerable<Future> futures)
            : base() {
            _Futures = futures;
        }

        public WaitForFirst (params Future[] futures)
            : base() {
            _Futures = futures;
        }

        protected override IEnumerator<object> WaiterTask () {
            while (true) {
                foreach (Future future in _Futures)
                    if (future.Completed)
                        yield return new Result(future);

                yield return new WaitForNextStep();
            }
        }
    }

    public class TaskRunner : ISchedulable {
        IEnumerator<object> _Task;
        Future _Future;

        public TaskRunner (IEnumerator<object> task) {
            _Task = task;
            _Future = null;
        }

        void Completed (object result, Exception error) {
            this._Future.Complete(result);
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            this._Future = scheduler.Start(_Task);
            future.Bind(this._Future);
        }

        public object Result {
            get {
                return _Future.Result;
            }
        }
    }

    public class SchedulableGeneratorThunk : ISchedulable {
        IEnumerator<object> _Task;
        WeakReference _FutureWr;

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            IEnumerator<object> task = _Task;
            _FutureWr = new WeakReference(future);
            scheduler.QueueWorkItem(() => { this.Step(scheduler); });
        }

        void Step (TaskScheduler scheduler) {
            Future future = _FutureWr.Target as Future;
            // Future was collected, so the task ends here
            if (future == null) {
                System.Diagnostics.Debug.WriteLine(String.Format("Task {0} aborted because its future was collected", _Task));
                return;
            }

            try {
                if (!_Task.MoveNext()) {
                    // Completed with no result
                    future.Complete(null);
                    return;
                }

                object value = _Task.Current;
                if (value is ISchedulable) {
                    Future temp = scheduler.Start(value as ISchedulable);
                    scheduler.HoldFuture(temp);
                    temp.RegisterOnComplete((result, error) => {
                        scheduler.QueueWorkItem(() => {
                            this.Step(scheduler);
                            scheduler.ReleaseFuture(temp);
                        });
                    });
                } else if (value is Future) {
                    Future f = (Future)value;
                    OnComplete oc = (result, error) => {
                        scheduler.QueueWorkItem(() => {
                            this.Step(scheduler);
                        });
                    };
                    if (f.Completed)
                        oc(null, null);
                    else
                        f.RegisterOnComplete(oc);
                } else {
                    // Completed with a result
                    if (value is Result)
                        value = ((Result)value).Value;

                    future.Complete(value);
                }
            } catch (Exception ex) {
                future.Fail(ex);
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

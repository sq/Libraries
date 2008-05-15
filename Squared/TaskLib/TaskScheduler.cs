using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Task {
    delegate object SleeperDelegate(long now);

    public enum TaskExecutionPolicy {
        RunWhileFutureLives,
        RunUntilComplete,
        Default = RunWhileFutureLives
    }

    public struct Result {
        public object Value;

        public Result (object value) {
            Value = value;
        }
    }

    public interface ISchedulable {
        void Schedule (TaskScheduler scheduler, Future future);
    }

    public struct WaitForNextStep : ISchedulable {
        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.AddStepListener(() => { future.Complete(null); });
        }
    }

    public abstract class WaiterBase : ISchedulable {
        Future _TaskFuture;

        public WaiterBase () {
        }

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

    public class WaitForAll : WaiterBase {
        IEnumerable<Future> _Futures;

        public WaitForAll (IEnumerable<Future> futures)
            : base() {
            _Futures = futures;
        }

        public WaitForAll (params Future[] futures)
            : base() {
            _Futures = futures;
        }

        protected override IEnumerator<object> WaiterTask () {
            foreach (Future future in _Futures)
                yield return future;
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

    public struct Sleep : ISchedulable {
        long _EndWhen;

        public Sleep (double duration) {
            _EndWhen = DateTime.Now.Ticks + TimeSpan.FromSeconds(duration).Ticks;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            scheduler.QueueSleep(_EndWhen, future);
        }
    }

    public class TaskScheduler : IDisposable {
        const long SleepFudgeFactor = 10000;

        private List<Future> _HeldFutures = new List<Future>();
        private Queue<Action> _JobQueue = new Queue<Action>();
        private Queue<Action> _StepListeners = new Queue<Action>();
        private List<SleeperDelegate> _PendingSleeps = new List<SleeperDelegate>();
        private Thread _SleepWorkerThread = null;
        private AutoResetEvent _NewSleepEvent = new AutoResetEvent(false);

        public Future Start (ISchedulable task, TaskExecutionPolicy executionPolicy) {
            var future = new Future();

            task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunUntilComplete:
                    this.HoldFuture(future);
                    future.RegisterOnComplete((result, error) => {
                        this.ReleaseFuture(future);
                    });
                    break;
                default:
                    break;
            }

            return future;
        }

        public Future Start (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
            return Start(new SchedulableGeneratorThunk(task), executionPolicy);
        }

        public Future Start (ISchedulable task) {
            return Start(task, TaskExecutionPolicy.Default);
        }

        public Future Start (IEnumerator<object> task) {
            return Start(task, TaskExecutionPolicy.Default);
        }

        public Future RunInThread (Delegate workItem, params object[] arguments) {
            var f = new Future();
            WaitCallback fn = (state) => {
                try {
                    var result = workItem.DynamicInvoke(arguments);
                    f.Complete(result);
                } catch (Exception ex) {
                    f.Fail(ex);
                }
            };
            ThreadPool.QueueUserWorkItem(fn);
            return f;
        }

        internal void HoldFuture (Future f) {
            _HeldFutures.Add(f);
        }

        internal void ReleaseFuture (Future f) {
            _HeldFutures.Remove(f);
        }

        internal void QueueWorkItem (Action workItem) {
            _JobQueue.Enqueue(workItem);
        }

        internal void AddStepListener (Action listener) {
            _StepListeners.Enqueue(listener);
        }

        internal static void SleepWorker (WeakReference scheduler, List<SleeperDelegate> pendingSleeps, AutoResetEvent newSleepEvent) {
            while (scheduler.IsAlive) {
                SleeperDelegate[] sleepers;
                lock (pendingSleeps) {
                    sleepers = pendingSleeps.ToArray();
                }
                if (sleepers.Length == 0) {
                    newSleepEvent.WaitOne();
                    continue;
                }

                object sleepUntil = null;
                List<SleeperDelegate> killList = new List<SleeperDelegate>();
                foreach (SleeperDelegate sleeper in sleepers) {
                    long now = DateTime.Now.Ticks;
                    object r = sleeper(now);
                    if (r == null) {
                        killList.Add(sleeper);
                    } else if (sleepUntil != null) {
                        sleepUntil = Math.Min((long)sleepUntil, (long)r);
                    } else {
                        sleepUntil = r;
                    }
                }

                lock (pendingSleeps) {
                    pendingSleeps.RemoveAll(killList.Contains);
                }

                if (sleepUntil != null) {
                    long sleepUntilValue = (long)sleepUntil;
                    long timeToSleep = (sleepUntilValue - DateTime.Now.Ticks) - SleepFudgeFactor;
                    if (timeToSleep > 0) {
                        System.Diagnostics.Debug.WriteLine(String.Format("Sleeping for {0} ticks", timeToSleep));
                        using (var tempHandle = new ManualResetEvent(false)) {
                            WaitHandle.WaitAny(new WaitHandle[] { tempHandle }, TimeSpan.FromTicks(timeToSleep), false);
                        }
                    } else {
                        while (DateTime.Now.Ticks < sleepUntilValue) {
                        }
                    }
                }
            }
        }

        internal static SleeperDelegate BuildSleeper (long completeWhen, Future future) {
            SleeperDelegate sleeper = (long now) => {
                long ticksLeft = Math.Max(completeWhen - now, 0);
                if (ticksLeft == 0) {
                    future.Complete();
                    return null;
                } else {
                    return completeWhen;
                }
            };
            return sleeper;
        }

        internal static ThreadStart BuildSleepWorker (WeakReference wr, List<SleeperDelegate> pendingSleeps, AutoResetEvent newSleepEvent) {
            ThreadStart sleepWorker = () => {
                TaskScheduler.SleepWorker(wr, pendingSleeps, newSleepEvent);
                System.Diagnostics.Debug.WriteLine("TaskScheduler.SleepWorker finished running");
            };
            return sleepWorker;
        }

        internal void QueueSleep (long completeWhen, Future future) {
            SleeperDelegate sleeper = TaskScheduler.BuildSleeper(completeWhen, future);

            lock (_PendingSleeps) {
                _PendingSleeps.Add(sleeper);
                _NewSleepEvent.Set();
            }

            if (_SleepWorkerThread == null) {
                _SleepWorkerThread = new Thread(TaskScheduler.BuildSleepWorker(new WeakReference(this), _PendingSleeps, _NewSleepEvent));
                _SleepWorkerThread.Name = "TaskScheduler._SleepWorkerThread";
                _SleepWorkerThread.IsBackground = true;
                _SleepWorkerThread.Start();
            }
        }

        public void Step () {
            while (_StepListeners.Count > 0) {
                _StepListeners.Dequeue()();
            }
            while (_JobQueue.Count > 0) {
                _JobQueue.Dequeue()();
            }
        }

        public bool HasPendingTasks {
            get {
                return (_StepListeners.Count > 0) || (_JobQueue.Count > 0);
            }
        }

        public void Dispose () {
            if (_SleepWorkerThread != null) {
                _SleepWorkerThread.Abort();
                System.Diagnostics.Debug.WriteLine("TaskScheduler.SleepWorker aborted");
                _SleepWorkerThread = null;
            }

            lock (_PendingSleeps) {
                _PendingSleeps.Clear();
            }

            _JobQueue.Clear();
            _StepListeners.Clear();
            _HeldFutures.Clear();
        }
    }
}

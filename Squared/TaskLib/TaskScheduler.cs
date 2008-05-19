using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Task {
    delegate object SleeperDelegate(long now);

    public interface ISchedulable {
        void Schedule (TaskScheduler scheduler, Future future);
    }

    public enum TaskExecutionPolicy {
        RunWhileFutureLives,
        RunUntilComplete,
        RunAsBackgroundTask,
        Default = RunWhileFutureLives
    }

    public struct Result {
        public object Value;

        public Result (object value) {
            Value = value;
        }
    }

    public class TaskException : Exception {
        public TaskException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }

    struct BoundWaitHandle {
        public WaitHandle Handle;
        public Future Future;

        public BoundWaitHandle(WaitHandle handle, Future future) {
            this.Handle = handle;
            this.Future = future;
        }
    }

    public class SchedulableGeneratorThunk : ISchedulable, IDisposable {
        IEnumerator<object> _Task;
        WeakReference _FutureWr;
        List<Future> _HeldFutures = new List<Future>();

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
        }

        public void Dispose () {
            if (_Task != null) {
                _Task.Dispose();
                _Task = null;
            }

            _HeldFutures.Clear();
            _FutureWr = null;
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            IEnumerator<object> task = _Task;
            _FutureWr = new WeakReference(future);
            scheduler.QueueWorkItem(() => { this.Step(scheduler); });
        }

        void ScheduleNextStepForSchedulable (TaskScheduler scheduler, ISchedulable value, Future future) {
            if (value is WaitForNextStep) {
                scheduler.AddStepListener(() => {
                    scheduler.QueueWorkItem(() => {
                        this.Step(scheduler);
                    });
                });
            } else {
                Future temp = scheduler.Start(value);
                _HeldFutures.Add(temp);
                temp.RegisterOnComplete((result, error) => {
                    scheduler.QueueWorkItem(() => {
                        this.Step(scheduler);
                        _HeldFutures.Remove(temp);
                    });
                });
            }
        }

        void ScheduleNextStep (TaskScheduler scheduler, Object value, Future future) {
            if (value is ISchedulable) {
                ScheduleNextStepForSchedulable(scheduler, value as ISchedulable, future);
            } else if (value is Future) {
                Future f = (Future)value;
                f.RegisterOnComplete((result, error) => {
                    scheduler.QueueWorkItem(() => {
                        this.Step(scheduler);
                    });
                });
            } else if (value is Result) {
                future.Complete(((Result)value).Value);
                Dispose();
            } else {
                future.Complete(value);
                Dispose();
            }
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
                    Dispose();
                    return;
                }

                object value = _Task.Current;
                ScheduleNextStep(scheduler, value, future);
            } catch (Exception ex) {
                future.Fail(ex);
                Dispose();
            }
        }
    }

    public class TaskScheduler : IDisposable {
        const long SleepFudgeFactor = 10000;

        private JobQueue _JobQueue = new JobQueue();
        private List<Future> _HeldFutures = new List<Future>();
        private Queue<Action> _StepListeners = new Queue<Action>();
        private WorkerThread<SleeperDelegate> _SleepWorker;
        private WorkerThread<BoundWaitHandle> _WaitWorker;

        public void WaitForWorkItems () {
            WaitForWorkItems(0);
        }

        public void WaitForWorkItems (double timeout) {
            _JobQueue.WaitForWorkItems(timeout);
        }

        public Future Start (ISchedulable task, TaskExecutionPolicy executionPolicy) {
            Future future = new Future();
            task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunUntilComplete:
                    this.HoldFuture(future);
                    future.RegisterOnComplete((result, error) => {
                        this.ReleaseFuture(future);
                    });
                    break;
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    this.HoldFuture(future);
                    future.RegisterOnComplete((result, error) => {
                        this.ReleaseFuture(future);
                        if (error != null) {
                            this.QueueWorkItem(() => {
                                throw new TaskException("Unhandled exception in background task", error);
                            });
                        }
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
                } catch (System.Reflection.TargetInvocationException ex) {
                    f.Fail(ex.InnerException);
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
            _JobQueue.QueueWorkItem(workItem);
        }

        internal void AddStepListener (Action listener) {
            _StepListeners.Enqueue(listener);
        }

        internal static void SleepWorkerThreadFunc (List<SleeperDelegate> pendingSleeps, AutoResetEvent newSleepEvent) {
            while (true) {
                SleeperDelegate[] sleepers;
                lock (pendingSleeps) {
                    sleepers = pendingSleeps.ToArray();
                }
                if (sleepers.Length == 0) {
                    newSleepEvent.WaitOne();
                    continue;
                }

                object sleepUntil = null;
                var killList = new List<SleeperDelegate>();
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
                    }

                    while (DateTime.Now.Ticks < sleepUntilValue) {
                    }
                }
            }
        }

        internal static void WaitWorkerThreadFunc (List<BoundWaitHandle> pendingWaits, AutoResetEvent newWaitEvent) {
            while (true) {
                BoundWaitHandle[] waits;
                WaitHandle[] waitHandles;
                lock (pendingWaits) {
                    waits = pendingWaits.ToArray();
                }
                waitHandles = new WaitHandle[waits.Length + 1];
                waitHandles[0] = newWaitEvent;
                for (int i = 0; i < waits.Length; i++)
                    waitHandles[i + 1] = waits[i].Handle;

                System.Diagnostics.Debug.WriteLine(String.Format("WaitWorker waiting on {0} handle(s)", waitHandles.Length - 1));
                int completedWait = WaitHandle.WaitAny(waitHandles);
                if (completedWait == 0)
                    continue;
                BoundWaitHandle w = waits[completedWait - 1];
                lock (pendingWaits) {
                    pendingWaits.Remove(w);
                }
                w.Future.Complete();
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

        public TaskScheduler (bool threadSafe) {
            _SleepWorker = new WorkerThread<SleeperDelegate>(SleepWorkerThreadFunc, ThreadPriority.AboveNormal);
            _WaitWorker = new WorkerThread<BoundWaitHandle>(WaitWorkerThreadFunc, ThreadPriority.AboveNormal);
            _JobQueue.ThreadSafe = threadSafe;
        }

        public TaskScheduler ()
            : this(false) {
        }

        internal void QueueWait (WaitHandle handle, Future future) {
            BoundWaitHandle wait = new BoundWaitHandle(handle, future);

            _WaitWorker.QueueWorkItem(wait);
        }

        internal void QueueSleep (long completeWhen, Future future) {
            SleeperDelegate sleeper = TaskScheduler.BuildSleeper(completeWhen, future);

            _SleepWorker.QueueWorkItem(sleeper);
        }

        public void Step () {
            while (_StepListeners.Count > 0)
                _StepListeners.Dequeue()();

            _JobQueue.Step();
        }

        public bool HasPendingTasks {
            get {
                return (_StepListeners.Count > 0) || (_JobQueue.Count > 0);
            }
        }

        public void Dispose () {
            _SleepWorker.Dispose();
            _WaitWorker.Dispose();

            _StepListeners.Clear();
            _HeldFutures.Clear();
        }
    }
}

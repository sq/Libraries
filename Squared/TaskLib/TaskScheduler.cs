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
        public static List<SchedulableGeneratorThunk> RunningTasks = new List<SchedulableGeneratorThunk>();
        IEnumerator<object> _Task;
        Future _Future;
        public object WakeCondition;

        public override string ToString () {
            return String.Format("<Task {0} waiting on {1}>", _Task, WakeCondition);
        }

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
        }

        public void Dispose () {
            if (_Task != null) {
                _Task.Dispose();
                _Task = null;
            }

            if (_Future != null) {
                _Future.Dispose();
                _Future = null;
            }

            try {
                RunningTasks.Remove(this);
            } catch {
            }
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            RunningTasks.Add(this);
            IEnumerator<object> task = _Task;
            _Future = future;
            scheduler.QueueWorkItem(() => { this.Step(scheduler); });
        }

        void ScheduleNextStepForSchedulable (TaskScheduler scheduler, ISchedulable value) {
            if (value is WaitForNextStep) {
                scheduler.AddStepListener(() => {
                    scheduler.QueueWorkItem(() => {
                        this.Step(scheduler);
                    });
                });
            } else {
                Future temp = scheduler.Start(value);
                temp.RegisterOnComplete((result, error) => {
                    scheduler.QueueWorkItem(() => {
                        this.Step(scheduler);
                    });
                });
            }
        }

        void ScheduleNextStep (TaskScheduler scheduler, Object value) {
            if (value is ISchedulable) {
                this.WakeCondition = value;
                ScheduleNextStepForSchedulable(scheduler, value as ISchedulable);
            } else if (value is Future) {
                this.WakeCondition = value;
                Future f = (Future)value;
                f.RegisterOnComplete((result, error) => {
                    scheduler.QueueWorkItem(() => {
                        this.Step(scheduler);
                    });
                });
            } else if (value is Result) {
                _Future.Complete(((Result)value).Value);
                Dispose();
            } else {
                _Future.Complete(value);
                Dispose();
            }
        }

        void Step (TaskScheduler scheduler) {
            WakeCondition = null;

            if (_Future.Disposed) {
                System.Diagnostics.Debug.WriteLine(String.Format("Task {0} aborted because its future was disposed", _Task));
                return;
            }

            try {
                if (!_Task.MoveNext()) {
                    // Completed with no result
                    _Future.Complete(null);
                    Dispose();
                    return;
                }

                object value = _Task.Current;
                ScheduleNextStep(scheduler, value);
            } catch (Exception ex) {
                _Future.Fail(ex);
                Dispose();
            }
        }
    }

    public class TaskScheduler : IDisposable {
        const long SleepFudgeFactor = 100;

        private JobQueue _JobQueue = new JobQueue();
        private Queue<Action> _StepListeners = new Queue<Action>();
        private WorkerThread<SleeperDelegate> _SleepWorker;
        private WorkerThread<BoundWaitHandle> _WaitWorker;

        public bool WaitForWorkItems () {
            return WaitForWorkItems(0);
        }

        public bool WaitForWorkItems (double timeout) {
            return _JobQueue.WaitForWorkItems(timeout);
        }

        public Future Start (ISchedulable task, TaskExecutionPolicy executionPolicy) {
            Future future = new Future();
            task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnComplete((result, error) => {
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

        public void QueueWorkItem (Action workItem) {
            _JobQueue.QueueWorkItem(workItem);
        }

        internal void AddStepListener (Action listener) {
            _StepListeners.Enqueue(listener);
        }

        internal static void SleepWorkerThreadFunc (List<SleeperDelegate> pendingSleeps, ManualResetEvent newSleepEvent) {
            var tempHandle = new ManualResetEvent(false);
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
                    long timeToSleep = (sleepUntilValue - DateTime.Now.Ticks) + SleepFudgeFactor;
                    if (timeToSleep > 0) {
                        System.Diagnostics.Debug.WriteLine(String.Format("Sleeping for {0} ticks", timeToSleep));
                        try {
                            int result = WaitHandle.WaitAny(new WaitHandle[] { newSleepEvent }, TimeSpan.FromTicks(timeToSleep), true);
                            if (result == 0) {
                                newSleepEvent.Reset();
                                continue;
                            }
                        } catch (ThreadInterruptedException) {
                            break;
                        }
                    }
                }
            }
        }

        internal static void WaitWorkerThreadFunc (List<BoundWaitHandle> pendingWaits, ManualResetEvent newWaitEvent) {
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
                try {
                    int completedWait = WaitHandle.WaitAny(waitHandles);
                    if (completedWait == 0) {
                        newWaitEvent.Reset();
                        continue;
                    }
                    BoundWaitHandle w = waits[completedWait - 1];
                    lock (pendingWaits) {
                        pendingWaits.Remove(w);
                    }
                    w.Future.Complete();
                } catch (ThreadInterruptedException) {
                    break;
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
        }
    }
}

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
        IEnumerator<object> _Task;
        Future _Future;
        public Future WakeCondition;
        TaskScheduler _Scheduler;

        public override string ToString () {
            return String.Format("<Task {0} waiting on {1}>", _Task, WakeCondition);
        }

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
        }

        public void Dispose () {
            if (WakeCondition != null) {
                WakeCondition.Dispose();
                WakeCondition = null;
            }

            if (_Task != null) {
                _Task.Dispose();
                _Task = null;
            }

            if (_Future != null) {
                _Future.Dispose();
                _Future = null;
            }
        }

        void OnDisposed (Future _) {
            System.Diagnostics.Debug.WriteLine(String.Format("Task {0}'s future disposed. Aborting.", _Task));
            Dispose();
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, Future future) {
            IEnumerator<object> task = _Task;
            _Future = future;
            _Scheduler = scheduler;
            _Future.RegisterOnDispose(this.OnDisposed);
            QueueStep();
        }

        void QueueStepOnComplete (Future f, object r, Exception e) {
            _Scheduler.QueueWorkItem(this.Step);
        }

        void QueueStep () {
            _Scheduler.QueueWorkItem(this.Step);
        }

        void ScheduleNextStepForSchedulable (ISchedulable value) {
            if (value is WaitForNextStep) {
                _Scheduler.AddStepListener(QueueStep);
            } else if (value is Yield) {
                QueueStep();
            } else {
                Future temp = _Scheduler.Start(value);
                this.WakeCondition = temp;
                temp.RegisterOnComplete(QueueStepOnComplete);
            }
        }

        void ScheduleNextStep (Object value) {
            if (value is ISchedulable) {
                ScheduleNextStepForSchedulable(value as ISchedulable);
            } else if (value is Future) {
                Future f = (Future)value;
                this.WakeCondition = f;
                f.RegisterOnComplete(QueueStepOnComplete);
            } else if (value is Result) {
                _Future.Complete(((Result)value).Value);
                Dispose();
            } else {
                _Future.Complete(value);
                Dispose();
            }
        }

        void Step () {
            if (_Task == null || _Future == null)
                return;

            WakeCondition = null;

            try {
                if (!_Task.MoveNext()) {
                    // Completed with no result
                    _Future.Complete(null);
                    Dispose();
                    return;
                }

                object value = _Task.Current;
                ScheduleNextStep(value);
            } catch (Exception ex) {
                if (_Future != null)
                    _Future.Fail(ex);
                Dispose();
            }
        }
    }

    public class TaskScheduler : IDisposable {
        const long SleepFudgeFactor = 100;
        const long MinimumSleepLength = 12500;

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

        private void BackgroundTaskOnComplete (Future f, object r, Exception e) {
            if (e != null) {
                this.QueueWorkItem(() => {
                    throw new TaskException("Unhandled exception in background task", e);
                });
            }
        }

        public Future Start (ISchedulable task, TaskExecutionPolicy executionPolicy) {
            Future future = new Future();
            task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnComplete(BackgroundTaskOnComplete);
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
                    newSleepEvent.Reset();
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
                        if (timeToSleep < MinimumSleepLength)
                            timeToSleep = MinimumSleepLength;
                        System.Diagnostics.Debug.WriteLine(String.Format("Sleeping for {0} ticks", timeToSleep));
                        try {
                            newSleepEvent.Reset();
                            int result = WaitHandle.WaitAny(new WaitHandle[] { newSleepEvent }, TimeSpan.FromTicks(timeToSleep), true);
                            if (result == 0) {
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

            future.RegisterOnDispose((_) => {
                _WaitWorker.DequeueWorkItem(wait);
            });
        }

        internal void QueueSleep (long completeWhen, Future future) {
            SleeperDelegate sleeper = TaskScheduler.BuildSleeper(completeWhen, future);

            _SleepWorker.QueueWorkItem(sleeper);

            future.RegisterOnDispose((_) => {
                _SleepWorker.DequeueWorkItem(sleeper);
            });
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

            _JobQueue.Clear();
            _StepListeners.Clear();
        }
    }
}

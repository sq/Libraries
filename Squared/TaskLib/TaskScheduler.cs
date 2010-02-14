using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
    public interface ISchedulable {
        void Schedule (TaskScheduler scheduler, IFuture future);
    }

    public enum TaskExecutionPolicy {
        RunWhileFutureLives,
        RunAsBackgroundTask,
        Default = RunWhileFutureLives
    }

    public class Result {
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

    public class TaskYieldedValueException : Exception {
        public IEnumerator<object> Task;

        public TaskYieldedValueException (IEnumerator<object> task)
            : base("A task directly yielded a value. To yield a result from a task, yield a Result() object containing the value.") {
            Task = task;
        }
    }

    struct SleepItem : IComparable<SleepItem> {
        public long Until;
        public IFuture Future;

        public bool Tick (long now) {
            long ticksLeft = Math.Max(Until - now, 0);
            if (ticksLeft == 0) {
                try {
                    Future.Complete();
                } catch (FutureAlreadyHasResultException ex) {
                    if (ex.Future != Future)
                        throw;
                }

                return true;
            } else {
                return false;
            }
        }

        public int CompareTo (SleepItem rhs) {
            return Until.CompareTo(rhs.Until);
        }
    }

    public delegate bool BackgroundTaskErrorHandler (Exception error);

    public class TaskScheduler : IDisposable {
        const long SleepFudgeFactor = 100;
        const long SleepSpinThreshold = 1000;
        const long MinimumSleepLength = 10000;
        const long MaximumSleepLength = Time.SecondInTicks * 60;

        public BackgroundTaskErrorHandler ErrorHandler = null;

        private IJobQueue _JobQueue = null;
        private AtomicQueue<Action> _StepListeners = new AtomicQueue<Action>();
        private WorkerThread<PriorityQueue<SleepItem>> _SleepWorker;

        public TaskScheduler (Func<IJobQueue> JobQueueFactory) {
            _JobQueue = JobQueueFactory();
            _SleepWorker = new WorkerThread<PriorityQueue<SleepItem>>(SleepWorkerThreadFunc, ThreadPriority.AboveNormal);
        }

        public TaskScheduler ()
            : this(JobQueue.ThreadSafe) {
        }

        public bool WaitForWorkItems () {
            return WaitForWorkItems(0);
        }

        public bool WaitForWorkItems (double timeout) {
            return _JobQueue.WaitForWorkItems(timeout);
        }

        private void BackgroundTaskOnComplete (IFuture f) {
            var e = f.Error;
            if (e != null) {
                this.QueueWorkItem(() => {
                    if (ErrorHandler != null)
                        if (ErrorHandler(e))
                            return;

                    throw new TaskException("Unhandled exception in background task", e);
                });
            }
        }

        public void Start (IFuture future, ISchedulable task, TaskExecutionPolicy executionPolicy) {
            task.Schedule(this, future);

            switch (executionPolicy) {
                case TaskExecutionPolicy.RunAsBackgroundTask:
                    future.RegisterOnComplete(BackgroundTaskOnComplete);
                break;
                default:
                break;
            }
        }

        public IFuture Start (ISchedulable task, TaskExecutionPolicy executionPolicy) {
            var future = new Future();
            Start(future, task, executionPolicy);
            return future;
        }

        public IFuture Start (IEnumerator<object> task, TaskExecutionPolicy executionPolicy) {
            return Start(new SchedulableGeneratorThunk(task), executionPolicy);
        }

        public IFuture Start (ISchedulable task) {
            return Start(task, TaskExecutionPolicy.Default);
        }

        public IFuture Start (IEnumerator<object> task) {
            return Start(task, TaskExecutionPolicy.Default);
        }

        public void QueueWorkItem (Action workItem) {
            _JobQueue.QueueWorkItem(workItem);
        }

        internal void AddStepListener (Action listener) {
            _StepListeners.Enqueue(listener);
        }

        internal static void SleepWorkerThreadFunc (PriorityQueue<SleepItem> pendingSleeps, ManualResetEvent newSleepEvent) {
            while (true) {
                long now = Time.Ticks;

                SleepItem currentSleep;
                Monitor.Enter(pendingSleeps);
                if (pendingSleeps.Peek(out currentSleep)) {
                    if (currentSleep.Tick(now)) {
                        pendingSleeps.Dequeue();
                        Monitor.Exit(pendingSleeps);
                        continue;
                    } else {
                        Monitor.Exit(pendingSleeps);
                    }
                } else {
                    Monitor.Exit(pendingSleeps);
                    newSleepEvent.WaitOne(-1, false);
                    newSleepEvent.Reset();
                    continue;
                }

                long sleepUntil = currentSleep.Until;

                now = Time.Ticks;
                long timeToSleep = (sleepUntil - now) + SleepFudgeFactor;

#if !XBOX
                if (timeToSleep < SleepSpinThreshold) {
                    int iteration = 1;

                    while (Time.Ticks < sleepUntil) {
                        Thread.SpinWait(20 * iteration);
                        iteration += 1;
                    }

                    timeToSleep = 0;
                }
#endif

                if (timeToSleep > 0) {
                    if (timeToSleep > MaximumSleepLength)
                        timeToSleep = MaximumSleepLength;

                    int msToSleep = 0;
                    if (timeToSleep >= MinimumSleepLength) {
                        msToSleep = (int)(timeToSleep / Time.MillisecondInTicks);
                    }

                    newSleepEvent.Reset();
                    newSleepEvent.WaitOne(msToSleep, false);
                }
            }
        }

        internal void QueueSleep (long completeWhen, IFuture future) {
            long now = Time.Ticks;
            if (now > completeWhen) {
                future.Complete();
                return;
            }

            SleepItem sleep = new SleepItem { Until = completeWhen, Future = future };

            lock (_SleepWorker.WorkItems)
                _SleepWorker.WorkItems.Enqueue(sleep);
            _SleepWorker.Wake();
        }

        public Clock CreateClock (double tickInterval) {
            Clock result = new Clock(this, tickInterval);
            return result;
        }

        internal void BeforeStep () {
            Action item = null;

            while (true) {
                if (_StepListeners.Dequeue(out item))
                    item();
                else
                    break;
            }
        }

        public void Step () {
            BeforeStep();

            _JobQueue.Step();
        }

        public object WaitFor (ISchedulable schedulable) {
            var f = Start(schedulable, TaskExecutionPolicy.RunWhileFutureLives);
            return WaitFor(f);
        }

        public object WaitFor (IEnumerator<object> task) {
            var f = Start(task, TaskExecutionPolicy.RunWhileFutureLives);
            return WaitFor(f);
        }

        public T WaitFor<T> (Future<T> future) {
            while (true) {
                BeforeStep();

                if (_JobQueue.WaitForFuture(future))
                    return future.Result;
            }
        }

        public object WaitFor (IFuture future) {
            while (true) {
                BeforeStep();

                if (_JobQueue.WaitForFuture(future))
                    return future.Result;
            }
        }

        public bool HasPendingTasks {
            get {
                return (_StepListeners.Count > 0) || (_JobQueue.Count > 0);
            }
        }

        public void Dispose () {
            _JobQueue.Dispose();
            _SleepWorker.Dispose();
        }
    }
}

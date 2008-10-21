using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
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

    public class TaskYieldedValueException : Exception {
        public TaskYieldedValueException ()
            : base("Task directly yielded a value. To yield a result from a task, yield a Result() object containing the value.") {
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

    struct SleepItem : IComparable<SleepItem> {
        public long Until;
        public Future Future;

        public bool Tick (long now) {
            long ticksLeft = Math.Max(Until - now, 0);
            if (ticksLeft == 0) {
                Future.Complete();
                return true;
            } else {
                return false;
            }
        }

        public int CompareTo (SleepItem rhs) {
            return Until.CompareTo(rhs.Until);
        }
    }

    public class TaskScheduler : IDisposable {
        const long SleepFudgeFactor = 10;
        const long MinimumSleepLength = 10000;
        const long MaximumSleepLength = Time.SecondInTicks * 60;

        private IJobQueue _JobQueue = null;
        private Queue<Action> _StepListeners = new Queue<Action>();
        private WorkerThread<PriorityQueue<SleepItem>> _SleepWorker;
        private WorkerThread<List<BoundWaitHandle>> _WaitWorker;

        public TaskScheduler (Func<IJobQueue> JobQueueFactory) {
            _JobQueue = JobQueueFactory();
            _SleepWorker = new WorkerThread<PriorityQueue<SleepItem>>(SleepWorkerThreadFunc, ThreadPriority.AboveNormal);
            _WaitWorker = new WorkerThread<List<BoundWaitHandle>>(WaitWorkerThreadFunc, ThreadPriority.AboveNormal);
        }

        public TaskScheduler ()
            : this(JobQueue.SingleThreaded) {
        }

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
                    newSleepEvent.WaitOne(-1, true);
                    newSleepEvent.Reset();
                    continue;
                }

                long sleepUntil = currentSleep.Until;

                now = Time.Ticks;
                long timeToSleep = (sleepUntil - now) + SleepFudgeFactor;
                if (timeToSleep > 0) {
                    if (timeToSleep < MinimumSleepLength)
                        timeToSleep = MinimumSleepLength;
                    if (timeToSleep > MaximumSleepLength)
                        timeToSleep = MaximumSleepLength;

                    long estWakeTime = now + timeToSleep;
                    try {
                        newSleepEvent.Reset();
                        newSleepEvent.WaitOne(TimeSpan.FromTicks(timeToSleep), true);
                        long wakeTime = Time.Ticks;
                    } catch (ThreadInterruptedException) {
                        break;
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

        internal void QueueWait (WaitHandle handle, Future future) {
            BoundWaitHandle wait = new BoundWaitHandle(handle, future);

            lock (_WaitWorker.WorkItems)
                _WaitWorker.WorkItems.Add(wait);
            _WaitWorker.Wake();

            future.RegisterOnDispose((_) => {
                lock (_WaitWorker.WorkItems)
                    _WaitWorker.WorkItems.Remove(wait);
            });
        }

        internal void QueueSleep (long completeWhen, Future future) {
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
            _JobQueue.Dispose();
            _SleepWorker.Dispose();
            _WaitWorker.Dispose();
            _StepListeners.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
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
                if (value is IEnumerator<object>) {
                    ScheduleNextStepForSchedulable(new RunToCompletion(value as IEnumerator<object>, TaskExecutionPolicy.RunAsBackgroundTask));
                } else if (value == null) {
                    QueueStep();
                } else {
                    throw new TaskYieldedValueException();
                }
            }
        }

        void Step () {
            if (_Task == null)
                return;

            WakeCondition = null;

            try {
                if (!_Task.MoveNext()) {
                    // Completed with no result
                    _Future.Complete(null);
                    Dispose();
                    return;
                }

                // Disposed during execution
                if (_Task == null)
                    return;

                object value = _Task.Current;
                ScheduleNextStep(value);
            } catch (Exception ex) {
                if (_Future != null)
                    _Future.Fail(ex);
                Dispose();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
    public class SchedulableGeneratorThunk : ISchedulable, IDisposable {
        public Func<object, IFuture> OnNextValue = null;

        IEnumerator<object> _Task;
        IFuture _Future;
        public IFuture WakeCondition;
        IFuture _WakePrevious = null;
        bool _WakeDiscardingResult = false;
        bool _ErrorChecked = false;
        TaskScheduler _Scheduler;
        Action _Step, _QueueStep, _OnErrorChecked;
        OnComplete _QueueStepOnComplete;

        public override string ToString () {
            return String.Format("<Task {0} waiting on {1}>", _Task, WakeCondition);
        }

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
            _QueueStep = QueueStep;
            _QueueStepOnComplete = QueueStepOnComplete;
            _OnErrorChecked = OnErrorChecked;
            _Step = Step;
        }

        internal void CompleteWithResult (object result) {
            if (CheckForDiscardedError())
                return;

            _Future.Complete(result);
            Dispose();
        }

        internal void Abort (Exception ex) {
            if (_Future != null)
                _Future.Fail(ex);
            Dispose();
        }

        public void Dispose () {
            _WakePrevious = null;

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

        void OnDisposed (IFuture _) {
            Dispose();
        }

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            IEnumerator<object> task = _Task;
            _Future = future;
            _Scheduler = scheduler;
            _Future.RegisterOnDispose(this.OnDisposed);
            QueueStep();
        }

        void QueueStepOnComplete (IFuture f) {
            if (_WakeDiscardingResult && f.Failed) {
                Abort(f.Error);
                return;
            }

            if (WakeCondition != null) {
                _WakePrevious = WakeCondition;
                WakeCondition = null;
            }

            _Scheduler.QueueWorkItem(_Step);
        }

        void QueueStep () {
            _Scheduler.QueueWorkItem(_Step);
        }

        void ScheduleNextStepForSchedulable (ISchedulable value) {
            if (value is WaitForNextStep) {
                _Scheduler.AddStepListener(_QueueStep);
            } else if (value is Yield) {
                QueueStep();
            } else {
                var temp = _Scheduler.Start(value, TaskExecutionPolicy.RunWhileFutureLives);
                SetWakeCondition(temp, true);
                temp.RegisterOnComplete(_QueueStepOnComplete);
            }
        }

        bool CheckForDiscardedError () {
            if ((!_WakeDiscardingResult) && (_WakePrevious != null)) {
                bool shouldRethrow = !_ErrorChecked;
                if (shouldRethrow && _WakePrevious.Failed) {
                    Abort(_WakePrevious.Error);
                    return true;
                }
            }

            return false;
        }

        void SetWakeCondition (IFuture f, bool discardingResult) {
            _WakePrevious = WakeCondition;

            if (CheckForDiscardedError())
                return;

            WakeCondition = f;
            _WakeDiscardingResult = discardingResult;
            if (f != null) {
                _ErrorChecked = false;
                f.RegisterOnErrorCheck(_OnErrorChecked);
            }
        }

        void OnErrorChecked () {
            _ErrorChecked = true;
        }

        void ScheduleNextStep (Object value) {
            if (CheckForDiscardedError())
                return;

            NextValue nv;
            IFuture f;
            Result r;
            IEnumerator<object> e;

            if (value == null) {
                QueueStep();
            } else if (value is ISchedulable) {
                ScheduleNextStepForSchedulable(value as ISchedulable);
            } else if ((nv = (value as NextValue)) != null) {
                if (OnNextValue != null)
                    f = OnNextValue(nv.Value);
                else
                    f = null;

                if (f != null) {
                    SetWakeCondition(f, true);
                    f.RegisterOnComplete(_QueueStepOnComplete);
                } else {
                    QueueStep();
                }
            } else if ((f = (value as IFuture)) != null) {
                SetWakeCondition(f, false);
                f.RegisterOnComplete(_QueueStepOnComplete);
            } else if ((r = (value as Result)) != null) {
                CompleteWithResult(r.Value);
            } else if ((e = (value as IEnumerator<object>)) != null) {
                ScheduleNextStepForSchedulable(new SchedulableGeneratorThunk(value as IEnumerator<object>));
            } else {
                throw new TaskYieldedValueException(_Task);
            }
        }

        void Step () {
            if (_Task == null)
                return;

            if (WakeCondition != null) {
                _WakePrevious = WakeCondition;
                WakeCondition = null;
            }

            try {
                if (!_Task.MoveNext()) {
                    // Completed with no result
                    CompleteWithResult(null);
                    return;
                }

                // Disposed during execution
                if (_Task == null)
                    return;

                object value = _Task.Current;
                ScheduleNextStep(value);
            } catch (Exception ex) {
                Abort(ex);
            }
        }
    }
}

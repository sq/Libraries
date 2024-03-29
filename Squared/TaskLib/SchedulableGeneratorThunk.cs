﻿using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;
using Squared.Threading;
using System.Runtime.ExceptionServices;

namespace Squared.Task {
    public class SchedulableGeneratorThunk : ISchedulable, IDisposable {
        public Func<object, IFuture> OnNextValue = null;

        IEnumerator<object> _Task;
        IFuture _Future;
        public IFuture WakeCondition;
        IFuture _WakePrevious = null;
        System.Threading.Tasks.Task _AwaitingCLRTask = null;
        bool _WakeDiscardingResult = false;
        bool _ErrorChecked = false;
        TaskScheduler _Scheduler;
        readonly Action _Step, _QueueStep, _OnErrorChecked;
        readonly OnFutureResolved _QueueStepOnComplete;
        readonly OnFutureResolved _QueueStepOnDispose;

        public override string ToString () {
            return String.Format("<Task {0} waiting on {1}>", _Task, WakeCondition);
        }

        public SchedulableGeneratorThunk (IEnumerator<object> task) {
            _Task = task;
            _QueueStep = QueueStep;
            _QueueStepOnComplete = QueueStepOnComplete;
            _QueueStepOnDispose = QueueStepOnDispose;
            _OnErrorChecked = OnErrorChecked;
            _Step = Step;
        }

        internal void CompleteWithResult (ITaskResult result) {
            if (CheckForDiscardedError())
                return;

            if (_Future == null) {
                if (result == null) {
                    // Disposed without result
                    return;
                } else {
                    // FIXME: is this right?
                    // Disposed with result but nowhere to send it.
                    return;
                }
            } else if (result != null) {
                _Future.Complete(result.Value);
            } else {
                _Future.Complete(null);
            }

            Dispose();
        }

        internal void Abort (IFuture f) {
            if (_Future != null)
                _Future.CopyFrom(f);

            Dispose();
        }

        internal void Abort (ExceptionDispatchInfo exInfo) {
            if (_Future != null)
                _Future.SetResult2(null, exInfo);

            Dispose();
        }

        public void Dispose () {
            _WakePrevious = null;

            if (_AwaitingCLRTask != null) {
                var id = _AwaitingCLRTask as IDisposable;
                id?.Dispose();
                _AwaitingCLRTask = null;
            }

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
            if (future == null)
                throw new ArgumentNullException("future");

            _Future = future;
            _Scheduler = scheduler;
            _Future.RegisterOnDispose(this.OnDisposed);
            QueueStep();
        }

        void QueueStepOnComplete (IFuture f) {
            if (_WakeDiscardingResult && f.Failed) {
                Abort(f);
                return;
            }

            if (WakeCondition != null) {
                _WakePrevious = WakeCondition;
                WakeCondition = null;
            }

            _Scheduler.QueueWorkItem(_Step);
        }

        void QueueStepOnDispose (IFuture f) {
            if (WakeCondition != null) {
                _WakePrevious = WakeCondition;
                WakeCondition = null;
            }

            if (!_Scheduler.IsDisposed)                
                _Scheduler.QueueWorkItem(_Step);
        }

        void QueueStep () {
            _Scheduler.QueueWorkItem(_Step);
        }

        void ScheduleNextStepForSchedulable (ISchedulable value) {
            if (value is WaitForNextStep) {
                _Scheduler.QueueWorkItemForNextStep(_QueueStep);
            } else if (value is Yield) {
                QueueStep();
            } else {
                var temp = _Scheduler.Start(value, TaskExecutionPolicy.RunWhileFutureLives);
                SetWakeConditionAndSubscribe(temp, true);
            }
        }

        void ScheduleNextStepForCLRTask (System.Threading.Tasks.Task stt) {
            _AwaitingCLRTask = stt;
            var awaiter = stt.GetAwaiter();
            awaiter.OnCompleted(_QueueStep);
        }

        bool CheckForDiscardedError () {
            if (_ErrorChecked)
                return false;

            if (_WakePrevious == null)
                return false;

            if (!_WakeDiscardingResult) {
                if (_WakePrevious.Failed) {
                    Abort(_WakePrevious);
                    _WakePrevious = null;
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

        void SetWakeConditionAndSubscribe (IFuture f, bool discardingResult) {
            SetWakeCondition(f, discardingResult);
            f.RegisterHandlers(_QueueStepOnComplete, _QueueStepOnDispose);
        }

        void OnErrorChecked () {
            _ErrorChecked = true;
        }

        void ScheduleNextStep (Object value) {
            if (CheckForDiscardedError())
                return;

            NextValue nv;
            IFuture f;
            ITaskResult r;
            IEnumerator<object> e;
            ISchedulable s;
            System.Threading.Tasks.Task stt;

            if (value == null) {
                QueueStep();
            } else if ((s = value as ISchedulable) != null) {
                ScheduleNextStepForSchedulable(s);
            } else if ((stt = value as System.Threading.Tasks.Task) != null) {
                ScheduleNextStepForCLRTask(stt);
            } else if ((nv = (value as NextValue)) != null) {
                if (OnNextValue != null)
                    f = OnNextValue(nv.Value);
                else
                    f = null;

                if (f != null) {
                    SetWakeConditionAndSubscribe(f, true);
                } else {
                    QueueStep();
                }
            } else if ((f = (value as IFuture)) != null) {
                SetWakeConditionAndSubscribe(f, false);
            } else if ((r = (value as ITaskResult)) != null) {
                CompleteWithResult(r);
            } else if ((e = (value as IEnumerator<object>)) != null) {
                ScheduleNextStepForSchedulable(new SchedulableGeneratorThunk(e));
            } else {
                throw new TaskYieldedValueException(_Task);
            }
        }

        void Step () {
            if (_Task == null)
                return;

            _AwaitingCLRTask = null;
            if (WakeCondition != null) {
                _WakePrevious = WakeCondition;
                WakeCondition = null;
            }

            using (_Scheduler.IsActive)
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
                Abort(ExceptionDispatchInfo.Capture(ex));
            }
        }
    }
}

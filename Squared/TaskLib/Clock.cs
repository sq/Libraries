using System;
using System.Collections.Generic;
using System.Threading;
using Squared.Util;

namespace Squared.Task {
    struct TickWaiter : IComparable<TickWaiter> {
        public Future Future;
        public int Until;

        public bool Tick (int currentTick) {
            long ticksLeft = Math.Max(Until - currentTick, 0);
            if (ticksLeft == 0) {
                return true;
            } else {
                return false;
            }
        }

        public int CompareTo (TickWaiter rhs) {
            return Until.CompareTo(rhs.Until);
        }
    }

    public class Clock {
        private TaskScheduler _Scheduler;
        private long _CreatedWhen;
        private long _LastTick;
        private long _TickInterval;
        private int _ElapsedTicks;
        private PriorityQueue<TickWaiter> _WaitingTicks = new PriorityQueue<TickWaiter>();

        internal Clock (TaskScheduler scheduler, double tickInterval) {
            _Scheduler = scheduler;
            _LastTick = _CreatedWhen = Time.Ticks;
            _TickInterval = TimeSpan.FromSeconds(tickInterval).Ticks;
            ScheduleNextTick();
        }

        public double Interval {
            get {
                return TimeSpan.FromTicks(_TickInterval).TotalSeconds;
            }
        }

        public int ElapsedTicks {
            get {
                return _ElapsedTicks;
            }
        }

        public double ElapsedSeconds {
            get {
                return _ElapsedTicks * Interval;
            }
        }

        private void Tick () {
            _LastTick += _TickInterval;
            int thisTick = Interlocked.Increment(ref _ElapsedTicks);
            var completedWaits = new List<Future>();

            lock (_WaitingTicks) {
                TickWaiter tw;
                if (_WaitingTicks.Peek(out tw)) {
                    while (tw.Tick(thisTick)) {
                        completedWaits.Add(tw.Future);
                        _WaitingTicks.Dequeue();
                        if (!_WaitingTicks.Peek(out tw))
                            break;
                    }
                }
            }

            foreach (Future cwf in completedWaits)
                cwf.Complete();
            ScheduleNextTick();
        }

        private void OnTick (IFuture f, object r, Exception e) {
            _Scheduler.QueueWorkItem(Tick);
        }

        private void ScheduleNextTick () {
            long nextTick = _LastTick + _TickInterval;
            Future nextTickFuture = _Scheduler.Start(new SleepUntil(nextTick));
            nextTickFuture.RegisterOnComplete(OnTick);
        }

        public Future WaitForTick (int tick) {
            lock (_WaitingTicks) {
                TickWaiter tw;
                if (_WaitingTicks.Peek(out tw)) {
                    if (tw.Until == tick)
                        return tw.Future;
                }

                Future f = new Future();
                _WaitingTicks.Enqueue(new TickWaiter { Until = tick, Future = f });
                return f;
            }
        }

        public IFuture WaitForNextTick () {
            return WaitForTick(_ElapsedTicks + 1);
        }
    }
}

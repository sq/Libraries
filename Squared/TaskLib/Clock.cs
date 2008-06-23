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
                Future.Complete();
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
            _LastTick = _CreatedWhen = DateTime.Now.Ticks;
            _TickInterval = TimeSpan.FromSeconds(tickInterval).Ticks;
            ScheduleNextTick();
        }

        public int ElapsedTicks {
            get {
                return _ElapsedTicks;
            }
        }

        private void OnTick (Future f, object r, Exception e) {
            _LastTick += _TickInterval;
            int thisTick = Interlocked.Increment(ref _ElapsedTicks);
            try {
                TickWaiter tw = _WaitingTicks.Peek();
                while (tw.Tick(thisTick)) {
                    _WaitingTicks.Dequeue();
                    tw = _WaitingTicks.Peek();
                }
            } catch (InvalidOperationException) {
            }
            ScheduleNextTick();
        }

        private void ScheduleNextTick () {
            long nextTick = _LastTick + _TickInterval;
            Future nextTickFuture = _Scheduler.Start(new SleepUntil(nextTick));
            nextTickFuture.RegisterOnComplete(OnTick);
        }

        public Future WaitForTick (int tick) {
            Future f = new Future();
            _WaitingTicks.Enqueue(new TickWaiter { Until = tick, Future = f });
            return f;
        }

        public Future WaitForNextTick () {
            return WaitForTick(_ElapsedTicks + 1);
        }
    }
}

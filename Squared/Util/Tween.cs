using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Squared.Util;

namespace Squared.Util {
    public struct Tween<T> 
        where T : struct {

        private static readonly BoundInterpolatorSource<T, Tween<T>> GetValue;

        public readonly int RepeatCount;
        public readonly T From, To;
        public readonly BoundInterpolator<T, Tween<T>> Interpolator;
        public readonly long StartedWhen, EndWhen;

        static Tween () {
            GetValue = _GetValue;
        }

        private static T _GetValue (ref Tween<T> tween, int index) {
            if (index == 0)
                return tween.From;
            else if (index == 1)
                return tween.To;
            else
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        public Tween (T value) {
            From = To = value;
            StartedWhen = EndWhen = 0;
            Interpolator = null;
            RepeatCount = 0;
        }

        public Tween (
            T from, T to,
            long startWhen, long endWhen,
            BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0
        ) {
            From = from;
            To = to;
            StartedWhen = startWhen;
            EndWhen = endWhen;
            Interpolator = interpolator ?? Interpolators<T>.GetBoundDefault<Tween<T>>();
            RepeatCount = repeatCount;
        }

        public static Tween<T> StartNow (
            T from, T to, long ticks, long? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null, int repeatCount = 0
        ) {
            var _now = now.HasValue ? now.Value : Time.Ticks;
            return new Tween<T>(
                from, to,
                _now + delay.GetValueOrDefault(0),
                _now + delay.GetValueOrDefault(0) + ticks,
                interpolator: interpolator, repeatCount: repeatCount
            );
        }

        public static Tween<T> StartNow (
            T from, T to, float seconds, float? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null, int repeatCount = 0
        ) {
            return StartNow(
                from, to,
                ticks: TimeSpan.FromSeconds(seconds).Ticks,
                delay: TimeSpan.FromSeconds(delay.GetValueOrDefault(0)).Ticks,
                now: now, interpolator: interpolator, repeatCount: repeatCount
            );
        }

        private float GetProgressUnclamped (long now) {
            if (now < StartedWhen)
                return 0f;

            if ((EndWhen <= StartedWhen) || (EndWhen <= 0))
                return 1f;

            var durationTicks = EndWhen - StartedWhen;
            var elapsedTicks = now - StartedWhen;

            return (float)(elapsedTicks / ((double)durationTicks));
        }

        public float GetProgress (long now) {
            var unclamped = GetProgressUnclamped(now);
            if (RepeatCount == int.MaxValue)
                return Math.Max(0, unclamped);

            var clampedToRepeatCount = Arithmetic.Clamp(unclamped, 0, 1f + RepeatCount);
            if (RepeatCount > 0)
                return Arithmetic.WrapInclusive(clampedToRepeatCount, 0, 1f);
            else
                return clampedToRepeatCount;
        }

        public T Get (float now) {
            return Get((long)(now * Time.SecondInTicks));
        }

        public T Get (long now) {
            var progress = GetProgress(now);

            // Default-init / completed
            if (progress >= 1f)
                return To;
            else if (progress <= 0f)
                return From;

            if (Interpolator != null)
                return Interpolator(GetValue, ref this, 0, progress);
            else
                return From;
        }

        public bool IsOver (long now) {
            // FIXME: Is this right?
            if (StartedWhen == EndWhen)
                return true;

            if (EndWhen <= 0)
                return false;

            if (RepeatCount == int.MaxValue)
                return false;

            return GetProgressUnclamped(now) >= (1f + RepeatCount);
        }

        public static implicit operator Tween<T> (T value) {
            return new Tween<T>(value);
        }
    }
}

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
        }

        public Tween (
            T from, T to,
            long startWhen, long endWhen,
            BoundInterpolator<T, Tween<T>> interpolator = null 
        ) {
            From = from;
            To = to;
            StartedWhen = startWhen;
            EndWhen = endWhen;
            Interpolator = interpolator ?? Interpolators<T>.GetBoundDefault<Tween<T>>();
        }

        public static Tween<T> StartNow (
            T from, T to, long ticks, long? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null
        ) {
            var _now = now.HasValue ? now.Value : Time.Ticks;
            return new Tween<T>(
                from, to,
                _now + delay.GetValueOrDefault(0),
                _now + delay.GetValueOrDefault(0) + ticks,
                interpolator
            );
        }

        public static Tween<T> StartNow (
            T from, T to, float seconds, float? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null
        ) {
            return StartNow(
                from, to,
                ticks: TimeSpan.FromSeconds(seconds).Ticks,
                delay: TimeSpan.FromSeconds(delay.GetValueOrDefault(0)).Ticks,
                now: now, interpolator: interpolator
            );
        }

        public float GetProgress (long now) {
            if ((EndWhen <= StartedWhen) || (EndWhen <= 0)) {
                if (now < StartedWhen)
                    return 0f;
                else
                    return 1f;
            }

            return Arithmetic.Clamp((float)((now - StartedWhen) / (double)(EndWhen - StartedWhen)), 0, 1);
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

            return GetProgress(now) >= 1;
        }

        public static implicit operator Tween<T> (T value) {
            return new Tween<T>(value);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Squared.Util;

namespace Squared.Util {
    public static class Tween {
        public static Tween<T> StartNow<T> (
            T from, T to, long ticks, long? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop
        ) 
            where T : struct {
            var _now = now.HasValue ? now.Value : Time.Ticks;
            return new Tween<T>(
                from, to,
                _now + delay.GetValueOrDefault(0),
                _now + delay.GetValueOrDefault(0) + ticks,
                interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode
            );
        }

        public static Tween<T> StartNow<T> (
            T from, T to, float seconds, float? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop
        )
            where T : struct {
            return StartNow(
                from, to,
                ticks: TimeSpan.FromSeconds(seconds).Ticks,
                delay: TimeSpan.FromSeconds(delay.GetValueOrDefault(0)).Ticks,
                now: now, interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode
            );
        }
    }

    public struct Tween<T> 
        where T : struct {

        private static readonly BoundInterpolatorSource<T, Tween<T>> GetValue;

        public readonly int RepeatCount;
        public readonly TweenRepeatMode RepeatMode;
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
            RepeatMode = TweenRepeatMode.Loop;
        }

        public Tween (
            T from, T to,
            long startWhen, long endWhen,
            BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop
        ) {
            From = from;
            To = to;
            StartedWhen = startWhen;
            EndWhen = endWhen;
            Interpolator = interpolator ?? Interpolators<T>.GetBoundDefault<Tween<T>>();
            RepeatCount = repeatCount;
            RepeatMode = repeatMode;
        }

        public static Tween<T> StartNow (
            T from, T to, long ticks, long? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop
        ) {
            var _now = now.HasValue ? now.Value : Time.Ticks;
            return new Tween<T>(
                from, to,
                _now + delay.GetValueOrDefault(0),
                _now + delay.GetValueOrDefault(0) + ticks,
                interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode
            );
        }

        public static Tween<T> StartNow (
            T from, T to, float seconds, float? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop
        ) {
            return StartNow(
                from, to,
                ticks: TimeSpan.FromSeconds(seconds).Ticks,
                delay: TimeSpan.FromSeconds(delay.GetValueOrDefault(0)).Ticks,
                now: now, interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode
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

            var clampedToRepeatCount = 
                (RepeatCount == int.MaxValue) 
                    ? unclamped
                    : Arithmetic.Clamp(unclamped, 0, 1f + RepeatCount);

            if (RepeatCount > 0) {
                switch (RepeatMode) {
                    case TweenRepeatMode.Loop:
                        return Arithmetic.WrapInclusive(clampedToRepeatCount, 0, 1f);
                    case TweenRepeatMode.Pulse:
                        return Arithmetic.Pulse(clampedToRepeatCount / 2f, 1f, 0f);
                    // FIXME: Not sure these two work right
                    case TweenRepeatMode.PulseExp:
                        return Arithmetic.PulseExp(clampedToRepeatCount / 2f, 1f, 0f);
                    case TweenRepeatMode.PulseSine:
                        return Arithmetic.PulseSine(clampedToRepeatCount / 2f, 1f, 0f);
                    default:
                        throw new ArgumentException("repeatMode");
                }
            } else
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

        public bool IsConstant => StartedWhen >= EndWhen;

        public bool IsOver (long now) {
            // FIXME: Is this right?
            if (StartedWhen == EndWhen)
                return true;

            if (EndWhen <= 0)
                return false;

            if (RepeatCount == int.MaxValue)
                return false;

            var unclamped = GetProgressUnclamped(now);
            return unclamped >= (1f + RepeatCount);
        }

        public Tween<U> CloneWithNewValues<U> (U from, U to, BoundInterpolator<U, Tween<U>> interpolator = null)
            where U : struct {

            // HACK: This interpolator lookup is gross
            if ((interpolator == null) && (Interpolator != null))
                interpolator = Interpolators<U>.GetBoundByName<Tween<U>>(Interpolator.Method.Name);

            return new Tween<U>(
                from, to, StartedWhen, EndWhen, 
                interpolator, RepeatCount, RepeatMode
            );
        }

        public static implicit operator Tween<T> (T value) {
            return new Tween<T>(value);
        }

        public static bool operator == (Tween<T> lhs, Tween<T> rhs) {
            return lhs.Equals(ref rhs);
        }

        public static bool operator != (Tween<T> lhs, Tween<T> rhs) {
            return !lhs.Equals(ref rhs);
        }

        public bool Equals (ref Tween<T> rhs) {
            return
                (StartedWhen == rhs.StartedWhen) &&
                (EndWhen == rhs.EndWhen) &&
                (RepeatCount == rhs.RepeatCount) &&
                (RepeatMode == rhs.RepeatMode) &&
                (Interpolator == rhs.Interpolator) &&
                object.Equals(From, rhs.From) &&
                object.Equals(To, rhs.To);
        }

        public bool Equals (Tween<T> rhs) {
            return Equals(ref rhs);
        }

        public override bool Equals (object obj) {
            if (obj is Tween<T>)
                return Equals((Tween<T>)obj);
            else
                return false;
        }

        public override string ToString () {
            if (EndWhen <= StartedWhen)
                return string.Format("constant <{0}>", From);
            else
                return string.Format("from <{0}> to <{1}> duration {2:0000.00}ms [started at {3}]", From, To, (double)(EndWhen - StartedWhen) / Time.MillisecondInTicks, StartedWhen);
        }
    }

    public enum TweenRepeatMode : int {
        /// <summary>
        /// Repeats N times, starting from 0 each time
        /// </summary>
        Loop = 0,
        /// <summary>
        /// Repeats N times, going 0-1, 1-0, 0-1, ...
        /// </summary>
        Pulse = 1,
        /// <summary>
        /// Like Pulse, but exponential
        /// </summary>
        PulseExp = 2,
        /// <summary>
        /// Like Pulse, but sinusoidal
        /// </summary>
        PulseSine = 3
    }
}

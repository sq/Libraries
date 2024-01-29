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
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop,
            long repeatDelay = 0, long repeatExtraDuration = 0
        ) 
            where T : struct {
            var _now = now.HasValue ? now.Value : Time.Ticks;
            return new Tween<T>(
                from, to,
                _now + delay.GetValueOrDefault(0),
                _now + delay.GetValueOrDefault(0) + ticks,
                interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode,
                repeatDelay: repeatDelay,
                repeatExtraDuration: repeatExtraDuration
            );
        }

        public static Tween<T> StartNow<T> (
            T from, T to, float seconds, float? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop,
            float repeatDelay = 0, float repeatExtraDuration = 0
        )
            where T : struct {
            return StartNow(
                from, to,
                ticks: Time.TicksFromSeconds(seconds),
                delay: Time.TicksFromSeconds(delay.GetValueOrDefault(0)),
                now: now, interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode,
                repeatDelay: Time.TicksFromSeconds(repeatDelay),
                repeatExtraDuration: Time.TicksFromSeconds(repeatExtraDuration)
            );
        }

        public static BoundInterpolator<T, Tween<T>> GetEasing<T> (Interpolators<T>.Easing ease)
            where T : struct
        {
            return Interpolators<T>.Eased<Tween<T>>(ease);
        }
    }

    public readonly struct Tween<T> 
        where T : struct {

        private static readonly BoundInterpolatorSource<T, Tween<T>> GetValue;

        public readonly int RepeatCount;
        public readonly TweenRepeatMode RepeatMode;
        public readonly T From, To;
        public readonly BoundInterpolator<T, Tween<T>> Interpolator;
        public readonly long StartedWhen, EndWhen;
        /// <summary>
        /// If repeating is enabled, the tween will pause for this long before starting to repeat.
        /// This effectively increases the duration of each loop.
        /// </summary>
        public readonly long RepeatDelay;
        /// <summary>
        /// If repeating is enabled, all loops after the first time will have this added to their duration.
        /// </summary>
        public readonly long RepeatExtraDuration;

        static Tween () {
            GetValue = _GetValue;
        }

        static EqualityComparer<T> DefaultComparer = EqualityComparer<T>.Default;

        private static ref readonly T _GetValue (in Tween<T> tween, int index) {
            if (index == 0)
                return ref tween.From;
            else if (index == 1)
                return ref tween.To;
            else
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        public Tween (in T value) {
            From = To = value;
            StartedWhen = EndWhen = 0;
            Interpolator = null;
            RepeatCount = 0;
            RepeatDelay = 0;
            RepeatExtraDuration = 0;
            RepeatMode = TweenRepeatMode.Loop;
        }

        public Tween (
            in T from, in T to,
            long startWhen, long endWhen,
            BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop,
            long repeatDelay = 0, long repeatExtraDuration = 0
        ) {
            From = from;
            To = to;
            StartedWhen = startWhen;
            EndWhen = endWhen;
            Interpolator = interpolator ?? Interpolators<T>.GetBoundDefault<Tween<T>>();
            RepeatCount = repeatCount;
            RepeatMode = repeatMode;
            RepeatDelay = repeatDelay;
            RepeatExtraDuration = repeatExtraDuration;
        }

        public static Tween<T> StartNow (
            in T from, in T to, long ticks, long? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop,
            long repeatDelay = 0, long repeatExtraDuration = 0
        ) {
            var _now = now.HasValue ? now.Value : Time.Ticks;
            return new Tween<T>(
                from, to,
                _now + delay.GetValueOrDefault(0),
                _now + delay.GetValueOrDefault(0) + ticks,
                interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode, repeatDelay: repeatDelay,
                repeatExtraDuration: repeatExtraDuration
            );
        }

        public static Tween<T> StartNow (
            T from, T to, float seconds, float? delay = null,
            long? now = null, BoundInterpolator<T, Tween<T>> interpolator = null,
            int repeatCount = 0, TweenRepeatMode repeatMode = TweenRepeatMode.Loop,
            float repeatDelay = 0, float repeatExtraDuration = 0
        ) {
            // FIXME: TimeSpan.FromSeconds sucks, so don't use it here.
            return StartNow(
                from, to,
                ticks: Time.TicksFromSeconds(seconds),
                delay: Time.TicksFromSeconds(delay.GetValueOrDefault(0)),
                now: now, interpolator: interpolator, repeatCount: repeatCount,
                repeatMode: repeatMode, repeatDelay: Time.TicksFromSeconds(repeatDelay),
                repeatExtraDuration: Time.TicksFromSeconds(repeatExtraDuration)
            );
        }

        private float GetProgressUnclamped (long now) {
            if (now < StartedWhen)
                return 0f;

            if ((EndWhen <= StartedWhen) || (EndWhen <= 0))
                return 1f;

            var durationTicks = EndWhen - StartedWhen;
            var elapsedTicks = now - StartedWhen;

            if ((elapsedTicks <= durationTicks) || ((RepeatDelay == 0) && (RepeatExtraDuration == 0)))
                return (float)(elapsedTicks / ((double)durationTicks));

            // slow path: We have a repeat delay and/or extended duration and have begun to repeat
            var extendedDuration = durationTicks + RepeatDelay + RepeatExtraDuration;
            var adjustedElapsedTicks = elapsedTicks + RepeatExtraDuration;
            var icnt = adjustedElapsedTicks / extendedDuration;
            var subTicks = adjustedElapsedTicks - (icnt * extendedDuration);
            var localResult = (float)(subTicks / ((double)(durationTicks + RepeatExtraDuration)));
            if (localResult > 1)
                localResult = 1;
            return localResult + icnt;
        }

        public float GetProgress (long now) {
            return GetProgress(now, out _);
        }

        public float GetProgress (long now, out float unclamped) {
            unclamped = GetProgressUnclamped(now);

            var clampedToRepeatCount = 
                (RepeatCount == int.MaxValue) 
                    ? unclamped
                    : Arithmetic.Saturate(unclamped, 1f + RepeatCount);

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
            Get((long)(now * Time.SecondInTicks), out T result);
            return result;
        }

        /// <param name="now">The current time (in seconds)</param>
        /// <param name="result">The value at time <paramref name="now"/></param>
        /// <returns>True if the tween has ended at time <paramref name="now"/></returns>
        public bool Get (float now, out T result) {
            return Get((long)(now * Time.SecondInTicks), out result);
        }

        public T Get (long now) {
            Get(now, out T result);
            return result;
        }

        /// <param name="now">The current time (in ticks)</param>
        /// <param name="result">The value at time <paramref name="now"/></param>
        /// <returns>True if the tween has ended at time <paramref name="now"/></returns>
        public bool Get (long now, out T result) {
            var progress = GetProgress(now, out float unclamped);

            // Default-init / completed
            if (progress >= 1f)
                result = To;
            else if (progress <= 0f)
                result = From;
            else if (Interpolator != null)
                result = Interpolator(GetValue, in this, 0, progress);
            else
                result = From;

            var isOver = unclamped >= (1f + RepeatCount);
            return isOver;
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

        public Tween<T> ChangeDirection (T to, long now, float? seconds = null, float? delaySeconds = null, BoundInterpolator<T, Tween<T>> interpolator = null) {
            if (RepeatCount > 0)
                throw new InvalidOperationException("Changing direction is not possible for a repeating tween");

            if (((seconds ?? 0) <= 0) && ((delaySeconds ?? 0) <= 0))
                return new Tween<T>(to, to, now, now, interpolator ?? Interpolator);

            var startWhen =
                delaySeconds.HasValue
                    ? now + (long)(delaySeconds.Value * Time.SecondInTicks)
                    : now;
            var endWhen = 
                seconds.HasValue
                    ? startWhen + (long)(seconds.Value * Time.SecondInTicks)
                    : startWhen + (EndWhen - StartedWhen);
            return new Tween<T>(
                Get(now), to, 
                startWhen: startWhen, endWhen: endWhen,
                interpolator: interpolator ?? Interpolator,
                repeatCount: 0,
                repeatMode: RepeatMode,
                repeatDelay: RepeatDelay,
                repeatExtraDuration: RepeatExtraDuration
            );
        }

        public Tween<U> CloneWithNewValues<U> (U from, U to, BoundInterpolator<U, Tween<U>> interpolator = null)
            where U : struct {

            // HACK: This interpolator lookup is gross
            if ((interpolator == null) && (Interpolator != null)) {
                if (typeof(T) == typeof(U))
                    interpolator = (BoundInterpolator<U, Tween<U>>)((object)this.Interpolator);
                else
                    interpolator = Interpolators<U>.GetBoundByName<Tween<U>>(Interpolator.Method.Name);
            }

            return new Tween<U>(
                from, to, StartedWhen, EndWhen, 
                interpolator, RepeatCount, RepeatMode, 
                RepeatDelay, RepeatExtraDuration
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

        public override int GetHashCode () {
            return From.GetHashCode() ^ To.GetHashCode();
        }

        public bool Equals (ref Tween<T> rhs, EqualityComparer<T> comparer = null) {
            if (comparer == null)
                comparer = DefaultComparer;

            return
                (StartedWhen == rhs.StartedWhen) &&
                (EndWhen == rhs.EndWhen) &&
                (RepeatCount == rhs.RepeatCount) &&
                (RepeatMode == rhs.RepeatMode) &&
                (RepeatDelay == rhs.RepeatDelay) &&
                (RepeatExtraDuration == rhs.RepeatExtraDuration) &&
                (Interpolator == rhs.Interpolator) &&
                comparer.Equals(From, rhs.From) &&
                comparer.Equals(To, rhs.To);
        }

        public bool Equals (Tween<T> rhs, EqualityComparer<T> comparer) {
            return Equals(ref rhs, comparer);
        }

        public bool Equals (Tween<T> rhs) {
            return Equals(ref rhs, null);
        }

        public override bool Equals (object obj) {
            if (obj is Tween<T> t)
                return Equals(ref t, null);
            else
                return false;
        }

        public override string ToString () {
            if (EndWhen <= StartedWhen)
                return string.Format("constant <{0}>", From);
            else if (RepeatCount > 0)
                return string.Format("from <{0}> to <{1}> duration {2:0000.00}ms [started at {3}] {4} repeat(s) [delay {5}ms extra-duration {6}ms]", 
                    From, To, (double)(EndWhen - StartedWhen) / Time.MillisecondInTicks, StartedWhen, 
                    RepeatCount, (double)RepeatDelay / Time.MillisecondInTicks,
                    (double)RepeatExtraDuration / Time.MillisecondInTicks
                );
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

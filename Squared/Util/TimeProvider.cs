﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Squared.Util {
    public static class Time {
        /// <summary>
        /// The length of a second in ticks.
        /// </summary>
        public const long SecondInTicks = 10000000;
        public const double SecondInTicksD = SecondInTicks;

        /// <summary>
        /// The length of a millisecond in ticks.
        /// </summary>
        public const long MillisecondInTicks = SecondInTicks / 1000;
        public const double MillisecondInTicksD = MillisecondInTicks;

        /// <summary>
        /// The default time provider.
        /// </summary>
        public static ITimeProvider DefaultTimeProvider;

        public static long Ticks {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return DefaultTimeProvider.Ticks;
            }
        }

        public static double Seconds {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return DefaultTimeProvider.Seconds;
            }
        }

        public static long TicksFromSeconds (double seconds) {
            // FIXME: Split seconds and subseconds? Probably not important
            return (long)(seconds * SecondInTicksD);
        }

        public static long TicksFromMsecs (double msecs) {
            // FIXME: Split seconds and subseconds? Probably not important
            return (long)(msecs * MillisecondInTicksD);
        }

        public static double SecondsFromTicks (long ticks) {
            long seconds = ticks / SecondInTicks;
            double subsec = (ticks - (seconds * SecondInTicks)) / SecondInTicksD;
            return subsec + seconds;
        }

        static Time () {
            DefaultTimeProvider = new DotNetTimeProvider();
        }
    }

    public static class TimeExtensions {
        public static long MsecFromNow (this ITimeProvider provider, double msecs) {
            var now = provider.Ticks;
            var duration = Time.TicksFromMsecs(msecs);
            return now + duration;
        }

        public static long SecondsFromNow (this ITimeProvider provider, double seconds) {
            var now = provider.Ticks;
            var duration = Time.TicksFromSeconds(seconds);
            return now + duration;
        }
    }

    public interface ITimeProvider {
        long Ticks {
            get;
        }

        double Seconds {
            get;
        }
    }

    public sealed class DotNetTimeProvider : ITimeProvider {
        public static readonly double SecondInStopwatchTicksD = Stopwatch.Frequency;

        /// <summary>
        /// The length of a millisecond in ticks.
        /// </summary>
        public static readonly long MillisecondInStopwatchTicks = Stopwatch.Frequency / 1000;
        public static readonly double StopwatchTicksToTicks = Time.SecondInTicks / (double)Stopwatch.Frequency;

        public static readonly bool NeedsExpensiveConversion = Stopwatch.Frequency != Time.SecondInTicks;

        long _Offset;

        public DotNetTimeProvider () {
            // FIXME: Detect low resolution stopwatch and do something?
            _Offset = Stopwatch.GetTimestamp();
        }

        public static double SecondsFromStopwatchTicks (long ticks) {
            long seconds = ticks / Stopwatch.Frequency;
            double subsec = (ticks - (seconds * Stopwatch.Frequency)) / SecondInStopwatchTicksD;
            return subsec + seconds;
        }

        public long Ticks {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                long ticks = Stopwatch.GetTimestamp() - _Offset;
                if (NeedsExpensiveConversion)
                    return (long)(ticks * StopwatchTicksToTicks);
                else
                    return ticks;
            }
        }

        public double Seconds {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                long ticks = Stopwatch.GetTimestamp() - _Offset;
                if (NeedsExpensiveConversion)
                    return SecondsFromStopwatchTicks(ticks);
                else
                    return Time.SecondsFromTicks(ticks);
            }
        }
    }

    [SuppressUnmanagedCodeSecurity]
    public sealed class Win32TimeProvider : ITimeProvider {
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceCounter (out long lpPerformanceCount);

        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceFrequency (out long lpFrequency);

        // FIXME: Don't use decimal since it's slow
        private decimal _Frequency;
        private long _Offset;

        public Win32TimeProvider () {
            long temp;
            if (!QueryPerformanceFrequency(out temp))
                throw new InvalidOperationException("High performance timing not supported");

            _Frequency = temp;

            QueryPerformanceCounter(out _Offset);
        }

        public long RawToTicks (long raw) {
            decimal ticks = raw;
            ticks /= _Frequency;
            ticks *= Time.SecondInTicks;
            return (long)ticks;
        }

        public long Raw {
            get {
                long temp;
                QueryPerformanceCounter(out temp);
                temp -= _Offset;
                return temp;
            }
        }

        public long Ticks {
            get {
                long temp;
                QueryPerformanceCounter(out temp);
                temp -= _Offset;
                decimal ticks = temp;
                ticks /= _Frequency;
                ticks *= Time.SecondInTicks;
                return (long)ticks;
            }
        }

        public double Seconds {
            get {
                long temp;
                QueryPerformanceCounter(out temp);
                temp -= _Offset;
                decimal ticks = temp;
                ticks /= _Frequency;
                return (double)ticks;
            }
        }
    }

    public sealed class MockTimeProvider : ITimeProvider {
        public long CurrentTime = 0;

        public long Ticks {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return CurrentTime; }
        }

        public double Seconds {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Time.SecondsFromTicks(CurrentTime);
            }
        }

        public void Advance (long ticks) {
            CurrentTime += ticks;
        }
    }

    public sealed class PausableTimeProvider : ITimeProvider {
        public readonly ITimeProvider Source;

        private long? _DesiredTime = null;
        private long? _PausedSince = null;
        private long _Offset = 0;

        public PausableTimeProvider (ITimeProvider source) {
            Source = source;
        }

        public PausableTimeProvider (ITimeProvider source, long desiredTime) {
            Source = source;
            SetTime(desiredTime);
        }

        public PausableTimeProvider (ITimeProvider source, double desiredTime) {
            Source = source;
            SetTime(desiredTime);
        }

        public long Ticks {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (_DesiredTime.HasValue)
                    return _DesiredTime.Value;
                else
                    return Source.Ticks + _Offset;
            }
        }

        public double Seconds {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Time.SecondsFromTicks(Ticks);
            }
        }

        public bool Paused {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _PausedSince.HasValue;
            }
            set {
                if (_PausedSince.HasValue == value)
                    return;

                long now = Source.Ticks;

                if (value == true) {
                    _DesiredTime = Ticks;
                    _PausedSince = now;
                } else if (_DesiredTime.HasValue) {
                    _PausedSince = null;
                    _Offset = -now + _DesiredTime.Value;
                    _DesiredTime = null;
                } else {
                    long since = _PausedSince.Value;
                    _PausedSince = null;
                    _Offset = (now - since);
                }
            }
        }

        public void SetTime (double seconds) {
            SetTime((long)(seconds * Time.SecondInTicks));
        }

        public void SetTime (long ticks) {
            if (_PausedSince.HasValue)
                _DesiredTime = ticks;
            else
                _Offset = -Source.Ticks + ticks;
        }

        public void Reset () {
            _Offset = 0;
        }

        public void StartNow () {
            _Offset = -Source.Ticks;
        }
    }

    public sealed class ScalableTimeProvider : ITimeProvider {
        public readonly ITimeProvider Source;

        private long? _LastTimeScaleChange = null;
        private int _TimeScale = 10000;
        private long _Offset = 0;

        public ScalableTimeProvider (ITimeProvider source) {
            Source = source;
        }

        long Elapsed {
            get {
                return Source.Ticks + _Offset;
            }
        }

        long ScaledElapsed {
            get {
                long ticks = Source.Ticks;

                if (_LastTimeScaleChange.HasValue)
                    ticks -= _LastTimeScaleChange.Value;

                return _Offset + (ticks * _TimeScale / 10000);
            }
        }

        public long Ticks {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return ScaledElapsed;
            }
        }

        public double Seconds {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Time.SecondsFromTicks(ScaledElapsed);
            }
        }

        public float TimeScale {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _TimeScale / 10000.0f;
            }
            set {
                var oldTimeScale = _TimeScale;
                var newTimeScale = (int)Math.Floor(value * 10000.0f);
                if (oldTimeScale == newTimeScale)
                    return;

                OnTimeScaleChange(oldTimeScale, newTimeScale);

                _TimeScale = newTimeScale;
            }
        }

        void OnTimeScaleChange (int oldTimeScale, int newTimeScale) {
            long ticks = Source.Ticks;
            long offset = 0;

            if (_LastTimeScaleChange.HasValue)
                offset = _LastTimeScaleChange.Value;

            _Offset += (ticks - offset) * oldTimeScale / 10000;

            _LastTimeScaleChange = ticks;
        }

        public void Reset () {
            _Offset = 0;
        }

        public void StartNow () {
            _Offset = -Source.Ticks;
        }
    }
}

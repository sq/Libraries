using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using System.Diagnostics;

namespace Squared.Util {
    public static class Time {
        /// <summary>
        /// The length of a second in ticks.
        /// </summary>
        public const long SecondInTicks = 10000000;

        /// <summary>
        /// The length of a millisecond in ticks.
        /// </summary>
        public const long MillisecondInTicks = SecondInTicks / 1000;

        /// <summary>
        /// The default time provider.
        /// </summary>
#if XBOX
        public static ITimeProvider DefaultTimeProvider = new XBoxTimeProvider();
#else
        public static ITimeProvider DefaultTimeProvider = new Win32TimeProvider();
#endif

        public static long Ticks {
            get {
                return DefaultTimeProvider.Ticks;
            }
        }

        public static double Seconds {
            get {
                return DefaultTimeProvider.Seconds;
            }
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

    public class DotNetTimeProvider : ITimeProvider {
        long _Offset;
        decimal _Scale;

        public DotNetTimeProvider () {
            _Offset = DateTime.UtcNow.Ticks;
            _Scale = Time.SecondInTicks;
        }

        public long Ticks {
            get {
                return DateTime.UtcNow.Ticks - _Offset;
            }
        }

        public double Seconds {
            get {
                decimal scaledTicks = DateTime.UtcNow.Ticks - _Offset;
                scaledTicks /= _Scale;
                return (double)scaledTicks;
            }
        }
    }

#if XBOX
    public class XBoxTimeProvider : ITimeProvider {
        private decimal _Frequency;
        private long _Offset;

        public XBoxTimeProvider () {
            _Frequency = Stopwatch.Frequency;

            _Offset = Stopwatch.GetTimestamp();
        }

        public long Ticks {
            get {
                long temp;
                temp = Stopwatch.GetTimestamp();
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
                temp = Stopwatch.GetTimestamp();
                temp -= _Offset;
                decimal ticks = temp;
                ticks /= _Frequency;
                return (double)ticks;
            }
        }
    }
#endif

#if !XBOX
    public class Win32TimeProvider : ITimeProvider {
        [DllImport("Kernel32.dll")]
        [SuppressUnmanagedCodeSecurity()]
        private static extern bool QueryPerformanceCounter (out long lpPerformanceCount);

        [DllImport("Kernel32.dll")]
        [SuppressUnmanagedCodeSecurity()]
        private static extern bool QueryPerformanceFrequency (out long lpFrequency);

        private decimal _Frequency;
        private long _Offset;

        public Win32TimeProvider () {
            long temp;
            if (!QueryPerformanceFrequency(out temp))
                throw new InvalidOperationException("High performance timing not supported");

            _Frequency = temp;

            QueryPerformanceCounter(out _Offset);
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
#endif

    public class MockTimeProvider : ITimeProvider {
        public long CurrentTime = 0;

        public long Ticks {
            get { return CurrentTime; }
        }

        public double Seconds {
            get { 
                decimal ticks = CurrentTime;
                return (double)(ticks / Squared.Util.Time.SecondInTicks); 
            }
        }

        public void Advance (long ticks) {
            CurrentTime += ticks;
        }
    }

    public class PausableTimeProvider : ITimeProvider {
        public readonly ITimeProvider Source;

        private long? _PausedSince = null;
        private long _Offset = 0;

        public PausableTimeProvider (ITimeProvider source) {
            Source = source;
        }

        public long Ticks {
            get {
                if (_PausedSince.HasValue)
                    return _PausedSince.Value + _Offset;

                return Source.Ticks + _Offset; 
            }
        }

        public double Seconds {
            get {
                decimal ticks;
                if (_PausedSince.HasValue)
                    ticks = _PausedSince.Value + _Offset;
                else
                    ticks = Source.Ticks + _Offset;

                return (double)(ticks / Squared.Util.Time.SecondInTicks);
            }
        }

        public bool Paused {
            get {
                return _PausedSince.HasValue;
            }
            set {
                if (_PausedSince.HasValue == value)
                    return;

                long now = Source.Ticks;

                if (value == true) {
                    _PausedSince = now;
                } else {
                    long since = _PausedSince.Value;
                    _PausedSince = null;
                    _Offset -= (now - since);
                }
            }
        }

        public void Reset () {
            _Offset = 0;
        }
    }
}

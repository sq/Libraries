using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;

namespace Squared.Util {
    public static class Time {
        /// <summary>
        /// The length of a second in ticks.
        /// </summary>
        public const long SecondInTicks = 10000000;

        /// <summary>
        /// The default time provider.
        /// </summary>
        public static ITimeProvider DefaultTimeProvider = new Win32TimeProvider();

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
}

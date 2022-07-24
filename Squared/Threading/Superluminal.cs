using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Threading.Profiling {
    [SuppressUnmanagedCodeSecurity]
    public static class Superluminal {
        public static bool Enabled = Environment.CommandLine.Contains("--superluminal");

        public enum LoadResult {
            NotLoaded,
            OK,
            InvalidVersion,
            DllNotFound,
            UncaughtException
        }

        private const int PERFORMANCEAPI_MAJOR_VERSION = 2;
        private const int PERFORMANCEAPI_MINOR_VERSION = 0;
        private const int PERFORMANCEAPI_VERSION = ((PERFORMANCEAPI_MAJOR_VERSION << 16) | PERFORMANCEAPI_MINOR_VERSION);

        [DllImport("kernel32", SetLastError=true, CharSet=CharSet.Auto)]
        private static extern IntPtr LoadLibrary (string lpFileName);

        [DllImport("PerformanceAPI.dll")]
        private static extern int PerformanceAPI_GetAPI (int version, out PerformanceAPI_Functions result);

        [StructLayout(LayoutKind.Sequential)]
        internal struct PerformanceAPI_Functions
        {
	        public IntPtr pSetCurrentThreadName;
	        public IntPtr pSetCurrentThreadNameN;
	        public IntPtr pBeginEvent;
	        public IntPtr pBeginEventN;
	        public IntPtr pBeginEventWide;
	        public IntPtr pBeginEventWideN;
	        public IntPtr pEndEvent;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [SuppressUnmanagedCodeSecurity]
        unsafe delegate void TSetCurrentThreadName (byte* inThreadName);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet=CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity]
        unsafe delegate void TBeginEventWide (
            [MarshalAs(UnmanagedType.LPWStr)]
            string inId,
            [MarshalAs(UnmanagedType.LPWStr)]
            string inData, UInt32 inColor
        );
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [SuppressUnmanagedCodeSecurity]
        delegate PerformanceAPI_SuppressTailCallOptimization TEndEvent ();

        private static TSetCurrentThreadName _SetCurrentThreadName;
        private static TBeginEventWide _BeginEventWide;
        private static TEndEvent _EndEvent;

        private static Exception _LoadException;
        private static LoadResult _LoadResult;
        internal static PerformanceAPI_Functions APIPointers;
        private static object _Lock = new object();

        public static LoadResult LoadAPI () => LoadAPI(out _);

        public static LoadResult LoadAPI (out Exception error) {
            lock (_Lock) {
                if (_LoadResult == LoadResult.NotLoaded) {
                    _LoadException = null;
                    var libPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolderOption.DoNotVerify),
                        "Superluminal", "Performance", "API", "DLL",
                        Environment.Is64BitProcess ? "x64" : "x86",
                        "PerformanceAPI.dll"
                    );
                    var hDll = LoadLibrary(libPath);
                    if (hDll != IntPtr.Zero) {
                        try {
                            var getResult = PerformanceAPI_GetAPI(PERFORMANCEAPI_VERSION, out APIPointers);
                            _SetCurrentThreadName = Marshal.GetDelegateForFunctionPointer<TSetCurrentThreadName>(APIPointers.pSetCurrentThreadName);
                            _BeginEventWide = Marshal.GetDelegateForFunctionPointer<TBeginEventWide>(APIPointers.pBeginEventWide);
                            _EndEvent = Marshal.GetDelegateForFunctionPointer<TEndEvent>(APIPointers.pEndEvent);
                            _LoadResult = (getResult != 0) ? LoadResult.OK : LoadResult.InvalidVersion;
                        } catch (Exception exc) {
                            _LoadResult = LoadResult.UncaughtException;
                            _LoadException = exc;
                        }
                    } else
                        _LoadResult = LoadResult.DllNotFound;

                    if (_LoadResult != LoadResult.OK)
                        Console.WriteLine($"Superluminal load result: {_LoadResult} error: {_LoadException}");
                    Enabled = _LoadResult == LoadResult.OK;
                }

                error = _LoadException;
                return _LoadResult;
            }
        }

        public static unsafe void SetCurrentThreadName (string name) {
            if (!Enabled)
                return;

            if (LoadAPI() != LoadResult.OK)
                return;

            const int bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            fixed (char * pName = name) {
                var length = Encoding.UTF8.GetBytes(pName, name.Length, buffer, bufferSize - 1);
                buffer[length] = 0;
                _SetCurrentThreadName(buffer);
            }
        }

        public static unsafe void BeginEvent (string scopeId, string data = null, UInt32 color = 0xFFFFFFu) {
            if (!Enabled)
                return;

            if (LoadAPI() != LoadResult.OK)
                return;

            var _data = data ?? string.Empty;
            _BeginEventWide(scopeId, _data, (color << 8) | 0xFF);
        }

        private static readonly ThreadLocal<StringBuilder> FormatBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder());

        public static unsafe void BeginEventFormat<T1> (string scopeId, string dataFormat, in T1 arg1, UInt32 color = 0xFFFFFFu) {
            if (!Enabled)
                return;

            var builder = FormatBuilder.Value;
            builder.Clear();
            builder.AppendFormat(dataFormat, arg1);
            BeginEvent(scopeId, builder.ToString(), color);
        }

        public static unsafe void BeginEventFormat<T1, T2> (string scopeId, string dataFormat, in T1 arg1, in T2 arg2, UInt32 color = 0xFFFFFFu) {
            if (!Enabled)
                return;

            var builder = FormatBuilder.Value;
            builder.Clear();
            builder.AppendFormat(dataFormat, arg1, arg2);
            BeginEvent(scopeId, builder.ToString(), color);
        }

        public static unsafe void BeginEventFormat<T1, T2, T3> (string scopeId, string dataFormat, in T1 arg1, in T2 arg2, in T3 arg3, UInt32 color = 0xFFFFFFu) {
            if (!Enabled)
                return;

            var builder = FormatBuilder.Value;
            builder.Clear();
            builder.AppendFormat(dataFormat, arg1, arg2, arg3);
            BeginEvent(scopeId, builder.ToString(), color);
        }

        public static unsafe void EndEvent () {
            if (!Enabled)
                return;

            if (LoadAPI() != LoadResult.OK)
                return;

            _EndEvent();
        }

        private struct PerformanceAPI_SuppressTailCallOptimization {
            UInt64 A, B, C;
        }
    }
}

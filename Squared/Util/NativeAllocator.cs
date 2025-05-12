// #define CAPTURE_ALLOCATION_STACKS

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Util {
    public static unsafe class MemoryUtil {
        public static void Memset (byte* ptr, byte value, int count) {
#if !NOSPAN
            System.Runtime.CompilerServices.Unsafe.InitBlock(ptr, value, (uint)count);
#else
            for (int i = 0; i < count; i++)
                ptr[i] = value;
#endif
        }
    }

    public class NativeAllocator {
        public string Name;

        internal long _TotalBytesAllocated;
        internal volatile int _BytesInUse;

        public long TotalBytesAllocated => _TotalBytesAllocated;
        public int BytesInUse => _BytesInUse;

        public NativeAllocation Allocate (int bytes, bool reportToGc = false) =>
            new NativeAllocation(this, bytes, reportToGc);

        public NativeAllocation Allocate<T> (int elements, bool reportToGc = false)
            where T : unmanaged 
        {
            return Allocate(elements * Marshal.SizeOf<T>(), reportToGc);
        }

        public override string ToString () {
            return $"<NativeAllocator {Name} {BytesInUse} in use>";
        }
    }

    public unsafe sealed class NativeAllocation : IDisposable {
#if DEBUG
        public readonly StackTrace AllocationStack;
#endif

        private volatile int _RefCount;
        private volatile bool _Released;
        private void* _Data;

        public int RefCount => _RefCount;
        public bool IsReleased => _Released;

        public void* Data => _Released ? null : _Data;
        public IntPtr IntPtr => (IntPtr)Data;
        public readonly int Size;
        public readonly NativeAllocator Allocator;
        public readonly bool ReportToGc;

        internal NativeAllocation (NativeAllocator allocator, int size, bool reportToGc) {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            Allocator = allocator;
            Interlocked.Add(ref allocator._TotalBytesAllocated, (long)size);
            Interlocked.Add(ref allocator._BytesInUse, size);
            _RefCount = 1;
            _Data = (void*)Marshal.AllocHGlobal(size);
            // FIXME: This can trigger synchronous garbage collections at inopportune times.
            ReportToGc = reportToGc;
            if (reportToGc)
                GC.AddMemoryPressure(size);
            MemoryUtil.Memset((byte*)_Data, 0, size);
            Size = size;

#if DEBUG
#if CAPTURE_ALLOCATION_STACKS
            AllocationStack = new StackTrace(1);
#else
            AllocationStack = null;
#endif
#endif
        }

        public void AddReference () {
            if (_Released)
                throw new ObjectDisposedException("NativeAllocation");
            Interlocked.Increment(ref _RefCount);
        }

        public void ReleaseReference () {
            if (Interlocked.Decrement(ref _RefCount) == 0)
                ReleaseAllocation();
        }

        void IDisposable.Dispose () =>
            ReleaseReference();

        private void ReleaseAllocation () {
            if (_Released)
                return;

            _Released = true;
            Marshal.FreeHGlobal((IntPtr)_Data);
            if (ReportToGc)
                GC.RemoveMemoryPressure(Size);
            Interlocked.Add(ref Allocator._BytesInUse, -Size);
        }

        public static explicit operator IntPtr (NativeAllocation allocation)
            => (IntPtr)allocation.Data;

        public static IntPtr operator + (NativeAllocation lhs, int rhs)
            => (IntPtr)(((byte*)lhs.Data) + rhs);

#if DEBUG
        ~NativeAllocation () {
            if (!_Released) {
                Debug.WriteLine($"Native allocation of {Size}b leaked from {Allocator}");
                if (AllocationStack != null)
                    Debug.WriteLine(AllocationStack.ToString());
            }
        }
#endif
    }
}

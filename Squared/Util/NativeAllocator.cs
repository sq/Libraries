using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Util {
    public class NativeAllocator {
        internal long _TotalBytesAllocated;
        internal volatile int _BytesInUse;

        public long TotalBytesAllocated => _TotalBytesAllocated;
        public int BytesInUse => _BytesInUse;

        public NativeAllocation Allocate (int size) =>
            new NativeAllocation(this, size);
    }

    public unsafe sealed class NativeAllocation : IDisposable {
        private volatile int _RefCount;
        private volatile bool _Released;
        private void* _Data;

        public int RefCount => _RefCount;
        public bool IsReleased => _Released;

        public void* Data => _Released ? null : _Data;
        public readonly int Size;
        public readonly NativeAllocator Allocator;

        internal NativeAllocation (NativeAllocator allocator, int size) {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            Allocator = allocator;
            Interlocked.Add(ref allocator._TotalBytesAllocated, (long)size);
            Interlocked.Add(ref allocator._BytesInUse, size);
            _RefCount = 1;
            _Data = (void*)Marshal.AllocHGlobal(size);
#if !NOSPAN
            System.Runtime.CompilerServices.Unsafe.InitBlock(_Data, 0, (uint)size);
#else
            var temp = (byte*)_Data;
            for (int i = 0; i < size; i++)
                temp[i] = 0;
#endif
            Size = size;
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
            Interlocked.Add(ref Allocator._BytesInUse, -Size);
        }

        public static explicit operator IntPtr (NativeAllocation allocation)
            => (IntPtr)allocation.Data;

        public static IntPtr operator + (NativeAllocation lhs, int rhs)
            => (IntPtr)(((byte*)lhs.Data) + rhs);
    }
}

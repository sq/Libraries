using System;
using System.Collections.Generic;
using System.Threading;

namespace Squared.Util {
    public static class DisposableBufferPool<T>
        where T : IDisposable {

        public struct DisposableBuffer : IDisposable {
            public T[] Data;

            public DisposableBuffer (T[] data) {
                Data = data;
            }

            public static implicit operator T[] (DisposableBuffer _) {
                return _.Data;
            }

            public void Clear (int index, int length) {
                Array.Clear(Data, index, length);
            }

            public void Dispose () {
                T[] data = Data;
                Data = null;

                foreach (var item in data)
                    item.Dispose();

                BufferPool<T>.AddToPool(data);
            }
        }

        public static DisposableBuffer Allocate (int size) {
            var b = BufferPool<T>.Allocate(size);
            return new DisposableBuffer(b.Data);
        }
    }

    public static class BufferPool<T> {
        public const int BigSizeThresholdBytes = 128;
        public const int DefaultSmallMaxBufferSize = 8192;
        public const int DefaultBigMaxBufferSize = 1024;

        public static bool EnableThreadLocalPools = true;
        public static int MaxGlobalPoolCount = 8;
        public static int MaxLocalPoolCount = 8;
        public static int MaxBufferSize;

        static BufferPool () {
            var t = typeof(T);
            int estimatedSize = 0;
            estimatedSize = t.GetFields().Length * 4;

            if (estimatedSize > BigSizeThresholdBytes)
                MaxBufferSize = DefaultBigMaxBufferSize;
            else
                MaxBufferSize = DefaultSmallMaxBufferSize;
        }

        public struct Buffer : IDisposable {
            public T[] Data;
            public bool ReturnToPool;

            public Buffer (T[] data, bool returnToPool) {
                Data = data;
                ReturnToPool = returnToPool;
            }

            public static implicit operator T[] (Buffer _) {
                return _.Data;
            }

            public static implicit operator ArraySegment<T> (Buffer _) {
                return new ArraySegment<T>(_.Data, 0, _.Data.Length);
            }

            public void Clear () {
                Array.Clear(Data, 0, Data.Length);
            }

            public void Clear (int index, int length) {
                Array.Clear(Data, index, length);
            }

            // FIXME: Maybe make this always clear the buffer,
            //  or always clear if T contains any reference types (via reflection check)?
            public void Dispose () {
                T[] data = Data;
                Data = null;

                if (ReturnToPool)
                    BufferPool<T>.AddToPool(data);
            }
        }

        private class PoolEntryComparer : IRefComparer<T[]> {
            public static readonly PoolEntryComparer Instance = new PoolEntryComparer();

            public int Compare (ref T[] x, ref T[] y) {
                return x.Length.CompareTo(y.Length);
            }
        }

        private class Pool : UnorderedList<T[]> {
        }

        private static readonly ThreadLocal<Pool> ThreadLocalPool =
            new ThreadLocal<Pool>(() => new Pool());
        private static readonly Pool GlobalPool = new Pool();

        private static bool TryAddToPool (T[] buffer, Pool pool, int maxCount, bool removeIfFull) {
            if (pool.Count >= maxCount) {
                var firstItem = pool.DangerousGetItem(0);
                if ((firstItem.Length < buffer.Length) && removeIfFull)
                    pool.RemoveAtOrdered(0);
                else
                    return false;
            }

            pool.Add(buffer);
            pool.FastCLRSortRef(PoolEntryComparer.Instance);
            return true;
        }

        internal static void AddToPool (T[] buffer) {
            if (buffer.Length > MaxBufferSize)
                return;

            if (!EnableThreadLocalPools || !TryAddToPool(buffer, ThreadLocalPool.Value, MaxLocalPoolCount, false)) {
                lock (GlobalPool)
                    TryAddToPool(buffer, GlobalPool, MaxGlobalPoolCount, true);
            }
        }

        public static Buffer Allocate (int? size = null) {
            int _size = size.GetValueOrDefault(MaxBufferSize);

            T[] resultArray;
            Buffer result = default(Buffer);
            if (_size > MaxBufferSize) {
                resultArray = new T[_size];
                return new Buffer(resultArray, true);
            }

            var allocated = false;
            allocated = EnableThreadLocalPools && TryAllocateFromPool(ThreadLocalPool.Value, _size, MaxLocalPoolCount, out result);
            if (!allocated) {
                lock (GlobalPool)
                    allocated = TryAllocateFromPool(GlobalPool, _size, MaxGlobalPoolCount, out result);
            }
            if (allocated)
                return result;

            // Nothing big enough was found in the pool.
            resultArray = new T[_size];
            return new Buffer(resultArray, true);
        }

        private static bool TryAllocateFromPool (Pool pool, int? size, int maxCount, out Buffer result) {
            // Find the smallest buffer that can hold the requested size
            for (int i = pool.Count - 1; i >= 0; i--) {
                var item = pool.DangerousGetItem(i);
                if (item.Length >= size) {
                    T[] resultArray = item;
                    pool.RemoveAtOrdered(i);
                    result = new Buffer(resultArray, true);
                    return true;
                }
            }

            result = default(Buffer);
            return false;
        }
    }

    public class GrowableBuffer<T> : IDisposable {
        public static int DefaultBufferSize = 1024;

        protected BufferPool<T>.Buffer _Buffer;
        protected T[] _Data;
        protected int _Length = 0;

        public GrowableBuffer () {
            ResizeBuffer(DefaultBufferSize);
        }

        public GrowableBuffer (int capacity) {
            ResizeBuffer(capacity);
        }

        private void ResizeBuffer (int size) {
            BufferPool<T>.Buffer temp = BufferPool<T>.Allocate(size);

            if (_Buffer.Data != null) {
                Array.Copy(_Buffer.Data, temp.Data, _Length);
                _Buffer.Dispose();
            }

            _Buffer = temp;
            _Data = _Buffer.Data;
        }

        public void DisposeAndGetContents (T[] output, int offset, int count = -1) {
            if (count <= -1)
                count = _Length;
            Array.Copy(_Data, 0, output, offset, count);

            Dispose();
        }

        public void Dispose () {
            _Length = 0;
            if (_Data != null)
                _Buffer.Dispose();
            _Data = null;
        }

        public void EnsureCapacity (int capacity) {
            var delta = capacity - _Data.Length;
            if (delta > 0)
                Grow(delta);
        }

        public void Grow (int extraCharactersNeeded) {
            int newLength = _Length + extraCharactersNeeded;
            int bufferSize = _Data.Length;

            while (bufferSize < newLength)
                bufferSize *= 2;

            if (bufferSize > _Data.Length)
                ResizeBuffer(bufferSize);
        }

        public void Append (T[] source, int offset, int count) {
            int newLength = _Length + count;
            int bufferSize = _Data.Length;

            while (bufferSize < newLength)
                bufferSize *= 2;

            if (bufferSize > _Data.Length)
                ResizeBuffer(bufferSize);

            Array.Copy(source, offset, _Data, _Length, count);
            _Length += count;
        }

        public void Append (T item) {
            int bufferSize = _Data.Length;
            if (_Length == bufferSize)
                ResizeBuffer(bufferSize * 2);

            _Data[_Length] = item;
            _Length += 1;
        }

        public void Remove (int position, int length) {
            int newLength = _Length - length;
            int sourceIndex = position + length;
            int copySize = _Length - position;

            if ((position + copySize) < _Length)
                Array.Copy(_Data, sourceIndex, _Data, position, copySize);

            _Length = newLength;
        }

        public void Clear () {
            _Length = 0;
        }

        public T this[int index] {
            get {
                return _Data[index];
            }
            set {
                _Data[index] = value;
            }
        }

        public ArraySegment<T> Buffer {
            get {
                return new ArraySegment<T>(_Data, 0, _Data.Length);
            }
        }

        public ArraySegment<T> Data {
            get {
                return new ArraySegment<T>(_Data, 0, _Length);
            }
        }

        public int Capacity {
            get {
                return _Data.Length;
            }
        }

        public int Length {
            get {
                return _Length;
            }
        }
    }

    public class CharacterBuffer : GrowableBuffer<char> {
        public string DisposeAndGetContents () {
            string result = ToString();
            Dispose();
            return result;
        }
 
        public override string ToString () {
            return new String(_Data, 0, _Length);
        }
    }
}

using System;
using System.Collections.Generic;

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
        public static int MaxPoolCount = 8;
        public static int MaxBufferSize = 4096;

        public struct Buffer : IDisposable {
            public T[] Data;

            public Buffer (T[] data) {
                Data = data;
            }

            public static implicit operator T[] (Buffer _) {
                return _.Data;
            }

            public void Clear (int index, int length) {
                Array.Clear(Data, index, length);
            }

            public void Dispose () {
                T[] data = Data;
                Data = null;

                BufferPool<T>.AddToPool(data);
            }
        }

        private static List<T[]> Pool = new List<T[]>();

        internal static void AddToPool (T[] buffer) {
            if (buffer.Length > MaxBufferSize)
                return;

            lock (Pool) {
                if (Pool.Count < MaxPoolCount)
                    Pool.Add(buffer);
            }
        }

        public static Buffer Allocate (int size) {
            lock (Pool) {
                for (int i = Pool.Count - 1; i >= 0; i--) {
                    var item = Pool[i];
                    if (item.Length >= size) {
                        T[] result = item;
                        Pool.RemoveAt(i);
                        return new Buffer(result);
                    }
                }

                if ((Pool.Count == MaxPoolCount) && (size < MaxBufferSize)) {
                    int smallest = int.MaxValue;
                    int smallestIndex = -1;
                    for (int i = 0; i < Pool.Count; i++) {
                        if (Pool[i].Length < smallest) {
                            smallest = Pool[i].Length;
                            smallestIndex = i;
                        }
                    }

                    if (smallestIndex != -1)
                        Pool.RemoveAt(smallestIndex);
                }
            }

            {
                T[] result = new T[size];
                return new Buffer(result);
            }
        }
    }

    public class CharacterBuffer : IDisposable {
        public static int DefaultBufferSize = 1024;

        private BufferPool<char>.Buffer _Buffer;
        private char[] _Data;
        private int _Length = 0;

        public CharacterBuffer () {
            ResizeBuffer(DefaultBufferSize);
        }

        private void ResizeBuffer (int size) {
            BufferPool<char>.Buffer temp = BufferPool<char>.Allocate(size);

            if (_Buffer.Data != null) {
                Array.Copy(_Buffer.Data, temp.Data, _Length);
                _Buffer.Dispose();
            }

            _Buffer = temp;
            _Data = _Buffer.Data;
        }

        public string DisposeAndGetContents () {
            string result = ToString();
            Dispose();
            return result;
        }

        public void Dispose () {
            _Length = 0;
            if (_Data != null)
                _Buffer.Dispose();
            _Data = null;
        }

        public void Grow (int extraCharactersNeeded) {
            int newLength = _Length + extraCharactersNeeded;
            int bufferSize = _Data.Length;

            while (bufferSize < newLength)
                bufferSize *= 2;

            if (bufferSize > _Data.Length)
                ResizeBuffer(bufferSize);
        }

        public void Append (char[] source, int offset, int count) {
            int newLength = _Length + count;
            int bufferSize = _Data.Length;

            while (bufferSize < newLength)
                bufferSize *= 2;

            if (bufferSize > _Data.Length)
                ResizeBuffer(bufferSize);

            Array.Copy(source, offset, _Data, _Length, count);
            _Length += count;
        }

        public void Append (char character) {
            int bufferSize = _Data.Length;
            if (_Length == bufferSize)
                ResizeBuffer(bufferSize * 2);

            _Data[_Length] = character;
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

        public override string ToString () {
            return new String(_Data, 0, _Length);
        }

        public char this[int index] {
            get {
                return _Data[index];
            }
            set {
                _Data[index] = value;
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
}

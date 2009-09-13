using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;

namespace CS_SQLite3 {
    public static class XPath {
#if XBOX360
        public static string TempPath = null;
#endif

        public static string GetTempPath () {
#if XBOX360
            return TempPath;
#else
            return Path.GetTempPath();
#endif
        }
    }

    public static class XBitConverter {
        public static long DoubleToInt64Bits (double d) {
#if XBOX360
            return BitConverter.ToInt64(BitConverter.GetBytes(d), 0);
#else
            return BitConverter.DoubleToInt64Bits(d);
#endif
        }

        public static double Int64BitsToDouble (long l) {
#if XBOX360
            return BitConverter.ToDouble(BitConverter.GetBytes(l), 0);
#else
            return BitConverter.Int64BitsToDouble(l);
#endif
        }
    }

    public static class XDouble {
        public static bool TryParse (string text, out double result) {
#if XBOX360
            try {
                result = double.Parse(text);
                return true;
            } catch {
                result = default(double);
                return false;
            }
#else
            return Double.TryParse(text, out result);
#endif
        }
    }

    public static class XInt64 {
        public static bool TryParse (string text, out long result) {
#if XBOX360
            try {
                result = long.Parse(text);
                return true;
            } catch {
                result = default(long);
                return false;
            }
#else
            return Int64.TryParse(text, out result);
#endif
        }
    }

    public static class XInt32 {
        public static bool TryParse (string text, out int result) {
#if XBOX360
            try {
                result = int.Parse(text);
                return true;
            } catch {
                result = default(int);
                return false;
            }
#else
            return Int32.TryParse(text, out result);
#endif
        }
    }

    public static class BufferPool<T> {
        public static int MaxFreeBuffers = 16;
        public static int MaxBufferSize = 4096;

        static readonly List<T[]> _FreeList = new List<T[]>();

        public static Buffer<T> Allocate (int count) {
            T[] array = null;

            int smallestIndex = -1;

            for (int i = 0; i < _FreeList.Count; i++) {
                var b = _FreeList[i];

                if (b.Length >= count) {
                    array = b;
                    _FreeList[i] = _FreeList[_FreeList.Count - 1];
                    _FreeList.RemoveAt(_FreeList.Count - 1);
                    break;
                } else {
                    if ((smallestIndex == -1) || (_FreeList[smallestIndex].Length > b.Length))
                        smallestIndex = i;
                }
            }

            if ((array == null) && (smallestIndex != -1)) {
                _FreeList[smallestIndex] = _FreeList[_FreeList.Count - 1];
                _FreeList.RemoveAt(_FreeList.Count - 1);
            }

            if (array == null)
                array = new T[count];

            if (array.Length < count)
                throw new InvalidDataException();

            return new Buffer<T>(array, 0, count, true);
        }

        public static void Release (T[] array) {
            if (array.Length > MaxBufferSize) {
                Debugger.Break();
                return;
            }

            if (_FreeList.Count >= MaxFreeBuffers)
                return;

            _FreeList.Add(array);
        }
    }

    public struct Buffer<T> : IDisposable {
        public T[] Array;
        public readonly int Offset;
        public readonly int Length;
        public readonly bool OwnsMemory;

        public Buffer (T[] array, int offset, int length, bool ownsMemory) {
            Array = array;
            Offset = offset;
            Length = length;
            OwnsMemory = ownsMemory;
        }

        public T this[int index] {
            get {
                return Array[index + Offset];
            }
            set {
                Array[index + Offset] = value;
            }
        }

        public bool IsNull {
            get {
                return (Array == null);
            }
        }

        public Buffer<T> Clone () {
            var result = BufferPool<T>.Allocate(Length);
            System.Array.Copy(Array, Offset, result.Array, 0, Length);
            return result;
        }

        public void Dispose () {
            if (Array == null)
                return;

            if (OwnsMemory)
                BufferPool<T>.Release(Array);

            Array = null;
        }
    }
}
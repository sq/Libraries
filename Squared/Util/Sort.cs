using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TimSort;
using tIndex = System.Int32;
using tCount = System.Int32;

namespace Squared.Util {
    public static class Sort {
        public static void Timsort<T> (T[] data, int? offset = null, int? count = null, IComparer<T> comparer = null) {
            if (comparer == null)
                comparer = Comparer<T>.Default;

            Timsort<T>(data, offset, count, comparer.Compare);
        }

        public static void Timsort<T> (T[] data, int? offset = null, int? count = null, Comparison<T> comparer = null) {
            tIndex _offset;
            tCount _count;

            if (data == null)
                throw new ArgumentNullException("data");

            if (!offset.HasValue)
                _offset = 0;
            else
                _offset = offset.Value;

            if (!count.HasValue)
                _count = data.Length;
            else
                _count = count.Value;

            if ((_count < 0) || (_offset + _count > data.Length))
                throw new ArgumentException("count");
            if ((_offset < 0) || (_offset > data.Length))
                throw new ArgumentException("offset");

            ArrayTimSort<T>.Sort(data, _offset, _offset + _count, comparer);
        }
    }
}

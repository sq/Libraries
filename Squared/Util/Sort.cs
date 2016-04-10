using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util {
    public class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class {

        public bool Equals (T x, T y) {
            return (x == y);
        }

        public int GetHashCode (T obj) {
            return obj.GetHashCode();
        }
    }

    public static class Sort {
        public static void FastCLRSort<TElement, TComparer>(
            TElement[] data, TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IComparer<TElement>
        {
            var sorter = new FastCLRSorter<TElement, TComparer>(data, comparer);
            var actualOffset = offset.GetValueOrDefault(0);

            sorter.Sort(
                actualOffset,
                count.GetValueOrDefault(data.Length - actualOffset)
            );
        }
    }
}

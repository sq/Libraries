using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Squared.Util {
    public interface IRefComparer<T> {
        int Compare (ref T lhs, ref T rhs);
    }

    public interface IRefComparerAdapter<T> {
        IComparer<T> Comparer { get; }
    }

    public struct RefComparerAdapter<TComparer, TElement> : IRefComparer<TElement>, IRefComparerAdapter<TElement>
        where TComparer : IComparer<TElement> 
    {
        private IComparer<TElement> _Boxed;
        private TComparer _Comparer;

        public RefComparerAdapter (TComparer comparer) {
            _Comparer = comparer;
            _Boxed = _Comparer;
        }

        public TComparer Comparer {
            get {
                return _Comparer;
            }
        }

        IComparer<TElement> IRefComparerAdapter<TElement>.Comparer {
            get {
                return _Boxed;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare (ref TElement lhs, ref TElement rhs) {
            return _Comparer.Compare(lhs, rhs);
        }
    }

    public sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class {

        public bool Equals (T x, T y) {
            return object.ReferenceEquals(x, y);
        }

        public int GetHashCode (T obj) {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    public static class Sort {
        public static void FastCLRSort<TElement, TComparer>(
            TElement[] data, TComparer comparer
        )
            where TComparer : IComparer<TElement>
        {
            FastCLRSort(new ArraySegment<TElement>(data), comparer);
        }

        public static void FastCLRSort<TElement, TComparer>(
            ArraySegment<TElement> data, TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IComparer<TElement>
        {
            int actualOffset = offset.GetValueOrDefault(0),
                actualCount = count.GetValueOrDefault(data.Count - actualOffset);
            var sorter = new FastCLRSorter<TElement, IComparer<TElement>>(data, comparer);
            sorter.Sort(actualOffset, actualCount);
        }

        public static void IndexedSort<TElement, TComparer>(
            ArraySegment<TElement> data, ArraySegment<int> indices, TComparer comparer
        )
            where TComparer : IComparer<TElement>
        {
            var adapter = new RefComparerAdapter<TComparer, TElement>(comparer);
            IndexedSortRef(data, indices, adapter);
        }

        public static void FastCLRSortRef<TElement, TComparer>(
            ArraySegment<TElement> data, TComparer comparer, int? offset = null, int? count = null
        )
            where TComparer : IRefComparer<TElement>
        {
            int actualOffset = offset.GetValueOrDefault(0),
                actualCount = count.GetValueOrDefault(data.Count - actualOffset);

            if (comparer is IRefComparerAdapter<TElement> wrapper) {
                // FIXME: Devirtualize somehow
                var sorter = new FastCLRSorter<TElement, IComparer<TElement>>(data, wrapper.Comparer);
                sorter.Sort(actualOffset, actualCount);
            } else {
                var sorter = new FastCLRSorterRef<TElement, TComparer>(data, comparer);
                sorter.Sort(actualOffset, actualCount);
            }
        }

        public static void IndexedSortRef<TElement, TComparer>(
            ArraySegment<TElement> data, ArraySegment<int> indices, TComparer comparer
        )
            where TComparer : IRefComparer<TElement>
        {
            var sorter = new IndexedSorter<TElement, TComparer>(
                data, indices, comparer
            );

            sorter.Sort(0, data.Count);
        }
    }
}

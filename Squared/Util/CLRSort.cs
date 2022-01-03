// from CoreCLR, MIT license follows:
/*
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Squared.Util {
    public readonly struct FastCLRSorterRef<TElement, TComparer>
        where TComparer : IRefComparer<TElement>
    {
        internal const int IntrosortSizeThreshold = 16;
        internal const int QuickSortDepthThreshold = 32;

        private readonly ArraySegment<TElement> Items;
        private readonly TComparer              RefComparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FastCLRSorterRef (ArraySegment<TElement> items, TComparer comparer) {
            Items = items;
            RefComparer = comparer;

            if (comparer is IRefComparerAdapter<TElement>)
                throw new ArgumentException("This is an adapter, use FastCLRSorter");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SwapIfGreaterWithItems (int a, int b) {
            if (a == b)
                return;

            var array = Items.Array;
            var offset = Items.Offset;

            if (RefComparer.Compare(ref array[offset + a], ref array[offset + b]) > 0) {
                var key = array[offset + a];
                array[offset + a] = array[offset + b];
                array[offset + b] = key;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap (int i, int j) {
            var array = Items.Array;
            var offset = Items.Offset;
            var t1 = array[offset + i];
            array[offset + i] = array[offset + j];
            array[offset + j] = t1;
        }

        internal void Sort(int left, int length) {
            IntrospectiveSort(left, length);
        }

        private void IntrospectiveSort(int left, int length) {
            if (length < 2)
                return;

            IntroSort(left, length + left - 1, 2 * FastMath.FloorLog2(length));
        }

        private void IntroSort(int lo, int hi, int depthLimit) {
            while (hi > lo) {
                int partitionSize = hi - lo + 1;
                if (partitionSize <= IntrosortSizeThreshold) {
                    if (partitionSize == 1) {
                        return;
                    }
                    if (partitionSize == 2) {
                        SwapIfGreaterWithItems(lo, hi);
                        return;
                    }
                    if (partitionSize == 3) {
                        SwapIfGreaterWithItems(lo, hi-1);
                        SwapIfGreaterWithItems(lo, hi);
                        SwapIfGreaterWithItems(hi-1, hi);
                        return;
                    }

                    InsertionSort(lo, hi);
                    return;
                }

                if (depthLimit == 0) {
                    Heapsort(lo, hi);
                    return;
                }

                depthLimit--;

                int p = PickPivotAndPartition(lo, hi);
                IntroSort(p + 1, hi, depthLimit);
                hi = p - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PickPivotAndPartition(int lo, int hi) {
            var array = Items.Array;
            var offset = Items.Offset;

            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int mid = lo + (hi - lo) / 2;

            SwapIfGreaterWithItems(lo, mid);
            SwapIfGreaterWithItems(lo, hi);
            SwapIfGreaterWithItems(mid, hi);

            var pivot = Items.Array[Items.Offset + mid];
            Swap(mid, hi - 1);
            int left = lo, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.
                
            while (left < right) {
                while (RefComparer.Compare(ref array[offset + ++left], ref pivot) < 0) ;
                while (RefComparer.Compare(ref pivot, ref array[offset + --right]) < 0) ;

                if (left >= right)
                    break;

                Swap(left, right);
            }

            // Put pivot in the right location.
            Swap(left, (hi - 1));
            return left;
        }

        private void Heapsort(int lo, int hi) {
            int n = hi - lo + 1;
            for (int i = n / 2; i >= 1; i = i - 1) {
                DownHeap(i, n, lo);
            }

            for (int i = n; i > 1; i = i - 1) {
                Swap(lo, lo + i - 1);

                DownHeap(1, i - 1, lo);
            }
        }

        private void DownHeap(int i, int n, int lo) {
            var array = Items.Array;
            var offset = Items.Offset;
            var d = array[offset + lo + i - 1];
            int child;

            while (i <= n / 2) {
                child = 2 * i;

                if (
                    (child < n) && 
                    (RefComparer.Compare(ref array[offset + lo + child - 1], ref array[offset + lo + child]) < 0)
                ) {
                    child++;
                }

                ref var current = ref array[offset + lo + child - 1];

                if (!(RefComparer.Compare(ref d, ref current) < 0))
                    break;

                array[offset + lo + i - 1] = current;
                i = child;
            }

            array[offset + lo + i - 1] = d;
        }

        private void InsertionSort (int lo, int hi) {
            var array = Items.Array;
            var offset = Items.Offset;
            int i, j;
            TElement t;
            for (i = lo; i < hi; i++)
            {
                j = i;
                t = array[offset + i + 1];

                while (j >= lo && RefComparer.Compare(ref t, ref array[offset + j]) < 0)
                {
                    array[offset + j + 1] = array[offset + j];
                    j--;
                }

                array[offset + j + 1] = t;
            }
        }
    }

    public readonly struct FastCLRSorter<TElement, TComparer>
        where TComparer : IComparer<TElement>
    {
        internal const int IntrosortSizeThreshold = 16;
        internal const int QuickSortDepthThreshold = 32;

        private readonly ArraySegment<TElement> Items;
        private readonly TComparer              NonRefComparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FastCLRSorter (ArraySegment<TElement> items, TComparer comparer) {
            Items = items;
            NonRefComparer = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SwapIfGreaterWithItems (int a, int b) {
            if (a == b)
                return;

            var array = Items.Array;
            var offset = Items.Offset;

            if (NonRefComparer.Compare(array[offset + a], array[offset + b]) > 0) {
                var key = array[offset + a];
                array[offset + a] = array[offset + b];
                array[offset + b] = key;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap (int i, int j) {
            var array = Items.Array;
            var offset = Items.Offset;
            var t1 = array[offset + i];
            array[offset + i] = array[offset + j];
            array[offset + j] = t1;
        }

        internal void Sort(int left, int length) {
            IntrospectiveSort(left, length);
        }

        private void IntrospectiveSort(int left, int length) {
            if (length < 2)
                return;

            IntroSort(left, length + left - 1, 2 * FastMath.FloorLog2(length));
        }

        private void IntroSort(int lo, int hi, int depthLimit) {
            while (hi > lo) {
                int partitionSize = hi - lo + 1;
                if (partitionSize <= IntrosortSizeThreshold) {
                    if (partitionSize == 1) {
                        return;
                    }
                    if (partitionSize == 2) {
                        SwapIfGreaterWithItems(lo, hi);
                        return;
                    }
                    if (partitionSize == 3) {
                        SwapIfGreaterWithItems(lo, hi-1);
                        SwapIfGreaterWithItems(lo, hi);
                        SwapIfGreaterWithItems(hi-1, hi);
                        return;
                    }

                    InsertionSort(lo, hi);
                    return;
                }

                if (depthLimit == 0) {
                    Heapsort(lo, hi);
                    return;
                }

                depthLimit--;

                int p = PickPivotAndPartition(lo, hi);
                IntroSort(p + 1, hi, depthLimit);
                hi = p - 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PickPivotAndPartition(int lo, int hi) {
            var array = Items.Array;
            var offset = Items.Offset;
            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int mid = lo + (hi - lo) / 2;

            SwapIfGreaterWithItems(lo, mid);
            SwapIfGreaterWithItems(lo, hi);
            SwapIfGreaterWithItems(mid, hi);

            var pivot = array[offset + mid];
            Swap(mid, hi - 1);
            int left = lo, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.
                
            while (left < right) {
                while (NonRefComparer.Compare(array[offset + ++left], pivot) < 0) ;
                while (NonRefComparer.Compare(pivot, array[offset + --right]) < 0) ;

                if (left >= right)
                    break;

                Swap(left, right);
            }

            // Put pivot in the right location.
            Swap(left, (hi - 1));
            return left;
        }

        private void Heapsort(int lo, int hi) {
            int n = hi - lo + 1;
            for (int i = n / 2; i >= 1; i = i - 1) {
                DownHeap(i, n, lo);
            }

            for (int i = n; i > 1; i = i - 1) {
                Swap(lo, lo + i - 1);

                DownHeap(1, i - 1, lo);
            }
        }

        private void DownHeap(int i, int n, int lo) {
            var array = Items.Array;
            var offset = Items.Offset;
            var d = array[offset + lo + i - 1];
            int child;

            while (i <= n / 2) {
                child = 2 * i;
                if (
                    (child < n) && 
                    (NonRefComparer.Compare(array[offset + lo + child - 1], array[offset + lo + child]) < 0)
                ) {
                    child++;
                }

                if (!(NonRefComparer.Compare(d, array[offset + lo + child - 1]) < 0))
                    break;

                array[offset + lo + i - 1] = array[offset + lo + child - 1];
                i = child;
            }

            array[offset + lo + i - 1] = d;
        }

        private void InsertionSort (int lo, int hi) {
            var array = Items.Array;
            var offset = Items.Offset;

            int i, j;
            TElement t;
            for (i = lo; i < hi; i++)
            {
                j = i;
                t = array[offset + i + 1];

                while (j >= lo && NonRefComparer.Compare(t, array[offset + j]) < 0)
                {
                    array[offset + j + 1] = array[offset + j];
                    j--;
                }

                array[offset + j + 1] = t;
            }
        }
    }
}

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

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Squared.Util {
    public struct FastCLRSorter<TElement, TComparer>
        where TComparer : IRefComparer<TElement>
    {
        internal const int IntrosortSizeThreshold = 16;
        internal const int QuickSortDepthThreshold = 32;

        private readonly TElement[] Items;
        private readonly TComparer  Comparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal FastCLRSorter (TElement[] items, TComparer comparer) {
            Items = items;
            Comparer = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SwapIfGreaterWithItems (int a, int b) {
            if (a != b) {
                if (Comparer.Compare(ref Items[a], ref Items[b]) > 0) {
                    var key = Items[a];
                    Items[a] = Items[b];
                    Items[b] = key;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap (int i, int j) {
            var t1 = Items[i];
            Items[i] = Items[j];
            Items[j] = t1;
        }

        internal void Sort(int left, int length) {
            IntrospectiveSort(left, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorLog2 (int n) {
            int result = 0;
            while (n >= 1) {
                result++;
                n /= 2;
            }
            return result;
        }

        private void IntrospectiveSort(int left, int length) {
            if (length < 2)
                return;

            IntroSort(left, length + left - 1, 2 * FloorLog2(Items.Length));
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
            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int mid = lo + (hi - lo) / 2;

            SwapIfGreaterWithItems(lo, mid);
            SwapIfGreaterWithItems(lo, hi);
            SwapIfGreaterWithItems(mid, hi);

            var pivot = Items[mid];
            Swap(mid, hi - 1);
            int left = lo, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.
                
            while (left < right) {
                while (Comparer.Compare(ref Items[++left], ref pivot) < 0) ;
                while (Comparer.Compare(ref pivot, ref Items[--right]) < 0) ;

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
            var d = Items[lo + i - 1];
            int child;

            while (i <= n / 2) {
                child = 2 * i;
                if (child < n && Comparer.Compare(ref Items[lo + child - 1], ref Items[lo + child]) < 0) {
                    child++;
                }

                if (!(Comparer.Compare(ref d, ref Items[lo + child - 1]) < 0))
                    break;

                Items[lo + i - 1] = Items[lo + child - 1];
                i = child;
            }

            Items[lo + i - 1] = d;
        }

        private void InsertionSort (int lo, int hi) {
            int i, j;
            TElement t;
            for (i = lo; i < hi; i++)
            {
                j = i;
                t = Items[i + 1];

                while (j >= lo && Comparer.Compare(ref t, ref Items[j]) < 0)
                {
                    Items[j + 1] = Items[j];
                    j--;
                }

                Items[j + 1] = t;
            }
        }
    }
}

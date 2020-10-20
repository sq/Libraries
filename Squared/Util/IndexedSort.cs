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
    public struct IndexedSorter<TElement, TComparer>
        where TComparer : IRefComparer<TElement>
    {
        internal const int IntrosortSizeThreshold = 16;
        internal const int QuickSortDepthThreshold = 32;

        private readonly ArraySegment<TElement> Items;
        private readonly ArraySegment<int>      Indices;
        private readonly TComparer  Comparer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal IndexedSorter (ArraySegment<TElement> items, ArraySegment<int> indices, TComparer comparer) {
            Items = items;
            Indices = indices;
            Comparer = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int Compare (int i, int j) {
            int a = Indices.Array[Indices.Offset + i], b = Indices.Array[Indices.Offset + j];
            return Comparer.Compare(ref Items.Array[Items.Offset + a], ref Items.Array[Items.Offset + b]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SwapIfGreaterWithItems (int i, int j) {
            if (i != j) {
                if (Compare(i, j) > 0) {
                    var temp = Indices.Array[Indices.Offset + i];
                    Indices.Array[Indices.Offset + i] = Indices.Array[Indices.Offset + j];
                    Indices.Array[Indices.Offset + j] = temp;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap (int i, int j) {
            var temp = Indices.Array[Indices.Offset + i];
            Indices.Array[Indices.Offset + i] = Indices.Array[Indices.Offset + j];
            Indices.Array[Indices.Offset + j] = temp;
        }

        internal void Sort(int left, int length) {
            IntrospectiveSort(left, length);
        }

        private void IntrospectiveSort(int left, int length) {
            if (length < 2)
                return;

            IntroSort(left, length + left - 1, 2 * FastMath.FloorLog2(Items.Count));
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
        private int ScanLeft (int left, ref TElement pivot) {
            while (Comparer.Compare(ref Items.Array[Items.Offset + Indices.Array[Indices.Offset + ++left]], ref pivot) < 0) ;
            return left;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ScanRight (int right, ref TElement pivot) {
            while (Comparer.Compare(ref pivot, ref Items.Array[Items.Offset + Indices.Array[Indices.Offset + --right]]) < 0) ;
            return right;
        }

        private int PickPivotAndPartition(int lo, int hi) {
            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int mid = lo + (hi - lo) / 2;

            SwapIfGreaterWithItems(lo, mid);
            SwapIfGreaterWithItems(lo, hi);
            SwapIfGreaterWithItems(mid, hi);

            var pivotIndex = mid;
            var pivot = Items.Array[Items.Offset + Indices.Array[Indices.Offset + pivotIndex]];
            Swap(mid, hi - 1);
            int left = lo, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.
                
            while (left < right) {
                left = ScanLeft(left, ref pivot);
                right = ScanRight(right, ref pivot);

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
            var dIndex = lo + i - 1;
            var dRawIndex = Indices.Array[Indices.Offset + dIndex];
            var d = Items.Array[Items.Offset + dIndex];
            int child;

            while (i <= n / 2) {
                child = 2 * i;
                if (child < n && Compare(lo + child - 1, lo + child) < 0) {
                    child++;
                }

                if (!(Compare(dIndex, lo + child - 1) < 0))
                    break;

                Indices.Array[Indices.Offset + lo + i - 1] = Indices.Array[Indices.Offset + lo + child - 1];
                i = child;
            }

            Indices.Array[Indices.Offset + lo + i - 1] = dRawIndex;
        }

        private void InsertionSort (int lo, int hi) {
            int i, j, tIndex, t;
            for (i = lo; i < hi; i++)
            {
                j = i;
                tIndex = i + 1;
                t = Indices.Array[Indices.Offset + tIndex];

                while (j >= lo && Comparer.Compare(ref Items.Array[Items.Offset + t], ref Items.Array[Items.Offset + Indices.Array[Indices.Offset + j]]) < 0)
                {
                    Indices.Array[Indices.Offset + j + 1] = Indices.Array[Indices.Offset + j];
                    Indices.Array[Indices.Offset + j + 1] = Indices.Array[Indices.Offset + j];
                    j--;
                }

                Indices.Array[Indices.Offset + j + 1] = t;
            }
        }
    }
}

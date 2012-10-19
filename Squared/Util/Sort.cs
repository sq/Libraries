using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using tIndex = System.Int32;
using tCount = System.Int32;

namespace Squared.Util {
    public static class Sort {
        public static void Timsort<T> (T[] data, int? offset = null, int? count = null, IComparer<T> comparer = null) {
            tIndex _offset;
            tCount _count;

            if (data == null)
                throw new ArgumentNullException("data");

            if (comparer == null)
                comparer = Comparer<T>.Default;

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
            if ((_offset < 0) || (_offset >= data.Length))
                throw new ArgumentException("offset");

            var impl = new TimsortImpl<T>(data, _offset, _count, comparer);
            impl.Sort();
        }

        private struct TimsortImpl<T> {
            public struct Run {
                public tIndex start;
                public tCount length;
            }

            public const int MaxStackSize = 128;

            public readonly T[] Data;
            public readonly tIndex Offset;
            public readonly tCount Count;
            public readonly IComparer<T> Comparer;

            public TimsortImpl (T[] data, tIndex offset, tCount count, IComparer<T> comparer) {
                Data = data;
                Offset = offset;
                Count = count;
                Comparer = comparer;
            }

            private static int clzll (UInt64 x) {
              if (x == 0)
                return 64;

              int n = 0;
              if (x <= 0x00000000FFFFFFFFL) {
                  n = n + 32; 
                  x = x << 32;
              }
              if (x <= 0x0000FFFFFFFFFFFFL) {
                  n = n + 16; 
                  x = x << 16;
              }
              if (x <= 0x00FFFFFFFFFFFFFFL) {
                  n = n + 8; 
                  x = x << 8;
              }
              if (x <= 0x0FFFFFFFFFFFFFFFL) {
                  n = n + 4; 
                  x = x << 4;
              }
              if (x <= 0x3FFFFFFFFFFFFFFFL) {
                  n = n + 2; 
                  x = x << 2;
              }
              if (x <= 0x7FFFFFFFFFFFFFFFL) {
                  n = n + 1;
              }

              return n;
            }

            private static int ComputeMinrun (UInt64 size) {
              int top_bit = 64 - clzll(size);
              int shift = Math.Max(top_bit, 6) - 6;
              int minrun = (int)(size >> shift);
              UInt64 mask = (1UL << shift) - 1;

              if ((mask & size) != 0)
                return minrun + 1;

              return minrun;
            }

            public void Swap (tIndex first, tIndex second) {
                T temp = Data[Offset + first];
                Data[Offset + first] = Data[Offset + second];
                Data[Offset + second] = temp;
            }

            public int Compare (tIndex first, tIndex second) {
                return Comparer.Compare(
                    Data[Offset + first], Data[Offset + second]
                );
            }

            public void ReverseElements (tIndex start, tIndex end) {
                while (true) {
                    if (start > end)
                        return;

                    Swap(start, end);
                    start++;
                    end--;
                }
            }

            public tIndex BinaryInsertionFind (tIndex offset, ref T x, tIndex size) {
                tIndex l = 0, r = size - 1, c = r >> 1;
                    
                T lx = Data[offset + Offset + l];

                /* check for beginning conditions */
                var comparisonResult = Comparer.Compare(x, lx);

                if (comparisonResult < 0) {
                    return 0;
                } else if (comparisonResult == 0) {
                    tIndex i = 1;

                    while (Comparer.Compare(x, Data[offset + Offset + i]) == 0)
                        i++;

                    return i;
                }

                T cx = Data[offset + Offset + c];
                while (true) {
                    comparisonResult = Comparer.Compare(x, cx);

                    if (comparisonResult < 0) {
                        if ((c - l) <= 1) 
                            return c;

                        r = c;
                    } else if (comparisonResult > 0) {
                        if ((r - c) <= 1) 
                            return c + 1;

                        l = c;
                    } else {
                        do {
                            cx = Data[offset + Offset + (++c)];
                        } while (Comparer.Compare(x, cx) == 0);

                        return c;
                    }

                    c = l + ((r - l) >> 1);
                    cx = Data[offset + Offset + c];
                }
            }

            public void BinaryInsertionSortStart(tIndex offset, tIndex start, tCount size) {
                for (tIndex i = start; i < size; i++) {
                    // If this entry is already correct, just move along
                    if (Compare(offset + i - 1, offset + i) <= 0)
                        continue;

                    // Else we need to find the right place, shift everything over, and squeeze in
                    T x = Data[offset + Offset + i];
                    tIndex location = BinaryInsertionFind(offset, ref x, i);
                    for (tIndex j = i - 1; j >= location; j--)
                        Data[offset + Offset + j + 1] = Data[offset + Offset + j];

                    Data[offset + location] = x;
                }
            }

            public void BinaryInsertionSort(tCount size) {
                BinaryInsertionSortStart(0, 1, size);
            }

            public tCount CountRun (tIndex start, tCount size) {
                tIndex curr;

                if ((size - start) == 1)
                    return 1;

                if (start >= (size - 2)) {
                    if (Compare(size - 2, size - 1) > 0)
                        Swap(size - 2, size - 1);

                    return 2;
                }

                curr = start + 2;

                if (Compare(start, start + 1) <= 0) {
                    // Increasing run
                    while (true) {
                        if (curr == (size - 1))
                            break;

                        if (Compare(curr - 1, curr) > 0)
                            break;

                        curr++;
                    }

                    return curr - start;
                } else {
                    // Decreasing run
                    while (true) {
                        if (curr == (size - 1))
                            break;

                        if (Compare(curr - 1, curr) <= 0)
                            break;

                        curr++;
                    }

                    ReverseElements(start, curr - 1);
                    return curr - start;
                }
            }

            // Returns true if the sort should abort
            public bool PushNext (
                Run[] run_stack, ref int stack_curr, 
                ref tIndex curr, ref tCount len, 
                ref tIndex run, int minrun
            ) {
                tCount size = Count;

                len = CountRun(curr, size);

                run = minrun;
                if (run < minrun)
                    run = minrun;

                if (run > size - curr)
                    run = size - curr;

                if (run > len) {
                    BinaryInsertionSortStart(curr, len, run);
                    len = run;
                }
                
                run_stack[stack_curr].start = curr;
                run_stack[stack_curr].length = len;
                stack_curr++;
                curr += len;

                if (curr == size) {
                    // finish up
                    while (stack_curr > 1) {
                        Merge(run_stack, stack_curr);
                        run_stack[stack_curr - 2].length += run_stack[stack_curr - 1].length;
                        stack_curr--;
                    }

                    return true;
                }

                return false;
            }

            public static bool CheckInvariant (Run[] stack, int stack_curr) {
                if (stack_curr < 2)
                    return true;

                if (stack_curr == 2) {
                    tCount A1 = stack[stack_curr - 2].length;
                    tCount B1 = stack[stack_curr - 1].length;

                    if (A1 <= B1)
                        return false;
                    else
                        return true;
                }

                tCount A = stack[stack_curr - 3].length;
                tCount B = stack[stack_curr - 2].length;
                tCount C = stack[stack_curr - 1].length;

                if ((A <= B + C) || (B <= C))
                    return false;
                else
                    return true;
            }

            private void CopyFromData (T[] dest, int sourceOffset, int countElements) {
                Array.Copy(Data, Offset + sourceOffset, dest, 0, countElements);
            }

            public void Merge (Run[] stack, int stack_curr) {
                tCount A = stack[stack_curr - 2].length;
                tCount B = stack[stack_curr - 1].length;
                tIndex curr = stack[stack_curr - 2].start;

                using (var storageBuffer = BufferPool<T>.Allocate(Math.Min(A, B))) {
                    var storage = storageBuffer.Data;

                    if (A < B) {
                        // left merge
                        CopyFromData(storage, curr, A);
                        tIndex i = 0;
                        tIndex j = curr + A;

                        for (tIndex k = curr; k < curr + A + B; k++) {
                            if ((i < A) && (j < curr + A + B)) {
                                if (Comparer.Compare(storage[i], Data[Offset + j]) <= 0) {
                                    Data[Offset + k] = storage[i++];
                                } else {
                                    Data[Offset + k] = Data[Offset + (j++)];
                                }
                            } else if (i < A) {
                                Data[Offset + k] = storage[i++];
                            } else {
                                Data[Offset + k] = Data[Offset + (j++)];
                            }
                        }
                    } else {
                        // right merge
                        CopyFromData(storage, curr + A, B);
                        tIndex i = B - 1;
                        tIndex j = curr + A - 1;

                        for (tIndex k = curr + A + B - 1; k >= curr; k--) {
                            if ((i >= 0) && (j >= curr)) {
                                if (Comparer.Compare(Data[Offset + j], storage[i]) > 0) {
                                    Data[Offset + k] = Data[Offset + (j--)];
                                } else {
                                    Data[Offset + k] = storage[i--];
                                }
                            } else if (i >= 0) {
                                Data[Offset + k] = storage[i--];
                            } else {
                                Data[Offset + k] = Data[Offset + (j--)];
                            }
                        }
                    }
                }
            }

            public int Collapse (Run[] stack, int stack_curr) {
                tCount size = Count;

                while (true) {
                    // if the stack only has one thing on it, we are done with the collapse
                    if (stack_curr <= 1)
                        break;

                    if ((stack_curr == 2) && (stack[0].length + stack[1].length == size)) {
                        // if this is the last merge, just do it
                        Merge(stack, stack_curr);
                        stack[0].length += stack[1].length;
                        stack_curr--;
                        break;
                    } else if ((stack_curr == 2) && (stack[0].length <= stack[1].length)) {
                        // check if the invariant is off for a stack of 2 elements
                        Merge(stack, stack_curr);
                        stack[0].length += stack[1].length;
                        stack_curr--;
                        break;
                    } else if (stack_curr == 2) {
                        break;
                    }

                    tCount A = stack[stack_curr - 3].length;
                    tCount B = stack[stack_curr - 2].length;
                    tCount C = stack[stack_curr - 1].length;

                    if (A <= B + C) {
                        // check first invariant
                        if (A < C) {
                            Merge(stack, stack_curr);
                            stack[stack_curr - 3].length += stack[stack_curr - 2].length;
                            stack[stack_curr - 2] = stack[stack_curr - 1];
                            stack_curr--;
                        } else {
                            Merge(stack, stack_curr);
                            stack[stack_curr - 2].length += stack[stack_curr - 1].length;
                            stack_curr--;
                        }
                    } else if (B <= C) {
                        // check second invariant
                        Merge(stack, stack_curr);
                        stack[stack_curr - 2].length += stack[stack_curr - 1].length;
                        stack_curr--;
                    } else {
                        break;
                    }
                }

                return stack_curr;
            }

            public void Sort () {
                tIndex curr = 0, run = 0;
                tCount len = 0;
                int stack_curr = 0;

                int minrun = ComputeMinrun((ulong)Count);

                using (var runStackBuffer = BufferPool<Run>.Allocate(MaxStackSize)) {
                    var run_stack = runStackBuffer.Data;

                    for (int i = 0; i < 3; i++) {
                        if (PushNext(run_stack, ref stack_curr, ref curr, ref len, ref run, minrun))
                            return;
                    }

                    while (true) {
                        if (!CheckInvariant(run_stack, stack_curr)) {
                            stack_curr = Collapse(run_stack, stack_curr);
                            continue;
                        }

                        if (PushNext(run_stack, ref stack_curr, ref curr, ref len, ref run, minrun))
                            return;
                    }
                }
            }
        }
    }
}

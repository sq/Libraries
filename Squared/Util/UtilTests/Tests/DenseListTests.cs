using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util {
    [TestFixture]
    public class DenseListTests {
        private int[] GenerateSequence (int length, int step = 1) {
            var result = new int[length];
            for (int i = 0; i < length; i++)
                result[i] = i * step;
            return result;
        }

        private int[] GenerateRandomInts (System.Random rng, int length) {
            var result = new int[length];
            for (int i = 0; i < length; i++)
                result[i] = rng.Next(int.MinValue / 100, int.MaxValue / 100);
            return result;
        }

        static int[] SequenceLengths = new[] {
            0, 1, 3, 4, 5, 9, 27, 99, 100, 101, 102, 500, 2048, 10239
        };

        private T[] MapIndirect<T> (T[] values, int[] indices) {
            var result = new T[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                result[i] = values[indices[i]];
            return result;
        }

        [Test, TestCaseSource("SequenceLengths")]
        public void SortRandomSequencesOfLength (int length) {
            var comparer = new RefComparerAdapter<Comparer<int>, int>(Comparer<int>.Default);
            var r = new System.Random(37);
            int sequenceCount = (length >= 500) ? 128 : 1024;

            for (int i = 0; i < sequenceCount; i++) {
                var seq = GenerateRandomInts(r, length);
                var dl = new DenseList<int>(seq);
                dl.Sort(comparer);
                Array.Sort(seq, Comparer<int>.Default);
                Assert.AreEqual(seq, dl.ToArray());
            }
        }

        [Test, TestCaseSource("SequenceLengths")]
        public void SortBackwardsSequenceOfLength (int length) {
            var comparer = new RefComparerAdapter<Comparer<int>, int>(Comparer<int>.Default);

            var seq = GenerateSequence(length);
            Array.Reverse(seq);
            var dl = new DenseList<int>(seq);
            dl.Sort(comparer);
            Array.Sort(seq, Comparer<int>.Default);
            Assert.AreEqual(seq, dl.ToArray());
        }

        [Test, TestCaseSource("SequenceLengths")]
        public void SortRandomSequencesOfLengthWithIndices (int length) {
            var comparer = new RefComparerAdapter<Comparer<int>, int>(Comparer<int>.Default);
            var r = new System.Random(37);
            int sequenceCount = (length >= 500) ? 128 : 1024;

            int[] indicesA = GenerateSequence(length), indicesB = GenerateSequence(length);

            for (int i = 0; i < sequenceCount; i++) {
                Array.Sort(indicesA);
                Array.Sort(indicesB);

                var seq = GenerateRandomInts(r, length);
                var dl = new DenseList<int>(seq);

                dl.Sort(comparer, indicesB);

                // FIXME: Why does the argument order here need to be the opposite of the backwards sequence test?
                Array.Sort(
                    // HACK: Make a temporary copy of the array because we only want to sort the indices.
                    indicesB, seq.ToArray(), Comparer<int>.Default
                );

                try {
                    Assert.AreEqual(MapIndirect(seq, indicesB), MapIndirect(dl.ToArray(), indicesA), "indirect mapped values");
                } catch {
                    Console.WriteLine("dl.values: {0}", string.Join(", ", dl.ToArray()));
                    Console.WriteLine("dl.indices: {0}", string.Join(", ", indicesA.ToArray()));
                    Console.WriteLine("arr.values: {0}", string.Join(", ", seq.ToArray()));
                    Console.WriteLine("arr.indices: {0}", string.Join(", ", indicesB.ToArray()));
                    Console.WriteLine();
                    Console.WriteLine("dl.mapindirect: {0}", string.Join(", ", MapIndirect(dl.ToArray(), indicesA)));
                    Console.WriteLine("arr.mapindirect: {0}", string.Join(", ", MapIndirect(seq, indicesB)));

                    throw;
                }
            }
        }

        [Test, TestCaseSource("SequenceLengths")]
        public void SortBackwardsSequenceOfLengthWithIndices (int length) {
            var comparer = new RefComparerAdapter<Comparer<int>, int>(Comparer<int>.Default);
            int[] indicesA = GenerateSequence(length), indicesB = GenerateSequence(length);

            var seq = GenerateSequence(length, 2);
            Array.Reverse(seq);

            var dl = new DenseList<int>(seq);

            dl.Sort(comparer, indicesA);

            Array.Sort(
                // HACK: Make a temporary copy of the array because we only want to sort the indices.
                seq.ToArray(), indicesB, Comparer<int>.Default
            );

            try {
                Assert.AreEqual(MapIndirect(seq, indicesB), MapIndirect(dl.ToArray(), indicesA), "indirect mapped values");
            } catch {
                Console.WriteLine("dl.values: {0}", string.Join(", ", dl.ToArray()));
                Console.WriteLine("dl.indices: {0}", string.Join(", ", indicesA.ToArray()));
                Console.WriteLine("arr.values: {0}", string.Join(", ", seq.ToArray()));
                Console.WriteLine("arr.indices: {0}", string.Join(", ", indicesB.ToArray()));
                Console.WriteLine();
                Console.WriteLine("dl.mapindirect: {0}", string.Join(", ", MapIndirect(dl.ToArray(), indicesA)));
                Console.WriteLine("arr.mapindirect: {0}", string.Join(", ", MapIndirect(seq, indicesB)));

                throw;
            }
        }

        public class Batch {
            public int Layer;
            public int MaterialID;
        }

        public sealed class BatchComparer : IRefComparer<Batch>, IComparer<Batch> {
            public int Compare (ref Batch x, ref Batch y) {
                if (x == null) {
                    if (y == null)
                        return 0;
                    else
                        return 1;
                } else if (y == null) {
                    return -1;
                }

                int result = x.Layer.CompareTo(y.Layer);
                if (result == 0) {
                    int mx = 0, my = 0;

                    mx = x.MaterialID;
                    my = y.MaterialID;

                    result = mx.CompareTo(my);
                }

                return result;
            }

            public int Compare (Batch x, Batch y) {
                return Compare(ref x, ref y);
            }
        }

        [Test]
        public void SortTwoSpecificItemsWithCustomComparer () {
            var items = new DenseList<Batch> {
                new Batch { Layer = 0, MaterialID = 1 },
                new Batch { Layer = -9999, MaterialID = 0 }
            };

            items.Sort(new BatchComparer());

            Assert.AreEqual(items[0].Layer, -9999);
            Assert.AreEqual(items[1].Layer, 0);
        }

        [Test]
        public void AddItemsAndTransition () {
            var l = new DenseList<int>();

            l.Add(0);
            l.Add(2);
            l.Add(3);
            l.Add(5);

            Assert.IsFalse(l.HasList);

            Assert.AreEqual(
                new int[] { 0, 2, 3, 5 },
                l.ToArray()
            );

            l.Add(7);
            l.Add(9);

            Assert.IsTrue(l.HasList);

            Assert.AreEqual(
                new int[] { 0, 2, 3, 5, 7, 9 },
                l.ToArray()
            );
        }

        [Test]
        public void Clear () {
            var l = new DenseList<int> { 1, 2 };

            l.Clear();
            Assert.AreEqual(
                new int[0],
                l.ToArray()
            );

            l.Add(1);
            l.Add(2);
            Assert.AreEqual(
                new int[] { 1, 2 },
                l.ToArray()
            );
        }

        [Test]
        public void RemoveRange () {
            var items = new int[] { 1, 2, 3, 4, 5, 6, 7 };
            var l = new DenseList<int>(items);
            l.RemoveRange(5, 1);
            l.RemoveRange(1, 2);
            var l2 = new List<int>(items);
            l2.RemoveRange(5, 1);
            l2.RemoveRange(1, 2);

            Assert.AreEqual(
                l2.ToArray(),
                l.ToArray()
            );
        }

        [Test]
        public void OverwriteWith () {
            var dl = new DenseList<int> { 1, 2 };
            dl.OverwriteWith(new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, 2, 5);
            Assert.AreEqual(
                new int[] { 2, 3, 4, 5, 6 },
                dl.ToArray()
            );
        }

        [Test]
        public void CloneWithList () {
            var dl = new DenseList<int> { 1, 2, 3, 4, 5, 6 };
            var cloned = dl.Clone();
            cloned.Add(7);
            Assert.IsTrue(cloned.HasList);
            Assert.AreEqual(
                new int[] { 1, 2, 3, 4, 5, 6 },
                dl.ToArray()
            );
            Assert.AreEqual(
                new int[] { 1, 2, 3, 4, 5, 6, 7 },
                cloned.ToArray()
            );
        }

        [Test]
        public void CloneWithoutList () {
            var dl = new DenseList<int> { 1, 2 };
            var cloned = dl.Clone();
            cloned.Add(3);
            Assert.IsFalse(dl.HasList);
            Assert.IsFalse(cloned.HasList);
            Assert.AreEqual(
                new int[] { 1, 2 },
                dl.ToArray()
            );
            Assert.AreEqual(
                new int[] { 1, 2, 3 },
                cloned.ToArray()
            );
        }

        [Test]
        public void CopyToWithList () {
            var dl = new DenseList<int> { 1, 2, 3, 4, 5, 6 };
            var destination = new DenseList<int> { 7, 8, 9, 10, 11 };
            dl.CopyTo(ref destination);
            destination.Add(12);
            Assert.IsTrue(destination.HasList);
            Assert.AreEqual(
                new int[] { 1, 2, 3, 4, 5, 6 },
                dl.ToArray()
            );
            Assert.AreEqual(
                new int[] { 7, 8, 9, 10, 11, 1, 2, 3, 4, 5, 6, 12 },
                destination.ToArray()
            );
        }

        [Test]
        public void CopyToWithoutList () {
            var dl = new DenseList<int> { 1 };
            var destination = new DenseList<int> { 2, 3 };
            dl.CopyTo(ref destination);
            destination.Add(4);
            Assert.IsFalse(dl.HasList);
            Assert.IsFalse(destination.HasList);
            Assert.AreEqual(
                new int[] { 1},
                dl.ToArray()
            );
            Assert.AreEqual(
                new int[] { 2, 3, 1, 4 },
                destination.ToArray()
            );
        }

        [Test]
        public void SimpleBenchmark () {
            // The list contains 4 items so that the DenseList doesn't need a heap allocation
            // You can test the heap allocated backing store with 5 items, it ends up being somewhat slower
            var dense = new DenseList<decimal> { 1, 2, 3, 4 };
            var list = new List<decimal> { 1, 2, 3, 4 };
            // for int64 try something like 32
            const int count = 1024000 * 10;

            var started1 = Stopwatch.GetTimestamp();
            unchecked {
                for (int i = 0; i < count; i++) {
                    for (int j = 0; j < dense.Count; j++)
                        dense[j]++;
                }
            }
            var ended1 = Stopwatch.GetTimestamp();

            for (int i = 0; i < 4; i++)
                dense[i] = i + 1;

            var started2 = Stopwatch.GetTimestamp();
            unchecked {
                for (int i = 0; i < count; i++) {
                    for (int j = 0; j < dense.Count; j++) {
                        dense.GetItem(j, out decimal item);
                        item++;
                        dense.SetItem(j, ref item);
                    }
                }
            }

            var started3 = Stopwatch.GetTimestamp();
            unchecked {
                for (int i = 0; i < count; i++) {
                    for (int j = 0; j < list.Count; j++)
                        list[j]++;
                }
            }

            var ended = Stopwatch.GetTimestamp();
            var elapsedDense = (double)(ended1 - started1) / Time.MillisecondInTicks;
            var elapsedDenseRef = (double)(started3 - started2) / Time.MillisecondInTicks;
            var elapsedList = (double)(ended - started3) / Time.MillisecondInTicks;
            // FIXME: for x64 release on my pc the ratio is 4 for int64s, it seems like it could be much lower...
            // It's pretty good for big types like Decimal though
            Console.WriteLine($"dense: {elapsedDense}, dense ref: {elapsedDenseRef}, list: {elapsedList}. ratio={elapsedDense / elapsedList}");
        }

        [Test]
        public void Queries () {
            var items = new DenseList<int> { 1, 2, 4, 8, 16, 24, 32, 16 };
            var below = items.Where(i => i < 16);
            var above = items.Where(i => i >= 16);

            Assert.AreEqual(
                new int[] { 1, 2, 4, 8 },
                below.ToArray()
            );
            Assert.AreEqual(
                new int[] { 16, 24, 32, 16 },
                above.ToArray()
            );

            var distinct = items.Distinct();
            Assert.AreEqual(
                new int[] { 1, 2, 4, 8, 16, 24, 32 },
                distinct.ToArray()
            );

            var shifted = below.Select(i => i + 1);
            Assert.AreEqual(
                new int[] { 2, 3, 5, 9 },
                shifted.ToArray()
            );

            var shiftedThenSelected = items.Select(i => i - 1).Where(i => i < 16);
            Assert.AreEqual(
                new int[] { 0, 1, 3, 7, 15, 15 },
                shiftedThenSelected.ToArray()
            );

            Assert.True(items.Any());
            Assert.True(items.All(i => i > 0));
            Assert.False(items.All(i => i > 24));
            Assert.True(items.Any(i => i > 24));
            Assert.False(items.Any(i => i > 48));

            var expanded = below.ToDenseList().SelectMany(i => new[] { i, i });
            Assert.AreEqual(
                new int[] { 1, 1, 2, 2, 4, 4, 8, 8 },
                expanded.ToArray()
            );
        }

        [Test]
        public void SortSubset () {
            var items = new DenseList<int> { 16, 14, 15, 2, 3, 1, 4 };
            items.SortNonRef(2, 4, Comparer<int>.Default);
            Assert.AreEqual(
                new int[] { 16, 14, 1, 2, 3, 15, 4 },
                items.ToArray()
            );
        }
    }
}

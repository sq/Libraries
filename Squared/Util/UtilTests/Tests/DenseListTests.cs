using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util {
    [TestFixture]
    public class DenseListTests {
        private static void FisherYates<T> (
            Random rng,
            T[] values
        ) {
            var n = values.Length;
            for (var i = 0; i < n; i += 1) {
                var j = rng.Next(i, n);
                var temp = values[j];
                values[j] = values[i];
                values[i] = temp;
            }
        }

        private int[] GenerateSequence (int length, int step = 1) {
            var result = new int[length];
            for (int i = 0; i < length; i++)
                result[i] = i * step;
            return result;
        }

        private int[] GenerateRandomInts (Random rng, int length) {
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
            var r = new Random(37);
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
            var r = new Random(37);
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
    }
}

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

        private int[] GenerateSequence (int length) {
            var result = new int[length];
            for (int i = 0; i < length; i++)
                result[i] = i;
            return result;
        }

        private int[] GenerateRandomInts (Random rng, int length) {
            var result = new int[length];
            for (int i = 0; i < length; i++)
                result[i] = rng.Next(int.MinValue, int.MaxValue);
            return result;
        }

        static int[] SequenceLengths = new[] {
            0, 1, 3, 4, 5, 9, 27, 99, 100, 101, 102, 500, 2048, 10239
        };

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
            var l = new DenseList<int>(new int[] { 1, 2 });

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
    }
}

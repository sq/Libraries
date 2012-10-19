using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util {
    [TestFixture]
    public class SortTests {
        public readonly int[] SortedSequence, ReversedSequence, RandomSequence;

        public SortTests () {
            SortedSequence = new[] { 1, 2, 4, 8, 16, 32, 64, 128, 129, 130, 140, 150, 160, 1234567 };
            ReversedSequence = SortedSequence.Reverse().ToArray();

            // Fixed seed for reproducability
            var rng = new Random(123456);
            RandomSequence = new int[4096];

            for (var i = 0; i < RandomSequence.Length; i++)
                RandomSequence[i] = rng.Next();
        }

        private void AssertSortsCorrectly<T> (T[] theSequence, IComparer<T> comparer = null) {
            var BCLCopy = (T[])theSequence.Clone();
            var TimsortCopy = (T[])theSequence.Clone();

            Array.Sort(BCLCopy, comparer ?? Comparer<T>.Default);
            Sort.Timsort(TimsortCopy, comparer: comparer);

            Assert.AreEqual(BCLCopy, TimsortCopy, "Sorted sequences are not equal");
        }

        [Test]
        public void SortsSortedSequence () {
            AssertSortsCorrectly(SortedSequence);
        }

        [Test]
        public void SortsReversedSequence () {
            AssertSortsCorrectly(ReversedSequence);
        }

        [Test]
        public void SortsRandomSequence () {
            AssertSortsCorrectly(RandomSequence);
        }
    }
}

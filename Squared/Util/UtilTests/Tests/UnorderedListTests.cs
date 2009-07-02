using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using System.Threading;

namespace Squared.Util {
    [TestFixture]
    public class UnorderedListTests {
        [Test]
        public void AddItems () {
            var l = new UnorderedList<int>();

            l.Add(0);
            l.Add(2);
            l.Add(3);
            l.Add(5);

            Assert.AreEqual(
                new int[] { 0, 2, 3, 5 },
                l.ToArray()
            );                
        }

        [Test]
        public void RemoveAt () {
            var l = new UnorderedList<int>(new int[] { 1, 2, 3, 4, 5 });

            l.RemoveAt(1);
            Assert.AreEqual(
                new int[] { 1, 5, 3, 4 },
                l.ToArray()
            );

            l.RemoveAt(2);
            Assert.AreEqual(
                new int[] { 1, 5, 4 },
                l.ToArray()
            );
        }

        [Test]
        public void Clear () {
            var l = new UnorderedList<int>(new int[] { 1, 2 });

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
        public void MutableEnumerator () {
            var l = new UnorderedList<int>(new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });

            using (var e = l.GetEnumerator())
            while (e.MoveNext()) {
                if ((e.Current % 2) == 0)
                    e.RemoveCurrent();
            }

            Assert.AreEqual(
                new int[] { 1, 9, 3, 7, 5 },
                l.ToArray()
            );
        }
    }
}

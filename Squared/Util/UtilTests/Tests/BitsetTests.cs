using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Squared.Util.Containers;

namespace Squared.Util {
    public class BitSetTests {
        [Test]
        public void BasicTest () {
            var set = new BitSet();
            Assert.IsFalse(set[0]);
            set[0] = true;
            set[1] = true;
            set[69] = true;
            set[70] = true;
            Assert.IsTrue(set[0]);
            Assert.IsTrue(set[1]);
            Assert.IsTrue(set[69]);
            Assert.IsTrue(set[70]);
            set[1] = false;
            set[70] = false;
            Assert.IsTrue(set[0]);
            Assert.IsFalse(set[1]);
            Assert.IsTrue(set[69]);
            Assert.IsFalse(set[70]);
        }

        [Test]
        public void Change () {
            var set = new BitSet();
            Assert.IsTrue(set.Change(0, true));
            Assert.IsFalse(set.Change(0, true));
            Assert.IsFalse(set.Change(1, false));
        }

        [Test]
        public void Enumerators () {
            var set = new BitSet { 1, 3, 7, 63 };
            set.Add(5);
            set[7] = false;
            Assert.AreEqual(new[] { 1, 3, 5, 63 }, set.ToArray());
        }
    }
}

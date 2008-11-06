using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class LRUCacheTests {
        protected LRUCache<string, int> MakeCache (int size, int count) {
            var lru = new LRUCache<string, int>(size);
            for (int i = 0; i < count; i++)
                lru[i.ToString()] = i;
            return lru;
        }

        [Test]
        public void BasicTest () {
            var lru = MakeCache(4, 6);

            Assert.IsFalse(lru.ContainsKey("0"));
            Assert.IsFalse(lru.ContainsKey("1"));
            Assert.IsTrue(lru.ContainsKey("2"));
            Assert.IsTrue(lru.ContainsKey("3"));
            Assert.IsTrue(lru.ContainsKey("4"));
            Assert.IsTrue(lru.ContainsKey("5"));

            for (int i = 2; i < 6; i++)
                Assert.AreEqual(lru[i.ToString()], i);
        }

        [Test]
        public void AccessingItemsMakesThemRecentlyUsed () {
            var lru = MakeCache(4, 4);

            int _ = lru["1"];
            _ = lru["3"];

            lru["4"] = 4;
            lru["5"] = 5;

            Assert.IsFalse(lru.ContainsKey("0"));
            Assert.IsTrue(lru.ContainsKey("1"));
            Assert.IsFalse(lru.ContainsKey("2"));
            Assert.IsTrue(lru.ContainsKey("3"));
            Assert.IsTrue(lru.ContainsKey("4"));
            Assert.IsTrue(lru.ContainsKey("5"));
        }

        [Test]
        public void IterationFollowsOrderOfUse () {
            var lru = MakeCache(4, 4);

            int _ = lru["1"];
            _ = lru["3"];

            var items = lru.ToArray();
            var expected = new KeyValuePair<string, int>[] { 
                new KeyValuePair<string, int>("0", 0),
                new KeyValuePair<string, int>("2", 2),
                new KeyValuePair<string, int>("1", 1),
                new KeyValuePair<string, int>("3", 3)
            };

            Assert.AreEqual(expected, items);
        }

        [Test]
        public void ModifyingInvalidatesEnumerators () {
            var lru = MakeCache(4, 4);

            var enumerator = lru.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());

            lru["2"] = 2;

            try {
                enumerator.MoveNext();
                Assert.Fail("MoveNext did not raise an InvalidOperationException");
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void AddingDuplicateRaises () {
            var lru = MakeCache(4, 4);

            try {
                lru.Add("2", 2);
                Assert.Fail("Add did not raise an InvalidOperationException");
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void Interfaces () {
            var lru = MakeCache(4, 4);

            var idict = (IDictionary<string, int>)lru;

            Assert.IsTrue(idict.Contains(new KeyValuePair<string, int>("0", 0)));
            Assert.IsFalse(idict.Contains(new KeyValuePair<string, int>("0", 1)));

            string[] keys = idict.Keys.ToArray();
            Array.Sort(keys);
            string[] expected = new string[] { "0", "1", "2", "3" };

            Assert.AreEqual(expected, keys);
        }
    }
}

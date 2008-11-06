using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;

namespace Squared.Util {
    [TestFixture]
    public class LRUCacheTests {
        [Test]
        public void BasicTest () {
            var lru = new LRUCache<string, int>(4);
            for (int i = 0; i < 6; i++)
                lru[i.ToString()] = i;

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
            var lru = new LRUCache<string, int>(4);
            for (int i = 0; i < 4; i++)
                lru[i.ToString()] = i;

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
            var lru = new LRUCache<string, int>(4);
            for (int i = 0; i < 4; i++)
                lru[i.ToString()] = i;

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
            var lru = new LRUCache<string, int>(4);
            for (int i = 0; i < 4; i++)
                lru[i.ToString()] = i;

            var enumerator = lru.GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());

            lru["2"] = 2;

            try {
                enumerator.MoveNext();
                Assert.Fail("MoveNext did not raise an InvalidOperationException");
            } catch (InvalidOperationException) {
            }
        }
    }
}

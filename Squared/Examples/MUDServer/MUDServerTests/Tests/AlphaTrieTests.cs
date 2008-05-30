using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MUDServer {
    [TestFixture]
    public class AlphaTrieTests {
        [Test]
        public void FirstInsertion () {
            var AT = new AlphaTrie<string>();
            AT.Insert("first", "word");
            Assert.IsNotNull(AT.FindByKeyExact("first"));
        }

        [Test]
        public void MultipleInsertion () {
            var AT = new AlphaTrie<string>();
            AT.Insert("first", "word");
            Assert.IsNotNull(AT.FindByKeyExact("first"));
            AT.Insert("firsts", "awesome");
            Assert.IsNotNull(AT.FindByKeyExact("first"));
            Assert.IsNotNull(AT.FindByKeyExact("firsts"));
            AT.Insert("second", "time");
            Assert.IsNotNull(AT.FindByKeyExact("first"));
            Assert.IsNotNull(AT.FindByKeyExact("firsts"));
            Assert.IsNotNull(AT.FindByKeyExact("second"));
            AT.Insert("seconds", "matter");
            Assert.IsNotNull(AT.FindByKeyExact("first"));
            Assert.IsNotNull(AT.FindByKeyExact("firsts"));
            Assert.IsNotNull(AT.FindByKeyExact("second"));
            Assert.IsNotNull(AT.FindByKeyExact("seconds"));
            AT.Insert("secondary", "place");
            Assert.IsNotNull(AT.FindByKeyExact("first"));
            Assert.IsNotNull(AT.FindByKeyExact("firsts"));
            Assert.IsNotNull(AT.FindByKeyExact("second"));
            Assert.IsNotNull(AT.FindByKeyExact("seconds"));
            Assert.IsNotNull(AT.FindByKeyExact("secondary"));
        }

        [Test]
        public void RemoveTest () {
            var AT = new AlphaTrie<string>();
            AT.Insert("first", "word");
            AT.Insert("firsts", "awesome");
            AT.Insert("second", "time");
            AT.Insert("seconds", "matter");
            AT.Insert("secondary", "place");

            Assert.IsTrue(AT.Remove("first"));
            Assert.IsTrue(AT.Remove("seconds"));
            Assert.IsFalse(AT.Remove("seconds"));
            Assert.IsFalse(AT.Remove("pancakes"));
            Assert.IsNotNull(AT.FindByKeyExact("firsts"));
            Assert.IsNotNull(AT.FindByKeyExact("second"));
            Assert.IsNotNull(AT.FindByKeyExact("secondary"));
        }

        [Test]
        public void NoNullValues () {
            var AT = new AlphaTrie<string>();
            try {
                AT.Insert("null", null);
                Assert.Fail("Inserting a Null value into the AlphaTrie succeeded.");
            }
            catch (InvalidOperationException) {
            }
        }
    }
}
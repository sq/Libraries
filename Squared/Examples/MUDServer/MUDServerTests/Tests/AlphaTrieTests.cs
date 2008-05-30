using System;
using System.Collections.Generic;
using System.Linq;
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
        public void GetStartKeys () {
            var AT = new AlphaTrie<string>();
            Assert.IsNotNull(AT.FindByKeyStart("a").Count() == 0);

            AT.Insert("a", "word");
            Assert.IsNotNull(AT.FindByKeyStart("a").Count() == 1);

            AT.Insert("ab", "word");
            Assert.IsNotNull(AT.FindByKeyStart("a").Count() == 2);

            AT.Insert("aba", "word");
            Assert.IsNotNull(AT.FindByKeyStart("a").Count() == 3);

            AT.Insert("abb", "word");
            Assert.IsNotNull(AT.FindByKeyStart("a").Count() == 4);

            AT.Insert("abab", "word");
            Assert.IsNotNull(AT.FindByKeyStart("a").Count() == 5);


            Assert.IsNotNull(AT.FindByKeyStart("b").Count() == 0);

            AT.Insert("bbbb", "word");
            Assert.IsNotNull(AT.FindByKeyStart("b").Count() == 1);

            AT.Insert("bbba", "word");
            Assert.IsNotNull(AT.FindByKeyStart("b").Count() == 2);

            AT.Insert("bbb", "word");
            Assert.IsNotNull(AT.FindByKeyStart("b").Count() == 3);

            AT.Insert("bb", "word");
            Assert.IsNotNull(AT.FindByKeyStart("b").Count() == 4);

            AT.Insert("b", "word");
            Assert.IsNotNull(AT.FindByKeyStart("b").Count() == 5);
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

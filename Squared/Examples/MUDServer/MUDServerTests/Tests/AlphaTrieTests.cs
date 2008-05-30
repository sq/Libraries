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
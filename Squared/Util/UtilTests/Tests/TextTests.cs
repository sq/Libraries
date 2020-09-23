using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Squared.Util.Text;

namespace Squared.Util {
    [TestFixture]
    public class TextTests {
        [Test]
        public void CodepointEnumerable () {
            Assert.AreEqual(0, "".Codepoints().Count());
            Assert.AreEqual(1, "a".Codepoints().Count());
            Assert.AreEqual(1, "こ".Codepoints().Count());
            Assert.AreEqual(1, "\U0002F8B6".Codepoints().Count());
            Assert.AreEqual(3, "\U0002F8B6\U0002F8CD\U0002F8D3".Codepoints().Count());
            Assert.AreEqual(4, "\U0002F8B6a\U0002F8D3b".Codepoints().Count());
        }

        [Test]
        public void DecodeSurrogatePair () {
            uint codepoint;
            Assert.False(Unicode.DecodeSurrogatePair('\0', '\0', out codepoint));
            Assert.AreEqual(0, codepoint);
            Assert.False(Unicode.DecodeSurrogatePair('a', '\0', out codepoint));
            Assert.AreEqual((uint)'a', codepoint);
            Assert.False(Unicode.DecodeSurrogatePair('a', 'b', out codepoint));
            Assert.AreEqual((uint)'a', codepoint);
            Assert.True(Unicode.DecodeSurrogatePair("\U0002F8B6"[0], "\U0002F8B6"[1], out codepoint));
            Assert.AreEqual((uint)0x2F8B6, codepoint);
            Assert.False(Unicode.DecodeSurrogatePair("\U0002F8B6"[0], '\0', out codepoint));
            Assert.AreEqual(0, codepoint);
        }
    }
}

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
            var cps = "\U0002F8B6a\U0002F8D3b".Codepoints(0).Select(cp => cp.Codepoint).ToArray();
            Assert.AreEqual(
                new uint[] {
                    0x2F8B6, 'a', 0x2F8D3, 'b'
                }, cps
            );
            cps = "\U0002F8B6a\U0002F8D3b".Codepoints(1).Select(cp => cp.Codepoint).ToArray();
            Assert.AreEqual(
                new uint[] {
                    0xFFFD, 'a', 0x2F8D3, 'b'
                }, cps                
            );
            var chis = "\U0002F8B6a\U0002F8D3b".Codepoints(0).Select(cp => cp.CharacterIndex).ToArray();
            Assert.AreEqual(
                new int[] {
                    0, 2, 3, 5
                }, chis
            );
            var cpis = "\U0002F8B6a\U0002F8D3b".Codepoints(0).Select(cp => cp.CodepointIndex).ToArray();
            Assert.AreEqual(
                new int[] {
                    0, 1, 2, 3
                }, cpis
            );
            Assert.AreEqual(3, "\U0002F8B6a\U0002F8D3b".Codepoints(2).Count());
            Assert.AreEqual(2, "\U0002F8B6a\U0002F8D3b".Codepoints(3).Count());
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
            Assert.AreEqual(0xFFFD, codepoint);
        }

        [Test]
        public void NthCodepoint () {
            var str = "\0\U0002F8B6a\U0002F8D3b\0c";
            Assert.AreEqual(null, Unicode.NthCodepoint(str, -1));
            Assert.AreEqual(0, Unicode.NthCodepoint(str, 0));
            Assert.AreEqual(0x2F8B6, Unicode.NthCodepoint(str, 1));
            Assert.AreEqual('a', Unicode.NthCodepoint(str, 2));
            Assert.AreEqual(null, Unicode.NthCodepoint(str, 12));
        }

        [Test]
        public void FindWordBoundary () {
            var str = "  hello world   test \t  \U0002F8B6\U0002F8D3b ok\0";
            Assert.AreEqual(new Pair<int>(0, 2), Unicode.FindWordBoundary(str, 0));
            Assert.AreEqual(new Pair<int>(0, 2), Unicode.FindWordBoundary(str, 1));
            Assert.AreEqual(new Pair<int>(2, 7), Unicode.FindWordBoundary(str, 2));
            Assert.AreEqual(new Pair<int>(2, 7), Unicode.FindWordBoundary(str, 4));
            Assert.AreEqual(new Pair<int>(24, 29), Unicode.FindWordBoundary(str, 24));
            Assert.AreEqual(new Pair<int>(24, 29), Unicode.FindWordBoundary(str, 25));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util.DeclarativeSort;

namespace Squared.Util {
    [TestFixture]
    public class DeclarativeSortTests {
        public DeclarativeSortTests () {
        }
    }

    [TestFixture]
    public class TaggingTests {
        private readonly Tag A, B, C;

        public TaggingTests () {
            A = Tag.New("a");
            B = Tag.New("b");
            C = Tag.New("c");
        }

        [Test]
        public void TagsAreInterned () {
            Assert.AreSame(A, Tag.New("a"));
        }

        [Test]
        public void AddSelfIsNoOp () {
            Assert.AreSame(A, A + A);
            Assert.AreSame(A, A + A + A);

            var ab = A + B;
            Assert.AreSame(ab, ab + (TagSet)ab);
            Assert.AreSame(ab, ab + A);
        }

        [Test]
        public void TagSetsAreInterned () {
            var ab = A + B;
            Assert.AreSame(B + A, ab);
        }

        [Test]
        public void RedundantAddIsNoOp () {
            var ab = A + B;
            Assert.AreSame(B + A + B, ab);
        }

        [Test]
        public void Contains () {
            var ab = A + B;
            Assert.IsTrue(ab.Contains(A));
            Assert.IsTrue(ab.Contains(B));
            Assert.IsFalse(ab.Contains(C));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util.DeclarativeSort {
    [TestFixture]
    public class DSTaggingTests {
        private readonly Tag A, B, C;

        public DSTaggingTests () {
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
            var a = (ITags)A;
            Assert.IsTrue(a.Contains(A));
            Assert.IsFalse(a.Contains(B));

            var ab = A + B;
            Assert.IsTrue(ab.Contains(A));
            Assert.IsTrue(ab.Contains(B));
            Assert.IsFalse(ab.Contains(C));

            Assert.IsTrue (ab & A);
            Assert.IsFalse(ab & C);

            Assert.IsFalse(ab ^ A);
            Assert.IsTrue (ab ^ C);
        }
    }

    [TestFixture]
    public class DSOrderingRuleTests {
        private readonly Tag A, B, C;

        public DSOrderingRuleTests () {
            A = Tag.New("a");
            B = Tag.New("b");
            C = Tag.New("c");
        }

        [Test]
        public void RulesAreUnique () {
            var sorter = new Sorter {
                A < B,
                B < C
            };

            Assert.AreEqual(2, sorter.Orderings.Count);

            sorter.Add(A < B);

            Assert.AreEqual(2, sorter.Orderings.Count);
        }

        [Test]
        public void CanCompareUsingOrderings () {
            var rs = new TagOrderingCollection {
                A < B,
                B < C
            };

            Assert.AreEqual(0, rs.Compare(A, A));
            Assert.AreEqual(0, rs.Compare(A, C));
            Assert.AreEqual(0, rs.Compare(C, A));
            Assert.AreEqual(-1, rs.Compare(A, B));
            Assert.AreEqual(-1, rs.Compare(B, C));
            Assert.AreEqual(1, rs.Compare(B, A));
            Assert.AreEqual(1, rs.Compare(C, B));
        }

        [Test]
        public void CompareAbortsOnContradiction () {
            var rs = new TagOrderingCollection {
                A < B,
                A > C
            };

            Exception error;
            var result = rs.Compare(A + B + C, A, out error);
            Assert.IsTrue(error is ContradictoryOrderingException);
            Assert.IsFalse(result.HasValue);
            Console.WriteLine(error.Message);
        }

        [Test]
        public void CompositeOrderings () {
            var rs = new TagOrderingCollection {
                {A + B, C + A}
            };

            Assert.AreEqual(0, rs.Compare(A, B));
            Assert.AreEqual(0, rs.Compare(A + B, B));

            Assert.AreEqual(-1, rs.Compare(A + B, C + A));
            Assert.AreEqual(1, rs.Compare(C + A, A + B));

            Assert.AreEqual(0, rs.Compare(A, A + B + C));
            Assert.AreEqual(0, rs.Compare(B, A + B + C));
            Assert.AreEqual(0, rs.Compare(C, A + B + C));
        }
    }

    [TestFixture]
    public class DSAutoCreateTests {
        public static Tag A, B, C;
        public Tag D;

        private void ValidateStaticInitialization () {
            Assert.AreEqual("A", A.Name);
            Assert.AreEqual("B", B.Name);
            Assert.AreEqual("custom c", C.Name);
        }

        [Test]
        public void AutoCreatesStaticTags () {
            A = B = D = null;
            C = Tag.New("custom c");

            Tag.AutoCreate<DSAutoCreateTests>();
            ValidateStaticInitialization();

            Assert.AreEqual(null, D);
        }

        [Test]
        public void AutoCreatesInstanceTags () {
            A = B = D = null;
            C = Tag.New("custom c");

            Tag.AutoCreate(this);

            ValidateStaticInitialization();
            Assert.AreEqual("D", D.Name);
        }
    }
}

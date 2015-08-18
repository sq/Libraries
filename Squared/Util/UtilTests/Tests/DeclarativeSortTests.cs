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
            Assert.AreSame(A, (A + A).Object);
            Assert.AreSame(A, (A + A + A).Object);

            var ab = A + B;
            Assert.AreSame(ab.Object, (ab + ab).Object);
            Assert.AreSame(ab.Object, (ab + A).Object);
        }

        [Test]
        public void TagSetsAreInterned () {
            var ab = A + B;
            Assert.AreSame((B + A).Object, ab.Object);
        }

        [Test]
        public void RedundantAddIsNoOp () {
            var ab = A + B;
            Assert.AreSame((B + A + B).Object, ab.Object);
        }

        [Test]
        public void Contains () {
            var a = (Tags)A;
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
        private readonly Tag A, B, C, D;

        public DSOrderingRuleTests () {
            A = Tag.New("a");
            B = Tag.New("b");
            C = Tag.New("c");
            D = Tag.New("d");
        }

        [Test]
        public void CanCompareUsingOrderings () {
            var rs = new TagOrderingCollection {
                A < B,
                B < C
            };

            Assert.AreEqual(0, rs.Compare(A, A));
            Assert.AreEqual(-1, rs.Compare(A, C));
            Assert.AreEqual(1, rs.Compare(C, A));
            Assert.AreEqual(-1, rs.Compare(A, B));
            Assert.AreEqual(-1, rs.Compare(B, C));
            Assert.AreEqual(1, rs.Compare(B, A));
            Assert.AreEqual(1, rs.Compare(C, B));
        }

        [Test]
        [Ignore]
        public void CompareAbortsOnContradiction () {
            // FIXME
            var rs = new TagOrderingCollection {
                A < B,
                B < A
            };

            Exception error;
            Assert.Throws<OrderingCycleException>(
                () => rs.Compare(A, B)
            );
        }

        [Test]
        public void CompositeOrderings () {
            var rs = new TagOrderingCollection {
                (A + B) < (C + A)
            };

            Assert.AreEqual(-1, rs.Compare(A + B, C + A));
            Assert.AreEqual(1, rs.Compare(C + A, A + B));
            Assert.AreEqual(-1, rs.Compare(A + B + D, A + C + D));
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

    public struct Taggable : IHasTags {
        public readonly Tags Tags;

        public Taggable (Tags tags) {
            Tags = tags;
        }

        public void GetTags (out Tags tags) {
            tags = Tags;
        }

        public static implicit operator Taggable (Tag tag) {
            return new Taggable(tag);
        }

        public static implicit operator Taggable (Tags tags) {
            return new Taggable(tags);
        }

        public override string ToString () {
            return Tags.ToString();
        }
    }

    [TestFixture]
    public class DSValueSortingTests {
        public Tag A, B, C, D;
        public Sorter<Taggable> Sorter;

        [TestFixtureSetUp]
        public void SetUp () {
            Tag.AutoCreate(this);

            Sorter = new Sorter<Taggable> {
                { t => t.Tags, A < B, B < C, C < D }
            };
        }

        [Test]
        public void SortsValuesImplementingInterface () {
            var values = new Taggable[] {
                A,
                A + B,
                A + B + C,
                A + B + C + D,
                A + C,
                A + C + D,
                B,
                B + D,
                B + C + D,
                C,
                C + D,
                D
            };
            int l = values.Length - 1;

            Sorter.Sort(values, ascending: true);
            Console.WriteLine(string.Join(", ", values));

            var expected = (Taggable[])values.Clone();
            Array.Reverse(expected);

            Sorter.Sort(values, ascending: false);
            Console.WriteLine(string.Join(", ", values));
            Assert.AreEqual(expected, values);
        }
    }
}

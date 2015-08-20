using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util.DeclarativeSort {
    public static class Util {
        public static int? IndexOf<T> (T[] haystack, T[] needle, IComparer<T> comparer = null) {
            if (comparer == null)
                comparer = Comparer<T>.Default;

            var maxI = haystack.Length - needle.Length;

            for (int i = 0; i <= maxI; i++) {
                // FIXME: Inefficient
                for (int j = 0; j < needle.Length; j++) {
                    if (comparer.Compare(haystack[i + j], needle[j]) != 0)
                        break;

                    if (j == needle.Length - 1)
                        return i;
                }
            }

            return null;
        }

        public static void AssertPrecedes<T> (T[] haystack, params T[][] orderedSequences) {
            var positions = new int?[orderedSequences.Length];

            for (int i = 0; i < orderedSequences.Length; i++) {
                var seq = orderedSequences[i];
                positions[i] = IndexOf(haystack, seq);
                Assert.IsTrue(positions[i].HasValue, "sequence #{0} ({1}) not found in haystack", i, string.Join(", ", seq));
            }

            for (int i = 0; i < orderedSequences.Length - 1; i++)
                Assert.Less(
                    positions[i].Value, positions[i + 1].Value, 
                    "sequence #{0} ({1})  does not precede #{2} ({3}) ", 
                    i, string.Join(", ", orderedSequences[i]), 
                    i + 1, string.Join(", ", orderedSequences[i + 1])
                );
        }
    }

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

    public struct Taggable : IComparable<Taggable> {
        public readonly Tags Tags;

        public Taggable (Tags tags) {
            Tags = tags;
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

        public int CompareTo (Taggable other) {
            return Tags.Id.CompareTo(other.Tags.Id);
        }
    }

    public struct TaggableWithIndex : IComparable<TaggableWithIndex> {
        public readonly Tags Tags;
        public readonly int Index;

        public TaggableWithIndex (Tags tags, int index) {
            Tags = tags;
            Index = index;
        }

        public override string ToString () {
            return String.Format("<{0}, {1}>", Tags, Index);
        }

        public int CompareTo (TaggableWithIndex other) {
            var result = Tags.Id.CompareTo(other.Tags.Id);
            if (result == 0)
                result = Index.CompareTo(other.Index);

            return result;
        }
    } 

    [TestFixture]
    public class DSTagSortingTests {
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
        public void SortsTags () {
            var values = new Taggable[] {
                A + B + C,
                A,
                B + C + D,
                A + D,
                A + B,
                B,
                D,
                A + C,
                B + D,
                C,
                A + B + C + D,
                C + D,
                A + C + D,
            };

            Sorter.Sort(values, ascending: true);
            Console.WriteLine(string.Join(", ", values));

            Util.AssertPrecedes(values, 
                new Taggable[] { A }, 
                new Taggable[] { B }, 
                new Taggable[] { C },
                new Taggable[] { D }
            );

            Util.AssertPrecedes(values, 
                new Taggable[] { A + B }, 
                new Taggable[] { C }
            );

            Util.AssertPrecedes(values, 
                new Taggable[] { A + B + C }, 
                new Taggable[] { D }
            );

            Util.AssertPrecedes(values, 
                new Taggable[] { A + B }, 
                new Taggable[] { C + D }
            );
        }
    }

    [TestFixture]
    public class DSTagValueSortingTests {
        public Tag A, B, C;
        public Sorter<TaggableWithIndex> Sorter;

        [TestFixtureSetUp]
        public void SetUp () {
            Tag.AutoCreate(this);

            Sorter = new Sorter<TaggableWithIndex> {
                { v => v.Tags, A < B, B < C },
                { v => v.Index }
            };
        }

        [Test]
        public void SortsValues () {
            var values = new TaggableWithIndex[] {
                new TaggableWithIndex(B, 0),
                new TaggableWithIndex(C, 7),
                new TaggableWithIndex(A, 0),
                new TaggableWithIndex(C, -1),
                new TaggableWithIndex(A, 2),
                new TaggableWithIndex(A + B, 0),
                new TaggableWithIndex(A + B, 3),
                new TaggableWithIndex(B, 2),
                new TaggableWithIndex(C, 3),
                new TaggableWithIndex(B, 1),
                new TaggableWithIndex(A, 1),
            };

            Sorter.Sort(values, ascending: true);
            Console.WriteLine(string.Join(", ", values));

            Util.AssertPrecedes(values, new[] {
                    new TaggableWithIndex(A, 0),
                    new TaggableWithIndex(A, 1)
                }, new[] {
                    new TaggableWithIndex(B, 0),
                    new TaggableWithIndex(B, 1)
                }, new[] {
                    new TaggableWithIndex(C, 3),
                    new TaggableWithIndex(C, 7)
                }
            );

            Util.AssertPrecedes(values,
                // No explicit ordering for (A, A + B) or (B, A + B)
                new [] {
                    new TaggableWithIndex(A + B, 0),
                    new TaggableWithIndex(A + B, 3)
                }, new[] {
                    new TaggableWithIndex(C, 3),
                    new TaggableWithIndex(C, 7)
                }
            );
        }
    }}

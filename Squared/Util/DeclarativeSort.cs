using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

using RuleSet = System.Linq.Expressions.Expression<System.Func<bool>>;

namespace Squared.Util.DeclarativeSort {
    public interface ITags {
        bool Contains (Tag tag);
        int Count { get; }
        Tag this [ int index ] { get; }
        Dictionary<Tag, ITags> TransitionCache { get; }
    }

    public class Tag : ITags {
        public class EqualityComparer : IEqualityComparer<Tag> {
            public static readonly EqualityComparer Instance = new EqualityComparer();

            public bool Equals (Tag x, Tag y) {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode (Tag tag) {
                return tag.Id;
            }
        }

        public class Comparer : IComparer<Tag> {
            public static readonly Comparer Instance = new Comparer();

            public int Compare (Tag x, Tag y) {
                return y.Id - x.Id;
            }
        }

        private static int NextId = 1;
        private static readonly Dictionary<string, Tag> TagCache = new Dictionary<string, Tag>();

        public readonly string Name;
        public readonly int    Id;
        public Dictionary<Tag, ITags> TransitionCache { get; private set; }

        internal Tag (string name) {
            Name = name;
            Id = NextId++;
            TransitionCache = new Dictionary<Tag, ITags>(EqualityComparer.Instance);
        }

        Tag ITags.this [int index] {
            get {
                if (index == 0)
                    return this;
                else
                    throw new ArgumentOutOfRangeException("index");
            }
        }

        int ITags.Count {
            get {
                return 1;
            }
        }

        bool ITags.Contains (Tag tag) {
            return (tag == this);
        }

        public override int GetHashCode () {
            return Id;
        }

        public override bool Equals (object obj) {
            return ReferenceEquals(this, obj);
        }

        public override string ToString () {
            return Name;
        }

        public static ITags operator + (Tag lhs, ITags rhs) {
            return TagSet.Transition(rhs, lhs);
        }

        public static ITags operator + (ITags lhs, Tag rhs) {
            return TagSet.Transition(lhs, rhs);
        }

        public static ITags operator + (Tag lhs, Tag rhs) {
            return TagSet.Transition(lhs, rhs);
        }

        public static Tag New (string name) {
            Tag result;
            if (!TagCache.TryGetValue(name, out result))
                TagCache.Add(name, result = new Tag(string.Intern(name)));

            return result;
        }
    }

    public partial class TagSet : ITags {
        private static int NextId = 1;

        private readonly Tag[] Tags;
        public Dictionary<Tag, ITags> TransitionCache { get; private set; }
        public readonly int Id;

        private TagSet (Tag[] tags) {
            Tags = (Tag[]) tags.Clone();
            TransitionCache = new Dictionary<Tag, ITags>(Tag.EqualityComparer.Instance);
            Id = NextId++;
        }

        public int Count {
            get {
                return Tags.Length;
            }
        }

        public Tag this [int index] {
            get {
                return Tags[index];
            }
        }

        public bool Contains (Tag tag) {
            for (var i = 0; i < Tags.Length; i++)
                if (Tags[i] == tag)
                    return true;

            return false;
        }

        public override int GetHashCode () {
            return Id;
        }

        public override bool Equals (object obj) {
            return ReferenceEquals(this, obj);
        }

        public override string ToString () {
            return string.Join<Tag>(", ", Tags);
        }
    }

    public partial class TagSet : ITags {
        private class TagArrayComparer : IEqualityComparer<Tag[]> {
            public bool Equals (Tag[] x, Tag[] y) {
                return x.SequenceEqual(y);
            }

            public int GetHashCode (Tag[] tags) {
                var result = 0;
                foreach (var tag in tags)
                    result = (result << 2) ^ tag.Id;
                return result;
            }
        }

        internal static readonly Dictionary<Tag[], TagSet> SetCache = new Dictionary<Tag[], TagSet>(new TagArrayComparer());

        internal static ITags Transition (ITags lhs, Tag rhs) {
            ITags result;

            if (rhs == lhs)
                return lhs;

            if (!lhs.TransitionCache.TryGetValue(rhs, out result)) {
                var newTags = new Tag[lhs.Count + 1];

                for (var i = 0; i < newTags.Length - 1; i++) {
                    var tag = lhs[i];
                    if (tag == rhs)
                        return lhs;

                    newTags[i] = tag;
                }

                newTags[newTags.Length - 1] = rhs;

                Array.Sort(newTags, Tag.Comparer.Instance);

                result = New(newTags);

                lhs.TransitionCache.Add(rhs, result);
            }

            return result;
        }

        internal static TagSet New (Tag[] tags) {
            TagSet result;

            if (!SetCache.TryGetValue(tags, out result))
                SetCache.Add(tags, result = new TagSet(tags));

            return result;
        }

        public static ITags New (ITags lhs, ITags rhs) {
            if (lhs == rhs)
                return lhs;

            var lhsCount = lhs.Count;
            var rhsCount = rhs.Count;

            if (lhsCount < 1)
                throw new ArgumentOutOfRangeException("lhs.Count");

            ITags result = lhs[0];

            for (var i = 1; i < lhsCount; i++)
                result = Transition(result, lhs[i]);

            for (var i = 0; i < rhsCount; i++)
                result = Transition(result, rhs[i]);

            return result;
        }

        public static ITags operator + (TagSet lhs, ITags rhs) {
            return New(lhs, rhs);
        }

        public static ITags operator + (ITags lhs, TagSet rhs) {
            return New(lhs, rhs);
        }
    }

    public class Group {
        public readonly string Name;
    }

    public class Sorter {
        public void AddRules (RuleSet ruleSet) {
            throw new NotImplementedException();
        }
    }
}

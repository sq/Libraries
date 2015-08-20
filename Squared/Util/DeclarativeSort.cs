using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Squared.Util.DeclarativeSort {
    public struct Tags {
        internal static int NextId = 0;
        internal static readonly Dictionary<int, Tags> Registry = new Dictionary<int, Tags>();

        public static readonly Tags Null = default(Tags);

        private readonly Tag Tag;
        private readonly TagSet TagSet;

        public Tags (Tag tag) {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            Tag = tag;
            TagSet = null;
        }

        public Tags (TagSet tagSet) {
            if (tagSet == null)
                throw new ArgumentNullException(nameof(tagSet));

            Tag = null;
            TagSet = tagSet;
        }

        public static Tags[] GetAllTags () {
            lock (Registry)
                return Registry.Values.ToArray();
        }

        public bool Contains (Tags tags) {
            if (TagSet != null)
                return TagSet.Contains(tags);
            else if (Tag != null)
                return (tags.Count == 1) && (Tag == tags[0]);
            else
                return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Contains (Tag tag) {
            return Contains((Tags)tag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Contains (TagSet tags) {
            return Contains((Tags)tags);
        }

        public bool IsNull {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                return (Tag == null) && (TagSet == null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (Tags tags) {
            return (Tag == tags.Tag) && (TagSet == tags.TagSet);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (Tag tag) {
            return (Tag == tag) && (TagSet == null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (TagSet tagSet) {
            return (Tag == null) && (TagSet == tagSet);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public override bool Equals (object obj) {
            if (obj == null)
                return false;

            if (obj is Tags)
                return Equals((Tags)obj);

            var tag = obj as Tag;
            if (tag != null)
                return Equals(tag);

            var tagset = obj as TagSet;
            if (tagset != null)
                return Equals(tagset);

            return false;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.Tags.Length;
                else if (Tag != null)
                    return 1;
                else
                    return 0;
            }
        }

        public int Id {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.Id;
                else if (Tag != null)
                    return Tag.Id;
                else
                    return 0;
            }
        }

        public Tag this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.Tags[index];
                else if (Tag != null)
                    return Tag;
                else
                    throw new IndexOutOfRangeException();
            }
        }

        internal Dictionary<Tag, Tags> TransitionCache {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (TagSet != null)
                    return TagSet.TransitionCache;
                else if (Tag != null)
                    return Tag.TransitionCache;
                else
                    // FIXME
                    throw new InvalidOperationException();
            }
        }

        public override int GetHashCode () {
            if (Tag != null)
                return Tag.GetHashCode();
            else if (TagSet != null)
                return TagSet.GetHashCode();
            else
                return 0;
        }

        public override string ToString () {
            if (Tag != null)
                return Tag.ToString();
            else if (TagSet != null)
                return TagSet.ToString();
            else
                return "<Null Tags>";
        }

        public static bool operator == (Tags lhs, Tags rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Tags lhs, Tags rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (Tags lhs, Tag rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Tags lhs, Tag rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (Tags lhs, TagSet rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Tags lhs, TagSet rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (Tag lhs, Tags rhs) {
            return rhs.Equals(lhs);
        }

        public static bool operator != (Tag lhs, Tags rhs) {
            return !rhs.Equals(lhs);
        }

        public static bool operator == (TagSet lhs, Tags rhs) {
            return rhs.Equals(lhs);
        }

        public static bool operator != (TagSet lhs, Tags rhs) {
            return !rhs.Equals(lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tag lhs, Tags rhs) {
            return TagSet.Transition(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tags lhs, Tag rhs) {
            return TagSet.Transition(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tags lhs, Tags rhs) {
            return TagSet.New(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tags lhs, Tags rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tags lhs, Tags rhs) {
            return new TagOrdering(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tags lhs, Tag rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tags lhs, Tag rhs) {
            return new TagOrdering(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tag lhs, Tags rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tag lhs, Tags rhs) {
            return new TagOrdering(rhs, lhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static implicit operator Tags (Tag tag) {
            return new Tags(tag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static implicit operator Tags (TagSet tagSet) {
            return new Tags(tagSet);
        }

        public object Object {
            get {
                return (object)Tag ?? (object)TagSet;
            }
        }
    }

    public class Tag {
        public class EqualityComparer : IEqualityComparer<Tag> {
            public static readonly EqualityComparer Instance = new EqualityComparer();

            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            public bool Equals (Tag x, Tag y) {
                return ReferenceEquals(x, y);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            public int GetHashCode (Tag tag) {
                return tag.Id;
            }
        }

        // Only for sorting within tag arrays to make equality comparisons of tag arrays valid
        internal class Comparer : IComparer<Tag> {
            public static readonly Comparer Instance = new Comparer();

            public int Compare (Tag x, Tag y) {
                return y.Id - x.Id;
            }
        }

        private static readonly Dictionary<string, Tag> TagCache = new Dictionary<string, Tag>();

        internal readonly Dictionary<Tag, Tags> TransitionCache = new Dictionary<Tag, Tags>(EqualityComparer.Instance);

        public readonly string Name;
        public readonly int    Id;

        internal Tag (string name) {
            Name = name;
            Id = Interlocked.Increment(ref Tags.NextId);

            lock (Tags.Registry)
                Tags.Registry[Id] = new Tags(this);
        }

        public override int GetHashCode () {
            return Id;
        }

        public override bool Equals (object obj) {
            if (obj is Tags)
                return ReferenceEquals(this, ((Tags)obj).Object);
            else
                return ReferenceEquals(this, obj);
        }

        public override string ToString () {
            return Name;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tag lhs, Tag rhs) {
            return new Tags(lhs) + rhs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator < (Tag lhs, Tag rhs) {
            return new TagOrdering(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static TagOrdering operator > (Tag lhs, Tag rhs) {
            return new TagOrdering(rhs, lhs);
        }

        /// <returns>Whether lhs contains rhs.</returns>
        public static bool operator & (Tags lhs, Tag rhs) {
            return lhs.Contains(rhs);
        }

        /// <returns>Whether lhs does not contain rhs.</returns>
        public static bool operator ^ (Tags lhs, Tag rhs) {
            return !lhs.Contains(rhs);
        }

        public static Tag New (string name) {
            Tag result;

            lock (TagCache)
            if (!TagCache.TryGetValue(name, out result))
                TagCache.Add(name, result = new Tag(string.Intern(name)));

            return result;
        }

        /// <summary>
        /// Finds all static Tag fields of type and ensures they are initialized.
        /// If instance is provided, also initializes all non-static Tag fields of that instance.
        /// </summary>
        public static void AutoCreate (Type type, object instance = null) {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            if (instance != null)
                flags |= BindingFlags.Instance;

            var tTag = typeof(Tag);

            lock (type)
            foreach (var f in type.GetFields(flags)) {
                if (f.FieldType != tTag)
                    continue;

                object lookupInstance = null;
                if (!f.IsStatic)
                    lookupInstance = instance;

                var tag = f.GetValue(lookupInstance);
                if (tag == null)
                    f.SetValue(lookupInstance, New(f.Name));
            }
        }

        /// <summary>
        /// Finds all static Tag fields of type and ensures they are initialized.
        /// If instance is provided, also initializes all non-static Tag fields of that instance.
        /// </summary>
        public static void AutoCreate<T> (T instance = default(T)) {
            AutoCreate(typeof(T), instance);
        }
    }

    public partial class TagSet : IEnumerable<Tag> {
        private string _CachedToString;
        private readonly HashSet<Tag> HashSet = new HashSet<Tag>();
        internal readonly Tag[] Tags;
        internal Dictionary<Tag, Tags> TransitionCache { get; private set; }
        public readonly int Id;

        private TagSet (Tag[] tags) {
            if (tags == null)
                throw new ArgumentNullException(nameof(tags));
            if (tags.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(tags), "Must not be empty");

            Tags = (Tag[]) tags.Clone();
            foreach (var tag in tags)
                HashSet.Add(tag);

            TransitionCache = new Dictionary<Tag, Tags>(Tag.EqualityComparer.Instance);

            Id = Interlocked.Increment(ref DeclarativeSort.Tags.NextId);

            lock (DeclarativeSort.Tags.Registry)
                DeclarativeSort.Tags.Registry[Id] = new Tags(this);
        }

        /// <returns>Whether this tagset contains all the tags in rhs.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Contains (Tags rhs) {
            if (rhs == this)
                return true;

            for (int l = rhs.Count, i = 0; i < l; i++) {
                if (!HashSet.Contains(rhs[i]))
                    return false;
            }

            return true;
        }

        public override int GetHashCode () {
            return Id;
        }

        public override bool Equals (object obj) {
            if (obj is Tags)
                return ReferenceEquals(this, ((Tags)obj).Object);
            else
                return ReferenceEquals(this, obj);
        }

        public override string ToString () {
            if (_CachedToString != null)
                return _CachedToString;

            return _CachedToString = string.Format("<{0}>", string.Join<Tag>(", ", Tags.OrderBy(t => t.ToString())));
        }

        IEnumerator<Tag> IEnumerable<Tag>.GetEnumerator () {
            return ((IEnumerable<Tag>)Tags).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Tags.GetEnumerator();
        }
    }

    public partial class TagSet {
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        internal static Tags Transition (Tags lhs, Tag rhs) {
            Tags result;

            if (rhs == lhs)
                return lhs;

            bool existing;
            lock (lhs.TransitionCache)
                existing = lhs.TransitionCache.TryGetValue(rhs, out result);

            if (existing)
                return result;
            else
                return TransitionSlow(lhs, rhs);
        }

        internal static Tags TransitionSlow (Tags lhs, Tag rhs) {
            var newTags = new Tag[lhs.Count + 1];

            for (var i = 0; i < newTags.Length - 1; i++) {
                var tag = lhs[i];
                if (tag == rhs)
                    return lhs;

                newTags[i] = tag;
            }

            newTags[newTags.Length - 1] = rhs;
                
            Array.Sort(newTags, Tag.Comparer.Instance);

            var result = New(newTags);
                
            lock (lhs.TransitionCache) {
                if (!lhs.TransitionCache.ContainsKey(rhs))
                    lhs.TransitionCache.Add(rhs, result);
            }

            return result;
        }

        internal static TagSet New (Tag[] tags) {
            TagSet result;

            lock (SetCache)
            if (!SetCache.TryGetValue(tags, out result))
                SetCache.Add(tags, result = new TagSet(tags));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags New (Tags lhs, Tags rhs) {
            if (lhs == rhs)
                return lhs;

            var lhsCount = lhs.Count;
            var rhsCount = rhs.Count;

            Tags result = lhs[0];

            for (int i = 1, l = lhs.Count; i < l; i++)
                result = Transition(result, lhs[i]);

            for (int i = 0, l = rhs.Count; i < l; i++)
                result = Transition(result, rhs[i]);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (TagSet lhs, Tags rhs) {
            return New(lhs, rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public static Tags operator + (Tags lhs, TagSet rhs) {
            return New(lhs, rhs);
        }
    }

    public struct TagOrdering {
        public  readonly Tags Lower, Higher;
        private readonly int   HashCode;

        public TagOrdering (Tags lower, Tags higher) {
            if (lower.IsNull)
                throw new ArgumentNullException(nameof(lower));
            else if (higher.IsNull)
                throw new ArgumentNullException(nameof(higher));

            Lower = lower;
            Higher = higher;

            HashCode = Lower.GetHashCode() ^ (Higher.GetHashCode() << 2);
        }

        public override int GetHashCode () {
            return HashCode;
        }

        public bool Equals (TagOrdering rhs) {
            return (Lower == rhs.Lower) && (Higher == rhs.Higher);
        }

        public override bool Equals (object rhs) {
            if (rhs is TagOrdering)
                return Equals((TagOrdering)rhs);
            else
                return false;
        }

        public override string ToString () {
            return string.Format("{0} < {1}", Lower, Higher);
        }
    }

    public class Group {
        public readonly string Name;
    }

    public class TagOrderingCollection : IEnumerable<TagOrdering> {
        internal const bool Tracing = true;

        private struct DownwardEdge {
            public class Comparer : IComparer<DownwardEdge> {
                public static readonly Comparer Instance = new Comparer();

                public int Compare (DownwardEdge lhs, DownwardEdge rhs) {
                    return lhs.From.Count.CompareTo(rhs.From.Count);
                }
            }

            public readonly Tags From, To;

            public DownwardEdge (Tags from, Tags to) {
                From = from;
                To = to;
            }
        }

        private class DownwardEdges : List<DownwardEdge> {
            public readonly Tags From;

            public DownwardEdges (Tags from) {
                From = from;
            }

            public void Add (Tags to) {
                Add(new DownwardEdge(From, to));
            }

            public override string ToString () {
                return string.Format(
                    "{0} -> ({1})",  
                    From, string.Join(", ", this.Select(de => de.To))
                );
            }
        }

        private class EdgeGraph : KeyedCollection<Tags, DirectedEdgeList> {
            public EdgeGraph ()
                : base () {
            }

            public EdgeGraph (IEqualityComparer<Tags> comparer)
                : base (comparer) {
            }

            public DirectedEdgeList GetOrCreate (Tags key) {
                DirectedEdgeList result;

                if (!base.Contains(key)) {
                    base.Add(result = new DirectedEdgeList(key));
                } else {
                    result = this[key];
                }

                return result;
            }

            public void Connect (Tags from, Tags to) {
                if (from == to)
                    return;

                var fromList = GetOrCreate(from);
                var toList = GetOrCreate(to);

                fromList.Connect(to, 1);
                toList.Connect(from, -1);
            }

            public Dictionary<Tags, DownwardEdges> Finalize () {
                var result = new Dictionary<Tags, DownwardEdges>(Count);

                foreach (var tag in Tags.Registry.Values)
                    result.Add(tag, new DownwardEdges(tag));

                foreach (var item in this) {
                    var from = item.Source;

                    // HACK: Only select downward edges that are not outweighed by upward edges.
                    // Essentially, in cases where nodes have a mutual relationship, we
                    //  want to make sure the upward dependencies aren't in excess of the
                    //  downward dependencies.
                    // This hack helps deal with cycles where it's 'obvious' which nodes
                    //  are more important than others.
                    foreach (var edge in item)
                        if (edge.Directionality >= 1)
                            result[from].Add(edge.Target);
                }

                foreach (var tag in Tags.Registry.Values) {
                    var items = result[tag];

                    if (items.Count == 0) {
                        // A node with no outward edges still needs to be included.
                        // Otherwise, it may not get assigned an ordering value even
                        //  though it participates in the sort order.
                    } else {
                        // HACK: Make sure least-complicated edges are visited first.
                        // The ordering of these is most important.
                        items.Sort(DownwardEdge.Comparer.Instance);
                    }
                }

                if (Tracing) {
                    foreach (var kvp in result)
                        if (kvp.Value.Count > 0)
                            Console.WriteLine(kvp.Value);
                }

                return result;
            }

            protected override Tags GetKeyForItem (DirectedEdgeList item) {
                return item.Source;
            }
        }

        private class DirectedEdgeList : KeyedCollection<Tags, DirectedEdgeList.DirectedEdge> {
            public class DirectedEdge {
                public readonly Tags Target;
                public int           Directionality;

                public DirectedEdge (Tags target) {
                    Target = target;
                    Directionality = 0;
                }
            }

            public readonly Tags Source;

            public DirectedEdgeList (Tags source) 
                : base () {
                Source = source;
            }

            public DirectedEdgeList (Tags source, IEqualityComparer<Tags> comparer)
                : base (comparer) {
                Source = source;
            }

            public void Connect (Tags target, int direction) {
                if (target == Source)
                    return;

                var edge = GetValueOrDefault(target);
                edge.Directionality += direction;
            }

            public DirectedEdge GetValueOrDefault (Tags key) {
                DirectedEdge result;

                if (base.Contains(key)) {
                    result = this[key];
                } else {
                    this.Add(result = new DirectedEdge(key));
                }

                return result;
            }

            protected override Tags GetKeyForItem (DirectedEdge item) {
                return item.Target;
            }
        }

        private readonly List<TagOrdering> Orderings = new List<TagOrdering>();

        private object SortKeyLock    = new object();
        private int    CachedTagCount = 0;
        private int[]  SortKeys       = null;

        public int[] GetSortKeys () {
            lock (SortKeyLock) {
                int tagCount;
                lock (Tags.Registry)
                    tagCount = Tags.Registry.Count;

                if (CachedTagCount != tagCount)
                    SortKeys = null;
                else if (SortKeys != null)
                    return SortKeys;

                CachedTagCount = tagCount;

                SortKeys = GenerateSortKeys(tagCount);

                return SortKeys;
            }
        }

        private Dictionary<Tags, DownwardEdges> GenerateEdges (List<TagOrdering> orderings) {
            var result = new EdgeGraph();

            lock (Tags.Registry)
            foreach (var ordering in orderings) {
                result.Connect(ordering.Higher, ordering.Lower);

                foreach (var kvp in Tags.Registry) {
                    if (kvp.Value.Contains(ordering.Lower))
                        result.Connect(ordering.Higher, kvp.Value);
                    else if (kvp.Value.Contains(ordering.Higher))
                        result.Connect(kvp.Value, ordering.Lower);
                }
            }

            return result.Finalize();
        }

        private int[] GenerateSortKeys (int count) {
            // Tags.Null has an Id of 0, the first Tag/TagSet has an Id of 1
            var result = new int[count + 1];

            var edges = GenerateEdges(Orderings);

            // HACK: Visit the edges starting with 'most important' (singular) tagsets first.
            // The ordering of these is most important to us.
            var orderedEdges = edges.OrderBy(kvp => kvp.Key.Count).ToList();

            int nextIndex = 1;

            var state = new Dictionary<int, bool>();
            foreach (var kvp in orderedEdges)
                ToposortVisit(edges, kvp.Key, true, result, state, ref nextIndex);

            Console.WriteLine(string.Join(", ", result));

            return result;
        }

        private static int Depth = 0;

        private static Tags? ToposortVisit (Dictionary<Tags, DownwardEdges> allEdges, Tags tag, bool isTopLevel, int[] result, Dictionary<int, bool> state, ref int nextIndex) {
            var id = tag.Id;

            bool visiting;
            if (state.TryGetValue(id, out visiting)) {
                if (visiting) {
                    if (Tracing)
                        Console.WriteLine("{0}Cycle @{1}", new string('.', Depth + 1), tag);

                    // HACK: Break this cycle.
                    return tag;
                } else
                    return null;
            }

            if (Tracing)
                Console.WriteLine("{0}{1}", new string('.', Depth), tag);

            Depth++;
            state[id] = true;

            bool aborted = false;
            DownwardEdges downwardEdges;

            Tags? cycle = null;

            if (allEdges.TryGetValue(tag, out downwardEdges))
            foreach (var edge in downwardEdges) {
                cycle = ToposortVisit(allEdges, edge.To, false, result, state, ref nextIndex);
                
                // We hit a cycle, so we bail out.
                if (cycle.HasValue) {
                    if (state[id] == true)
                        state.Remove(id);

                    Depth--;
                    // We don't propagate the cycle, though.
                    return null;
                }
            }

            Assign(tag, result, state, ref nextIndex);
            Depth--;

            return cycle;
        }

        private static bool Assign (Tags tag, int[] result, Dictionary<int, bool> state, ref int nextIndex) {
            var id = tag.Id;

            if (state[id] == true) {
                if (result[id] != 0)
                    throw new Exception("Topological sort visited tag twice: " + tag);

                // Assign this tag the next sort index.
                if (Tracing)
                    Console.WriteLine("{0}#{1}: {2}", new string('.', Depth), nextIndex, tag);

                result[id] = nextIndex++;
                state[id] = false;

                return true;
            }

            return false;
        }

        public void Add (TagOrdering ordering) {
            lock (SortKeyLock) {
                Orderings.Add(ordering);
                SortKeys = null;
            }
        }

        public void Clear () {
            lock (SortKeyLock) {
                Orderings.Clear();
                SortKeys = null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public int Compare (Tags lhs, Tags rhs) {
            if (lhs == rhs)
                return 0;

            var keys = GetSortKeys();
            return keys[lhs.Id].CompareTo(keys[rhs.Id]);
        }

        public List<TagOrdering>.Enumerator GetEnumerator () {
            return Orderings.GetEnumerator();
        }

        IEnumerator<TagOrdering> IEnumerable<TagOrdering>.GetEnumerator () {
            return ((IEnumerable<TagOrdering>)Orderings).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<TagOrdering>)Orderings).GetEnumerator();
        }
    }

    public class Sorter<TValue> : IEnumerable<Sorter<TValue>.SortRule> {
        public abstract class SortRule {
            internal virtual void Prepare () {
            }

            internal abstract int Compare (ref TValue lhs, ref TValue rhs);
        }

        public class PropertySortRule<TProperty> : SortRule {
            public readonly Func<TValue, TProperty> GetProperty;
            public readonly IComparer<TProperty>    Comparer;
            
            public PropertySortRule (Func<TValue, TProperty> getProperty, IComparer<TProperty> comparer) {
                if (getProperty == null)
                    throw new ArgumentNullException("getProperty");
                if (comparer == null)
                    throw new ArgumentNullException("comparer");

                GetProperty = getProperty;
                Comparer = comparer;
            }

            internal override int Compare (ref TValue lhs, ref TValue rhs) {
                var lhsValue = GetProperty(lhs);
                var rhsValue = GetProperty(rhs);

                return Comparer.Compare(lhsValue, rhsValue);
            }
        }

        public class TagSortRule : SortRule {
            public readonly Func<TValue, Tags>    GetTags;
            public readonly TagOrderingCollection Orderings = new TagOrderingCollection();

            private int[] SortKeys;

            public TagSortRule (Func<TValue, Tags> getTags) {
                if (getTags == null)
                    throw new ArgumentNullException("getTags");

                GetTags = getTags;
            }

            internal override void Prepare () {
                SortKeys = Orderings.GetSortKeys();
            }

            internal override int Compare (ref TValue lhs, ref TValue rhs) {
                var lhsTags = GetTags(lhs);
                var rhsTags = GetTags(rhs);

                var lhsKey  = SortKeys[lhsTags.Id];
                var rhsKey  = SortKeys[rhsTags.Id];

                return lhsKey.CompareTo(rhsKey);
            }
        }

        private class ValueComparer : IComparer<TValue> {
            public readonly Sorter<TValue> Sorter;
            public readonly bool           Ascending;

            public ValueComparer (Sorter<TValue> sorter, bool ascending) {
                Sorter = sorter;
                Ascending = ascending;

                foreach (var rule in Sorter.Rules)
                    rule.Prepare();
            }

            public int Compare (TValue lhs, TValue rhs) {
                foreach (var rule in Sorter.Rules) {
                    var result = rule.Compare(ref lhs, ref rhs);
                    if (result == 0)
                        continue;

                    return (Ascending)
                        ? result
                        : -result;
                }

                return 0;
            }
        }

        private readonly List<SortRule> Rules = new List<SortRule>();

        public Sorter () {
        }

        public PropertySortRule<TProperty> Add<TProperty> (Func<TValue, TProperty> getProperty, IComparer<TProperty> comparer = null) {
            if (comparer == null)
                comparer = Comparer<TProperty>.Default;

            var result = new PropertySortRule<TProperty>(getProperty, comparer);
            Rules.Add(result);
            return result;
        }

        public TagSortRule Add (Func<TValue, Tags> getTags, params TagOrdering[] orderings) {
            var result = new TagSortRule(getTags);

            foreach (var o in orderings)
                result.Orderings.Add(o);

            Rules.Add(result);
            return result;
        }

        public void Sort (TValue[] values, bool ascending = true) {
            Sort(new ArraySegment<TValue>(values), ascending: ascending);
        }

        public void Sort (ArraySegment<TValue> values, bool ascending = true) {
            Array.Sort(
                values.Array, values.Offset, values.Count,
                // Heap allocation :-(
                new ValueComparer(this, ascending)
            );
        }

        public void Sort<TTag> (TValue[] values, TTag[] tags, bool ascending = true) {
            Sort(
                new ArraySegment<TValue>(values), tags, 
                ascending: ascending
            );
        }

        public void Sort<TTag> (ArraySegment<TValue> values, TTag[] tags, bool ascending = true) {
            Array.Sort(
                values.Array, tags, values.Offset, values.Count,
                // Heap allocation :-(
                new ValueComparer(this, ascending)
            );
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Rules.GetEnumerator();
        }

        public IEnumerator<SortRule> GetEnumerator () {
            return Rules.GetEnumerator();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Squared.Util.DeclarativeSort {
    public struct Tags {
        internal static int NextId = 0;
        internal static readonly Dictionary<int, Tag> Registry = new Dictionary<int, Tag>();
        internal static readonly Dictionary<Tag, Tags> NullTransitionCache = new Dictionary<Tag, Tags>(Tag.EqualityComparer.Instance);

        public static readonly Tags Null = default(Tags);

        private int _Id;
        public int Id {
            get {
                return (_Id < 0) ? -_Id : _Id;
            }
        }

        public Tags (Tag tag) {
            if (tag == null)
                throw new ArgumentNullException(nameof(tag));

            _Id = tag.Id;
        }

        public Tags (TagSet tagSet) {
            if (tagSet == null)
                throw new ArgumentNullException(nameof(tagSet));

            _Id = -tagSet.Id;
        }

        public bool IsTagSet => (_Id < 0);

        public static Tags[] GetAllTags () {
            lock (Registry) {
                // FIXME: Do we need to include tagsets here? Probably not
                var result = new Tags[Registry.Count];
                int i = 0;
                foreach (var v in Registry.Values)
                    result[i++] = v;
                return result;
            }
        }

        public bool Contains (Tags tags) {
            if (IsTagSet)
                return TagSet.Registry[Id].Contains(tags);
            else if (_Id != 0)
                return (tags.Count == 1) && (_Id == tags._Id);
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
                return _Id == 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (Tags tags) {
            return _Id == tags._Id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (Tag tag) {
            return _Id == tag.Id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public bool Equals (TagSet tagSet) {
            return Id == tagSet.Id;
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
                if (IsTagSet)
                    return TagSet.Registry[Id].Tags.Length;
                else if (Id > 0)
                    return 1;
                else
                    return 0;
            }
        }

        public Tag this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (IsTagSet)
                    return TagSet.Registry[Id].Tags[index];
                else if ((Id > 0) && (index == 0))
                    return Registry[Id];
                else
                    return default(Tag);
            }
        }

        internal Dictionary<Tag, Tags> TransitionCache {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] 
            get {
                if (IsTagSet)
                    return TagSet.Registry[Id].TransitionCache;
                else if (Id > 0)
                    return Registry[Id].TransitionCache;
                else
                    return NullTransitionCache;
            }
        }

        public override int GetHashCode () {
            return Id;
        }

        public override string ToString () {
            if (IsTagSet)
                return TagSet.Registry[Id].ToString();
            else if (Id > 0)
                return Registry[Id].ToString();
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
                if (IsTagSet)
                    return TagSet.Registry[Id];
                else if (Id > 0)
                    return Registry[Id];
                else
                    return null;
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
                Tags.Registry[Id] = this;
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
        public static void AutoCreate (Type type, object instance) {
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
        internal static readonly Dictionary<int, TagSet> Registry = new Dictionary<int, TagSet>();

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

            lock (Registry)
                Registry[Id] = this;
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
        internal const bool Tracing = false;

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
                foreach (var tagSet in TagSet.Registry.Values)
                    result.Add(tagSet, new DownwardEdges(tagSet));

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

                foreach (var tagSet in TagSet.Registry.Values) {
                    var items = result[tagSet];
                    // HACK: Make sure least-complicated edges are visited first.
                    // The ordering of these is most important.
                    items.Sort(DownwardEdge.Comparer.Instance);
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
                int tagCount = Tags.NextId;

                if (CachedTagCount != tagCount)
                    SortKeys = null;
                else if (SortKeys != null)
                    return SortKeys;

                CachedTagCount = tagCount;

                SortKeys = GenerateSortKeys(tagCount);

                return SortKeys;
            }
        }

        private void GenerateEdge (EdgeGraph result, Tag t, TagOrdering ordering) {
            var containsLower = (ordering.Lower.Count == 1) && (ordering.Lower.Id == t.Id);
            var containsHigher = (ordering.Higher.Count == 1) && (ordering.Higher.Id == t.Id);

            if (containsLower && containsHigher)
                return;
            else if (containsLower)
                result.Connect(ordering.Higher, t);
            else if (containsHigher)
                result.Connect(t, ordering.Lower);
        }

        private void GenerateEdge (EdgeGraph result, TagSet ts, TagOrdering ordering) {
            var containsLower = ts.Contains(ordering.Lower);
            var containsHigher = ts.Contains(ordering.Higher);

            if (containsLower && containsHigher)
                return;
            else if (containsLower)
                result.Connect(ordering.Higher, ts);
            else if (containsHigher)
                result.Connect(ts, ordering.Lower);
        }

        private Dictionary<Tags, DownwardEdges> GenerateEdges (List<TagOrdering> orderings) {
            var result = new EdgeGraph();

            lock (Tags.Registry)
            foreach (var ordering in orderings) {
                result.Connect(ordering.Higher, ordering.Lower);

                foreach (var kvp in Tags.Registry)
                    GenerateEdge(result, kvp.Value, ordering);

                foreach (var kvp in TagSet.Registry)
                    GenerateEdge(result, kvp.Value, ordering);
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

            if (Tracing)
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
        public delegate TProperty PropertyGetter<TProperty> (TValue value);
        public delegate Tags      TagGetter                 (TValue value);
        public delegate int       ValueComparer             (ref TValue lhs, ref TValue rhs);
        
        public class DelegateOrExpression<T> {
            public T Delegate {
                get; private set;
            }
            public readonly Expression<T> Expr;

            public DelegateOrExpression (T del) {
                Delegate = del;
            }

            public DelegateOrExpression (Expression<T> expression) {
                Expr = expression;
            }

            public Expression MakeInvocation (params Expression[] arguments) {
                if (Expr != null) {
                    var rebinder = new ArgumentRebinder<T>(Expr, arguments);
                    return rebinder.RebindBody();
                }

                var d = Delegate as System.Delegate;

                if (d != null)
                    return Expression.Call(
                        Expression.Constant(d.Target), d.Method, arguments
                    );
                else
                    throw new InvalidOperationException("No delegate or expression provided");
            }

            public T Compile () {
                if (Delegate != null)
                    return Delegate;
                else
                    return Delegate = Expr.Compile();
            }

            public static implicit operator DelegateOrExpression<T> (T del) {
                return new DelegateOrExpression<T>(del);
            }

            public static implicit operator DelegateOrExpression<T> (Expression<T> expr) {
                return new DelegateOrExpression<T>(expr);
            }
        }

        public abstract class SortRule {
            internal virtual void Prepare () {
            }

            internal virtual Expression MakeCompareExpression (Expression lhs, Expression rhs) {
                var tRule = GetType();
                var mCompare = tRule.GetMethod("Compare", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var eRule = Expression.Constant(this, tRule);
                var invocation = Expression.Call(eRule, mCompare, lhs, rhs);
                return invocation;
            }

            public abstract int Compare (ref TValue lhs, ref TValue rhs);
        }

        public class PropertySortRule<TProperty> : SortRule {
            public  readonly DelegateOrExpression<PropertyGetter<TProperty>> GetProperty;
            public  readonly IComparer<TProperty> Comparer;
            private readonly IComparer<TProperty> RuntimeComparer;
            private PropertyGetter<TProperty>     _GetProperty;
            
            public PropertySortRule (
                DelegateOrExpression<PropertyGetter<TProperty>> getProperty, 
                IComparer<TProperty> comparer
            ) {
                if (getProperty == null)
                    throw new ArgumentNullException("getProperty");
                GetProperty = getProperty;
                Comparer = comparer;
                RuntimeComparer = Comparer ?? Comparer<TProperty>.Default;
                _GetProperty = getProperty.Compile();
            }

            internal override Expression MakeCompareExpression (Expression lhs, Expression rhs) {
                var tProperty = typeof(TProperty);
                var lhsProperty = GetProperty.MakeInvocation(lhs);
                var rhsProperty = GetProperty.MakeInvocation(rhs);
                
                if (Comparer == null) {
                    var compareTo = tProperty.GetMethod(
                        "CompareTo", new [] { tProperty }
                    );
                    if (compareTo != null) {
                        return Expression.Call(
                            lhsProperty, compareTo, rhsProperty
                        );
                    }
                }

                var compare = RuntimeComparer.GetType().GetMethod("Compare", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                return Expression.Call(
                    Expression.Constant(RuntimeComparer),
                    compare, lhsProperty, rhsProperty
                );
            }

            public override int Compare (ref TValue lhs, ref TValue rhs) {
                var lhsProperty = _GetProperty(lhs);
                var rhsProperty = _GetProperty(rhs);

                return RuntimeComparer.Compare(lhsProperty, rhsProperty);
            }
        }

        public class TagSortRule : SortRule {
            public readonly DelegateOrExpression<TagGetter> GetTags;
            public readonly TagOrderingCollection           Orderings = new TagOrderingCollection();

            private int[] SortKeys;
            private TagGetter _GetTags;

            public TagSortRule (
                DelegateOrExpression<TagGetter> getTags,
                params TagOrdering[] orderings
            ) {
                if (getTags == null)
                    throw new ArgumentNullException("getTags");

                GetTags = getTags;
                _GetTags = GetTags.Compile();

                foreach (var o in orderings)
                    Orderings.Add(o);
            }

            internal override void Prepare () {
                base.Prepare();
                SortKeys = Orderings.GetSortKeys();
            }

            internal override Expression MakeCompareExpression (Expression lhs, Expression rhs) {
                var lhsTags = GetTags.MakeInvocation(lhs);
                var rhsTags = GetTags.MakeInvocation(rhs);

                var getSortKeys = Expression.Field(
                    Expression.Constant(this), GetType().GetField("SortKeys", BindingFlags.Instance | BindingFlags.NonPublic)
                );
                var sortKeys = Expression.Variable(typeof(int[]), "sortKeys");

                var pId = typeof(Tags).GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
                var lhsId = Expression.Property(lhsTags, pId);
                var rhsId = Expression.Property(rhsTags, pId);

                var lhsSortKey = Expression.ArrayIndex(sortKeys, lhsId);
                var rhsSortKey = Expression.ArrayIndex(sortKeys, rhsId);

                return Expression.Block(
                    typeof(int),
                    new[] { sortKeys },
                    Expression.Assign(sortKeys, getSortKeys),
                    Expression.Subtract(lhsSortKey, rhsSortKey)
                );
            }

            public override int Compare (ref TValue lhs, ref TValue rhs) {
                var lhsTags = _GetTags(lhs);
                var rhsTags = _GetTags(rhs);

                var lhsKey  = SortKeys[lhsTags.Id];
                var rhsKey  = SortKeys[rhsTags.Id];

                return lhsKey.CompareTo(rhsKey);
            }
        }

        public class SorterComparer : IRefComparer<TValue>, IComparer<TValue> {
            public const bool Tracing = false;

            public  readonly Sorter<TValue> Sorter;
            public  readonly bool           Ascending;
            private readonly ValueComparer  Comparer;

            public SorterComparer (Sorter<TValue> sorter, bool ascending) {
                Sorter = sorter;
                Ascending = ascending;

                foreach (var rule in Sorter.Rules)
                    rule.Prepare();

                if (Sorter.UseLCG) {
                    long started, ended;

                    if (Tracing)
                        started = Time.Ticks;

                    Comparer = MakeLCGComparer();

                    if (Tracing) {
                        ended = Time.Ticks;
                        Console.WriteLine("Compiling comparer took {0:0000.00}ms", TimeSpan.FromTicks(ended - started).TotalMilliseconds);
                    }
                } else {
                    Comparer = DelegateComparer;
                }
            }

            private int DelegateComparer (ref TValue lhs, ref TValue rhs) {
                int result = 0;

                foreach (var rule in Sorter.Rules) {
                    result = rule.Compare(ref lhs, ref rhs);
                    if (result != 0)
                        break;
                }

                return result;
            }

            private ValueComparer MakeLCGComparer () {
                var tValue = typeof(TValue);
                var pLhs = Expression.Parameter(tValue.MakeByRefType(), "lhs");
                var pRhs = Expression.Parameter(tValue.MakeByRefType(), "rhs");
                var vResult = Expression.Variable(typeof(int), "result");
                var lDefaultReturn = Expression.Label("return-result");
                var lReturnTarget = Expression.Label(typeof(int), " ");
                var isNotZero = Expression.NotEqual(vResult, Expression.Constant(0, typeof(int)));
                var gotoRet = Expression.Goto(lDefaultReturn);
                var branch = Expression.IfThen(isNotZero, gotoRet);

                var body = new List<Expression>();

                foreach (var rule in Sorter.Rules) {
                    var compareResult = rule.MakeCompareExpression(pLhs, pRhs);
                    var assignment = Expression.Assign(vResult, compareResult);

                    body.Add(assignment);
                    body.Add(branch);
                }

                var eResult = Ascending
                    ? (Expression)vResult
                    : Expression.Negate(vResult);
                body.Add(Expression.Label(lDefaultReturn));
                body.Add(Expression.Label(lReturnTarget, eResult));

                var bodyBlock = Expression.Block(new[] { vResult }, body);

                var expr = Expression.Lambda<ValueComparer>(bodyBlock, pLhs, pRhs);
                return expr.Compile();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare (ref TValue lhs, ref TValue rhs) {
                return Comparer(ref lhs, ref rhs);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare (TValue lhs, TValue rhs) {
                return Comparer(ref lhs, ref rhs);
            }
        }

        private readonly bool UseLCG;
        private readonly List<SortRule> Rules = new List<SortRule>();
        private readonly object ComparerLock = new object();
        private SorterComparer AscendingSorter, DescendingSorter;

        public Sorter (bool useLCG = true) {
            UseLCG = useLCG;
        }

        private void Invalidate () {
            lock (ComparerLock) {
                AscendingSorter = null;
                DescendingSorter = null;
            }
        }

        /// <summary>
        /// Call this before sorting if it's possible there are any new tags.
        /// </summary>
        public void PrepareSort () {
            foreach (var rule in Rules)
                rule.Prepare();
        }

        public SorterComparer GetComparer (bool ascending) {
            lock (ComparerLock) {
                PrepareSort();

                if (ascending) {
                    if (AscendingSorter == null)
                        AscendingSorter = new SorterComparer(this, ascending);

                    return AscendingSorter;
                } else {
                    if (DescendingSorter == null)
                        DescendingSorter = new SorterComparer(this, ascending);

                    return DescendingSorter;
                }
            }
        }

        public PropertySortRule<TProperty> Add<TProperty> (Expression<PropertyGetter<TProperty>> getProperty, IComparer<TProperty> comparer = null) {
            var result = new PropertySortRule<TProperty>(getProperty, comparer);
            Rules.Add(result);
            return result;
        }

        public TagSortRule Add (Expression<TagGetter> getTags, params TagOrdering[] orderings) {
            var result = new TagSortRule(getTags, orderings);
            Rules.Add(result);
            return result;
        }

        public void Sort (TValue[] values, bool ascending = true) {
            Sort(new ArraySegment<TValue>(values), ascending: ascending);
        }

        public void Sort (ArraySegment<TValue> values, bool ascending = true) {
            Array.Sort(
                values.Array, values.Offset, values.Count, GetComparer(ascending)
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
                values.Array, tags, values.Offset, values.Count, GetComparer(ascending)
            );
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return Rules.GetEnumerator();
        }

        public IEnumerator<SortRule> GetEnumerator () {
            return Rules.GetEnumerator();
        }
    }

    internal class ArgumentRebinder<TDelegate> : ExpressionVisitor {
        private readonly Expression<TDelegate> Lambda;
        private readonly Expression[] Parameters;

        public ArgumentRebinder (Expression<TDelegate> lambda, Expression[] parameters) {
            Lambda = lambda;
            Parameters = parameters;
        }

        public Expression RebindBody () {
            return Visit(Lambda.Body);
        }

        public override Expression Visit(Expression node) {
            for (int i = 0, l = Lambda.Parameters.Count; i < l; i++) {
                if (node == Lambda.Parameters[i])
                    return Parameters[i];
            }

            return base.Visit(node);
        }
    }
}

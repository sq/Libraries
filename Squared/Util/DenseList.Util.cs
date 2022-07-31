#define NOSPAN

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ext = Squared.Util.EnumerableExtensions;

namespace Squared.Util {
    public interface IDenseQuerySource<TSource> : IEnumerator {
        void CloneInto (out TSource result);
    }

    public struct ListWhereEnumerator<T> : IEnumerable<T>, IEnumerator<T> {
        private bool InUse;
        private List<T>.Enumerator Enumerator;
        private readonly List<T> List;
        private readonly Predicate<T> Predicate;

        public T Current => Enumerator.Current;
        object IEnumerator.Current => Current;

        public ListWhereEnumerator (List<T> list, Predicate<T> predicate) {
            List = list;
            Predicate = predicate;
            InUse = false;
            Enumerator = default;
        }

        public ListWhereEnumerator<T> GetEnumerator () {
            var result = this;
            result.InUse = true;
            result.Enumerator = List.GetEnumerator();
            return result;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => GetEnumerator();

        public void Dispose () {
            if (InUse) {
                InUse = false;
                Enumerator.Dispose();
                Enumerator = default;
            }
        }

        public bool MoveNext () {
            if (!InUse)
                throw new Exception("Not initialized");

            while (Enumerator.MoveNext()) {
                if (Predicate(Enumerator.Current))
                    return true;
            }

            return false;
        }

        public void Reset () {
            if (InUse)
                Reset();
        }

        public T[] ToArray () {
            var result = new List<T>();
            foreach (var item in this)
                result.Add(item);
            return result.ToArray();
        }
    }

    public struct ListSelectEnumerator<To, From> : IEnumerable<To>, IEnumerator<To> {
        private bool InUse;
        private List<From>.Enumerator Enumerator;
        private readonly List<From> List;
        private readonly Func<From, To> Selector;

        public To Current => Selector(Enumerator.Current);
        object IEnumerator.Current => Current;

        public ListSelectEnumerator (List<From> list, Func<From, To> selector) {
            List = list;
            Selector = selector;
            InUse = false;
            Enumerator = default;
        }

        public ListSelectEnumerator<To, From> GetEnumerator () {
            var result = this;
            result.InUse = true;
            result.Enumerator = List.GetEnumerator();
            return result;
        }

        IEnumerator<To> IEnumerable<To>.GetEnumerator () => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => GetEnumerator();

        public void Dispose () {
            if (InUse) {
                InUse = false;
                Enumerator.Dispose();
                Enumerator = default;
            }
        }

        public bool MoveNext () {
            if (!InUse)
                throw new Exception("Not initialized");

            return Enumerator.MoveNext();
        }

        public void Reset () {
            if (InUse)
                Reset();
        }

        public To[] ToArray () {
            var result = new List<To>();
            foreach (var item in this)
                result.Add(item);
            return result.ToArray();
        }
    }

    public static class EnumerableExtensions {
        // The default versions of these will allocate for lists, so just fix that pattern
        public static T First<T> (this List<T> list) => list[0];
        public static T Last<T> (this List<T> list) => list[list.Count - 1];
        public static T FirstOrDefault<T> (this List<T> list) => list.Count > 0 ? list[0] : default;
        public static T LastOrDefault<T> (this List<T> list) => list.Count > 0 ? list[list.Count - 1] : default;
        public static int Count<T> (this List<T> list) => list.Count;

        public static ListWhereEnumerator<T> Where<T> (this List<T> list, Predicate<T> predicate) =>
            new ListWhereEnumerator<T>(list, predicate);
        public static ListSelectEnumerator<To, From> Select<To, From> (this List<From> list, Func<From, To> selector) =>
            new ListSelectEnumerator<To, From>(list, selector);

        /// <summary>
        /// Returns a dense list containing all the items from a sequence.
        /// </summary>
        public static DenseList<T> ToDenseList<T> (this IEnumerable<T> enumerable) {
            DenseList<T> result = default;
            if (enumerable != null)
                result.AddRange(enumerable);
            return result;
        }

        /// <summary>
        /// Returns a dense list containing all the items from a sequence that satisfy a predicate.
        /// </summary>
        public static DenseList<T> ToDenseList<T> (this IEnumerable<T> enumerable, Func<T, bool> where) {
            DenseList<T> result = default;
            result.AddRange(enumerable, where);
            return result;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void BoundsCheckFailed () {
            throw new ArgumentOutOfRangeException("index");
        }

#if !NOSPAN
        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref readonly T ReadItem<T> (this in DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousReadItem(index);

            if ((index < 0) || (index >= list._Count))
                BoundsCheckFailed();
            return ref Unsafe.Add(ref Unsafe.AsRef(in list.Item1), index);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ref T Item<T> (this ref DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousItem(index);

            if ((index < 0) || (index >= list._Count))
                BoundsCheckFailed();
            return ref Unsafe.Add(ref list.Item1, index);
        }
#else
        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T ReadItem<T> (this in DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousItem(index);
            
            if ((index < 0) || (index >= list._Count))
                BoundsCheckFailed();

            switch (index) {
                default:
                case 0:
                    return ref list.Item1;
                case 1:
                    return ref list.Item2;
                case 2:
                    return ref list.Item3;
                case 3:
                    return ref list.Item4;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Item<T> (this ref DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousItem(index);
            
            if ((index < 0) || (index >= list._Count))
                BoundsCheckFailed();

            switch (index) {
                default:
                case 0:
                    return ref list.Item1;
                case 1:
                    return ref list.Item2;
                case 2:
                    return ref list.Item3;
                case 3:
                    return ref list.Item4;
            }
        }
#endif

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T First<T> (this ref DenseList<T> list) {
            return ref Item(ref list, 0);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Last<T> (this ref DenseList<T> list) {
            return ref Item(ref list, list.Count - 1);
        }

        // TODO: Add a select version of ToDenseList? Harder to do
    }

    public partial struct DenseList<T> : IDisposable, IEnumerable<T>, IList<T>, IOrderedEnumerable<T> {
        public delegate bool Predicate<TUserData> (in T item, in TUserData userData);
        public delegate bool Predicate (in T item);

        private static T[] EmptyArray;

        internal static readonly Func<T, T> NullSelector = _NullSelector;
        private static T _NullSelector (T value) => value;

        internal sealed class BoxedEmptyEnumerator : IEnumerator<T> {
            // HACK: While this will break any ABSURD code comparing enumerators by reference,
            //  this means that foreach over an empty denselist won't have to allocate
            public static readonly BoxedEmptyEnumerator Instance = new BoxedEmptyEnumerator();

            // FIXME
            public T Current => default;
            object IEnumerator.Current => null;

            public void Dispose () {
            }

            public bool MoveNext () {
                return false;
            }

            public void Reset () {
            }
        }

        public struct Enumerator : IEnumerator<T>, IDenseQuerySource<Enumerator> {
            private int Index;

            private readonly T Item1, Item2, Item3, Item4;
            private readonly bool HasList;
            private readonly T[] Items;
            private readonly int Offset, Count;

            internal Enumerator (in DenseList<T> list) {
                Index = -1;
                Count = list.Count;
                Item1 = list.Item1;
                Item2 = list.Item2;
                Item3 = list.Item3;
                Item4 = list.Item4;

                var items = list._Items;
                HasList = items != null;
                if (items != null) {
                    var buffer = items.GetBuffer();
                    Offset = buffer.Offset;
                    Items = buffer.Array;
                } else {
                    Offset = 0;
                    Items = null;
                }
            }

            // TODO: Find a way to expose a ref version of this without upsetting the compiler
            // The 'Unsafe.Add' approach used in .Item and .ReadItem doesn't work for some reason

            public T Current {
                [TargetedPatchingOptOut("")]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    if ((Index < 0) || (Index >= Count))
                        throw new InvalidOperationException("No current value");
                    else if (HasList)
                        return Items[Index];
                    else switch (Index) {
                        case 0:
                            return Item1;
                        case 1:
                            return Item2;
                        case 2:
                            return Item3;
                        case 3:
                            return Item4;
                        default:
                            throw new Exception("Internal Error");
                    }
                }
            }

            object IEnumerator.Current {
                [TargetedPatchingOptOut("")]
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Current;
            }

            public void Dispose () {
                Index = -1;
            }

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                if (Index < Count) {
                    Index++;
                    return Index < Count;
                }
                return false;
            }

            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryGetNext (ref T result) {
                var countMinus1 = Count - 1;
                if (Index++ < countMinus1) {
                    if (HasList) {
                        result = Items[Index];
                        return false;
                    } else switch (Index) {
                        case 0:
                            result = Item1;
                            return true;
                        case 1:
                            result = Item2;
                            return true;
                        case 2:
                            result = Item3;
                            return true;
                        case 3:
                            result = Item4;
                            return true;
                        default:
                            return false;
                    }
                }

                return false;
            }

            public void Reset () {
                Index = -1;
            }

            public void CloneInto (out Enumerator result) {
                result = this;
                result.Reset();
            }

            public bool GetNext (out object tup) {
                throw new NotImplementedException();
            }
        }

        public struct Buffer : IDisposable {
            internal BufferPool<T>.Buffer BufferPoolAllocation;
            internal bool IsTemporary;

            public int Offset;
            public int Count;
            public T[] Data;

            public T this[int index] {
                get {
                    return Data[Offset + index];
                }
                set {
                    Data[Offset + index] = value;
                }
            }            

            public void Dispose () {
                if (IsTemporary)
                    BufferPoolAllocation.Dispose();

                Data = null;
            }
        }

        public bool Any () {
            if (Count == 0)
                return false;
            else
                return true;
        }

        public bool Any (Func<T, bool> predicate) {
            if (Count == 0)
                return false;
            else
                return IndexOf(predicate) >= 0;
        }

        public bool Any<TUserData> (Predicate<TUserData> predicate, in TUserData userData) {
            if (Count == 0)
                return false;
            else
                return IndexOf(predicate, userData) >= 0;
        }

        public bool All (Func<T, bool> predicate) {
            return CountWhere(predicate) == Count;
        }

        public DenseDistinct<T, Enumerator, IEqualityComparer<T>> Distinct () {
            var e = GetEnumerator();
            return new DenseDistinct<T, Enumerator, IEqualityComparer<T>>(
                in e, EqualityComparer<T>.Default, null
            );
        }

        public DenseDistinct<T, Enumerator, TEqualityComparer> Distinct<TEqualityComparer> (TEqualityComparer comparer = default, HashSet<T> hash = null)
            where TEqualityComparer : IEqualityComparer<T>
        {
            var e = GetEnumerator();
            return new DenseDistinct<T, Enumerator, TEqualityComparer>(
                in e, comparer, hash
            );
        }

        // FIXME: Implement this as a lazy enumerable
        public DenseList<U> SelectMany<U> (Func<T, DenseList<U>> selector) {
            if (Count == 0)
                return default;
            else if (Count == 1)
                return selector(this[0]);

            var result = new DenseList<U>();
            for (int i = 0, c = Count; i < c; i++) {
                GetItem(i, out T item);
                var items = selector(item);
                result.AddRange(ref items);
            }
            return result;
        }

        public DenseList<U> SelectMany<U> (Func<T, IEnumerable<U>> selector) {
            if (Count == 0)
                return default;
            else if (Count == 1)
                return selector(this[0]).ToDenseList();

            var result = new DenseList<U>();
            for (int i = 0, c = Count; i < c; i++) {
                GetItem(i, out T item);
                var items = selector(item);
                result.AddRange(items);
            }
            return result;
        }

        public DenseQuery<T, Enumerator, U> Select<U> (Func<T, U> selector, Func<U, bool> where = null) {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            var result = new DenseQuery<T, Enumerator, U>(in e, selector, false);
            if (where != null)
                result.PostPredicates.Add(where);
            return result;
        }

        public DenseQuery<T, Enumerator, T> Where (Func<T, bool> predicate) {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            var result = new DenseQuery<T, Enumerator, T>(in e, NullSelector, true);
            result.PrePredicates.Add(predicate);
            return result;
        }

        private struct OrderByAdapter<TKey, TComparer> : IRefComparer<T>
            where TComparer : IComparer<TKey>
        {
            public Func<T, TKey> KeySelector;
            public TComparer Comparer;
            public int Sign;

            public OrderByAdapter (Func<T, TKey> keySelector, TComparer comparer, int sign) {
                KeySelector = keySelector;
                Comparer = comparer;
                Sign = sign;
            }

            public int Compare (ref T lhs, ref T rhs) {
                var lKey = KeySelector(lhs);
                var rKey = KeySelector(rhs);
                return Comparer.Compare(lKey, rKey) * Sign;
            }
        }

        // You may be wondering: Why do these return a DenseList instead of an enumerable?
        // Well the answer is simple: To sort a sequence you need to buffer the whole thing somehow,
        //  so we might as well buffer it up front and then sort + return it.
        // csc doesn't seem to mind and will happily use these methods anyway. Hooray!
        public DenseList<T> OrderBy<TKey> (Func<T, TKey> keySelector) {
            OrderByImpl(keySelector, Comparer<TKey>.Default, false, out DenseList<T> result);
            return result;
        }

        public DenseList<T> OrderBy<TKey, TComparer> (Func<T, TKey> keySelector, TComparer comparer)
            where TComparer : IComparer<TKey>
        {
            OrderByImpl(keySelector, comparer, false, out DenseList<T> result);
            return result;
        }

        public DenseList<T> OrderByDescending<TKey> (Func<T, TKey> keySelector) {
            OrderByImpl(keySelector, Comparer<TKey>.Default, true, out DenseList<T> result);
            return result;
        }

        public DenseList<T> OrderByDescending<TKey, TComparer> (Func<T, TKey> keySelector, TComparer comparer)
            where TComparer : IComparer<TKey>
        {
            OrderByImpl(keySelector, comparer, true, out DenseList<T> result);
            return result;
        }

        internal void OrderByImpl<TKey, TComparer> (Func<T, TKey> keySelector, TComparer comparer, bool descending, out DenseList<T> result)
            where TComparer : IComparer<TKey>
        {
            Clone(out result);
            result.OrderByImpl(keySelector, comparer, descending);
        }

        internal void OrderByImpl<TKey, TComparer> (Func<T, TKey> keySelector, TComparer comparer, bool descending) 
            where TComparer : IComparer<TKey> 
        {
            Sort(new OrderByAdapter<TKey, TComparer>(keySelector, comparer, descending ? -1 : 1));
        }

        public DenseList<T> Concat (DenseList<T> rhs) {
            Clone(out DenseList<T> result);
            result.AddRange(ref rhs);
            return result;
        }

        public DenseList<T> Concat (ref DenseList<T> rhs) {
            Clone(out DenseList<T> result);
            result.AddRange(ref rhs);
            return result;
        }

        public DenseList<T> Concat (IEnumerable<T> rhs) {
            Clone(out DenseList<T> result);
            result.AddRange(rhs);
            return result;
        }

        public U Reduce<U> (U initialValue, Func<U, T, U> reducer) {
            var result = initialValue;
            for (int i = 0, c = Count; i < c; i++) {
                ref var item = ref Ext.Item(ref this, i);
                result = reducer(result, item);
            }
            return result;
        }

        // It's unfortunate that this returns an interface, which means calls to it will box the result.
        // Not much we can do about it though.
        IOrderedEnumerable<T> IOrderedEnumerable<T>.CreateOrderedEnumerable<TKey> (Func<T, TKey> keySelector, IComparer<TKey> comparer, bool descending) {
            OrderByImpl(keySelector, comparer, descending, out DenseList<T> result);
            return result;
        }
    }

    public struct DenseQuery<T, TEnumerator, TResult> : 
        IEnumerable<TResult>, IEnumerator<TResult>, 
        IDenseQuerySource<DenseQuery<T, TEnumerator, TResult>>
        where TEnumerator : IEnumerator<T>, IDenseQuerySource<TEnumerator>
    {
        internal static Func<T, TResult> CastSelector = _CastSelector;
        // FIXME: Boxing
        private static TResult _CastSelector (T value) => (TResult)(object)value;

        private TResult _Current;
        private TEnumerator _Enumerator;
        private bool _IsNullSelector, _HasMoved;

        public DenseList<Func<T, bool>> PrePredicates;
        public DenseList<Func<TResult, bool>> PostPredicates;
        public Func<T, TResult> Selector;

        public TResult Current => _Current;
        object IEnumerator.Current => _Current;

        internal DenseQuery (
            in TEnumerator enumerator, Func<T, TResult> selector, bool isNullSelector
        ) {
            _IsNullSelector = isNullSelector;
            Selector = selector;
            _Enumerator = enumerator;
            PrePredicates = default;
            PostPredicates = default;
            Selector = selector;
            _Current = default;
            _HasMoved = false;
        }

        public void Dispose () {
            _Enumerator.Dispose();
        }

        public TResult First () {
            if (!_HasMoved) {
                if (MoveNext())
                    return _Current;
                else
                    throw new Exception("Sequence is empty");
            }

            using (var temp = GetEnumerator())
                return temp.First();
        }

        public TResult FirstOrDefault () {
            if (!_HasMoved) {
                if (MoveNext())
                    return _Current;
                else
                    return default;
            }

            using (var temp = GetEnumerator())
                return temp.FirstOrDefault();
        }

        public bool Any () {
            if (!_HasMoved)
                return MoveNext();

            using (var temp = GetEnumerator())
                return temp.MoveNext();
        }

        public bool MoveNext () {
            _HasMoved = true;

            while (true) {
                if (!_Enumerator.MoveNext())
                    return false;

                var input = _Enumerator.Current;

                bool predicateRejected = false;

                foreach (var pre in PrePredicates) {
                    if (!pre(input)) {
                        predicateRejected = true;
                        break;
                    }
                }

                if (predicateRejected)
                    continue;

                _Current = Selector(input);

                foreach (var post in PostPredicates) {
                    if (!post(_Current)) {
                        predicateRejected = true;
                        break;
                    }
                }

                if (predicateRejected)
                    continue;

                return true;
            }
        }

        public void Reset () {
            _Enumerator.Reset();
            _Current = default;
        }

        public void CloneInto (out DenseQuery<T, TEnumerator, TResult> result) {
            result = new DenseQuery<T, TEnumerator, TResult> {
                Selector = Selector
            };
            _Enumerator.CloneInto(out result._Enumerator);
            PrePredicates.Clone(out result.PrePredicates);
            PostPredicates.Clone(out result.PostPredicates);
        }

        public DenseQuery<TResult, DenseQuery<T, TEnumerator, TResult>, V> Select<V> (Func<TResult, V> selector, Func<V, bool> where = null) {
            var source = GetEnumerator();
            var result = new DenseQuery<TResult, DenseQuery<T, TEnumerator, TResult>, V>(
                in source, selector, false
            );
            if (where != null)
                result.PostPredicates.Add(where);
            return result;
        }

        public DenseQuery<T, TEnumerator, TResult> Where (Func<TResult, bool> predicate) {
            CloneInto(out DenseQuery<T, TEnumerator, TResult> result);
            if ((predicate is Func<T, bool> f) && _IsNullSelector)
                result.PrePredicates.Add(f);
            else
                result.PostPredicates.Add(predicate);
            return result;
        }

        public TResult[] ToArray () {
            var result = new DenseList<TResult>();
            Reset();
            while (MoveNext())
                result.Add(ref _Current);
            return result.ToArray();
        }

        public List<TResult> ToList () {
            var result = new List<TResult>();
            Reset();
            while (MoveNext())
                result.Add(_Current);
            return result;
        }

        public DenseList<TResult> ToDenseList () {
            var result = new DenseList<TResult>();
            Reset();
            while (MoveNext())
                result.Add(ref _Current);
            return result;
        }

        public U Reduce<U> (U initialValue, Func<U, TResult, U> reducer) {
            var result = initialValue;
            foreach (var item in this)
                result = reducer(result, item);
            return result;
        }

        public DenseQuery<T, TEnumerator, TResult> GetEnumerator () {
            if (_HasMoved) {
                CloneInto(out DenseQuery<T, TEnumerator, TResult> result);
                return result;
            }

            return this;
        }

        IEnumerator<TResult> IEnumerable<TResult>.GetEnumerator () {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return GetEnumerator();
        }

        public DenseDistinct<TResult, DenseQuery<T, TEnumerator, TResult>, IEqualityComparer<TResult>> Distinct () {
            var e = GetEnumerator();
            return new DenseDistinct<TResult, DenseQuery<T, TEnumerator, TResult>, IEqualityComparer<TResult>>(
                in e, EqualityComparer<TResult>.Default, null
            );
        }

        public DenseDistinct<TResult, DenseQuery<T, TEnumerator, TResult>, TEqualityComparer> Distinct<TEqualityComparer> (
            TEqualityComparer comparer, HashSet<TResult> hash = null
        ) where TEqualityComparer : IEqualityComparer<TResult> {
            var e = GetEnumerator();
            return new DenseDistinct<TResult, DenseQuery<T, TEnumerator, TResult>, TEqualityComparer>(
                in e, comparer, hash
            );
        }

        public DenseList<TResult> OrderBy<TKey> (Func<TResult, TKey> keySelector) {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, Comparer<TKey>.Default, false);
            return temp;
        }

        public DenseList<TResult> OrderBy<TKey, TComparer> (Func<TResult, TKey> keySelector, TComparer comparer)
            where TComparer : IComparer<TKey>
        {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, comparer, false);
            return temp;
        }

        public DenseList<TResult> OrderByDescending<TKey> (Func<TResult, TKey> keySelector) {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, Comparer<TKey>.Default, true);
            return temp;
        }

        public DenseList<TResult> OrderByDescending<TKey, TComparer> (Func<TResult, TKey> keySelector, TComparer comparer)
            where TComparer : IComparer<TKey>
        {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, comparer, true);
            return temp;
        }
    }

    public struct DenseDistinct<T, TEnumerator, TEqualityComparer> :
        IEnumerable<T>, IEnumerator<T>, 
        IDenseQuerySource<DenseDistinct<T, TEnumerator, TEqualityComparer>>
        where TEnumerator : IEnumerator<T>, IDenseQuerySource<TEnumerator>
        where TEqualityComparer : IEqualityComparer<T>
    {
        private T _Current;
        private TEnumerator _Enumerator;
        private bool _HasMoved;

        private TEqualityComparer _Comparer;
        private DenseList<T> _SeenItems;
        private HashSet<T> _SeenItemSet;

        public T Current => _Current;
        object IEnumerator.Current => _Current;

        internal DenseDistinct (
            in TEnumerator enumerator, TEqualityComparer comparer, HashSet<T> seenItemSet
        ) {
            _Enumerator = enumerator;
            _Current = default;
            _HasMoved = false;
            _Comparer = comparer;
            _SeenItemSet = seenItemSet;
            _SeenItems = default;
        }

        public void Dispose () {
            _Enumerator.Dispose();
            _SeenItemSet?.Clear();
        }

        public T First () {
            if (!_HasMoved) {
                if (MoveNext())
                    return _Current;
                else
                    throw new Exception("Sequence is empty");
            }

            using (var temp = GetEnumerator())
                return temp.First();
        }

        public T FirstOrDefault () {
            if (!_HasMoved) {
                if (MoveNext())
                    return _Current;
                else
                    return default;
            }

            using (var temp = GetEnumerator())
                return temp.FirstOrDefault();
        }

        public bool Any () {
            if (!_HasMoved)
                return MoveNext();

            using (var temp = GetEnumerator())
                return temp.MoveNext();
        }

        public bool MoveNext () {
            _HasMoved = true;

            while (true) {
                if (!_Enumerator.MoveNext())
                    return false;

                _Current = _Enumerator.Current;
                if ((_SeenItemSet != null) && _SeenItemSet.Contains(_Current))
                    continue;
                else if (_SeenItems.IndexOf(in _Current, _Comparer) >= 0)
                    continue;

                if (_SeenItems.Count >= 4) {
                    _SeenItemSet = new HashSet<T>(_Comparer);
                    foreach (var item in _SeenItems)
                        _SeenItemSet.Add(item);
                    _SeenItems.Clear();
                }

                if (_SeenItemSet != null)
                    _SeenItemSet.Add(_Current);
                else
                    _SeenItems.Add(ref _Current);

                return true;
            }
        }

        public void Reset () {
            _Enumerator.Reset();
            _Current = default;
        }

        public void CloneInto (out DenseDistinct<T, TEnumerator, TEqualityComparer> result) {
            result = new DenseDistinct<T, TEnumerator, TEqualityComparer> {
                _Comparer = _Comparer,
            };
            _Enumerator.CloneInto(out result._Enumerator);
        }

        public U Reduce<U> (U initialValue, Func<U, T, U> reducer) {
            var result = initialValue;
            foreach (var item in this)
                result = reducer(result, item);
            return result;
        }

        // FIXME: Don't make a temporary copy?
        public DenseDistinct<T, DenseList<T>.Enumerator, TEqualityComparer2> Distinct<TEqualityComparer2> (
            TEqualityComparer2 comparer, HashSet<T> hash = null
        ) where TEqualityComparer2 : IEqualityComparer<T> {
            var items = ToDenseList();
            var e = items.GetEnumerator();
            return new DenseDistinct<T, DenseList<T>.Enumerator, TEqualityComparer2>(in e, comparer, hash);
        }

        public DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, V> Select<V> (Func<T, V> selector, Func<V, bool> where = null) {
            var source = GetEnumerator();
            var result = new DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, V>(
                in source, selector, false
            );
            if (where != null)
                result.PostPredicates.Add(where);
            return result;
        }

        public DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, T> Where (Func<T, bool> predicate) {
            var source = GetEnumerator();
            var result = new DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, T>(
                in source, DenseList<T>.NullSelector, true
            );
            result.PrePredicates.Add(predicate);
            return result;
        }

        public T[] ToArray () {
            var result = new DenseList<T>();
            Reset();
            while (MoveNext())
                result.Add(ref _Current);
            return result.ToArray();
        }

        public List<T> ToList () {
            var result = new List<T>();
            Reset();
            while (MoveNext())
                result.Add(_Current);
            return result;
        }

        public DenseList<T> ToDenseList () {
            var result = new DenseList<T>();
            Reset();
            while (MoveNext())
                result.Add(ref _Current);
            return result;
        }

        public DenseList<T> OrderBy<TKey> (Func<T, TKey> keySelector) {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, Comparer<TKey>.Default, false);
            return temp;
        }

        public DenseList<T> OrderBy<TKey, TComparer> (Func<T, TKey> keySelector, TComparer comparer)
            where TComparer : IComparer<TKey>
        {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, comparer, false);
            return temp;
        }

        public DenseList<T> OrderByDescending<TKey> (Func<T, TKey> keySelector) {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, Comparer<TKey>.Default, true);
            return temp;
        }

        public DenseList<T> OrderByDescending<TKey, TComparer> (Func<T, TKey> keySelector, TComparer comparer)
            where TComparer : IComparer<TKey>
        {
            var temp = ToDenseList();
            temp.OrderByImpl(keySelector, comparer, true);
            return temp;
        }

        public DenseDistinct<T, TEnumerator, TEqualityComparer> GetEnumerator () {
            if (_HasMoved) {
                CloneInto(out DenseDistinct<T, TEnumerator, TEqualityComparer> result);
                return result;
            }

            return this;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return GetEnumerator();
        }
    }
}

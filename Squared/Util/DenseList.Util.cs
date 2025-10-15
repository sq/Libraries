// #define NOSPAN

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
        public static int Count<T> (this List<T> list, Func<T, bool> predicate) {
            int result = 0;
            foreach (var item in list)
                if (predicate(item))
                    result++;

            return result;
        }
        public static bool All<T> (this List<T> list, Func<T, bool> predicate) {
            foreach (var item in list)
                if (!predicate(item))
                    return false;

            return true;
        }
        public static bool Any<T> (this List<T> list, Func<T, bool> predicate) {
            foreach (var item in list)
                if (predicate(item))
                    return true;

            return false;
        }

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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T ReadItem<T> (this in DenseList<T> list, int index) {
            return ref Item(ref Unsafe.AsRef(in list), index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Item<T> (this ref DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousItem(index);

            if (list.IsIndexOutOfBounds(index))
                BoundsCheckFailed();

            return ref Unsafe.AddByteOffset(ref list.Item1, (IntPtr)(list.ItemStrideInBytes() * index));
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref readonly T ReadItem<T> (this in DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousItem(index);
            
            if (list.IsIndexOutOfBounds(index))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Item<T> (this ref DenseList<T> list, int index) {
            var items = list._Items;
            if (items != null)
                return ref items.DangerousItem(index);
            
            if (list.IsIndexOutOfBounds(index))
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T First<T> (this ref DenseList<T> list) {
            return ref Item(ref list, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Last<T> (this ref DenseList<T> list) {
            return ref Item(ref list, list.Count - 1);
        }

        // TODO: Add a select version of ToDenseList? Harder to do
    }

    public partial struct DenseList<T> : IDisposable, IEnumerable<T>, IList<T>, IOrderedEnumerable<T> {
        public delegate bool Predicate<TUserData> (in T item, in TUserData userData);
        public delegate bool Predicate (in T item);

        internal static class Statics {
            public static readonly T[] EmptyArray = new T[0];
            public static readonly Func<T, T> NullSelector = _NullSelector;
        }

        private static T _NullSelector (T value) => value;


        internal struct PredicateBox {
            // public Predicate RefPredicate;
            public Predicate<object> RefUserDataPredicate;
            public Func<T, bool> Predicate;
            public object UserData;

            public bool Eval (ref T value) {
                if (Predicate != null)
                    return Predicate(value);
                /*
                else if (RefPredicate != null)
                    return RefPredicate(in value);
                */
                else if (RefUserDataPredicate != null)
                    return RefUserDataPredicate(in value, UserData);
                else // HACK to allow Where(null) to enumerate the whole list
                    return true;
            }
        }

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
            // We initialize all this state from outside to avoid having to copy the list when creating
            //  its enumerator (since 'ref this' isn't legal and 'in DenseList<T>' might cause a copy)
            internal int Index;

            internal T Item1, Item2, Item3, Item4;
            internal T[] Items;
            internal int Offset, Count;

            // TODO: Find a way to expose a ref version of this without upsetting the compiler
            // The 'Unsafe.Add' approach used in .Item and .ReadItem doesn't work for some reason

            public T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    if ((Index < 0) || (Index >= Count))
                        throw new InvalidOperationException("No current value");
                    else if (Items != null)
                        return Items[Offset + Index];
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
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Current;
            }

            public void Dispose () {
                Index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext () {
                if (Index < Count) {
                    Index++;
                    return Index < Count;
                }
                return false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryGetNext (ref T result) {
                var countMinus1 = Count - 1;
                if (Index++ < countMinus1) {
                    if (Items != null) {
                        result = Items[Offset + Index];
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

        public static explicit operator DenseList<T> (List<T> list) {
            var result = new DenseList<T>();
            result.AddRange(list);
            return result;
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

        public bool SequenceEqual (ref DenseList<T> rhs) => SequenceEqual(ref rhs, EqualityComparer<T>.Default);

        public bool SequenceEqual<TEqualityComparer> (ref DenseList<T> rhs, TEqualityComparer comparer)
            where TEqualityComparer : IEqualityComparer<T>
        {
            if (Count != rhs.Count)
                return false;

            for (int i = 0, c = Count; i < c; i++) {
                ref var itemL = ref this.Item(i);
                ref var itemR = ref rhs.Item(i);
                if (!comparer.Equals(itemL, itemR))
                    return false;
            }

            return true;
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

            var result = new DenseQuery<T, Enumerator, U>(GetEnumerator(), selector, false);
            if (where != null)
                result.PostPredicates.Add(new DenseList<U>.PredicateBox { Predicate = where });
            return result;
        }

        public DenseQuery<T, Enumerator, U> Select<U> (Func<T, object, U> selector, object selectorUserData, Func<U, bool> where = null) {
            if (Count == 0)
                return default;

            var result = new DenseQuery<T, Enumerator, U>(GetEnumerator(), selector, selectorUserData);
            if (where != null)
                result.PostPredicates.Add(new DenseList<U>.PredicateBox { Predicate = where });
            return result;
        }

        public DenseQuery<T, Enumerator, T> Where (Predicate<object> predicate, object userData) {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            var result = new DenseQuery<T, Enumerator, T>(in e, Statics.NullSelector, true);
            result.PrePredicates.Add(new PredicateBox { RefUserDataPredicate = predicate, UserData = userData });
            return result;
        }

        public DenseQuery<T, Enumerator, T> Where (Func<T, bool> predicate) {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            var result = new DenseQuery<T, Enumerator, T>(in e, Statics.NullSelector, true);
            result.PrePredicates.Add(new PredicateBox { Predicate = predicate });
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
            if (Count <= 1)
                return;
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

        public DenseList<T> Concat<U> (U rhs) where U : IEnumerable<T> {
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

        public U Reduce<U, V> (U initialValue, Func<U, T, V, U> reducer, V userData) {
            var result = initialValue;
            for (int i = 0, c = Count; i < c; i++) {
                ref var item = ref Ext.Item(ref this, i);
                result = reducer(result, item, userData);
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
}

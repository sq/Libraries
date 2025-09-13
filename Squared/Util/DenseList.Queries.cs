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
    public interface IDenseQuerySource<TSource> : IEnumerator {
        void CloneInto (out TSource result);
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

        internal DenseList<DenseList<T>.PredicateBox> PrePredicates;
        internal DenseList<DenseList<TResult>.PredicateBox> PostPredicates;
        internal Func<T, TResult> Selector;
        internal Func<T, object, TResult> Selector2;
        internal object SelectorUserData;

        public TResult Current => _Current;
        object IEnumerator.Current => _Current;

        internal DenseQuery (
            in TEnumerator enumerator, Func<T, TResult> selector, bool isNullSelector
        ) {
            _IsNullSelector = isNullSelector;
            Selector = selector;
            Selector2 = null;
            SelectorUserData = null;
            _Enumerator = enumerator;
            PrePredicates = default;
            PostPredicates = default;
            _Current = default;
            _HasMoved = false;
        }

        internal DenseQuery (
            in TEnumerator enumerator, Func<T, object, TResult> selector, object selectorUserData
        ) {
            _IsNullSelector = false;
            Selector = null;
            Selector2 = selector;
            SelectorUserData = selectorUserData;
            _Enumerator = enumerator;
            PrePredicates = default;
            PostPredicates = default;
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
                    if (!pre.Eval(ref input)) {
                        predicateRejected = true;
                        break;
                    }
                }

                if (predicateRejected)
                    continue;

                if (Selector != null)
                    _Current = Selector(input);
                else
                    _Current = Selector2(input, SelectorUserData);

                foreach (var post in PostPredicates) {
                    if (!post.Eval(ref _Current)) {
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
                Selector = Selector,
                Selector2 = Selector2,
                SelectorUserData = SelectorUserData,
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
                result.PostPredicates.Add(new DenseList<V>.PredicateBox { Predicate = where });
            return result;
        }

        public DenseQuery<TResult, DenseQuery<T, TEnumerator, TResult>, V> Select<V> (Func<TResult, object, V> selector, object selectorUserData, Func<V, bool> where = null) {
            var source = GetEnumerator();
            var result = new DenseQuery<TResult, DenseQuery<T, TEnumerator, TResult>, V>(
                in source, selector, selectorUserData
            );
            if (where != null)
                result.PostPredicates.Add(new DenseList<V>.PredicateBox { Predicate = where });
            return result;
        }

        public DenseQuery<T, TEnumerator, TResult> Where (Func<TResult, bool> predicate) {
            CloneInto(out DenseQuery<T, TEnumerator, TResult> result);
            if ((predicate is Func<T, bool> f) && _IsNullSelector)
                result.PrePredicates.Add(new DenseList<T>.PredicateBox { Predicate = f });
            else
                result.PostPredicates.Add(new DenseList<TResult>.PredicateBox { Predicate = predicate });
            return result;
        }

        public DenseQuery<T, TEnumerator, TResult> Where (DenseList<TResult>.Predicate<object> predicate, object userData) {
            CloneInto(out DenseQuery<T, TEnumerator, TResult> result);
            if ((predicate is DenseList<T>.Predicate<object> f) && _IsNullSelector)
                result.PrePredicates.Add(new DenseList<T>.PredicateBox { RefUserDataPredicate = f, UserData = userData });
            else
                result.PostPredicates.Add(new DenseList<TResult>.PredicateBox { RefUserDataPredicate = predicate, UserData = userData });
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
                result.PostPredicates.Add(new DenseList<V>.PredicateBox { Predicate = where });
            return result;
        }

        public DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, T> Where (DenseList<T>.Predicate<object> predicate, object userData) {
            var source = GetEnumerator();
            var result = new DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, T>(
                in source, DenseList<T>.Statics.NullSelector, true
            );
            result.PrePredicates.Add(new DenseList<T>.PredicateBox { RefUserDataPredicate = predicate, UserData = userData });
            return result;
        }

        public DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, T> Where (Func<T, bool> predicate) {
            var source = GetEnumerator();
            var result = new DenseQuery<T, DenseDistinct<T, TEnumerator, TEqualityComparer>, T>(
                in source, DenseList<T>.Statics.NullSelector, true
            );
            result.PrePredicates.Add(new DenseList<T>.PredicateBox { Predicate = predicate });
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

﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Util {
    public interface IDenseQuerySource<TSource> {
        void CloneInto (out TSource result);
    }

    public static class DenseListExtensions {
        /// <summary>
        /// Returns a dense list containing all the items from a sequence.
        /// </summary>
        public static DenseList<T> ToDenseList<T> (this IEnumerable<T> enumerable) {
            DenseList<T> result = default;
            result.AddRange(enumerable);
            return result;
        }
    }

    public partial struct DenseList<T> : IDisposable, IEnumerable<T>, IList<T> {
        public delegate bool Predicate<TUserData> (ref T item, ref TUserData userData);
        public delegate bool Predicate (ref T item);

        private static T[] EmptyArray;

        internal static readonly Func<T, T> NullSelector = _NullSelector;
        private static T _NullSelector (T value) => value;

        internal class BoxedEmptyEnumerator : IEnumerator<T> {
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

        public struct Query<TEnumerator, TResult> : IEnumerable<TResult>, IEnumerator<TResult>, IDenseQuerySource<Query<TEnumerator, TResult>>
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

            internal Query (
                ref TEnumerator enumerator, Func<T, TResult> selector, bool isNullSelector
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

            public void CloneInto (out DenseList<T>.Query<TEnumerator, TResult> result) {
                result = new Query<TEnumerator, TResult> {
                    Selector = Selector
                };
                _Enumerator.CloneInto(out result._Enumerator);
                PrePredicates.Clone(out result.PrePredicates);
                PostPredicates.Clone(out result.PostPredicates);
            }

            public DenseList<TResult>.Query<Query<TEnumerator, TResult>, V> Select<V> (Func<TResult, V> selector, Func<V, bool> where = null) {
                var source = GetEnumerator();
                var result = new DenseList<TResult>.Query<Query<TEnumerator, TResult>, V>(
                    ref source, selector, false
                );
                if (where != null)
                    result.PostPredicates.Add(where);
                return result;
            }

            public Query<TEnumerator, TResult> Where (Func<TResult, bool> predicate) {
                CloneInto(out Query<TEnumerator, TResult> result);
                if ((predicate is Func<T, bool> f) && _IsNullSelector)
                    result.PrePredicates.Add(f);
                else
                    result.PostPredicates.Add(predicate);
                return result;
            }

            public DenseList<TResult> ToDenseList () {
                var result = new DenseList<TResult>();
                Reset();
                while (MoveNext())
                    result.Add(ref _Current);
                return result;
            }

            public Query<TEnumerator, TResult> GetEnumerator () {
                if (_HasMoved) {
                    CloneInto(out Query<TEnumerator, TResult> result);
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
        }

        public struct Enumerator : IEnumerator<T>, IDenseQuerySource<Enumerator> {
            private int Index;

            private readonly InlineStorage Storage;
            private readonly bool HasList;
            private readonly T[] Items;
            private readonly int Offset, Count;

            internal Enumerator (ref DenseList<T> list) {
                Index = -1;
                Count = list.Count;
                HasList = list.HasList;

                if (HasList) {
                    Storage = default;
                    var buffer = list.Items.GetBuffer();
                    Offset = buffer.Offset;
                    Items = buffer.Array;
                } else {
                    Offset = 0;
                    Storage = list.Storage;
                    Items = null;
                }
            }

            public T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    if (HasList)
                        return Items[Index];
                    else switch (Index) {
                        case 0:
                            return Storage.Item1;
                        case 1:
                            return Storage.Item2;
                        case 2:
                            return Storage.Item3;
                        case 3:
                            return Storage.Item4;
                    }

                    throw new InvalidOperationException("No current value");
                }
            }

            object IEnumerator.Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    if (HasList)
                        return Items[Index];
                    else switch (Index) {
                        case 0:
                            return Storage.Item1;
                        case 1:
                            return Storage.Item2;
                        case 2:
                            return Storage.Item3;
                        case 3:
                            return Storage.Item4;
                    }

                    throw new InvalidOperationException("No current value");
                }
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
                    if (HasList) {
                        result = Items[Index];
                        return false;
                    } else switch (Index) {
                        case 0:
                            result = Storage.Item1;
                            return true;
                        case 1:
                            result = Storage.Item2;
                            return true;
                        case 2:
                            result = Storage.Item3;
                            return true;
                        case 3:
                            result = Storage.Item4;
                            return true;
                        default:
                            return false;
                    }
                }

                result = default;
                return false;
            }

            public void Reset () {
                Index = -1;
            }

            public void CloneInto (out Enumerator result) {
                result = this;
                result.Reset();
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

        public bool All (Func<T, bool> predicate) {
            return CountWhere(predicate) == Count;
        }

        public DenseList<T> Distinct (IEqualityComparer<T> comparer = null, HashSet<T> hash = null) {
            comparer = comparer ?? EqualityComparer<T>.Default;

            hash = hash ??
                (
                    // FIXME: Make this larger?
                    (Count > 32) 
                        ? new HashSet<T>(Count, comparer) 
                        : null
                );
            hash?.Clear();

            var result = new DenseList<T>();
            for (int i = 0, c = Count; i < c; i++) {
                GetItem(i, out T item);
                if (hash != null) {
                    if (hash.Contains(item))
                        continue;
                    hash.Add(item);
                } else if (result.IndexOf(ref item, comparer) >= 0) {
                    continue;
                }
                result.Add(ref item);
            }
            return result;
        }

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

        public Query<Enumerator, U> Select<U> (Func<T, U> selector, Func<U, bool> where = null) {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            var result = new Query<Enumerator, U>(ref e, selector, false);
            if (where != null)
                result.PostPredicates.Add(where);
            return result;
        }

        public Query<Enumerator, T> Where (Func<T, bool> predicate) {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            var result = new Query<Enumerator, T>(ref e, NullSelector, true);
            result.PrePredicates.Add(predicate);
            return result;
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
    }

    public struct DenseListPin<T, J> : IDisposable 
        where T : struct
    {
        private bool IsBuffer;
        public GCHandle Buffer;
        private object Boxed;

        public DenseListPin (ref DenseList<J> list) {
            if (list.HasList) {
                IsBuffer = true;
                Boxed = list.Items.GetBuffer();
                Buffer = GCHandle.Alloc(Boxed);
            } else {
                IsBuffer = false;
                Boxed = list.Storage;
                Buffer = GCHandle.Alloc(Boxed);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void* GetItems () {
            return (void*)Buffer.AddrOfPinnedObject();
        }

        public void Dispose () {
            if (IsBuffer)
                Buffer.Free();
        }
    }
}

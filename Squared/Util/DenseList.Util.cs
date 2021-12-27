using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Squared.Util {
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

        public struct Query<U> : IEnumerable<U>, IEnumerator<U> {
            public struct SelectQuery<V> : IEnumerable<V>, IEnumerator<V> {
                V _Current;
                public Query<U> Query;
                public Func<U, V> Selector;

                public V Current => _Current;
                object IEnumerator.Current => _Current;

                void IDisposable.Dispose () {
                    Query.Dispose();
                }

                IEnumerator<V> IEnumerable<V>.GetEnumerator () {
                    return this;
                }

                IEnumerator IEnumerable.GetEnumerator () {
                    return this;
                }

                public DenseList<V> ToDenseList () {
                    var result = new DenseList<V>();
                    Reset();
                    while (MoveNext())
                        result.Add(ref _Current);
                    return result;
                }

                public bool MoveNext () {
                    if (!Query.MoveNext())
                        return false;

                    _Current = Selector(Query._Current);
                    return true;
                }

                public void Reset () {
                    Query.Reset();
                    _Current = default;
                }
            }

            public struct WhereQuery : IEnumerable<U>, IEnumerator<U> {
                public Query<U> Query;
                public U Current => Query.Current;
                public Func<U, bool> Predicate;

                object IEnumerator.Current => Query.Current;

                void IDisposable.Dispose () {
                    Query.Dispose();
                }

                IEnumerator<U> IEnumerable<U>.GetEnumerator () {
                    return this;
                }

                IEnumerator IEnumerable.GetEnumerator () {
                    return this;
                }

                public DenseList<U> ToDenseList () {
                    var result = new DenseList<U>();
                    Reset();
                    while (MoveNext())
                        result.Add(ref Query._Current);
                    return result;
                }

                public bool MoveNext () {
                    while (Query.MoveNext()) {
                        if (Predicate(Query._Current))
                            return true;
                    }

                    return false;
                }

                public void Reset () {
                    Query.Reset();
                }
            }

            internal static Func<T, U> CastSelector = _CastSelector;
            // FIXME: Boxing
            private static U _CastSelector (T value) => (U)(object)value;

            private int _Position;
            private U _Current;

            public DenseList<T> List;
            public Func<T, U> Selector;
            public Func<T, bool> PrePredicate;
            public Func<U, bool> PostPredicate;

            public U Current => _Current;
            object IEnumerator.Current => _Current;

            internal Query (
                ref DenseList<T> list, Func<T, U> selector, Func<T, bool> prePredicate = null, Func<U, bool> postPredicate = null
            ) {
                if (selector == null)
                    throw new ArgumentNullException(nameof(selector));

                List = list;
                Selector = selector;
                PrePredicate = prePredicate;
                PostPredicate = postPredicate;
                _Position = -1;
                _Current = default;
            }

            public void Dispose () {
            }

            public bool MoveNext () {
                while (true) {
                    _Position += 1;
                    if (_Position >= List.Count)
                        return false;

                    List.GetItem(_Position, out T input);

                    if ((PrePredicate != null) && !PrePredicate(input))
                        continue;

                    _Current = Selector(input);

                    if ((PostPredicate != null) && !PostPredicate(_Current))
                        continue;

                    return true;
                }
            }

            public void Reset () {
                _Position = -1;
                _Current = default;
            }

            public SelectQuery<V> Select<V> (Func<U, V> selector) {
                return new SelectQuery<V> {
                    Query = this,
                    Selector = selector
                };
            }

            public WhereQuery Where (Func<U, bool> postPredicate) {
                return new WhereQuery {
                    Query = this,
                    Predicate = postPredicate
                };
            }

            public DenseList<U> ToDenseList () {
                var result = new DenseList<U>();
                Reset();
                while (MoveNext())
                    result.Add(ref _Current);
                return result;
            }

            public Query<U> GetEnumerator () {
                return this;
            }

            IEnumerator<U> IEnumerable<U>.GetEnumerator () {
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return this;
            }
        }

        public struct Enumerator : IEnumerator<T> {
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
            comparer = EqualityComparer<T>.Default;

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

        public Query<U> Select<U> (Func<T, U> selector) {
            if (Count == 0)
                return default;
            else
                return new Query<U>(ref this, selector);
        }

        public Query<T> Where (Func<T, bool> predicate) {
            if (Count == 0)
                return default;
            else
                return new Query<T>(ref this, NullSelector, predicate);
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

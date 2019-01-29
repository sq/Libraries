using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Render {
    public struct DenseList<T> : IDisposable, IEnumerable<T> {
        public struct Enumerator : IEnumerator<T> {
            private int Index;

            private readonly T Item1, Item2, Item3, Item4;
            private readonly bool HasList;
            private readonly T[] Items;
            private readonly int Count;

            internal Enumerator (ref DenseList<T> list) {
                Index = -1;
                Count = list.Count;
                HasList = list._HasList;

                if (HasList) {
                    Item1 = Item2 = Item3 = Item4 = default(T);
                    Items = list.Items.GetBuffer();
                } else {
                    Item1 = list.Item1;
                    Item2 = list.Item2;
                    Item3 = list.Item3;
                    Item4 = list.Item4;
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
                            return Item1;
                        case 1:
                            return Item2;
                        case 2:
                            return Item3;
                        case 3:
                            return Item4;
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
                            return Item1;
                        case 1:
                            return Item2;
                        case 2:
                            return Item3;
                        case 3:
                            return Item4;
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
        }

        public struct Buffer : IDisposable {
            internal BufferPool<T>.Buffer BufferPoolAllocation;
            internal bool IsTemporary;

            public int Count;
            public T[] Data;

            public T this[int index] {
                get {
                    return Data[index];
                }
                set {
                    Data[index] = value;
                }
            }            

            public void Dispose () {
                if (IsTemporary)
                    BufferPoolAllocation.Dispose();

                Data = null;
            }
        }

        public ListPool<T> ListPool;
        public int? ListCapacity;

        private T Item1, Item2, Item3, Item4;
        private UnorderedList<T> Items;
        private bool _HasList;

        private int _Count;

        public void Clear () {
            if (_Count != 0) {
                _Count = 0;
                Item1 = Item2 = Item3 = Item4 = default(T);
            }

            if (_HasList)
                Items.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureList (int? capacity = null) {
            if (_HasList)
                return;

            CreateList(capacity);
        }

        private void CreateList (int? capacity = null) {
            if (!capacity.HasValue)
                capacity = ListCapacity;

            _HasList = true;
            if (ListPool != null)
                Items = ListPool.Allocate(capacity);
            else if (capacity.HasValue)
                Items = new UnorderedList<T>(capacity.Value);
            else
                Items = new UnorderedList<T>(); 

            if (_Count > 0)
                Items.Add(ref Item1);
            if (_Count > 1)
                Items.Add(ref Item2);
            if (_Count > 2)
                Items.Add(ref Item3);
            if (_Count > 3)
                Items.Add(ref Item4);
            Item1 = Item2 = Item3 = Item4 = default(T);
            _Count = 0;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (_HasList)
                    return Items.Count;
                else
                    return _Count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetItem (int index, out T result) {
            if (_HasList)
                Items.DangerousGetItem(index, out result);
            else switch (index) {
                case 0:
                    result = Item1;
                    return;
                case 1:
                    result = Item2;
                    return;
                case 2:
                    result = Item3;
                    return;
                case 3:
                    result = Item4;
                    return;
                default:
                    throw new IndexOutOfRangeException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetItem (int index, out T result) {
            if (_HasList)
                return Items.DangerousTryGetItem(index, out result);
            else switch (index) {
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
                    result = default(T);
                    return false;
            }
        }

        public T this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (_HasList)
                    return Items.DangerousGetItem(index);

                switch (index) {
                    case 0:
                        return Item1;
                    case 1:
                        return Item2;
                    case 2:
                        return Item3;
                    case 3:
                        return Item4;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }

        private void Add_Slow (ref T item) {
            EnsureList();
            Items.Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Add_Fast (ref T item) {
            var i = _Count++;
            switch (i) {
                case 0:
                    Item1 = item;
                    break;
                case 1:
                    Item2 = item;
                    break;
                case 2:
                    Item3 = item;
                    break;
                case 3:
                    Item4 = item;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            if (_HasList || (_Count >= 4)) {
                Add_Slow(ref item);
            } else {
                Add_Fast(ref item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<T> items) {
            AddRange(items.Array, items.Offset, items.Count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (T[] items, int offset, int count) {
            if (count < 1)
                return;

            var newCount = _Count + count;
            if (newCount <= 4) {
                for (int i = 0; i < count; i++)
                    Add(ref items[offset + i]);
            } else {
                EnsureList(newCount);
                Items.AddRange(items, offset, count);
            }
        }

        public ArraySegment<T> ReserveSpace (int count) {
            // FIXME: Slow
            EnsureList(count);
            return Items.ReserveSpace(count);
        }

        public void RemoveRange (int index, int count) {
            // FIXME: Slow
            EnsureList();
            Items.DangerousRemoveRange(index, count);
        }

        public void RemoveTail (int count) {
            if (count == 0)
                return;
            if (count > Count)
                throw new ArgumentException("count");

            if (!_HasList) {
                _Count -= count;
                // FIXME: Element leak
                return;
            }

            Items.DangerousRemoveRange(Items.Count - count, count);
        }

        public void OverwriteWith (T[] data) {
            var count = data.Length;

            if ((count > 4) || _HasList) {
                EnsureList(count);
                Items.Clear();
                Items.AddRange(data);
            } else {
                _Count = count;
                if (data.Length > 0)
                    Item1 = data[0];
                if (data.Length > 1)
                    Item2 = data[1];
                if (data.Length > 2)
                    Item3 = data[2];
                if (data.Length > 3)
                    Item4 = data[3];
            }
        }

        public Buffer GetBuffer (bool writable) {
            if (writable)
                EnsureList();

            if (_HasList)
                return new Buffer {
                    IsTemporary = false,
                    Data = Items.GetBuffer(),
                    Count = Items.Count
                };
            else {
                var alloc = BufferPool<T>.Allocate(4);
                var buf = alloc.Data;
                buf[0] = Item1;
                buf[1] = Item2;
                buf[2] = Item3;
                buf[3] = Item4;
                return new Buffer {
                    IsTemporary = true,
                    Data = buf,
                    BufferPoolAllocation = alloc,
                    Count = _Count
                };
            }
        }

        public void Sort<TComparer> (TComparer comparer, int[] indices = null)
            where TComparer : IRefComparer<T>
        {
            if (_HasList) {
                if (indices != null)
                    Items.IndexedSortRef(comparer, indices);
                else
                    Items.FastCLRSortRef(comparer);

                return;
            }

            if (_Count <= 1)
                return;

            T a, b;
            if (comparer.Compare(ref Item1, ref Item2) <= 0) {
                a = Item1; b = Item2;
            } else {
                a = Item2; b = Item1;
            }

            if (_Count == 2) {
                Item1 = a;
                Item2 = b;
                return;
            } else if (_Count == 3) {
                if (comparer.Compare(ref b, ref Item3) <= 0) {
                    Item1 = a;
                    Item2 = b;
                } else if (comparer.Compare(ref a, ref Item3) <= 0) {
                    Item1 = a;
                    Item2 = Item3;
                    Item3 = b;
                } else {
                    Item1 = Item3;
                    Item2 = a;
                    Item3 = b;
                }
            } else {
                T c, d;
                if (comparer.Compare(ref Item3, ref Item4) <= 0) {
                    c = Item3; d = Item4;
                } else {
                    c = Item4; d = Item3;
                }

                T m1;
                if (comparer.Compare(ref a, ref c) <= 0) {
                    Item1 = a;
                    m1 = c;
                } else {
                    Item1 = c;
                    m1 = a;
                }

                T m2;
                if (comparer.Compare(ref b, ref d) >= 0) {
                    Item4 = b;
                    m2 = d;
                } else {
                    Item4 = d;
                    m2 = b;
                }

                if (comparer.Compare(ref m1, ref m2) <= 0) {
                    Item2 = m1;
                    Item3 = m2;
                } else {
                    Item2 = m2;
                    Item3 = m1;
                }
            }
        }

        public void Dispose () {
            _Count = 0;
            Item1 = Item2 = Item3 = Item4 = default(T);
            if (ListPool != null)
                ListPool.Release(ref Items);
            else
                Items = null;
            _HasList = false;
        }

        public Enumerator GetEnumerator () {
            return new Enumerator(ref this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(ref this);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(ref this);
        }
    }
}

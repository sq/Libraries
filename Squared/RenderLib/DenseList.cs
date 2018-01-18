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
            private Buffer Buffer;
            private int Index;
            private int Count;

            internal Enumerator (Buffer buffer) {
                Buffer = buffer;
                Index = -1;
                Count = buffer.Count;
            }

            public T Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    return Buffer.Data[Index];
                }
            }

            object IEnumerator.Current {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    return Buffer.Data[Index];
                }
            }

            public void Dispose () {
                Buffer.Dispose();
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

        private T Item1, Item2, Item3, Item4;
        private UnorderedList<T> Items;

        private int _Count;

        public void Clear () {
            _Count = 0;
            Item1 = Item2 = Item3 = Item4 = default(T);
            if (Items != null)
                Items.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureList (int? capacity = null) {
            if (Items != null)
                return;

            CreateList(capacity);
        }

        private void CreateList (int? capacity = null) {
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
                if (Items != null)
                    return Items.Count;
                else
                    return _Count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            Add(ref item);
        }

        public T this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (Items != null)
                    return Items.GetBuffer()[index];

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
                        throw new Exception();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            if ((Items != null) || (_Count >= 4)) {
                EnsureList();
                Items.Add(ref item);
                return;
            }

            var i = _Count;
            _Count += 1;
            switch (i) {
                case 0:
                    Item1 = item;
                    return;
                case 1:
                    Item2 = item;
                    return;
                case 2:
                    Item3 = item;
                    return;
                case 3:
                    Item4 = item;
                    return;
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
            Items.RemoveRange(index, count);
        }

        public void RemoveTail (int count) {
            if (count == 0)
                return;
            if (count > Count)
                throw new ArgumentException("count");

            if (Items == null) {
                _Count -= count;
                return;
            }

            Items.RemoveRange(Items.Count - count, count);
        }

        public void OverwriteWith (T[] data) {
            var count = data.Length;

            if (count > 4) {
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

            if (Items != null)
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
            where TComparer : IComparer<T>
        {
            if (Items != null) {
                if (indices != null)
                    Items.IndexedSort(comparer, indices);
                else
                    Items.FastCLRSort(comparer);

                return;
            }

            if (_Count <= 1)
                return;

            T a, b;
            if (comparer.Compare(Item1, Item2) <= 0) {
                a = Item1; b = Item2;
            } else {
                a = Item2; b = Item1;
            }

            if (_Count == 2) {
                Item1 = a;
                Item2 = b;
                return;
            } else if (_Count == 3) {
                if (comparer.Compare(b, Item3) <= 0) {
                    Item1 = a;
                    Item2 = b;
                } else if (comparer.Compare(a, Item3) <= 0) {
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
                if (comparer.Compare(Item3, Item4) <= 0) {
                    c = Item3; d = Item4;
                } else {
                    c = Item4; d = Item3;
                }

                T m1;
                if (comparer.Compare(a, c) <= 0) {
                    Item1 = a;
                    m1 = c;
                } else {
                    Item1 = c;
                    m1 = a;
                }

                T m2;
                if (comparer.Compare(b, d) >= 0) {
                    Item4 = b;
                    m2 = d;
                } else {
                    Item4 = d;
                    m2 = b;
                }

                if (comparer.Compare(m1, m2) <= 0) {
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
        }

        Enumerator GetEnumerator () {
            return new Enumerator(GetBuffer(false));
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return new Enumerator(GetBuffer(false));
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(GetBuffer(false));
        }
    }
}

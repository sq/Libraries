using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.Render {
    public struct DenseList<T> : IDisposable, IEnumerable<T> {
        [StructLayout(LayoutKind.Sequential)]
        internal struct InlineStorage {
            public T Item1, Item2, Item3, Item4;
            public int Count;
        }

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
                    Item1 = list.Storage.Item1;
                    Item2 = list.Storage.Item2;
                    Item3 = list.Storage.Item3;
                    Item4 = list.Storage.Item4;
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

        internal InlineStorage Storage;
        internal UnorderedList<T> Items;
        internal bool _HasList;

        public void EnsureCapacity (int capacity) {
            if (capacity <= 4)
                return;

            EnsureList();
            Items.EnsureCapacity(capacity);
        }

        public void Clear () {
            if (Storage.Count != 0) {
                Storage.Count = 0;
                Storage.Item1 = Storage.Item2 = Storage.Item3 = Storage.Item4 = default(T);
            }

            if (_HasList)
                Items.Clear();
        }

        public DenseListPin<J, T> Pin<J> ()
            where J : struct
        {
            return new DenseListPin<J, T>(ref this);
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

            if (Storage.Count > 0)
                Items.Add(ref Storage.Item1);
            if (Storage.Count > 1)
                Items.Add(ref Storage.Item2);
            if (Storage.Count > 2)
                Items.Add(ref Storage.Item3);
            if (Storage.Count > 3)
                Items.Add(ref Storage.Item4);
            Storage.Item1 = Storage.Item2 = Storage.Item3 = Storage.Item4 = default(T);
            Storage.Count = 0;
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (_HasList)
                    return Items.Count;
                else
                    return Storage.Count;
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
                    result = Storage.Item1;
                    return;
                case 1:
                    result = Storage.Item2;
                    return;
                case 2:
                    result = Storage.Item3;
                    return;
                case 3:
                    result = Storage.Item4;
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
                        return Storage.Item1;
                    case 1:
                        return Storage.Item2;
                    case 2:
                        return Storage.Item3;
                    case 3:
                        return Storage.Item4;
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
            var i = Storage.Count++;
            switch (i) {
                case 0:
                    Storage.Item1 = item;
                    break;
                case 1:
                    Storage.Item2 = item;
                    break;
                case 2:
                    Storage.Item3 = item;
                    break;
                case 3:
                    Storage.Item4 = item;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            if (_HasList || (Storage.Count >= 4)) {
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

            var newCount = Storage.Count + count;
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
                Storage.Count -= count;
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
                Storage.Count = count;
                if (data.Length > 0)
                    Storage.Item1 = data[0];
                if (data.Length > 1)
                    Storage.Item2 = data[1];
                if (data.Length > 2)
                    Storage.Item3 = data[2];
                if (data.Length > 3)
                    Storage.Item4 = data[3];
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
                buf[0] = Storage.Item1;
                buf[1] = Storage.Item2;
                buf[2] = Storage.Item3;
                buf[3] = Storage.Item4;
                return new Buffer {
                    IsTemporary = true,
                    Data = buf,
                    BufferPoolAllocation = alloc,
                    Count = Storage.Count
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

            if (Storage.Count <= 1)
                return;

            T a, b;
            if (comparer.Compare(ref Storage.Item1, ref Storage.Item2) <= 0) {
                a = Storage.Item1; b = Storage.Item2;
            } else {
                a = Storage.Item2; b = Storage.Item1;
            }

            if (Storage.Count == 2) {
                Storage.Item1 = a;
                Storage.Item2 = b;
                return;
            } else if (Storage.Count == 3) {
                if (comparer.Compare(ref b, ref Storage.Item3) <= 0) {
                    Storage.Item1 = a;
                    Storage.Item2 = b;
                } else if (comparer.Compare(ref a, ref Storage.Item3) <= 0) {
                    Storage.Item1 = a;
                    Storage.Item2 = Storage.Item3;
                    Storage.Item3 = b;
                } else {
                    Storage.Item1 = Storage.Item3;
                    Storage.Item2 = a;
                    Storage.Item3 = b;
                }
            } else {
                T c, d;
                if (comparer.Compare(ref Storage.Item3, ref Storage.Item4) <= 0) {
                    c = Storage.Item3; d = Storage.Item4;
                } else {
                    c = Storage.Item4; d = Storage.Item3;
                }

                T m1;
                if (comparer.Compare(ref a, ref c) <= 0) {
                    Storage.Item1 = a;
                    m1 = c;
                } else {
                    Storage.Item1 = c;
                    m1 = a;
                }

                T m2;
                if (comparer.Compare(ref b, ref d) >= 0) {
                    Storage.Item4 = b;
                    m2 = d;
                } else {
                    Storage.Item4 = d;
                    m2 = b;
                }

                if (comparer.Compare(ref m1, ref m2) <= 0) {
                    Storage.Item2 = m1;
                    Storage.Item3 = m2;
                } else {
                    Storage.Item2 = m2;
                    Storage.Item3 = m1;
                }
            }
        }

        public void Dispose () {
            Storage.Count = 0;
            Storage.Item1 = Storage.Item2 = Storage.Item3 = Storage.Item4 = default(T);
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

    public struct DenseListPin<T, J> : IDisposable 
        where T : struct
    {
        private bool IsBuffer;
        public GCHandle Buffer;
        private object Boxed;

        public DenseListPin (ref DenseList<J> list) {
            if (list._HasList) {
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

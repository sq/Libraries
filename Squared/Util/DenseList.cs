using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util {
    public interface IListPool<T> {
        UnorderedList<T> Allocate (int? capacity, bool capacityIsHint);
        void Release (ref UnorderedList<T> items);
    }

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
            private readonly int Offset, Count;

            internal Enumerator (ref DenseList<T> list) {
                Index = -1;
                Count = list.Count;
                HasList = list._HasList;

                if (HasList) {
                    Item1 = Item2 = Item3 = Item4 = default(T);
                    var buffer = list.Items.GetBuffer();
                    Offset = buffer.Offset;
                    Items = buffer.Array;
                } else {
                    Offset = 0;
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

        public UnorderedList<T>.Allocator Allocator;
        public IListPool<T> ListPool;
        public int? ListCapacity;

        internal InlineStorage Storage;
        internal UnorderedList<T> Items;
        internal bool _HasList;

        public DenseList (T[] items) 
            : this (items, 0, items.Length) {
        }

        public DenseList (T[] items, int offset, int count) {
            this = default(DenseList<T>);

            for (int i = 0; i < count; i++)
                Add(ref items[offset + i]);
        }

        public DenseList (IEnumerable<T> items) {
            this = default(DenseList<T>);

            foreach (var item in items)
                Add(item);
        }

        public bool HasList {
            get {
                return _HasList;
            }
        }

        public void EnsureCapacity (int capacity, bool lazy = false) {
            if (capacity <= 4)
                return;

            if (lazy && !_HasList) {
                ListCapacity = Math.Max(capacity, ListCapacity.GetValueOrDefault(0));
            } else {
                EnsureList();
                Items.EnsureCapacity(capacity);
            }
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
            const int absoluteMinimum = 16;

            if (capacity.HasValue)
                capacity = Math.Max(capacity.Value, absoluteMinimum);
            else if (!capacity.HasValue && ListCapacity.HasValue)
                // Sensible minimum
                capacity = Math.Max(ListCapacity.Value, absoluteMinimum);

            _HasList = true;
            if (ListPool != null)
                Items = ListPool.Allocate(capacity, false);
            else if (capacity.HasValue)
                Items = new UnorderedList<T>(capacity.Value, Allocator);
            else
                Items = new UnorderedList<T>(Allocator); 

            if (Storage.Count > 0)
                Items.Add(ref Storage.Item1);
            if (Storage.Count > 1)
                Items.Add(ref Storage.Item2);
            if (Storage.Count > 2)
                Items.Add(ref Storage.Item3);
            if (Storage.Count > 3)
                Items.Add(ref Storage.Item4);

            Storage = default(InlineStorage);
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
            if (_HasList) {
                Items.DangerousGetItem(index, out result);
                return;
            }

            if (index >= Count)
                throw new IndexOutOfRangeException();

            switch (index) {
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

            if (index >= Count) {
                result = default(T);
                return false;
            }

            switch (index) {
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

                if (index >= Count)
                    throw new IndexOutOfRangeException();

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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (_HasList) {
                    Items.DangerousSetItem(index, ref value);
                    return;
                }

                if (index >= Count)
                    throw new IndexOutOfRangeException();

                switch (index) {
                    case 0:
                        Storage.Item1 = value;
                        return;
                    case 1:
                        Storage.Item2 = value;
                        return;
                    case 2:
                        Storage.Item3 = value;
                        return;
                    case 3:
                        Storage.Item4 = value;
                        return;
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
            OverwriteWith(data, 0, data.Length);
        }

        public void OverwriteWith (T[] data, int sourceOffset, int count) {
            if ((count > data.Length) || (count < 0))
                throw new ArgumentOutOfRangeException("count");

            if ((count > 4) || _HasList) {
                EnsureList(count);
                Items.Clear();
                Items.AddRange(data, sourceOffset, count);
            } else {
                Storage.Count = count;
                if (count > 0)
                    Storage.Item1 = data[sourceOffset];
                if (count > 1)
                    Storage.Item2 = data[sourceOffset + 1];
                if (count > 2)
                    Storage.Item3 = data[sourceOffset + 2];
                if (count > 3)
                    Storage.Item4 = data[sourceOffset + 3];
            }
        }

        public Buffer GetBuffer (bool writable) {
            if (writable)
                EnsureList();

            if (_HasList) {
                var segment = Items.GetBuffer();
                return new Buffer {
                    IsTemporary = false,
                    Offset = segment.Offset,
                    Data = segment.Array,
                    Count = Items.Count
                };
            } else {
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

        public T[] ToArray () {
            if (_HasList)
                return Items.ToArray();
            else {
                var result = new T[Storage.Count];
                if (Storage.Count > 0)
                    result[0] = Storage.Item1;
                if (Storage.Count > 1)
                    result[1] = Storage.Item2;
                if (Storage.Count > 2)
                    result[2] = Storage.Item3;
                if (Storage.Count > 3)
                    result[3] = Storage.Item4;
                return result;
            }
        }

        private struct IndexAndValue {
            public bool Valid;
            public int Index;
            public T Value;

            public IndexAndValue (ref DenseList<T> list, int[] indices, int index) {
                if (indices != null) {
                    if (index >= indices.Length) {
                        Index = -1;
                        Valid = false;
                    } else {
                        Index = indices[index];
                    }
                } else
                    Index = index;
                Valid = list.TryGetItem(Index, out Value);
            }
        }

        private int CompareValues<TComparer> (TComparer comparer, ref IndexAndValue a, ref IndexAndValue b) 
            where TComparer : IRefComparer<T>
        {
            return comparer.Compare(ref a.Value, ref b.Value);
        }

        private void FlushValueOrIndex (ref IndexAndValue value, int index, ref T field, int[] indices) {
            if (indices != null)
                indices[index] = value.Index;
            else
                field = value.Value;
        }

        /// <summary>
        /// Performs an in-place sort of the DenseList.
        /// NOTE: If the list is small this method may sort the values instead of the indices.
        /// </summary>
        /// <param name="indices">The element indices to use for sorting.</param>
        public void Sort<TComparer> (TComparer comparer, int[] indices = null)
            where TComparer : IRefComparer<T>
        {
            if (_HasList) {
                EnsureList ();
                if (indices != null)
                    Items.IndexedSortRef(comparer, indices);
                else
                    Items.FastCLRSortRef(comparer);

                return;
            }

            var count = Storage.Count;
            if (count <= 1)
                return;

            if ((indices != null) && (indices.Length < count))
                throw new ArgumentOutOfRangeException("indices", "index array length must must match or exceed number of elements");

            IndexAndValue v1 = new IndexAndValue(ref this, indices, 0),
                v2 = new IndexAndValue(ref this, indices, 1),
                v3 = new IndexAndValue(ref this, indices, 2),
                v4 = new IndexAndValue(ref this, indices, 3);

            IndexAndValue va, vb;
            if (CompareValues(comparer, ref v1, ref v2) <= 0) {
                va = v1; vb = v2;
            } else {
                va = v2; vb = v1;
            }

            if (count == 2) {
                v1 = va;
                v2 = vb;
            } else if (Storage.Count == 3) {
                if (CompareValues(comparer, ref vb, ref v3) <= 0) {
                    v1 = va;
                    v2 = vb;
                } else if (CompareValues(comparer, ref va, ref v3) <= 0) {
                    v1 = va;
                    v2 = v3;
                    v3 = vb;
                } else {
                    v1 = v3;
                    v2 = va;
                    v3 = vb;
                }
                ;
            } else {
                IndexAndValue vc, vd;
                if (CompareValues(comparer, ref v3, ref v4) <= 0) {
                    vc = v3; vd = v4;
                } else {
                    vc = v4; vd = v3;
                }

                IndexAndValue vm1, vm2;
                if (CompareValues(comparer, ref va, ref vc) <= 0) {
                    v1 = va;
                    vm1 = vc;
                } else {
                    v1 = vc;
                    vm1 = va;
                }

                if (CompareValues(comparer, ref vb, ref vd) >= 0) {
                    v4 = vb;
                    vm2 = vd;
                } else {
                    v4 = vd;
                    vm2 = vb;
                }

                if (CompareValues(comparer, ref vm1, ref vm2) <= 0) {
                    v2 = vm1;
                    v3 = vm2;
                } else {
                    v2 = vm2;
                    v3 = vm1;
                }
                ;
            }

            FlushValueOrIndex(ref v1, 0, ref Storage.Item1, indices);
            FlushValueOrIndex(ref v2, 1, ref Storage.Item2, indices);
            if (count > 2)
                FlushValueOrIndex(ref v3, 2, ref Storage.Item3, indices);
            if (count > 3)
                FlushValueOrIndex(ref v4, 3, ref Storage.Item4, indices);
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

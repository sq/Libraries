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

    public struct DenseList<T> : IDisposable, IEnumerable<T>, IList<T> {
        private static class ValueTraits {
            public static int Size8, Size32;
            private static T[] MeasurementArray;
            private static GCHandle MeasurementArrayPin;

            public unsafe static void ComputeSize () {
                if (typeof(T).IsValueType) {
                    MeasurementArray = new T[2];
                    MeasurementArrayPin = GCHandle.Alloc(MeasurementArray, GCHandleType.Normal);
                    var local = MeasurementArray;
                    RuntimeHelpers.PrepareConstrainedRegions();
                    unchecked {
                        try {
                        } finally {
                            var tRef0 = __makeref(local[0]);
                            var tRef1 = __makeref(local[1]);
                            var pRef0 = (long)*(IntPtr**)&tRef0;
                            var pRef1 = (long)*(IntPtr**)&tRef1;
                            var size = pRef1 - pRef0;
                            Size8 = (int)size;
                            Size32 = Size8 / 4;
                        }
                    }
                    MeasurementArrayPin.Free();
                    MeasurementArray = null;
                } else {
                    Size8 = Marshal.SizeOf<IntPtr>();
                    Size32 = Size8 / 4;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct InlineStorage {
            public T Item1, Item2, Item3, Item4;
            public int Count;

            // FIXME: The JIT is unwilling to inline the optimized methods and generates bad code even though it's branchless
#if USE_TYPEDREF
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private unsafe byte* GetPointerToElement(int index) {
                var r0 = __makeref(Item1);
                var p0 = (byte*)(*(IntPtr**)&r0);
                return p0 + (ValueTraits.Size8 * index);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void GetItemAtIndex (int index, out T result) {
                var pSrc = GetPointerToElement(index);
                result = default;
                var tr = __makeref(result);
                var pDest = (byte*)(*(IntPtr**)&tr);
                for (int i = 0, c = ValueTraits.Size8; i < c; i++)
                    pDest[i] = pSrc[i];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void SetItemAtIndex (int index, ref T value) {
                var pDest = GetPointerToElement(index);
                var tr = __makeref(value);
                var pSrc = (byte*)(*(IntPtr**)&tr);
                for (int i = 0, c = ValueTraits.Size8; i < c; i++)
                    pDest[i] = pSrc[i];
            }

            public unsafe T this[int index] {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    T result = default;
                    var pSrc = GetPointerToElement(index);
                    var tr = __makeref(result);
                    var pDest = (byte*)(*(IntPtr**)&tr);
                    for (int i = 0, c = ValueTraits.Size8; i < c; i++)
                        pDest[i] = pSrc[i];
                    return result;
                }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set {
                    var pDest = GetPointerToElement(index);
                    var tr = __makeref(value);
                    var pSrc = (byte*)(*(IntPtr**)&tr);
                    for (int i = 0, c = ValueTraits.Size8; i < c; i++)
                        pDest[i] = pSrc[i];
                }
            }
#else
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void GetItemAtIndex (int index, out T result) {
                switch (index) {
                    case 0:
                        result = Item1;
                        return;
                    case 1:
                        result = Item2;
                        return;
                    case 2:
                        result = Item3;
                        return;
                    default:
                        result = Item4;
                        return;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe void SetItemAtIndex (int index, ref T value) {
                switch (index) {
                    case 0:
                        Item1 = value;
                        return;
                    case 1:
                        Item2 = value;
                        return;
                    case 2:
                        Item3 = value;
                        return;
                    default:
                        Item4 = value;
                        return;
                }
            }

            public unsafe T this[int index] {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get {
                    switch (index) {
                        case 0:
                            return Item1;
                        case 1:
                            return Item2;
                        case 2:
                            return Item3;
                        default:
                            return Item4;
                    }
                }
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                set {
                    switch (index) {
                        case 0:
                            Item1 = value;
                            return;
                        case 1:
                            Item2 = value;
                            return;
                        case 2:
                            Item3 = value;
                            return;
                        default:
                            Item4 = value;
                            return;
                    }
                }
            }
#endif

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static void Add (ref InlineStorage storage, ref T item) {
                var i = storage.Count;
                storage.Count++;
                storage.SetItemAtIndex(i, ref item);
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

        private object _ListPoolOrAllocator;
        public object ListPoolOrAllocator {
            get => _ListPoolOrAllocator;
            set {
                if ((_ListPoolOrAllocator != null) && (Count > 0))
                    throw new InvalidOperationException("A list pool or allocator is already set and this list is not empty");
                _ListPoolOrAllocator = value;
            }
        }
        private int _ListCapacity;
        public int? ListCapacity {
            get => _ListCapacity > 0 ? _ListCapacity : (int?)null;
            set {
                _ListCapacity = value ?? 0;
            }
        }

        internal InlineStorage Storage;
        internal UnorderedList<T> Items;

#if USE_TYPEDREF
        static DenseList () {
            ValueTraits.ComputeSize();
        }
#endif

        public DenseList (T[] items) 
            : this (items, 0, items.Length) {
        }

        public DenseList (T[] items, int offset, int count) {
            this = default(DenseList<T>);
            AddRange(items, offset, count);
        }

        public DenseList (IEnumerable<T> items) {
            this = default(DenseList<T>);
            AddRange(items);
        }

        public UnorderedList<T>.Allocator Allocator =>
            _ListPoolOrAllocator as UnorderedList<T>.Allocator;
        public IListPool<T> ListPool =>
            _ListPoolOrAllocator as IListPool<T>;

        public bool HasList {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Items != null);
        }

        public DenseList<T> Clone () {
            DenseList<T> output;
            Clone(out output);
            return output;
        }

        public void CopyTo (ref DenseList<T> output) {
            if (HasList) {
                if (output.HasList) {
                    Items.CopyTo(output.Items);
                } else {
                    for (int i = 0, c = Items.Count; i < c; i++) {
                        var item = this[i];
                        output.Add(ref item);
                    }
                }
            } else if (output.HasList) {
                for (int i = 0, c = Count; i < c; i++) {
                    var item = this[i];
                    output.Items.Add(ref item);
                }
            } else {
                for (int i = 0, c = Count; i < c; i++) {
                    var item = this[i];
                    output.Add(ref item);
                }
            }
        }

        public void Clone (out DenseList<T> output) {
            if (HasList) {
                output = default(DenseList<T>);
                UnorderedList<T> newItems;
                if (output.HasList) {
                    newItems = output.Items;
                } else {
                    newItems = output.Items = new UnorderedList<T>(Items.Count);
                }
                Items.CopyTo(newItems);
            } else {
                output = this;
            }
        }

        public void Clone (ref DenseList<T> output, bool outputIsEmpty) {
            if (HasList) {
                if (!outputIsEmpty)
                    output = default(DenseList<T>);

                UnorderedList<T> newItems;
                if (output.HasList) {
                    newItems = output.Items;
                } else {
                    newItems = output.Items = new UnorderedList<T>(Items.Count);
                }
                Items.CopyTo(newItems);
            } else if ((Storage.Count > 0) || !outputIsEmpty) {
                output = this;
            }
        }

        public void EnsureCapacity (int capacity, bool lazy = false) {
            if (capacity <= 4)
                return;

            if (lazy && !HasList) {
                _ListCapacity = Math.Max(capacity, _ListCapacity);
            } else {
                EnsureList(capacity);
                Items.EnsureCapacity(capacity);
            }
        }

        public void Clear () {
            Storage = default;
            if (HasList)
                Items.Clear();
        }

        public DenseListPin<J, T> Pin<J> ()
            where J : struct
        {
            return new DenseListPin<J, T>(ref this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureList (int? capacity = null) {
            if (HasList)
                return;

            CreateList(capacity);
        }

        private void CreateList (int? capacity = null) {
            const int absoluteMinimum = 64;

            if (capacity.HasValue)
                capacity = Math.Max(capacity.Value, absoluteMinimum);
            else if (!capacity.HasValue && (_ListCapacity > 0))
                // Sensible minimum
                capacity = Math.Max(_ListCapacity, absoluteMinimum);
            else
                ;

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
                if (HasList)
                    return Items.Count;
                else
                    return Storage.Count;
            }
        }

        int ICollection<T>.Count => Count;

        bool ICollection<T>.IsReadOnly => false;

        T IList<T>.this[int index] { get => this[index]; set => this[index] = value; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            Add(ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetItem (int index, out T result) {
            if (Items != null) {
                Items.DangerousGetItem(index, out result);
                return;
            }

            if (index >= Storage.Count)
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
            if (Items != null)
                return Items.DangerousTryGetItem(index, out result);

            if (index >= Storage.Count) {
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T First () {
            if (Count <= 0)
                throw new ArgumentOutOfRangeException("List is empty");
            else
                return this[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T FirstOrDefault () {
            if (Count <= 0)
                return default(T);
            else if (HasList)
                return Items.DangerousGetItem(0);
            else
                return Storage.Item1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T FirstOrDefault (T defaultValue) {
            if (Count <= 0)
                return defaultValue;
            else if (HasList)
                return Items.DangerousGetItem(0);
            else
                return Storage.Item1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Last () {
            if (Count <= 0)
                throw new ArgumentOutOfRangeException("List is empty");
            else
                return this[Count - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T LastOrDefault () {
            if (Count <= 0)
                return default(T);
            else if (HasList)
                return Items.DangerousGetItem(Items.Count - 1);
            else
                return this[Count - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T LastOrDefault (T defaultValue) {
            if (Count <= 0)
                return defaultValue;
            else if (HasList)
                return Items.DangerousGetItem(Items.Count - 1);
            else
                return this[Count - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains (T value) {
            return IndexOf(value) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (T value) {
            return IndexOf(ref value, EqualityComparer<T>.Default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (ref T value) {
            return IndexOf(ref value, EqualityComparer<T>.Default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (T value, IEqualityComparer<T> comparer) {
            return IndexOf(ref value, comparer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (ref T value, IEqualityComparer<T> comparer) {
            var c = Count;
            if (c <= 0)
                return -1;
            for (int i = 0; i < c; i++)
                if (comparer.Equals(this[i], value))
                    return i;
            return -1;
        }

        public T this [int index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                if (Items != null)
                    return Items.DangerousGetItem(index);

                if (index >= Storage.Count)
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
                if (Items != null) {
                    Items.DangerousSetItem(index, ref value);
                    return;
                }

                if (index >= Storage.Count)
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
            var count = Storage.Count;
            Storage.Count = count + 1;
            Storage.SetItemAtIndex(count, ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnsafeAddWithKnownCapacity (ref DenseList<T> list, ref T item) {
            if (list.HasList) {
                list.Items.Add(ref item);
                return;
            }

            var count = list.Storage.Count;
            list.Storage.Count = count + 1;
            list.Storage.SetItemAtIndex(count, ref item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            if ((Items != null) || (Storage.Count >= 4)) {
                Add_Slow(ref item);
            } else {
                Add_Fast(ref item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ref DenseList<T> items) {
            // FIXME: Do an optimized copy when the source does not have a backing list
            // FIXME: Use ref
            foreach (var item in items)
                Add(item);
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
                EnsureCapacity(newCount);
                Items.AddRange(items, offset, count);
            }
        }

        public void AddRange<U> (U enumerable) where U : IEnumerable<T> {
            // FIXME: Find a way to do this without boxing?
            if (!typeof(U).IsValueType && object.ReferenceEquals(null, enumerable))
                return;

            T item;
            if (enumerable is T[] array) {
                EnsureCapacity(Count + array.Length);
                for (int i = 0, c = array.Length; i < c; i++)
                    Add(ref array[i]);
            } else if (enumerable is IList<T> list) {
                EnsureCapacity(Count + list.Count);
                for (int i = 0, c = list.Count; i < c; i++) {
                    item = list[i];
                    Add(ref item);
                }
            } else {
                using (var e = enumerable.GetEnumerator())
                while (e.MoveNext()) {
                    item = e.Current;
                    Add(ref item);
                }
            }
        }

        public ArraySegment<T> ReserveSpace (int count) {
            // FIXME: Slow
            EnsureList(count);
            return Items.ReserveSpace(count);
        }

        public void RemoveAt (int index) {
            // FIXME: Slow
            if ((index < Storage.Count) && !HasList) {
                switch (index) {
                    case 0:
                        Storage.Item1 = Storage.Item2;
                        Storage.Item2 = Storage.Item3;
                        Storage.Item3 = Storage.Item4;
                        Storage.Item4 = default(T);
                        break;
                    case 1:
                        Storage.Item2 = Storage.Item3;
                        Storage.Item3 = Storage.Item4;
                        Storage.Item4 = default(T);
                        break;
                    case 2:
                        Storage.Item3 = Storage.Item4;
                        Storage.Item4 = default(T);
                        break;
                    case 3:
                        Storage.Item4 = default(T);
                        break;
                }
                Storage.Count--;
            } else {
                EnsureList();
                Items.RemoveAtOrdered(index);
            }
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

            if (!HasList) {
                Storage.Count -= count;
                // FIXME: Element leak
                return;
            }

            Items.DangerousRemoveRange(Items.Count - count, count);
        }

        public int RemoveWhere (Func<T, bool> predicate) {
            int result = 0;
            for (int i = Count - 1; i >= 0; i--) {
                if (!predicate(this[i]))
                    continue;
                RemoveAt(i);
                result++;
            }
            return result;
        }

        public void OverwriteWith (T[] data) {
            OverwriteWith(data, 0, data.Length);
        }

        public void OverwriteWith (T[] data, int sourceOffset, int count) {
            if ((count > data.Length) || (count < 0))
                throw new ArgumentOutOfRangeException("count");

            if ((count > 4) || HasList) {
                EnsureList(count);
                Items.Clear();
                Items.AddRange(data, sourceOffset, count);
            } else {
                Storage.Count = count;
                for (int i = 0; i < count; i++)
                    Storage.SetItemAtIndex(i, ref data[sourceOffset]);
            }
        }

        public Buffer GetBuffer (bool writable) {
            if (writable)
                EnsureList();

            if (HasList) {
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
                for (int i = 0; i < Storage.Count; i++)
                    Storage.GetItemAtIndex(i, out buf[i]);
                return new Buffer {
                    IsTemporary = true,
                    Data = buf,
                    BufferPoolAllocation = alloc,
                    Count = Storage.Count
                };
            }
        }

        public T[] ToArray () {
            if (HasList)
                return Items.ToArray();
            else {
                var result = new T[Storage.Count];
                for (int i = 0; i < result.Length; i++)
                    Storage.GetItemAtIndex(i, out result[i]);
                return result;
            }
        }

        private struct IndexAndValue {
            public bool Valid;
            public int Index;
            public T Value;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        private void Sort_Small<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T>
        {
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

        private void Sort_Large<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T> 
        {
            if (indices != null)
                Items.IndexedSortRef(comparer, indices);
            else
                Items.FastCLRSortRef(comparer);
        }

        /// <summary>
        /// Performs an in-place sort of the DenseList.
        /// NOTE: If the list is small this method may sort the values instead of the indices.
        /// </summary>
        /// <param name="indices">The element indices to use for sorting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sort<TComparer> (TComparer comparer, int[] indices = null)
            where TComparer : IRefComparer<T>
        {
            if (HasList) {
                Sort_Large(comparer, indices);
            } else {
                Sort_Small(comparer, indices);
            }
        }

        /// <summary>
        /// Performs an in-place sort of the DenseList.
        /// NOTE: If the list is small this method may sort the values instead of the indices.
        /// </summary>
        /// <param name="indices">The element indices to use for sorting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SortNonRef (IComparer<T> comparer, int[] indices = null) {
            var wrapped = new RefComparerAdapter<IComparer<T>, T>(comparer);
            Sort(wrapped, indices);
        }

        public void Dispose () {
            Storage = default;
            if (ListPool != null)
                ListPool.Release(ref Items);
            else
                Items = null;
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

        int IList<T>.IndexOf (T item) {
            return IndexOf(item);
        }

        void IList<T>.Insert (int index, T item) {
            throw new NotImplementedException();
        }

        void IList<T>.RemoveAt (int index) {
            RemoveAt(index);
        }

        void ICollection<T>.Add (T item) {
            Add(ref item);
        }

        void ICollection<T>.Clear () {
            Clear();
        }

        bool ICollection<T>.Contains (T item) {
            return IndexOf(ref item) >= 0;
        }

        void ICollection<T>.CopyTo (T[] array, int arrayIndex) {
            for (int i = 0, c = Math.Min(array.Length - arrayIndex, Count); i < c; i++)
                GetItem(i, out array[arrayIndex + i]);
        }

        bool ICollection<T>.Remove (T item) {
            var index = IndexOf(ref item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
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

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
    public interface IListPool<T> {
        UnorderedList<T> Allocate (int? capacity, bool capacityIsHint);
        void Release (ref UnorderedList<T> items);
    }

    [StructLayout(LayoutKind.Sequential)]
    public partial struct DenseList<T> : IDisposable, IEnumerable<T>, IList<T> {
        // NOTE: It is important for the items to be at the front of the list,
        //  so that if it is boxed and pinned the pin pointer is aimed directly
        //  at the elements
        internal T Item1, Item2, Item3, Item4;
        internal int _Count;
        internal UnorderedList<T> _Items;

        private object _ListPoolOrAllocator;
        public object ListPoolOrAllocator {
            get => _ListPoolOrAllocator;
            set {
                if ((_ListPoolOrAllocator != null) && (Count > 0) && (_ListPoolOrAllocator != value))
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

        internal static void ListIsEmpty () {
            throw new ArgumentOutOfRangeException("List is empty");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void GetInlineItemAtIndex (int index, out T result) {
#if !NOSPAN
            // FIXME: This is a bit slower than the switch version for some reason?
            if ((index < 0) || (index > 3))
                EnumerableExtensions.BoundsCheckFailed();
            result = Unsafe.Add(ref Item1, index);
#else
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
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void SetInlineItemAtIndex (int index, ref T value) {
#if !NOSPAN
            // FIXME: This is a bit slower than the switch version for some reason?
            if ((index < 0) || (index > 3))
                EnumerableExtensions.BoundsCheckFailed();
            Unsafe.Add(ref Item1, index) = value;
#else
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
#endif
        }

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
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (_Items != null);
        }

        public DenseList<T> Clone () {
            DenseList<T> output;
            Clone(out output);
            return output;
        }

        public void CopyTo (T[] destination) => CopyTo(destination, 0, _Count);

        public void CopyTo (T[] destination, int destinationOffset, int count) {
            var items = _Items;
            if ((count > _Count) || (count < 0))
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count + destinationOffset > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(destinationOffset));

            if (items != null)
                _Items.CopyTo(destination, destinationOffset, count);
            else {
                if (count > 0)
                    destination[destinationOffset + 0] = Item1;
                if (count > 1)
                    destination[destinationOffset + 1] = Item2;
                if (count > 2)
                    destination[destinationOffset + 2] = Item3;
                if (count > 3)
                    destination[destinationOffset + 3] = Item4;
            }
        }

        public void CopyTo (ref DenseList<T> output) {
            var items = _Items;
            if (items != null) {
                if (output.HasList) {
                    items.CopyTo(output._Items);
                } else {
                    for (int i = 0, c = items.Count; i < c; i++) {
                        ref var item = ref items.DangerousItem(i);
                        output.Add(ref item);
                    }
                }
            } else if (output.HasList) {
                var outItems = output._Items;
                for (int i = 0, c = Count; i < c; i++) {
                    ref var item = ref Ext.Item(ref this, i);
                    outItems.Add(ref item);
                }
            } else {
                for (int i = 0, c = Count; i < c; i++) {
                    ref var item = ref Ext.Item(ref this, i);
                    output.Add(ref item);
                }
            }
        }

        public void Clone (out DenseList<T> output) {
            if (HasList) {
                output = default(DenseList<T>);
                UnorderedList<T> newItems;
                if (output.HasList) {
                    newItems = output._Items;
                } else {
                    newItems = output._Items = new UnorderedList<T>(_Items.Count);
                }
                _Items.CopyTo(newItems);
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
                    newItems = output._Items;
                } else {
                    newItems = output._Items = new UnorderedList<T>(_Items.Count);
                }
                _Items.CopyTo(newItems);
            } else if ((_Count > 0) || !outputIsEmpty) {
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
                _Items.EnsureCapacity(capacity);
            }
        }

        public void UnsafeFastClear () {
            _Count = 0;
            _Items?.UnsafeFastClear();
        }

        /// <summary>
        /// Clears the list's internal storage but does not release any heap objects (like backing stores).
        /// </summary>
        public void Clear () {
            Item1 = Item2 = Item3 = Item4 = default;
            _Count = 0;
            _Items?.Clear();
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // TODO: Make this return the list to avoid extra field reads
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

            UnorderedList<T> items;
            if (ListPool != null)
                items = ListPool.Allocate(capacity, false);
            else if (capacity.HasValue)
                items = new UnorderedList<T>(capacity.Value, Allocator);
            else
                items = new UnorderedList<T>(Allocator);

            _Items = items;
            if (_Count > 0)
                items.Add(ref Item1);
            if (_Count > 1)
                items.Add(ref Item2);
            if (_Count > 2)
                items.Add(ref Item3);
            if (_Count > 3)
                items.Add(ref Item4);

            Item1 = Item2 = Item3 = Item4 = default;
            _Count = 0;
        }

        public int Count {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var items = _Items;
                if (items != null)
                    return items._Count;
                else
                    return _Count;
            }
        }

        int ICollection<T>.Count => Count;

        bool ICollection<T>.IsReadOnly => false;

        T IList<T>.this[int index] { get => this[index]; set => this[index] = value; }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert (int index, T item) {
            Insert(index, ref item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert (int index, ref T item) {
            if (index == Count) {
                Add(ref item);
                return;
            }
            // FIXME: Add fast path
            Insert_Slow(index, ref item);
        }

        private void Insert_Slow (int index, ref T item) {
            EnsureList();
            _Items.InsertOrdered(index, in item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetItem (int index, out T result) {
            var items = _Items;
            if (items != null) {
                items.DangerousGetItem(index, out result);
                return;
            }

            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();

#if !NOSPAN
            result = Unsafe.Add(ref Item1, index);
#else
            switch (index) {
                default:
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
            }
#endif
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetItem (int index, out T result) {
            var items = _Items;
            if (items != null)
                return items.DangerousTryGetItem(index, out result);

            if (index >= _Count) {
                result = default(T);
                return false;
            }

            GetInlineItemAtIndex(index, out result);
            return (index <= 3);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T First () {
            if (Count <= 0) {
                ListIsEmpty();
                return default;
            } else
                return this[0];
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T FirstOrDefault (in T defaultValue = default) {
            if (Count <= 0)
                return defaultValue;

            var items = _Items;
            if (items != null)
                return items.DangerousItem(0);
            else
                return Item1;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T FirstOrDefault (Func<T, bool> predicate, in T defaultValue = default) {
            if (Count <= 0)
                return defaultValue;
            var index = IndexOf(predicate);
            if (index < 0)
                return defaultValue;
            else
                return this[index];
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Last () {
            if (Count <= 0) {
                ListIsEmpty();
                return default;
            } else
                return this[Count - 1];
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T LastOrDefault (in T defaultValue = default) {
            if (Count <= 0)
                return defaultValue;

            var items = _Items;
            if (items != null)
                return items.DangerousItem(items.Count - 1);
            else
                return this[Count - 1];
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains (in T value) {
            return IndexOf(in value) >= 0;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains<TComparer> (in T value, TComparer comparer)
            where TComparer : IEqualityComparer<T>
        {
            return IndexOf(in value, comparer) >= 0;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (in T value) {
            return IndexOf(in value, EqualityComparer<T>.Default);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf<TComparer> (in T value, TComparer comparer)
            where TComparer : IEqualityComparer<T>
        {
            var c = Count;
            if (c <= 0)
                return -1;
            for (int i = 0; i < c; i++) {
                ref var item = ref Ext.Item(ref this, i);
                if (comparer.Equals(item, value))
                    return i;
            }
            return -1;
        }

        public unsafe T this [int index] {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var items = _Items;
                if (items != null)
                    return items.DangerousItem(index);

                if ((index < 0) || (index >= _Count))
                    EnumerableExtensions.BoundsCheckFailed();

#if !NOSPAN
                return Unsafe.Add(ref Item1, index);
#else
                switch (index) {
                    default:
                    case 0:
                        return Item1;
                    case 1:
                        return Item2;
                    case 2:
                        return Item3;
                    case 3:
                        return Item4;
                }
#endif
            }
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                SetItem(index, ref value);
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetItem (int index, T value) {
            SetItem(index, ref value);
        }

        public void SetItem (int index, ref T value) {
            var items = _Items;
            if (items != null) {
                items.DangerousSetItem(index, ref value);
                return;
            }

            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();

#if !NOSPAN
            Unsafe.Add(ref Item1, index) = value;
#else
            switch (index) {
                default:
                case 0:
                    Item1 = value;
                    return;
                case 1:
                    Item2 = value;
                    return;
                case 2:
                    Item3 = value;
                    return;
                case 3:
                    Item4 = value;
                    return;
            }
#endif
        }

        private void Add_Slow (ref T item) {
            EnsureList();
            _Items.Add(ref item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnsafeAddWithKnownCapacity (ref DenseList<T> list, ref T item) {
            var items = list._Items;
            if (items != null) {
                items.Add(ref item);
                return;
            }

            var count = list._Count;
            list._Count = count + 1;
            list.SetItem(count, ref item);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            if ((_Count >= 4) || (_Items != null)) {
                Add_Slow(ref item);
            } else {
                SetInlineItemAtIndex(_Count, ref item);
                _Count += 1;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            if ((_Count >= 4) || (_Items != null)) {
                Add_Slow(ref item);
            } else {
                SetInlineItemAtIndex(_Count, ref item);
                _Count += 1;
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ref DenseList<T> items) {
            for (int i = 0, c = items.Count; i < c; i++)
                Add(ref items.Item(i));
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (ArraySegment<T> items) {
            AddRange(items.Array, items.Offset, items.Count);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRange (T[] items, int offset, int count) {
            if (count < 1)
                return;

            var newCount = _Count + count;
            if (newCount <= 4) {
                for (int i = 0; i < count; i++)
                    Add(ref items[offset + i]);
            } else {
                EnsureCapacity(newCount);
                _Items.AddRange(items, offset, count);
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

        public void AddRange<U> (U enumerable, Func<T, bool> where) where U : IEnumerable<T> {
            if (where == null)
                throw new ArgumentNullException(nameof(where));
            // FIXME: Find a way to do this without boxing?
            if (!typeof(U).IsValueType && object.ReferenceEquals(null, enumerable))
                return;

            T item;
            if (enumerable is T[] array) {
                EnsureCapacity(Count + array.Length);
                for (int i = 0, c = array.Length; i < c; i++) {
                    if (where(array[i]))
                        Add(ref array[i]);
                }
            } else if (enumerable is IList<T> list) {
                EnsureCapacity(Count + list.Count);
                for (int i = 0, c = list.Count; i < c; i++) {
                    item = list[i];
                    if (where(item))
                        Add(ref item);
                }
            } else {
                using (var e = enumerable.GetEnumerator())
                while (e.MoveNext()) {
                    item = e.Current;
                    if (where(item))
                        Add(ref item);
                }
            }
        }

        public ArraySegment<T> ReserveSpace (int count) {
            // FIXME: Slow
            EnsureList(count);
            return _Items.ReserveSpace(count);
        }

        public bool Remove<TComparer> (in T item, TComparer comparer)
            where TComparer : IEqualityComparer<T>            
        {
            var index = IndexOf(in item, comparer);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public bool Remove (in T item) {
            var index = IndexOf(in item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt (int index) {
            // FIXME: Slow
            if ((index < _Count) && !HasList) {
                switch (index) {
                    case 0:
                        Item1 = Item2;
                        Item2 = Item3;
                        Item3 = Item4;
                        Item4 = default(T);
                        break;
                    case 1:
                        Item2 = Item3;
                        Item3 = Item4;
                        Item4 = default(T);
                        break;
                    case 2:
                        Item3 = Item4;
                        Item4 = default(T);
                        break;
                    case 3:
                        Item4 = default(T);
                        break;
                }
                _Count--;
            } else {
                EnsureList();
                _Items.RemoveAtOrdered(index);
            }
        }

        public void RemoveRange (int index, int count) {
            // FIXME: Slow
            EnsureList();
            _Items.DangerousRemoveRange(index, count);
        }

        public void RemoveTail (int count) {
            if (count == 0)
                return;
            if (count > Count)
                throw new ArgumentException("count");

            if (!HasList) {
                _Count -= count;
                // FIXME: Element leak
                return;
            }

            _Items.DangerousRemoveRange(_Items.Count - count, count);
        }

        public int CountWhere (Func<T, bool> predicate) {
            int result = 0;
            for (int i = 0, c = Count; i < c; i++) {
                ref var item = ref Ext.Item(ref this, i);
                if (predicate(item))
                    result++;
            }
            return result;
        }

        public int RemoveAll (Func<T, bool> predicate) {
            int result = 0;
            for (int i = Count - 1; i >= 0; i--) {
                ref var item = ref Ext.Item(ref this, i);
                if (!predicate(item))
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
                _Items.Clear();
                _Items.AddRange(data, sourceOffset, count);
            } else {
                _Count = count;
                for (int i = 0; i < count; i++)
                    SetInlineItemAtIndex(i, ref data[sourceOffset]);
            }
        }

        public Buffer GetBuffer (bool writable, T[] scratchBuffer = null) {
            if (writable)
                EnsureList();

            if (HasList) {
                var segment = _Items.GetBuffer();
                return new Buffer {
                    IsTemporary = false,
                    Offset = segment.Offset,
                    Data = segment.Array,
                    Count = _Items.Count
                };
            } else if (scratchBuffer != null) {
                if (scratchBuffer.Length < _Count)
                    throw new ArgumentOutOfRangeException($"Scratch buffer should have room for at least {_Count} items");

                CopyTo(scratchBuffer);
                return new Buffer {
                    IsTemporary = false,
                    Data = scratchBuffer,
                    Count = _Count
                };
            } else {
                var alloc = BufferPool<T>.Allocate(4);
                var buf = alloc.Data;
                CopyTo(buf);
                return new Buffer {
                    IsTemporary = true,
                    Data = buf,
                    BufferPoolAllocation = alloc,
                    Count = _Count
                };
            }
        }

        public T[] ToArray () {
            if (Count == 0) {
                var result = EmptyArray;
                if (result == null) {
                    result = new T[0];
                    Interlocked.CompareExchange(ref EmptyArray, result, null);
                }
                return result;
            }

            else if (HasList)
                return _Items.ToArray();
            else {
                var result = new T[_Count];
                for (int i = 0; i < result.Length; i++)
                    GetInlineItemAtIndex(i, out result[i]);
                return result;
            }
        }

        private struct IndexAndValue {
            public bool Valid;
            public int Index;
            public T Value;

            [TargetedPatchingOptOut("")]
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

        [TargetedPatchingOptOut("")]
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

        private int IndexOf_Small<TUserData> (Predicate<TUserData> predicate, in TUserData userData) {
            if ((_Count > 0) && predicate(in Item1, in userData))
                return 0;
            if ((_Count > 1) && predicate(in Item2, in userData))
                return 1;
            if ((_Count > 2) && predicate(in Item3, in userData))
                return 2;
            if ((_Count > 3) && predicate(in Item4, in userData))
                return 3;
            return -1;
        }

        private static int IndexOf_Large<TUserData> (UnorderedList<T> items, Predicate<TUserData> predicate, in TUserData userData) {
            var buffer = items.GetBuffer();
            for (int i = 0, c = items.Count; i < c; i++) {
                if (predicate(in buffer.Array[i + buffer.Offset], in userData))
                    return i;
            }
            return -1;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf<TUserData> (Predicate<TUserData> predicate, in TUserData userData) {
            var items = _Items;
            if (items != null) {
                return IndexOf_Large(items, predicate, in userData);
            } else {
                return IndexOf_Small(predicate, in userData);
            }
        }

        private int IndexOf_Small (Predicate predicate) {
            if ((_Count > 0) && predicate(in Item1))
                return 0;
            if ((_Count > 1) && predicate(in Item2))
                return 1;
            if ((_Count > 2) && predicate(in Item3))
                return 2;
            if ((_Count > 3) && predicate(in Item4))
                return 3;
            return -1;
        }

        private static int IndexOf_Large (UnorderedList<T> items, Predicate predicate) {
            var buffer = items.GetBuffer();
            for (int i = 0, c = items.Count; i < c; i++) {
                if (predicate(in buffer.Array[i + buffer.Offset]))
                    return i;
            }
            return -1;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (Predicate predicate) {
            var items = _Items;
            if (items != null) {
                return IndexOf_Large(items, predicate);
            } else {
                return IndexOf_Small(predicate);
            }
        }

        private int IndexOf_Small (Func<T, bool> predicate) {
            if ((_Count > 0) && predicate(Item1))
                return 0;
            if ((_Count > 1) && predicate(Item2))
                return 1;
            if ((_Count > 2) && predicate(Item3))
                return 2;
            if ((_Count > 3) && predicate(Item4))
                return 3;
            return -1;
        }

        private int IndexOf_Large (Func<T, bool> predicate) {
            var buffer = _Items.GetBuffer();
            for (int i = 0, c = _Items.Count; i < c; i++) {
                if (predicate(buffer.Array[i + buffer.Offset]))
                    return i;
            }
            return -1;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IndexOf (Func<T, bool> predicate) {
            if (HasList) {
                return IndexOf_Large(predicate);
            } else {
                return IndexOf_Small(predicate);
            }
        }

        public T Find (Predicate predicate) {
            var index = IndexOf(predicate);
            if (index < 0)
                return default;
            else
                return this[index];
        }

        public T Find (Func<T, bool> predicate) {
            var index = IndexOf(predicate);
            if (index < 0)
                return default;
            else
                return this[index];
        }

        private void Sort_Small<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T>
        {
            var count = _Count;
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
            } else if (_Count == 3) {
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

            FlushValueOrIndex(ref v1, 0, ref Item1, indices);
            FlushValueOrIndex(ref v2, 1, ref Item2, indices);
            if (count > 2)
                FlushValueOrIndex(ref v3, 2, ref Item3, indices);
            if (count > 3)
                FlushValueOrIndex(ref v4, 3, ref Item4, indices);
        }

        private void Sort_Large<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T> 
        {
            if (indices != null)
                _Items.IndexedSortRef(comparer, indices);
            else
                _Items.FastCLRSortRef(comparer);
        }

        /// <summary>
        /// Performs an in-place sort of the DenseList.
        /// NOTE: If the list is small this method may sort the values instead of the indices.
        /// </summary>
        /// <param name="indices">The element indices to use for sorting.</param>
        [TargetedPatchingOptOut("")]
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
        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sort<TComparer> (int offset, int count, TComparer comparer)
            where TComparer : IRefComparer<T>
        {
            if ((offset == 0) && (count == Count)) {
                Sort(comparer, null);
                return;
            }

            if (!HasList)
                EnsureList();

            _Items.FastCLRSortRef(comparer, offset, count);
        }

        /// <summary>
        /// Performs an in-place sort of the DenseList.
        /// NOTE: If the list is small this method may sort the values instead of the indices.
        /// </summary>
        /// <param name="indices">The element indices to use for sorting.</param>
        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SortNonRef (IComparer<T> comparer, int[] indices = null) {
            var wrapped = new RefComparerAdapter<IComparer<T>, T>(comparer);
            Sort(wrapped, indices);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SortNonRef (int offset, int count, IComparer<T> comparer) {
            var wrapped = new RefComparerAdapter<IComparer<T>, T>(comparer);
            Sort(offset, count, wrapped);
        }

        /// <summary>
        /// Frees any heap resources used by the list and zeroes its count. *Does not* clear internal storage,
        ///  so it can still leak heap references and keep objects alive (call Clear first to do that.)
        /// </summary>
        public void Dispose () {
            _Count = 0;
            if (ListPool != null)
                ListPool.Release(ref _Items);
            else
                _Items = null;
        }

        public DenseQuery<T, Enumerator, U> Cast<U> () {
            if (Count == 0)
                return default;

            var e = GetEnumerator();
            return new DenseQuery<T, Enumerator, U>(in e, DenseQuery<T, Enumerator, U>.CastSelector, false);
        }

        public Enumerator GetEnumerator () {
            return new Enumerator(in this);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            if (Count == 0)
                return BoxedEmptyEnumerator.Instance;
            return new Enumerator(in this);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            if (Count == 0)
                return BoxedEmptyEnumerator.Instance;
            return new Enumerator(in this);
        }

        int IList<T>.IndexOf (T item) {
            return IndexOf(item);
        }

        void IList<T>.Insert (int index, T item) {
            Insert(index, ref item);
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
            return IndexOf(in item) >= 0;
        }

        void ICollection<T>.CopyTo (T[] array, int arrayIndex) {
            for (int i = 0, c = Math.Min(array.Length - arrayIndex, Count); i < c; i++)
                GetItem(i, out array[arrayIndex + i]);
        }

        bool ICollection<T>.Remove (T item) {
            var index = IndexOf(in item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }
    }
}

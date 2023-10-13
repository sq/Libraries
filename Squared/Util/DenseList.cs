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
    public interface IListPool<T> {
        UnorderedList<T> Allocate (int? capacity, bool capacityIsHint);
        void Release (ref UnorderedList<T> items);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public partial struct DenseList<T> : IDisposable, IEnumerable<T>, IList<T>, IOrderedEnumerable<T> {
#if !NOSPAN
        public static class ElementTraits {
            public static uint ListSize = ComputeListSize();

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static unsafe uint ComputeListSize () {
                var temp = new DenseList<T>();

                // Because this struct contains reference types, the runtime seems to be free to
                //  order the fields any way it likes. So Item1-4 may be at the front, for example
                // It does seem to maintain internal order for properties of the same type, at least.
                ulong p1 = (ulong)(byte*)Unsafe.AsPointer(ref temp._Count),
                    p2 = (ulong)(byte*)Unsafe.AsPointer(ref temp.Item1),
                    p3 = (ulong)(byte*)Unsafe.AsPointer(ref temp.Item2),
                    p4 = (ulong)(byte*)Unsafe.AsPointer(ref temp.Item4),
                    p5 = (ulong)(byte*)Unsafe.AsPointer(ref temp._Items);

                if ((p4 < p3) || (p3 < p2))
                    throw new Exception("Incoherent struct layout");

                var pMin = Math.Min(Math.Min(Math.Min(Math.Min(p1, p2), p3), p4), p5);
                var pMax = Math.Max(Math.Max(Math.Max(Math.Max(p1, p2), p3), p4), p5);
                return (uint)((pMax - pMin + (p3 - p2)));
            }
        }
#endif

        // Since our max capacity is 4 items anyway, we might as well use a smaller value type so that
        //  our struct is smaller. We put these at the front of the struct so that their alignment/packing
        //  isn't impacted by the size of T
        internal short _Count, _ListCapacity;
        // Then put these two reference types next to each other because we know they're 4 or 8 bytes each
        internal UnorderedList<T> _Items;
        private  object _ListPoolOrAllocator;
        // Finally put all the items at the end since they could be any size and pack weirdly
        internal T Item1, Item2, Item3, Item4;

        public object ListPoolOrAllocator {
            get => _ListPoolOrAllocator;
            set {
                if ((_ListPoolOrAllocator != null) && (Count > 0) && (_ListPoolOrAllocator != value))
                    throw new InvalidOperationException("A list pool or allocator is already set and this list is not empty");
                _ListPoolOrAllocator = value;
            }
        }
        public short? ListCapacity {
            get => _ListCapacity > 0 ? _ListCapacity : (short?)null;
            set {
                _ListCapacity = value ?? 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe T InlineItemAtIndexOrDefault (int index) {
#if !NOSPAN
            // FIXME: This is a bit slower than the switch version for some reason?
            if ((index < 0) || (index >= _Count))
                return default;

            return Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index));
#else
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
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void GetInlineItemAtIndex (int index, out T result) {
#if !NOSPAN
            // FIXME: This is a bit slower than the switch version for some reason?
            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();
            result = Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index));
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
            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();
            Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index)) = value;
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
            : this (items, 0, items?.Length ?? 0) {
        }

        public DenseList (T[] items, int offset, int count) : this() {
            if (items != null)
                AddRange(items, offset, count);
        }

        public DenseList (IEnumerable<T> items) : this() {
            if (items != null)
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

        public void CopyTo (IList<T> destination) {
            // FIXME: Optimize this
            for (int i = 0, c = Count; i < c; i++)
                destination.Add(this[i]);
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
                _ListCapacity = (short)Math.Max(Math.Min(capacity, short.MaxValue), _ListCapacity);
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
                capacity = Math.Max((int)_ListCapacity, absoluteMinimum);
            else
                ;

            UnorderedList<T> items;
            if (_ListPoolOrAllocator is IListPool<T> lp)
                items = lp.Allocate(capacity, false);
            else if (capacity.HasValue)
                items = new UnorderedList<T>(capacity.Value, (UnorderedList<T>.Allocator)_ListPoolOrAllocator);
            else
                items = new UnorderedList<T>((UnorderedList<T>.Allocator)_ListPoolOrAllocator);

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
        public unsafe void GetItem (int index, out T result) {
            var items = _Items;
            if (items != null) {
                items.DangerousGetItem(index, out result);
                return;
            }

            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();

#if !NOSPAN
            result = Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index));
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

            if ((index < 0) || (index >= _Count)) {
                result = default(T);
                return false;
            }

            GetInlineItemAtIndex(index, out result);
            return true;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ItemOrDefault (int index) {
            var items = _Items;
            if (items != null)
                return items.DangerousItemOrDefault(index);

            return InlineItemAtIndexOrDefault(index);
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

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int BinarySearchNonRef (in T value) {
            return BinarySearchNonRef(in value, Comparer<T>.Default);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int BinarySearchNonRef<TComparer> (in T value, TComparer comparer)
            where TComparer : IComparer<T>
        {
            int low = 0, high = Count - 1;
            while (low <= high) {
                int i = (int)(((uint)high + (uint)low) >> 1);
                int c = comparer.Compare(value, Ext.Item(ref this, i));
                if (c == 0)
                    return i;
                else if (c > 0)
                    low = i + 1;
                else
                    high = i - 1;
            }
            return ~low;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int BinarySearch<TComparer> (ref T value, TComparer comparer)
            where TComparer : IRefComparer<T>
        {
            int low = 0, high = Count - 1;
            while (low <= high) {
                int i = (int)(((uint)high + (uint)low) >> 1);
                int c = comparer.Compare(ref value, ref Ext.Item(ref this, i));
                if (c == 0)
                    return i;
                else if (c > 0)
                    low = i + 1;
                else
                    high = i - 1;
            }
            return ~low;
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
                return Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index));
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
        public unsafe void ClearItem (int index) {
            var items = _Items;
            if (items != null) {
                items.DangerousClearItem(index);
                return;
            }

            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();

#if !NOSPAN
            Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index)) = default;
#else
            switch (index) {
                default:
                case 0:
                    Item1 = default;
                    return;
                case 1:
                    Item2 = default;
                    return;
                case 2:
                    Item3 = default;
                    return;
                case 3:
                    Item4 = default;
                    return;
            }
#endif
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetItem (int index, T value) {
            SetItem(index, ref value);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void SetItem (int index, ref T value) {
            var items = _Items;
            if (items != null) {
                items.DangerousSetItem(index, ref value);
                return;
            }

            if ((index < 0) || (index >= _Count))
                EnumerableExtensions.BoundsCheckFailed();

#if !NOSPAN
            Unsafe.AddByteOffset(ref Item1, (IntPtr)(((byte*)Unsafe.AsPointer(ref Item2) - (byte*)Unsafe.AsPointer(ref Item1)) * index)) = value;
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

        public UnorderedList<T> GetStorage (bool ensureList) {
            if (ensureList)
                EnsureList();
            return _Items;
        }

        public void UseExistingStorage (UnorderedList<T> storage) {
            if (storage == null)
                return;
            if (_Items == storage)
                return;

            var oldItems = _Items;
            _Items = storage;
            storage.Clear();
            if (oldItems != null)
                oldItems.CopyTo(storage);
            else if (_Count >= 4) {
                storage.Add(Item1);
                storage.Add(Item2);
                storage.Add(Item3);
                storage.Add(Item4);
            } else if (_Count >= 3) {
                storage.Add(Item1);
                storage.Add(Item2);
                storage.Add(Item3);
            } else if (_Count >= 2) {
                storage.Add(Item1);
                storage.Add(Item2);
            } else if (_Count >= 1) {
                storage.Add(Item1);
            }

            oldItems?.Clear();
            if (ListPool != null)
                ListPool.Release(ref oldItems);
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

            var count = list._Count++;
            list.SetItem(count, ref item);
        }

        public void ReplaceWith (ref DenseList<T> newItems) {
            var count = Count;
            for (int i = 0, c = newItems.Count; i < c; i++) {
                ref var item = ref newItems.Item(i);
                if (i >= count)
                    Add(ref item);
                else
                    SetItem(i, ref item);
            }
            var toRemove = Count - newItems.Count;
            if (toRemove > 0)
                RemoveRange(newItems.Count, toRemove);

#if DEBUG
            if (Count != newItems.Count)
                throw new Exception();
#endif
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T UnsafeCreateSlotWithKnownCapacity (ref DenseList<T> list) {
            var items = list._Items;
            if (items != null)
                return ref items.CreateSlot();

            var count = list._Count++;
            return ref list.Item(count);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (T item) {
            if ((_Count >= 4) || (_Items != null)) {
                Add_Slow(ref item);
            } else {
                SetInlineItemAtIndex(_Count++, ref item);
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (ref T item) {
            if ((_Count >= 4) || (_Items != null)) {
                Add_Slow(ref item);
            } else {
                SetInlineItemAtIndex(_Count++, ref item);
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
            // Fast paths to avoid allocating/boxing enumerator instances
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
            if (HasList) {
                _Items.DangerousRemoveRange(index, count);
            } else {
                for (int i = index + count - 1; i >= index; i--)
                    RemoveAt(i);
            }
        }

        public void RemoveTail (int count) {
            if (count == 0)
                return;
            if (count > Count)
                throw new ArgumentException("count");

            if (!HasList) {
                _Count -= (short)count;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OverwriteWith (ref DenseList<T> source, bool unsafeFastClear) {
            if (source.Count == 0) {
                if (unsafeFastClear)
                    UnsafeFastClear();
                else
                    Clear();
                return;
            }

            if (source.HasList) {
                if (unsafeFastClear)
                    UnsafeFastClear();
                else
                    Clear();
                source.CopyTo(ref this);
            } else
                this = source;
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
                _Count = (short)count;
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
            if (Count == 0)
                return EmptyArray.Value;

            else if (HasList)
                return _Items.ToArray();
            else {
                var result = new T[_Count];
                for (int i = 0; i < result.Length; i++)
                    GetInlineItemAtIndex(i, out result[i]);
                return result;
            }
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

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Find<TUserData> (Predicate<TUserData> predicate, in TUserData userData) {
            var index = IndexOf(predicate, in userData);
            if (index < 0)
                return default;
            else
                return this[index];
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SortPair<TComparer> (TComparer comparer, ref T item1, ref T item2)
            where TComparer : IRefComparer<T>
        {
            if (comparer.Compare(ref item2, ref item1) >= 0)
                return;
            var temp = item1;
            item1 = item2;
            item2 = temp;
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SortPair_Indexed<TComparer> (TComparer comparer, ref int index1, ref int index2)
            where TComparer : IRefComparer<T>
        {
            if (comparer.Compare(ref this.Item(index2), ref this.Item(index1)) >= 0)
                return;
            var temp = index1;
            index1 = index2;
            index2 = temp;
        }

        private void Sort_Small<TComparer> (TComparer comparer, int[] indices)
            where TComparer : IRefComparer<T>
        {
            var count = _Count;
            if (count <= 1)
                return;

            if ((indices != null) && (indices.Length < count))
                throw new ArgumentOutOfRangeException("indices", "index array length must must match or exceed number of elements");

            // Use a sorting network to sort either the values or indices in-place
            if (count == 2) {
                if (indices != null)
                    SortPair_Indexed(comparer, ref indices[0], ref indices[1]);
                else
                    SortPair(comparer, ref Item1, ref Item2);
            } else if (count == 3) {
                if (indices != null) {
                    SortPair_Indexed(comparer, ref indices[0], ref indices[1]);
                    SortPair_Indexed(comparer, ref indices[0], ref indices[2]);
                    SortPair_Indexed(comparer, ref indices[1], ref indices[2]);
                } else {
                    SortPair(comparer, ref Item1, ref Item2);
                    SortPair(comparer, ref Item1, ref Item3);
                    SortPair(comparer, ref Item2, ref Item3);
                }
            } else {
                if (indices != null) {
                    SortPair_Indexed(comparer, ref indices[0], ref indices[1]);
                    SortPair_Indexed(comparer, ref indices[2], ref indices[3]);
                    SortPair_Indexed(comparer, ref indices[0], ref indices[2]);
                    SortPair_Indexed(comparer, ref indices[1], ref indices[3]);
                    SortPair_Indexed(comparer, ref indices[1], ref indices[2]);
                } else {
                    SortPair(comparer, ref Item1, ref Item2);
                    SortPair(comparer, ref Item3, ref Item4);
                    SortPair(comparer, ref Item1, ref Item3);
                    SortPair(comparer, ref Item2, ref Item4);
                    SortPair(comparer, ref Item2, ref Item3);
                }
            }
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
        public void SortNonRef<TComparer> (TComparer comparer, int[] indices = null)
            where TComparer : IComparer<T>
        {
            var wrapped = new RefComparerAdapter<TComparer, T>(comparer);
            Sort(wrapped, indices);
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SortNonRef<TComparer> (int offset, int count, TComparer comparer)
            where TComparer : IComparer<T>
        {
            var wrapped = new RefComparerAdapter<TComparer, T>(comparer);
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
            var result = new Enumerator {
                Index = -1,
                Count = Count,
            };
            if (_Items != null) {
                var buf = _Items.GetBuffer();
                result.Offset = buf.Offset;
                result.Items = buf.Array;
            } else {
                result.Item1 = Item1;
                result.Item2 = Item2;
                result.Item3 = Item3;
                result.Item4 = Item4;
            }
            return result;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            if (Count == 0)
                return BoxedEmptyEnumerator.Instance;
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            if (Count == 0)
                return BoxedEmptyEnumerator.Instance;
            return GetEnumerator();
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

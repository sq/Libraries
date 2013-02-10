using System;
using System.Collections.Generic;
using SectorIndex = Squared.Util.Pair<int>;
using Squared.Util;
using Microsoft.Xna.Framework;

namespace Squared.Game {
    public interface ISpatialCollectionChild {
        void AddedToCollection (WeakReference collection);
        void RemovedFromCollection (WeakReference collection);
    }

    public class IntPairComparer : IEqualityComparer<Pair<int>> {
        public bool Equals (Pair<int> x, Pair<int> y) {
            return (x.First == y.First) && (x.Second == y.Second);
        }

        public int GetHashCode (Pair<int> obj) {
            return obj.First + (obj.Second << 16);
        }
    }
    
    public class SpatialCollection<T> : IEnumerable<T>
        where T : class, IHasBounds {

        private WeakReference WeakSelf;

        public class Sector : HashSet<ItemInfo> {
            public SectorIndex Index;

            public Sector (IEqualityComparer<ItemInfo> comparer)
                : base(comparer) {
            }

            public Sector (SectorIndex index, IEqualityComparer<ItemInfo> comparer) 
                : base (comparer) {
                Index = index;
            }

            public IEnumerable<T> Items {
                get {
                    foreach (var itemInfo in this)
                        yield return itemInfo.Item;
                }
            }
        }

        public struct GetSectorsFromBoundsEnumerator : IEnumerator<Sector> {
            SectorIndex tl, br, item;
            Sector current;
            int x, y;
            SpatialCollection<T> collection;
            bool create;

            internal GetSectorsFromBoundsEnumerator (SpatialCollection<T> collection, SectorIndex tl_, SectorIndex br_, bool create) {
                tl = tl_;
                br = br_;
                item = new SectorIndex();
                this.create = create;
                this.collection = collection;
                x = tl_.First - 1;
                y = tl_.Second;
                current = null;
            }

            internal GetSectorsFromBoundsEnumerator (SpatialCollection<T> collection, Bounds bounds, bool create) 
                : this (collection, collection.GetIndexFromPoint(bounds.TopLeft), collection.GetIndexFromPoint(bounds.BottomRight), create) {
            }

            public Sector Current {
                get { return current; }
            }

            public void Dispose () {
            }

            object System.Collections.IEnumerator.Current {
                get { return current; }
            }

            public bool GetNext (out Sector value) {
                var result = MoveNext();
                value = current;
                return result;
            }

            public bool MoveNext () {
                current = null;

                while (current == null) {
                    x += 1;
                    if (x > br.First) {
                        x = tl.First;
                        y += 1;
                        if (y > br.Second) {
                            current = null;
                            return false;
                        }
                    }

                    item.First = x;
                    item.Second = y;

                    if (create)
                        current = collection.GetSectorFromIndex(item);
                    else
                        collection._Sectors.TryGetValue(item, out current);
                }

                return true;
            }

            public void Reset () {
                current = null;
                x = tl.First - 1;
                y = tl.Second;
            }
        }

        public struct ItemBoundsEnumerator : IEnumerator<ItemInfo>, IEnumerable<ItemInfo> {
            GetSectorsFromBoundsEnumerator _Sectors;
            Sector _Sector;
            HashSet<ItemInfo>.Enumerator _SectorEnumerator;
            ItemInfo _Current;
            Dictionary<ItemInfo, bool> _SeenList;
            readonly SpatialCollection<T> _Collection;
            readonly bool _AllowDuplicates;

            public ItemBoundsEnumerator (SpatialCollection<T> collection, Bounds bounds, bool allowDuplicates) {
                _Collection = collection;
                _Sectors = new SpatialCollection<T>.GetSectorsFromBoundsEnumerator(collection, bounds, false);
                _Sector = null;
                _Current = null;
                _SectorEnumerator = default(HashSet<ItemInfo>.Enumerator);
                _AllowDuplicates = allowDuplicates;
                if (!allowDuplicates)
                    _SeenList = collection.GetSeenList();
                else
                    _SeenList = null;
            }

            public ItemBoundsEnumerator (SpatialCollection<T> collection, SectorIndex tl, SectorIndex br, bool allowDuplicates) {
                _Collection = collection;
                _Sectors = new SpatialCollection<T>.GetSectorsFromBoundsEnumerator(collection, tl, br, false);
                _Sector = null;
                _Current = null;
                _SectorEnumerator = default(HashSet<ItemInfo>.Enumerator);
                _AllowDuplicates = allowDuplicates;
                if (!allowDuplicates)
                    _SeenList = collection.GetSeenList();
                else
                    _SeenList = null;
            }

            public BufferPool<ItemInfo>.Buffer GetAsBuffer (out int count) {
                var result = BufferPool<ItemInfo>.Allocate(_Collection.Count);

                int i = 0;
                while (MoveNext())
                    result.Data[i++] = _Current;

                count = i;

                return result;
            }

            public ItemInfo Current {
                get { return _Current; }
            }

            public void Dispose () {
                _Collection.DisposeSeenList(ref _SeenList);
                _Sectors.Dispose();
                _Sector = null;
            }

            object System.Collections.IEnumerator.Current {
                get { return _Current; }
            }

            public bool GetNext (out ItemInfo value) {
                var result = MoveNext();
                value = _Current;
                return result;
            }

            public bool MoveNext () {
                _Current = null;
                while (_Current == null) {
                    while (_Sector == null) {
                        if (_Sectors.GetNext(out _Sector)) {
                            if (_Sector != null) {
                                _SectorEnumerator = _Sector.GetEnumerator();

                                if (!_SectorEnumerator.MoveNext())
                                    _Sector = null;
                            }
                        } else {
                            return false;
                        }
                    }

                    _Current = _SectorEnumerator.Current;

                    if (!_AllowDuplicates) {
                        int oldCount = _SeenList.Count;
                        _SeenList[_Current] = true;

                        if (oldCount == _SeenList.Count)
                            _Current = null;
                    }

                    if (!_SectorEnumerator.MoveNext())
                        _Sector = null;
                }

                return true;
            }

            public void Reset () {
                _Sectors.Reset();
                _Sector = null;
                _SectorEnumerator = default(HashSet<ItemInfo>.Enumerator);

                if (!_AllowDuplicates)
                    _SeenList.Clear();
            }

            public IEnumerator<ItemInfo> GetEnumerator () {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
                return this;
            }
        }

        public class ItemInfo : IHasBounds {
            public T Item;
            public Bounds Bounds;
            internal SectorIndex TopLeft, BottomRight;
            internal int HashCode;

            internal ItemInfo (T item, SpatialCollection<T> parent) {
                Item = item;
                Bounds = item.Bounds;
                TopLeft = parent.GetIndexFromPoint(Bounds.TopLeft);
                BottomRight = parent.GetIndexFromPoint(Bounds.BottomRight);
                HashCode = item.GetHashCode();
            }

            Bounds IHasBounds.Bounds {
                get { return Bounds; }
            }
        }

        internal class ItemInfoComparer : IEqualityComparer<ItemInfo> {
            public bool Equals (ItemInfo x, ItemInfo y) {
                return (x == y);
            }

            public int GetHashCode (ItemInfo obj) {
                return (obj.HashCode);
            }
        }

        public const float DefaultSubdivision = 512.0f;
        public const int InitialFreeListSize = 8;
        public const int MaxFreeListSize = 32;

        internal ItemInfoComparer _ItemInfoComparer = new ItemInfoComparer();
        internal float _Subdivision;
        internal Dictionary<T, ItemInfo> _Items = new Dictionary<T, ItemInfo>(new ReferenceComparer<T>());
        internal Dictionary<SectorIndex, Sector> _Sectors;
        internal List<Sector> _FreeList = new List<Sector>();
        internal Dictionary<ItemInfo, bool>[] _SeenListCache = new Dictionary<ItemInfo, bool>[4];
        internal int _NumCachedSeenLists = 4;

        public SpatialCollection ()
            : this(DefaultSubdivision) {
        }

        public SpatialCollection (float subdivision) {
            _Subdivision = subdivision;
            _Sectors = new Dictionary<Squared.Util.Pair<int>, Sector>(new IntPairComparer());

            for (int i = 0; i < InitialFreeListSize; i++)
                _FreeList.Add(new Sector(_ItemInfoComparer));

            for (int i = 0; i < _NumCachedSeenLists; i++)
                _SeenListCache[i] = new Dictionary<ItemInfo, bool>(_ItemInfoComparer);
        }

        public Sector this[SectorIndex sectorIndex] {
            get {
                return _Sectors[sectorIndex];
            }
        }

        public Bounds Extent {
            get;
            private set;
        }

        public void CropBounds (ref Bounds bounds) {
            var extent = Extent;
            bounds.TopLeft = new Vector2(
                Math.Max(bounds.TopLeft.X, extent.TopLeft.X),
                Math.Max(bounds.TopLeft.Y, extent.TopLeft.Y)
            );
            bounds.BottomRight = new Vector2(
                Math.Min(bounds.BottomRight.X, extent.BottomRight.X),
                Math.Min(bounds.BottomRight.Y, extent.BottomRight.Y)
            );
        }

        public SectorIndex GetIndexFromPoint (Vector2 point) {
            return new SectorIndex((int)Math.Floor(point.X / _Subdivision), (int)Math.Floor(point.Y / _Subdivision));
        }

        internal Sector GetSectorFromIndex (SectorIndex index) {
            Sector sector = null;

            if (!_Sectors.TryGetValue(index, out sector)) {
                if (_FreeList.Count == 0) {
                    sector = new Sector(index, _ItemInfoComparer);
                } else {
                    sector = _FreeList[_FreeList.Count - 1];
                    _FreeList.RemoveAt(_FreeList.Count - 1);
                    sector.Index = index;
                }

                var sectorTopLeft = new Vector2(
                    index.First * _Subdivision,
                    index.Second * _Subdivision
                );
                var sectorBottomRight = new Vector2(
                    sectorTopLeft.X + _Subdivision,
                    sectorTopLeft.Y + _Subdivision
                );

                if (_Sectors.Count == 0) {
                    Extent = new Bounds(sectorTopLeft, sectorBottomRight);
                } else {
                    var currentExtent = Extent;
                    Extent = new Bounds(
                        new Vector2(
                            Math.Min(currentExtent.TopLeft.X, sectorTopLeft.X),
                            Math.Min(currentExtent.TopLeft.Y, sectorTopLeft.Y)
                        ),
                        new Vector2(
                            Math.Max(currentExtent.BottomRight.X, sectorBottomRight.X),
                            Math.Max(currentExtent.BottomRight.Y, sectorBottomRight.Y)
                        )
                    );
                }

                _Sectors[index] = sector;
            }

            return sector;
        }

        private WeakReference GetWeakSelf () {
            if (WeakSelf == null)
                WeakSelf = new WeakReference(this, false);

            return WeakSelf;
        }

        public void Add (T item) {
            var info = new ItemInfo(item, this);
            _Items.Add(item, info);

            using (var e = new GetSectorsFromBoundsEnumerator(this, info.Bounds, true))
            while (e.MoveNext())
                e.Current.Add(info);

            var ichild = item as ISpatialCollectionChild;
            if (ichild != null)
                ichild.AddedToCollection(GetWeakSelf());
        }

        public void AddRange (IEnumerable<T> items) {
            foreach (var item in items)
                Add(item);
        }

        internal bool InternalRemove (ItemInfo item, SectorIndex topLeft, SectorIndex bottomRight, bool notifyRemoval) {
            bool removed = false;

            using (var e = new GetSectorsFromBoundsEnumerator(this, topLeft, bottomRight, false))
            while (e.MoveNext()) {
                var sector = e.Current;
                removed |= sector.Remove(item);

                if (sector.Count == 0) {
                    if (_FreeList.Count < MaxFreeListSize)
                        _FreeList.Add(sector);

                    _Sectors.Remove(sector.Index);
                }
            }

            if (notifyRemoval) {
                var ichild = item.Item as ISpatialCollectionChild;
                if (ichild != null)
                    ichild.RemovedFromCollection(GetWeakSelf());
            }

            return removed;
        }

        public bool Remove (T item) {
            ItemInfo info;
            if (_Items.TryGetValue(item, out info)) {
                InternalRemove(info, info.TopLeft, info.BottomRight, true);
                _Items.Remove(item);
                return true;
            } else {
                return false;
            }
        }

        public void RemoveMany (T[] items) {
            foreach (var item in items)
                Remove(item);
        }

        public bool Contains (T item) {
            return _Items.ContainsKey(item);
        }

        public void Clear () {
            foreach (var ii in _Items.Values) {
                var ichild = ii.Item as ISpatialCollectionChild;
                if (ichild != null)
                    ichild.RemovedFromCollection(GetWeakSelf());
            }

            _Items.Clear();
            _Sectors.Clear();
            _FreeList.Clear();
            Extent = new Bounds(Vector2.Zero, Vector2.Zero);
        }

        public void UpdateItemBounds (T item) {
            ItemInfo info;
            if (_Items.TryGetValue(item, out info)) {
                var oldBounds = info.Bounds;
                var oldTopLeft = info.TopLeft;
                var oldBottomRight = info.BottomRight;
                info.Bounds = item.Bounds;
                info.TopLeft = GetIndexFromPoint(info.Bounds.TopLeft);
                info.BottomRight = GetIndexFromPoint(info.Bounds.BottomRight);

                if ((oldTopLeft.First == info.TopLeft.First) &&
                    (oldTopLeft.Second == info.TopLeft.Second) && 
                    (oldBottomRight.First == info.BottomRight.First) &&
                    (oldBottomRight.Second == info.BottomRight.Second))
                    return;

                InternalRemove(info, oldTopLeft, oldBottomRight, false);

                using (var e = new GetSectorsFromBoundsEnumerator(this, info.TopLeft, info.BottomRight, true))
                while (e.MoveNext())
                    e.Current.Add(info);
            }
        }

        public ItemBoundsEnumerator GetItemsFromSectors (SectorIndex tl, SectorIndex br) {
            return new ItemBoundsEnumerator(this, tl, br, false);
        }

        public ItemBoundsEnumerator GetItemsFromBounds (Bounds bounds) {
            CropBounds(ref bounds);

            return new ItemBoundsEnumerator(this, bounds, false);
        }

        public ItemBoundsEnumerator GetItemsFromBounds (Bounds bounds, bool allowDuplicates) {
            CropBounds(ref bounds);

            return new ItemBoundsEnumerator(this, bounds, allowDuplicates);
        }

        public GetSectorsFromBoundsEnumerator GetSectorsFromBounds (Bounds bounds) {
            return new GetSectorsFromBoundsEnumerator(this, bounds, false);
        }

        public int Count {
            get {
                return _Items.Count;
            }
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator () {
            return ((IEnumerable<T>)(_Items.Keys)).GetEnumerator();
        }

        public Dictionary<T, ItemInfo>.KeyCollection.Enumerator GetEnumerator () {
            return _Items.Keys.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return _Items.Keys.GetEnumerator();
        }

        internal Dictionary<SpatialCollection<T>.ItemInfo, bool> GetSeenList () {
            if (_NumCachedSeenLists > 0) {
                _NumCachedSeenLists -= 1;
                var result = _SeenListCache[_NumCachedSeenLists];
                _SeenListCache[_NumCachedSeenLists] = null;
                return result;
            } else {
                return new Dictionary<SpatialCollection<T>.ItemInfo, bool>(new ItemInfoComparer());
            }
        }

        internal void DisposeSeenList (ref Dictionary<SpatialCollection<T>.ItemInfo, bool> seenList) {
            if (seenList == null)
                return;

            if (_NumCachedSeenLists < _SeenListCache.Length) {
                seenList.Clear();
                _SeenListCache[_NumCachedSeenLists] = seenList;
                _NumCachedSeenLists += 1;
            }

            seenList = null;
        }
    }
}

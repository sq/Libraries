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

    public interface ISpatialPartitionSector {
    }

    public class SpatialPartition<TSector> : IEnumerable<TSector>
        where TSector : class, ISpatialPartitionSector
    {
        internal readonly Func<SectorIndex, TSector> _SectorCreator;
        internal readonly float _Subdivision;
        internal readonly Dictionary<SectorIndex, TSector> _Sectors;

        public struct GetSectorsFromBoundsEnumerator : IEnumerator<TSector> {
            SectorIndex tl, br, item;
            TSector current;
            int x, y;
            SpatialPartition<TSector> sectors;
            bool create;

            internal GetSectorsFromBoundsEnumerator (SpatialPartition<TSector> sectors, SectorIndex tl_, SectorIndex br_, bool create) {
                tl = tl_;
                br = br_;
                item = new SectorIndex();
                this.create = create;
                this.sectors = sectors;
                x = tl_.First - 1;
                y = tl_.Second;
                current = null;
            }

            internal GetSectorsFromBoundsEnumerator (SpatialPartition<TSector> sectors, Bounds bounds, bool create)
                : this(sectors, sectors.GetIndexFromPoint(bounds.TopLeft), sectors.GetIndexFromPoint(bounds.BottomRight), create) {
            }

            public TSector Current {
                get { return current; }
            }

            public void Dispose () {
            }

            object System.Collections.IEnumerator.Current {
                get { return current; }
            }

            public bool GetNext (out TSector value) {
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

                    current = sectors.GetSectorFromIndex(item, create);
                }

                return true;
            }

            public void Reset () {
                current = null;
                x = tl.First - 1;
                y = tl.Second;
            }
        }

        public SpatialPartition (float subdivision, Func<SectorIndex, TSector> sectorCreator) {
            _Subdivision = subdivision;
            _Sectors = new Dictionary<Pair<int>, TSector>(new IntPairComparer());
            _SectorCreator = sectorCreator;
        }

        public TSector this[SectorIndex sectorIndex] {
            get {
                return _Sectors[sectorIndex];
            }
        }

        public SectorIndex GetIndexFromPoint (Vector2 point) {
            return new SectorIndex((int)Math.Floor(point.X / _Subdivision), (int)Math.Floor(point.Y / _Subdivision));
        }

        public TSector GetSectorFromIndex (SectorIndex index, bool create) {
            TSector sector = null;

            if (!_Sectors.TryGetValue(index, out sector)) {
                if (!create)
                    return null;

                sector = _SectorCreator(index);
                var sectorBounds = GetSectorBounds(index);

                if (_Sectors.Count == 0) {
                    Extent = sectorBounds;
                } else {
                    var currentExtent = Extent;
                    Extent = new Bounds(
                        new Vector2(
                            Math.Min(currentExtent.TopLeft.X, sectorBounds.TopLeft.X),
                            Math.Min(currentExtent.TopLeft.Y, sectorBounds.TopLeft.Y)
                        ),
                        new Vector2(
                            Math.Max(currentExtent.BottomRight.X, sectorBounds.BottomRight.X),
                            Math.Max(currentExtent.BottomRight.Y, sectorBounds.BottomRight.Y)
                        )
                    );
                }

                _Sectors[index] = sector;
            }

            return sector;
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

        public GetSectorsFromBoundsEnumerator GetSectorsFromBounds (Bounds bounds, bool create) {
            return new GetSectorsFromBoundsEnumerator(this, bounds, create);
        }

        public GetSectorsFromBoundsEnumerator GetSectorsFromBounds (SectorIndex topLeft, SectorIndex bottomRight, bool create) {
            return new GetSectorsFromBoundsEnumerator(this, topLeft, bottomRight, create);
        }

        public IEnumerator<TSector> GetEnumerator () {
            return _Sectors.Values.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return _Sectors.Values.GetEnumerator();
        }

        public void Clear () {
            _Sectors.Clear();
            Extent = new Bounds(Vector2.Zero, Vector2.Zero);
        }

        public void RemoveAt (SectorIndex index) {
            _Sectors.Remove(index);
        }

        public Bounds GetSectorBounds (SectorIndex index) {
            var sectorTopLeft = new Vector2(
                index.First * _Subdivision,
                index.Second * _Subdivision
            );
            var sectorBottomRight = new Vector2(
                sectorTopLeft.X + _Subdivision,
                sectorTopLeft.Y + _Subdivision
            );

            return new Bounds(sectorTopLeft, sectorBottomRight);
        }
    }
    
    public class SpatialCollection<T> : IEnumerable<T>
        where T : class, IHasBounds {

        private WeakReference WeakSelf;

        public class Sector : HashSet<ItemInfo>, ISpatialPartitionSector {
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

        public struct ItemBoundsEnumerator : IEnumerator<ItemInfo>, IEnumerable<ItemInfo> {
            SpatialPartition<Sector>.GetSectorsFromBoundsEnumerator _Sectors;
            Sector _Sector;
            HashSet<ItemInfo>.Enumerator _SectorEnumerator;
            ItemInfo _Current;
            Dictionary<ItemInfo, bool> _SeenList;
            readonly SpatialCollection<T> _Collection;
            readonly bool _AllowDuplicates;

            public ItemBoundsEnumerator (SpatialCollection<T> collection, Bounds bounds, bool allowDuplicates) {
                _Collection = collection;
                _Sectors = new SpatialPartition<Sector>.GetSectorsFromBoundsEnumerator(collection._Partition, bounds, false);
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
                _Sectors = new SpatialPartition<Sector>.GetSectorsFromBoundsEnumerator(collection._Partition, tl, br, false);
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
                TopLeft = parent._Partition.GetIndexFromPoint(Bounds.TopLeft);
                BottomRight = parent._Partition.GetIndexFromPoint(Bounds.BottomRight);
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

        internal ItemInfoComparer _ItemInfoComparer = new ItemInfoComparer();
        internal SpatialPartition<Sector> _Partition;
        internal Dictionary<T, ItemInfo> _Items = new Dictionary<T, ItemInfo>(new ReferenceComparer<T>());
        internal Dictionary<ItemInfo, bool>[] _SeenListCache = new Dictionary<ItemInfo, bool>[4];
        internal int _NumCachedSeenLists = 4;

        public SpatialCollection ()
            : this(DefaultSubdivision) {
        }

        public SpatialCollection (float subdivision) {
            _Partition = new SpatialPartition<Sector>(subdivision, (index) => new Sector(index, _ItemInfoComparer));

            for (int i = 0; i < _NumCachedSeenLists; i++)
                _SeenListCache[i] = new Dictionary<ItemInfo, bool>(_ItemInfoComparer);
        }

        public Sector this[SectorIndex sectorIndex] {
            get {
                return _Partition[sectorIndex];
            }
        }

        private WeakReference GetWeakSelf () {
            if (WeakSelf == null)
                WeakSelf = new WeakReference(this, false);

            return WeakSelf;
        }

        public void Add (T item) {
            var info = new ItemInfo(item, this);
            _Items.Add(item, info);

            using (var e = _Partition.GetSectorsFromBounds(info.Bounds, true))
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

            using (var e = _Partition.GetSectorsFromBounds(topLeft, bottomRight, false))
            while (e.MoveNext()) {
                var sector = e.Current;
                removed |= sector.Remove(item);

                if (sector.Count == 0)
                    _Partition.RemoveAt(sector.Index);
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
            _Partition.Clear();
        }

        public void UpdateItemBounds (T item) {
            ItemInfo info;
            if (_Items.TryGetValue(item, out info)) {
                var oldBounds = info.Bounds;
                var oldTopLeft = info.TopLeft;
                var oldBottomRight = info.BottomRight;
                info.Bounds = item.Bounds;
                info.TopLeft = _Partition.GetIndexFromPoint(info.Bounds.TopLeft);
                info.BottomRight = _Partition.GetIndexFromPoint(info.Bounds.BottomRight);

                if ((oldTopLeft.First == info.TopLeft.First) &&
                    (oldTopLeft.Second == info.TopLeft.Second) && 
                    (oldBottomRight.First == info.BottomRight.First) &&
                    (oldBottomRight.Second == info.BottomRight.Second))
                    return;

                InternalRemove(info, oldTopLeft, oldBottomRight, false);

                using (var e = _Partition.GetSectorsFromBounds(info.TopLeft, info.BottomRight, true))
                while (e.MoveNext())
                    e.Current.Add(info);
            }
        }

        public ItemBoundsEnumerator GetItemsFromSectors (SectorIndex tl, SectorIndex br) {
            return new ItemBoundsEnumerator(this, tl, br, false);
        }

        public ItemBoundsEnumerator GetItemsFromBounds (Bounds bounds) {
            _Partition.CropBounds(ref bounds);

            return new ItemBoundsEnumerator(this, bounds, false);
        }

        public ItemBoundsEnumerator GetItemsFromBounds (Bounds bounds, bool allowDuplicates) {
            _Partition.CropBounds(ref bounds);

            return new ItemBoundsEnumerator(this, bounds, allowDuplicates);
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
            lock (_SeenListCache) {
                if (_NumCachedSeenLists > 0) {
                    _NumCachedSeenLists -= 1;
                    var result = _SeenListCache[_NumCachedSeenLists];
                    _SeenListCache[_NumCachedSeenLists] = null;
                    return result;
                } else {
                    return new Dictionary<SpatialCollection<T>.ItemInfo, bool>(new ItemInfoComparer());
                }
            }
        }

        internal void DisposeSeenList (ref Dictionary<SpatialCollection<T>.ItemInfo, bool> seenList) {
            lock (_SeenListCache) {
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

        public SpatialPartition<Sector>.GetSectorsFromBoundsEnumerator GetSectorsFromBounds (Bounds bounds) {
            return _Partition.GetSectorsFromBounds(bounds, false);
        }
    }
}

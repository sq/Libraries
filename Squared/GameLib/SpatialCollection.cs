using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SectorIndex = Squared.Util.Pair<int>;
using Microsoft.Xna.Framework;
using Squared.Util;
using System.Threading;

namespace Squared.Game {
    public class IntPairComparer : IEqualityComparer<Pair<int>> {
        public bool Equals (Pair<int> x, Pair<int> y) {
            return (x.First == y.First) && (x.Second == y.Second);
        }

        public int GetHashCode (Pair<int> obj) {
            return obj.First + (obj.Second << 16);
        }
    }

    internal class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class {

        public bool Equals (T x, T y) {
            return (x == y);
        }

        public int GetHashCode (T obj) {
            return obj.GetHashCode();
        }
    }
    
    public class SpatialCollection<T> : IEnumerable<T>
        where T : class, IHasBounds {

        internal class Sector : Dictionary<ItemInfo, bool> {
            public SectorIndex Index;

            public Sector ()
                : base() {
            }

            public Sector (SectorIndex index) 
                : base (new ReferenceComparer<ItemInfo>()) {
                Index = index;
            }
        }

        internal struct GetSectorsFromBounds : IEnumerator<Sector> {
            SectorIndex tl, br, item;
            Sector current;
            int x, y;
            SpatialCollection<T> collection;
            bool create;

            public GetSectorsFromBounds (SpatialCollection<T> collection, SectorIndex tl_, SectorIndex br_, bool create) {
                tl = tl_;
                br = br_;
                item = new SectorIndex();
                this.create = create;
                this.collection = collection;
                x = tl_.First - 1;
                y = tl_.Second;
                current = null;
            }

            public GetSectorsFromBounds (SpatialCollection<T> collection, Bounds bounds, bool create) 
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
            GetSectorsFromBounds _Sectors;
            Sector _Sector;
            Dictionary<ItemInfo, bool>.KeyCollection.Enumerator _SectorEnumerator;
            ItemInfo _Current;
            SpatialCollection<T> _Collection;
            Dictionary<ItemInfo, bool> _SeenList;

            public ItemBoundsEnumerator (SpatialCollection<T> collection, Bounds bounds) {
                _Collection = collection;
                _Sectors = new SpatialCollection<T>.GetSectorsFromBounds(collection, bounds, false);
                _Sector = null;
                _Current = null;
                _SectorEnumerator = default(Dictionary<ItemInfo, bool>.KeyCollection.Enumerator);
                _SeenList = collection.GetSeenList();
            }

            public ItemBoundsEnumerator (SpatialCollection<T> collection, SectorIndex tl, SectorIndex br) {
                _Collection = collection;
                _Sectors = new SpatialCollection<T>.GetSectorsFromBounds(collection, tl, br, false);
                _Sector = null;
                _Current = null;
                _SectorEnumerator = default(Dictionary<ItemInfo, bool>.KeyCollection.Enumerator);
                _SeenList = collection.GetSeenList();
            }

            public ItemInfo Current {
                get { return _Current; }
            }

            public void Dispose () {
                _Collection.DisposeSeenList(_SeenList);
                _Sectors.Dispose();
                _Sector = null;
                _SeenList = null;
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
                                _SectorEnumerator = _Sector.Keys.GetEnumerator();

                                if (!_SectorEnumerator.MoveNext())
                                    _Sector = null;
                            }
                        } else {
                            return false;
                        }
                    }

                    _Current = _SectorEnumerator.Current;

                    if (_SeenList.ContainsKey(_Current)) {
                        _Current = null;
                    } else {
                        _SeenList.Add(_Current, true);
                    }

                    if (!_SectorEnumerator.MoveNext())
                        _Sector = null;
                }

                return true;
            }

            public void Reset () {
                _Sectors.Reset();
                _Sector = null;
                _SectorEnumerator = default(Dictionary<ItemInfo, bool>.KeyCollection.Enumerator);
                _SeenList.Clear();
            }

            public IEnumerator<ItemInfo> GetEnumerator () {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
                return this;
            }
        }

        public class ItemInfo {
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

        internal float _Subdivision;
        internal Dictionary<T, ItemInfo> _Items = new Dictionary<T, ItemInfo>(new ReferenceComparer<T>());
        internal Dictionary<SectorIndex, Sector> _Sectors;
        internal List<Sector> _FreeList = new List<Sector>();
        internal Stack<Dictionary<ItemInfo, bool>> _SeenListCache = new Stack<Dictionary<ItemInfo, bool>>();

        public SpatialCollection ()
            : this(DefaultSubdivision) {
        }

        public SpatialCollection (float subdivision) {
            _Subdivision = subdivision;
            _Sectors = new Dictionary<Squared.Util.Pair<int>, Sector>(new IntPairComparer());

            for (int i = 0; i < InitialFreeListSize; i++)
                _FreeList.Add(new Sector());
        }

        public SectorIndex GetIndexFromPoint (Vector2 point) {
            return new SectorIndex((int)Math.Floor(point.X / _Subdivision), (int)Math.Floor(point.Y / _Subdivision));
        }

        internal Sector GetSectorFromIndex (SectorIndex index) {
            Sector sector = null;

            if (!_Sectors.TryGetValue(index, out sector)) {
                if (_FreeList.Count == 0) {
                    sector = new Sector(index);
                } else {
                    sector = _FreeList[_FreeList.Count - 1];
                    _FreeList.RemoveAt(_FreeList.Count - 1);
                    sector.Index = index;
                }

                _Sectors[index] = sector;
            }

            return sector;
        }

        public void Add (T item) {
            var info = new ItemInfo(item, this);
            _Items.Add(item, info);

            using (var e = new GetSectorsFromBounds(this, info.Bounds, true))
            while (e.MoveNext())
                e.Current.Add(info, false);
        }

        internal void InternalRemove (ItemInfo item, SectorIndex topLeft, SectorIndex bottomRight) {
            using (var e = new GetSectorsFromBounds(this, topLeft, bottomRight, false))
            while (e.MoveNext()) {
                var sector = e.Current;
                sector.Remove(item);

                if (sector.Count == 0) {
                    if (_FreeList.Count < MaxFreeListSize)
                        _FreeList.Add(sector);

                    _Sectors.Remove(sector.Index);
                }
            }
        }

        public bool Remove (T item) {
            ItemInfo info;
            if (_Items.TryGetValue(item, out info)) {
                InternalRemove(info, info.TopLeft, info.BottomRight);
                _Items.Remove(item);
                return true;
            } else {
                return false;
            }
        }

        public void Clear () {
            _Items.Clear();
            _Sectors.Clear();
            _FreeList.Clear();
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

                InternalRemove(info, oldTopLeft, oldBottomRight);

                using (var e = new GetSectorsFromBounds(this, info.TopLeft, info.BottomRight, true))
                while (e.MoveNext())
                    e.Current.Add(info, false);
            }
        }

        public ItemBoundsEnumerator GetItemsFromSectors (SectorIndex tl, SectorIndex br) {
            return new ItemBoundsEnumerator(this, tl, br);
        }

        public ItemBoundsEnumerator GetItemsFromBounds (Bounds bounds) {
            return new ItemBoundsEnumerator(this, bounds);
        }

        public int Count {
            get {
                return _Items.Count;
            }
        }

        public IEnumerator<T> GetEnumerator () {
            return ((IEnumerable<T>)(_Items.Keys)).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return _Items.Keys.GetEnumerator();
        }

        internal Dictionary<SpatialCollection<T>.ItemInfo, bool> GetSeenList () {
            lock (_SeenListCache) {
                if (_SeenListCache.Count > 0)
                    return _SeenListCache.Pop();
                else
                    return new Dictionary<SpatialCollection<T>.ItemInfo, bool>(new ItemInfoComparer());
            }
        }

        internal void DisposeSeenList (Dictionary<SpatialCollection<T>.ItemInfo, bool> _SeenList) {
            lock (_SeenListCache) {
                if (_SeenListCache.Count < 16) {
                    _SeenList.Clear();
                    _SeenListCache.Push(_SeenList);
                }
            }
        }
    }
}

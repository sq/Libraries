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

        internal class Sector : Dictionary<T, bool> {
            public SectorIndex Index;

            public Sector ()
                : base() {
            }

            public Sector (SectorIndex index) 
                : base (new ReferenceComparer<T>()) {
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

        public struct ItemBoundsEnumerator : IEnumerator<T>, IEnumerable<T> {
            Dictionary<T, bool> _SeenList;
            GetSectorsFromBounds _Sectors;
            Sector _Sector;
            Dictionary<T, bool>.KeyCollection.Enumerator _SectorEnumerator;
            T _Current;

            public ItemBoundsEnumerator (SpatialCollection<T> collection, Bounds bounds) {
                _SeenList = collection.GetSeenList();
                _Sectors = new SpatialCollection<T>.GetSectorsFromBounds(collection, bounds, false);
                _Sector = null;
                _Current = null;
                _SectorEnumerator = default(Dictionary<T, bool>.KeyCollection.Enumerator);
            }

            public ItemBoundsEnumerator (SpatialCollection<T> collection, SectorIndex tl, SectorIndex br) {
                _SeenList = collection.GetSeenList();
                _Sectors = new SpatialCollection<T>.GetSectorsFromBounds(collection, tl, br, false);
                _Sector = null;
                _Current = null;
                _SectorEnumerator = default(Dictionary<T, bool>.KeyCollection.Enumerator);
            }

            public T Current {
                get { return _Current; }
            }

            public void Dispose () {
                _Sectors.Dispose();
                _Sector = null;
            }

            object System.Collections.IEnumerator.Current {
                get { return _Current; }
            }

            public bool MoveNext () {
                _Current = null;
                while (_Current == null) {
                    while (_Sector == null) {
                        if (_Sectors.MoveNext()) {
                            _Sector = _Sectors.Current;
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
                    if (_SeenList.ContainsKey(_Current))
                        _Current = null;

                    if (!_SectorEnumerator.MoveNext())
                        _Sector = null;
                }

                _SeenList.Add(_Current, true);
                return true;
            }

            public void Reset () {
                _Sectors.Reset();
                _Sector = null;
                _SectorEnumerator = default(Dictionary<T, bool>.KeyCollection.Enumerator);
                _SeenList.Clear();
            }

            public IEnumerator<T> GetEnumerator () {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
                return this;
            }
        }

        public struct ItemInfo {
            public Bounds Bounds;
            public SectorIndex TopLeft, BottomRight;

            public ItemInfo (IHasBounds item, SpatialCollection<T> parent) {
                Bounds = item.Bounds;
                TopLeft = parent.GetIndexFromPoint(Bounds.TopLeft);
                BottomRight = parent.GetIndexFromPoint(Bounds.BottomRight);
            }
        }

        public const float DefaultSubdivision = 512.0f;
        public const int InitialFreeListSize = 8;
        public const int MaxFreeListSize = 32;

        internal float _Subdivision;
        internal Dictionary<T, ItemInfo> _Items = new Dictionary<T, ItemInfo>(new ReferenceComparer<T>());
        internal Dictionary<SectorIndex, Sector> _Sectors;
        internal Dictionary<T, bool> _SeenList = new Dictionary<T, bool>(new ReferenceComparer<T>());
        internal List<Sector> _FreeList = new List<Sector>();

        public SpatialCollection ()
            : this(DefaultSubdivision) {
        }

        public SpatialCollection (float subdivision) {
            _Subdivision = subdivision;
            _Sectors = new Dictionary<Squared.Util.Pair<int>, Sector>(new IntPairComparer());

            for (int i = 0; i < InitialFreeListSize; i++)
                _FreeList.Add(new Sector());
        }

        internal Dictionary<T, bool> GetSeenList () {
            _SeenList.Clear();
            return _SeenList;
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

        public bool TryGetBounds (T item, out Bounds bounds) {
            ItemInfo info;
            if (_Items.TryGetValue(item, out info)) {
                bounds = info.Bounds;
                return true;
            }

            bounds = default(Bounds);
            return false;
        }

        public void Add (T item) {
            var info = new ItemInfo(item, this);
            _Items.Add(item, info);

            using (var e = new GetSectorsFromBounds(this, info.Bounds, true))
            while (e.MoveNext())
                e.Current.Add(item, false);
        }

        internal void InternalRemove (T item, ref ItemInfo info) {
            using (var e = new GetSectorsFromBounds(this, info.Bounds, false))
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
                InternalRemove(item, ref info);
                _Items.Remove(item);
                return true;
            } else {
                return false;
            }
        }

        public void UpdateItemBounds (T item) {
            ItemInfo info;
            if (_Items.TryGetValue(item, out info)) {
                var newInfo = info;
                newInfo.Bounds = item.Bounds;
                newInfo.TopLeft = GetIndexFromPoint(newInfo.Bounds.TopLeft);
                newInfo.BottomRight = GetIndexFromPoint(newInfo.Bounds.BottomRight);
                _Items[item] = newInfo;

                if ((newInfo.TopLeft.First == info.TopLeft.First) &&
                    (newInfo.TopLeft.Second == info.TopLeft.Second) && 
                    (newInfo.BottomRight.First == info.BottomRight.First) &&
                    (newInfo.BottomRight.Second == info.BottomRight.Second))
                    return;

                InternalRemove(item, ref info);

                using (var e = new GetSectorsFromBounds(this, newInfo.TopLeft, newInfo.BottomRight, true))
                while (e.MoveNext())
                    e.Current.Add(item, false);
            }
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
    }
}

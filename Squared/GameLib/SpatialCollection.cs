using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SectorIndex = Squared.Util.Pair<int>;
using Microsoft.Xna.Framework;
using Squared.Util;
using System.Threading;

namespace Squared.Game {
    public class PairComparer<T> : IEqualityComparer<Pair<T>>
        where T : struct, IComparable<T> {
        public bool Equals (Pair<T> x, Pair<T> y) {
            return (x.First.CompareTo(y.First) == 0) && (x.Second.CompareTo(y.Second) == 0);
        }

        public int GetHashCode (Pair<T> obj) {
            return obj.First.GetHashCode() + obj.Second.GetHashCode();
        }
    }
    
    public class SpatialCollection<T> : IEnumerable<T>
        where T : class, IHasBounds {

        internal class Sector : Dictionary<T, bool> {
            public SectorIndex Index;

            public Sector (SectorIndex index) 
                : base () {
                Index = index;
            }
        }

        internal struct GetSectorsFromBounds : IEnumerator<Sector> {
            SectorIndex tl, br, item;
            Sector current;
            int x, y;
            SpatialCollection<T> collection;
            bool create;

            public GetSectorsFromBounds (SpatialCollection<T> collection, Bounds bounds, bool create) {
                tl = collection.GetIndexFromPoint(bounds.TopLeft, false);
                br = collection.GetIndexFromPoint(bounds.BottomRight, true);
                item = new SectorIndex();
                this.create = create;
                this.collection = collection;
                x = tl.First - 1;
                y = tl.Second;
                current = null;
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
            BufferPool<T>.Buffer _SeenList;
            GetSectorsFromBounds _Sectors;
            Sector _Sector;
            Dictionary<T, bool>.KeyCollection.Enumerator _SectorEnumerator;
            T _Current;
            int _SeenCount;

            public ItemBoundsEnumerator (SpatialCollection<T> collection, Bounds bounds) {
                _SeenList = BufferPool<T>.Allocate(collection.Count);
                _Sectors = new SpatialCollection<T>.GetSectorsFromBounds(collection, bounds, false);
                _Sector = null;
                _SeenCount = 0;
                _Current = null;
                _SectorEnumerator = default(Dictionary<T, bool>.KeyCollection.Enumerator);
            }

            public T Current {
                get { return _Current; }
            }

            public void Dispose () {
                _SeenList.Dispose();
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

                    for (int i = 0; i < _SeenCount; i++) {
                        if (_SeenList.Data[i] == _Current) {
                            _Current = null;
                            break;
                        }
                    }

                    if (!_SectorEnumerator.MoveNext())
                        _Sector = null;
                }

                _SeenList.Data[_SeenCount] = _Current;
                _SeenCount += 1;

                return true;
            }

            public void Reset () {
                _Sectors.Reset();
                _Sector = null;
                _SectorEnumerator = default(Dictionary<T, bool>.KeyCollection.Enumerator);
            }

            public IEnumerator<T> GetEnumerator () {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
                return this;
            }
        }

        public const float DefaultSubdivision = 512.0f;

        internal float _Subdivision;
        internal List<T> _Items = new List<T>();
        internal Dictionary<SectorIndex, Sector> _Sectors;

        public SpatialCollection ()
            : this(DefaultSubdivision) {
        }

        public SpatialCollection (float subdivision) {
            _Subdivision = subdivision;
            _Sectors = new Dictionary<Squared.Util.Pair<int>, Sector>(new PairComparer<int>());
        }

        internal SectorIndex GetIndexFromPoint (Vector2 point, bool roundUp) {
            if (roundUp)
                return new SectorIndex((int)Math.Ceiling(point.X / _Subdivision), (int)Math.Ceiling(point.Y / _Subdivision));
            else
                return new SectorIndex((int)Math.Floor(point.X / _Subdivision), (int)Math.Floor(point.Y / _Subdivision));
        }

        internal Sector GetSectorFromIndex (SectorIndex index) {
            Sector sector = null;

            if (!_Sectors.TryGetValue(index, out sector)) {
                sector = new Sector(index);
                _Sectors[index] = sector;
            }

            return sector;
        }

        public void Add (T item) {
            _Items.Add(item);

            using (var e = new GetSectorsFromBounds(this, item.Bounds, true))
            while (e.MoveNext())
                e.Current.Add(item, false);
        }

        internal void InternalRemove (T item, Bounds bounds) {
            using (var e = new GetSectorsFromBounds(this, item.Bounds, false))
            while (e.MoveNext()) {
                var sector = e.Current;
                sector.Remove(item);

                if (sector.Count == 0)
                    _Sectors.Remove(sector.Index);
            }
        }

        public bool Remove (T item) {
            if (_Items.Remove(item)) {
                InternalRemove(item, item.Bounds);

                return true;
            }

            return false;
        }

        public void RemoveAt (int index) {
            var item = _Items[index];
            _Items.RemoveAt(index);

            InternalRemove(item, item.Bounds);
        }

        public T this[int index] {
            get {
                return _Items[index];
            }
        }

        public void UpdateItemBounds (T item, Bounds previousBounds) {
            InternalRemove(item, previousBounds);

            using (var e = new GetSectorsFromBounds(this, item.Bounds, true))
            while (e.MoveNext())
                e.Current.Add(item, false);
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
            return ((IEnumerable<T>)_Items).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return _Items.GetEnumerator();
        }
    }
}

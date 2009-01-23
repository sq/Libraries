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

#if XBOX
        internal class Sector : List<T> {
#else
        internal class Sector : HashSet<T> {
#endif
            public SectorIndex Index;

            public Sector (SectorIndex index) 
                : base () {
                Index = index;
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

        internal SectorIndex GetIndexFromPoint (Vector2 point) {
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

        internal struct GetSectorsFromBounds : IEnumerator<Sector> {
            SectorIndex tl, br, item;
            Sector current;
            int x, y;
            SpatialCollection<T> collection;
            bool create;

            public GetSectorsFromBounds (SpatialCollection<T> collection, Bounds bounds, bool create) {
                tl = collection.GetIndexFromPoint(bounds.TopLeft);
                br = collection.GetIndexFromPoint(bounds.BottomRight);
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

        public void Add (T item) {
            _Items.Add(item);

            using (var e = new GetSectorsFromBounds(this, item.Bounds, true))
            while (e.MoveNext())
                e.Current.Add(item);
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
                e.Current.Add(item);
        }

        public IEnumerable<T> GetItemsFromBounds (Bounds bounds) {
            var e = new GetSectorsFromBounds(this, bounds, false);
            using (var seenList = BufferPool<T>.Allocate(Count)) {
                int numSeen = 0;

                while (e.MoveNext()) {
                    foreach (var item in e.Current) {
                        bool found = false;
                        for (int i = 0; i < numSeen; i++) {
                            if (seenList.Data[i] == item) {
                                found = true;
                                break;
                            }
                        }

                        if (!found) {
                            seenList.Data[numSeen] = item;
                            numSeen += 1;
                            yield return item;
                        }
                    }
                }
            }
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

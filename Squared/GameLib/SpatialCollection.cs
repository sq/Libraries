using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SectorIndex = Squared.Util.Pair<int>;
using Microsoft.Xna.Framework;

namespace Squared.Game {
    public class SpatialCollection<T> : IEnumerable<T>
        where T : IHasBounds {

        internal class Sector : HashSet<T> {
            public SectorIndex Index;

            public Sector (SectorIndex index) 
                : base () {
                Index = index;
            }
        }

        float _Subdivision;
        List<T> _Items = new List<T>();
        Dictionary<SectorIndex, Sector> _Sectors = new Dictionary<Squared.Util.Pair<int>, Sector>();

        public SpatialCollection (float subdivision) {
            _Subdivision = subdivision;
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

        internal IEnumerable<SectorIndex> GetIndicesFromBounds (Bounds bounds) {
            var tl = GetIndexFromPoint(bounds.TopLeft);
            var br = GetIndexFromPoint(bounds.BottomRight);
            var item = new SectorIndex();

            for (int y = tl.Second; y <= br.Second; y++) {
                for (int x = tl.First; x <= br.First; x++) {
                    item.First = x;
                    item.Second = y;
                    yield return item;
                }
            }
        }

        public void Add (T item) {
            _Items.Add(item);

            foreach (var index in GetIndicesFromBounds(item.Bounds)) {
                var sector = GetSectorFromIndex(index);
                sector.Add(item);
            }
        }

        public bool Remove (T item) {
            if (_Items.Remove(item)) {
                foreach (var index in GetIndicesFromBounds(item.Bounds)) {
                    var sector = GetSectorFromIndex(index);
                    sector.Remove(item);
                }

                return true;
            }

            return false;
        }

        public void RemoveAt (int index) {
            var item = _Items[index];
            _Items.RemoveAt(index);

            foreach (var i in GetIndicesFromBounds(item.Bounds)) {
                var sector = GetSectorFromIndex(i);
                sector.Remove(item);
            }
        }

        public T this[int index] {
            get {
                return _Items[index];
            }
        }

        public void UpdateItemBounds (T item, Bounds previousBounds) {
            foreach (var index in GetIndicesFromBounds(previousBounds)) {
                var sector = GetSectorFromIndex(index);
                sector.Remove(item);
            }

            foreach (var index in GetIndicesFromBounds(item.Bounds)) {
                var sector = GetSectorFromIndex(index);
                sector.Add(item);
            }
        }

        public IEnumerable<T> GetItemsFromBounds (Bounds bounds) {
            var seen = new HashSet<T>();
            Sector sector = null;

            foreach (var index in GetIndicesFromBounds(bounds)) {
                if (_Sectors.TryGetValue(index, out sector)) {
                    foreach (var item in sector) {
                        if (!seen.Contains(item)) {
                            seen.Add(item);
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

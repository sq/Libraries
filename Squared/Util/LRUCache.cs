using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using System.Threading;

namespace Squared.Util {
    public class LRUCache<K, V> : IEnumerable<KeyValuePair<K, V>>, IDictionary<K, V>
        where K : IComparable<K> {

        internal class Node {
            public K Key;
            public V Value;
            public Node Prev = null;
            public Node Next = null;
        }

        internal class Dict : Dictionary<K, Node> {
            internal Dict () 
                : base () {
            }

            internal Dict (IDictionary<K, Node> dictionary)
                : base (dictionary) {
            }

            internal Dict (IEqualityComparer<K> comparer)
                : base (comparer) {
            }

            internal Dict (int capacity)
                : base (capacity) {
            }

            internal Dict (IDictionary<K, Node> dictionary, IEqualityComparer<K> comparer)
                : base (dictionary, comparer) {
            }

            internal Dict (int capacity, IEqualityComparer<K> comparer)
                : base(capacity, comparer) {
            }
        }

        public struct Enumerator : IEnumerator<KeyValuePair<K, V>>, IEnumerator {
            LRUCache<K, V> _Cache;
            Node _Node;
            KeyValuePair<K, V> _Current;
            int _Version;
            bool _Finished;

            public Enumerator (LRUCache<K, V> cache) {
                _Cache = cache;
                _Version = cache._Version;
                _Node = null;
                _Current = new KeyValuePair<K, V>();
                _Finished = false;
            }

            public KeyValuePair<K, V> Current {
                get { return _Current; }
            }

            public void Dispose () {
            }

            object IEnumerator.Current {
                get { return _Current; }
            }

            public bool MoveNext () {
                if (_Finished)
                    return false;
                if (_Version != _Cache._Version)
                    throw new InvalidOperationException("LRUCache was modified during enumeration");

                if (_Node == null)
                    _Node = _Cache._First;
                else
                    _Node = _Node.Next;
                
                if (_Node != null) {
                    _Current = new KeyValuePair<K,V>(_Node.Key, _Node.Value);
                    return true;
                } else {
                    _Current = new KeyValuePair<K, V>();
                    _Finished = true;
                    return false;
                }
            }

            public void Reset () {
                _Finished = false;
                _Node = null;
                _Current = new KeyValuePair<K, V>();
                _Version = _Cache._Version;
            }
        }

        public const int DefaultCacheSize = 16;

        private Dict _Dict;
        private int _CacheSize;
        private int _Version = 0;
        internal Node _First = null;
        internal Node _Last = null;

        public LRUCache ()
            : this(DefaultCacheSize) {
        }

        public LRUCache (int cacheSize) {
            _CacheSize = cacheSize;
            _Dict = new Dict(cacheSize);
        }

        public V this[K key] {
            get {
                var item = _Dict[key];

                this[item.Key] = item.Value;

                return item.Value;
            }

            set {
                Remove(key);

                var item = new Node { Prev = _Last, Key = key, Value = value };

                if (_First == null)
                    _First = item;
                if (_Last != null)
                    _Last.Next = item;
                _Last = item;

                _Dict[key] = item;

                _Version += 1;

                if (_Dict.Count > _CacheSize) {
                    if (_First == _Last) {
                        _First = null;
                        _Last = null;
                        return;
                    }

                    var dead = _First;
                    dead.Next.Prev = null;
                    _First = dead.Next;
                    dead.Next = null;
                    _Dict.Remove(dead.Key);
                }
            }
        }

        public void Add (K key, V value) {
            if (ContainsKey(key))
                throw new InvalidOperationException("The specified key already exists in the LRUCache");

            this[key] = value;
        }

        public bool Remove (K key) {
            Node item;
            if (_Dict.TryGetValue(key, out item)) {
                if (item.Prev != null)
                    item.Prev.Next = item.Next;
                else
                    _First = item.Next;

                if (item.Next != null)
                    item.Next.Prev = item.Prev;
                else
                    _Last = item.Prev;

                _Version += 1;

                return _Dict.Remove(key);                
            }

            return false;
        }

        public bool ContainsKey (K key) {
            return _Dict.ContainsKey(key);
        }

        public bool TryGetValue (K key, out V value) {
            Node item;
            if (_Dict.TryGetValue(key, out item)) {
                value = item.Value;

                this[item.Key] = item.Value;

                return true;
            }

            value = default(V);
            return false;
        }

        public int Capacity {
            get {
                return _CacheSize;
            }
        }

        public int Count {
            get {
                return _Dict.Count;
            }
        }

        public void Clear () {
            _First = null;
            _Last = null;
            _Version += 1;
            _Dict.Clear();
        }

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator () {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return new Enumerator(this);
        }

        ICollection<K> IDictionary<K, V>.Keys {
            get { 
                return _Dict.Keys; 
            }
        }

        ICollection<V> IDictionary<K, V>.Values {
            get {
                throw new NotImplementedException();
            }
        }

        void ICollection<KeyValuePair<K, V>>.Add (KeyValuePair<K, V> item) {
            Add(item.Key, item.Value);
        }

        bool ICollection<KeyValuePair<K, V>>.Contains (KeyValuePair<K, V> item) {
            Node node;
            if (_Dict.TryGetValue(item.Key, out node))
                return EqualityComparer<V>.Default.Equals(node.Value, item.Value);

            return false;
        }

        void ICollection<KeyValuePair<K, V>>.CopyTo (KeyValuePair<K, V>[] array, int arrayIndex) {
            int index = arrayIndex;
            foreach (var item in this) {
                array[index] = item;
                index += 1;
            }
        }

        bool ICollection<KeyValuePair<K, V>>.IsReadOnly {
            get { 
                return false; 
            }
        }

        bool ICollection<KeyValuePair<K, V>>.Remove (KeyValuePair<K, V> item) {
            Node node;
            if (_Dict.TryGetValue(item.Key, out node))
                if (EqualityComparer<V>.Default.Equals(node.Value, item.Value))
                    return Remove(item.Key);

            return false;
        }
    }
}

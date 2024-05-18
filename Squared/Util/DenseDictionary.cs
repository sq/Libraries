using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util {
    public struct DenseDictionary<TKey, TValue> : IDictionary<TKey, TValue> {
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>> {
            private DenseList<TKey>.Enumerator KeyEnumerator;
            private DenseList<TValue>.Enumerator ValueEnumerator;

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(KeyEnumerator.Current, ValueEnumerator.Current);
            object IEnumerator.Current => Current;

            public Enumerator (DenseDictionary<TKey, TValue> dictionary) {
                KeyEnumerator = dictionary.KeyList.GetEnumerator();
                ValueEnumerator = dictionary.ValueList.GetEnumerator();
            }

            public void Dispose () {
                KeyEnumerator.Dispose();
                ValueEnumerator.Dispose();
            }

            public bool MoveNext () {
                return (KeyEnumerator.MoveNext() && ValueEnumerator.MoveNext());
            }

            public void Reset () {
                KeyEnumerator.Reset();
                ValueEnumerator.Reset();
            }
        }

        public readonly IEqualityComparer<TKey> Comparer;
        internal DenseList<TKey> KeyList;
        internal DenseList<TValue> ValueList;

        public DenseDictionary (IEqualityComparer<TKey> comparer) : this() {
            Comparer = comparer;
        }

        public int Count => KeyList.Count;
        public TValue this[TKey key] { 
            get {
                var index = FindIndex(key);
                if (index < 0)
                    throw new KeyNotFoundException();
                return ValueList[index];
            }
            set {
                var index = FindIndex(key);
                if (index >= 0)
                    ValueList[index] = value;
                else
                    Add(key, value);
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => KeyList;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => ValueList;
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        private int FindIndex (TKey key) =>
            KeyList.IndexOf(key, Comparer ?? EqualityComparer<TKey>.Default);

        public void Add (TKey key, TValue value) {
            if (FindIndex(key) >= 0)
                throw new InvalidOperationException("Key already exists");

            KeyList.Add(key);
            ValueList.Add(value);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add (KeyValuePair<TKey, TValue> item) =>
            Add(item.Key, item.Value);

        public void Clear () {
            KeyList.Clear();
            ValueList.Clear();
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains (KeyValuePair<TKey, TValue> item) =>
            TryGetValue(item.Key, out var value) &&
            value?.Equals(item.Value) != false;

        public bool ContainsKey (TKey key) =>
            FindIndex(key) >= 0;

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo (KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
            throw new NotImplementedException();

        public Enumerator GetEnumerator () =>
            new Enumerator(this);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator () =>
            new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator () =>
            new Enumerator(this);

        public bool Remove (TKey key) {
            var index = FindIndex(key);
            if (index < 0)
                return false;
            KeyList.RemoveAtUnordered(index);
            ValueList.RemoveAtUnordered(index);
            return true;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove (KeyValuePair<TKey, TValue> item) {
            var index = FindIndex(item.Key);
            if (index < 0)
                return false;
            if (ValueList[index]?.Equals(item.Value) == false)
                return false;
            KeyList.RemoveAtUnordered(index);
            ValueList.RemoveAtUnordered(index);
            return true;
        }

        public bool TryGetValue (TKey key, out TValue value) {
            var index = FindIndex(key);
            if (index < 0) {
                value = default;
                return false;
            }

            ValueList.GetItem(index, out value);
            return true;
        }

        public DenseDictionary<TKey, TValue> Clone () {
            var result = new DenseDictionary<TKey, TValue>();
            KeyList.Clone(out result.KeyList);
            ValueList.Clone(out result.ValueList);
            return result;
        }

        public Dictionary<TKey, TValue> ToDictionary () {
            var result = new Dictionary<TKey, TValue>();
            CopyTo(result);
            return result;
        }

        public void CopyTo (Dictionary<TKey, TValue> result) {
            for (int i = 0; i < KeyList.Count; i++)
                result.Add(KeyList[i], ValueList[i]);
        }
    }

    public static class DenseDictionaryExtensions {
        public static ref readonly DenseList<TKey> Keys<TKey, TValue> (ref readonly this DenseDictionary<TKey, TValue> self) =>
            ref self.KeyList;

        public static ref readonly DenseList<TValue> Values<TKey, TValue> (ref readonly this DenseDictionary<TKey, TValue> self) =>
            ref self.ValueList;
    }
}

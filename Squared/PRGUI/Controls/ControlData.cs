using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public struct ControlDataCollection : IEnumerable<KeyValuePair<string, object>> {
        DenseList<IControlData> Items;

        public void Clear () {
            Items.Clear();
        }

        private bool Find<T> (ref ControlDataKey key, out ControlData<T> result) {
            if (Items.Count == 0) {
                result = default;
                return false;
            }

            IControlData item;
            for (int i = 0, c = Items.Count; i < c; i++) {
                Items.GetItem(i, out item);
                if (item.KeyEquals(ref key)) {
                    result = (ControlData<T>)item;
                    return true;
                }
            }

            result = default;
            return false;
        }

        public T Get<T> (string name = null) {
            return Get(name, default(T));
        }

        public T Get<T> (string name, T defaultValue) {
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            if (Find(ref key, out ControlData<T> data))
                return data.Value;
            else
                return defaultValue;
        }

        public bool TryGet<T> (string name, out T result) {
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            if (Find(ref key, out ControlData<T> data)) {
                result = data.Value;
                return true;
            }
            result = default;
            return false;
        }

        public void Set<T> (T value) {
            Set(null, value);
        }

        public void Set<T> (string name, T value) {
            Set(name, ref value);
        }

        public void Set<T> (ref T value) {
            Set(null, ref value);
        }

        public void Set<T> (string name, ref T value) {
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            if (Find(ref key, out ControlData<T> data))
                data.Value = value;
            else
                Items.Add(new ControlData<T> {
                    Key = key,
                    Value = value
                });
        }

        public bool Remove<T> (string name) {
            if (Items.Count == 0)
                return false;
            var key = new ControlDataKey { Type = typeof(T), Key = name };

            IControlData item;
            for (int i = 0, c = Items.Count; i < c; i++) {
                Items.GetItem(i, out item);
                if (item.KeyEquals(ref key)) {
                    Items.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool UpdateOrCreate<T> (string name, T expected, T replacement)
            where T : IEquatable<T>
        {
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            if (Find(ref key, out ControlData<T> existingItem)) {
                if (expected.Equals(existingItem.Value)) {
                    existingItem.Value = replacement;
                    return true;
                } else
                    return false;
            }
            Items.Add(new ControlData<T> {
                Key = key,
                Value = replacement
            });
            return true;
        }

        // For compatibility with collection initializers
        public void Add<T> (T value) {
            Add(null, value);
        }

        public void Add<T> (string name, T value) {
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            if (Find<T>(ref key, out _))
                throw new ArgumentException("Key already exists");
            Items.Add(new ControlData<T> {
                Key = key,
                Value = value
            });
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator () {
            if (Items.Count == 0)
                yield break;

            foreach (var item in Items)
                yield return new KeyValuePair<string, object>(item.Key, item.Value);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return GetEnumerator();
        }
    }

    internal interface IControlData {
        bool KeyEquals (ref ControlDataKey key);
        string Key { get; }
        Type Type { get; }
        object Value { get; set; }
    }

    internal class ControlData<T> : IControlData {
        public ControlDataKey Key;
        public T Value;

        public Type Type => Key.Type;
        string IControlData.Key => Key.Key;
        object IControlData.Value {
            get => Value;
            set {
                Value = (T)value;
            }
        }

        public bool KeyEquals (ref ControlDataKey key) {
            return Key.Equals(ref key);
        }
    }

    internal struct ControlDataKey {
        public Type Type;
        public string Key;

        public bool Equals (ref ControlDataKey rhs) {
            return (Type == rhs.Type) &&
                string.Equals(Key, rhs.Key);
        }

        public bool Equals (ControlDataKey rhs) {
            return (Type == rhs.Type) &&
                string.Equals(Key, rhs.Key);
        }

        public override bool Equals (object obj) {
            if (obj is ControlDataKey)
                return Equals((ControlDataKey)obj);
            else
                return false;
        }

        public override int GetHashCode () {
            return Type.GetHashCode();
        }
    }
}

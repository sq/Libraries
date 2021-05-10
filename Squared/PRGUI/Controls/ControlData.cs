using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI.Controls {
    public struct ControlDataCollection : IEnumerable<KeyValuePair<string, object>> {
        Dictionary<ControlDataKey, object> Data;

        public void Clear () {
            if (Data == null)
                return;
            Data.Clear();
        }

        public T Get<T> (string name = null) {
            return Get(name, default(T));
        }

        public T Get<T> (string name, T defaultValue) {
            if (Data == null)
                return defaultValue;

            var key = new ControlDataKey { Type = typeof(T), Key = name };
            object existingValue;
            if (!Data.TryGetValue(key, out existingValue))
                return defaultValue;
            return (T)existingValue;
        }

        public bool TryGet<T> (string name, out T result) {
            if (Data == null) {
                result = default(T);
                return false;
            }

            var key = new ControlDataKey { Type = typeof(T), Key = name };
            object existingValue;
            if (!Data.TryGetValue(key, out existingValue)) {
                result = default(T);
                return false;
            }

            result = (T)existingValue;
            return true;
        }

        public bool Set<T> (T value) {
            return Set(null, value);
        }

        public bool Set<T> (string name, T value) {
            if (Data == null)
                Data = new Dictionary<ControlDataKey, object>(ControlDataKeyComparer.Instance);
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            Data[key] = value;
            return true;
        }

        public bool Set<T> (ref T value) {
            return Set(null, ref value);
        }

        public bool Set<T> (string name, ref T value) {
            if (Data == null)
                Data = new Dictionary<ControlDataKey, object>(ControlDataKeyComparer.Instance);
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            Data[key] = value;
            return true;
        }

        public bool Remove<T> (string name) {
            if (Data == null)
                return false;
            var key = new ControlDataKey { Type = typeof(T), Key = name };
            return Data.Remove(key);
        }

        public bool UpdateOrCreate<TExisting, TNew> (string name, TExisting expected, TNew replacement)
            where TExisting : IEquatable<TExisting>
        {
            if ((Data == null) && !Get<TExisting>(name).Equals(expected))
                return false;

            Remove<TExisting>(name);
            return Set(name, replacement);
        }

        public void Add<T> (string name, T value) {
            Set(name, value);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator () {
            if (Data == null)
                yield break;

            foreach (var kvp in Data)
                yield return new KeyValuePair<string, object>(kvp.Key.Key, kvp.Value);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return this.GetEnumerator();
        }
    }

    internal class ControlDataKeyComparer : IEqualityComparer<ControlDataKey> {
        public static readonly ControlDataKeyComparer Instance = new ControlDataKeyComparer();

        public bool Equals (ControlDataKey x, ControlDataKey y) {
            return x.Equals(y);
        }

        public int GetHashCode (ControlDataKey obj) {
            return obj.GetHashCode();
        }
    }

    internal struct ControlDataKey {
        public Type Type;
        public string Key;

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

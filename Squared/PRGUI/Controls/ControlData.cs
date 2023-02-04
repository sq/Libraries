using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public struct ControlDataCollection : IEnumerable<KeyValuePair<string, object>> {
        DenseList<ControlDataBase> Items;

        public void Clear () {
            Items.Clear();
        }

        private bool Find<T> (string key, out ControlData<T> result) {
            int count = Items.Count;
            if (count == 0) {
                result = default;
                return false;
            }

            for (int i = 0; i < count; i++) {
                result = Items[i] as ControlData<T>;
                if ((result != null) && string.Equals(result.Key, key))
                    return true;
            }

            result = default;
            return false;
        }

        public T Get<T> (string name = null) {
            return Get(name, default(T));
        }

        public T Get<T> (string name, T defaultValue) {
            if (Find(name, out ControlData<T> data))
                return data.Value;
            else
                return defaultValue;
        }

        public bool TryGet<T> (string name, out T result) {
            if (Find(name, out ControlData<T> data)) {
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
            if (Find(name, out ControlData<T> data))
                data.Value = value;
            else
                Items.Add(new ControlData<T> {
                    Key = name,
                    Value = value
                });
        }

        public bool Remove<T> (string name) {
            if (Items.Count == 0)
                return false;

            for (int i = 0, c = Items.Count; i < c; i++) {
                var item = Items[i] as ControlData<T>;
                if (item == null)
                    continue;
                if (string.Equals(item.Key, name)) {
                    Items.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool UpdateOrCreate<T> (string name, T expected, T replacement)
            where T : IEquatable<T>
        {
            if (Find(name, out ControlData<T> existingItem)) {
                if (expected.Equals(existingItem.Value)) {
                    existingItem.Value = replacement;
                    return true;
                } else
                    return false;
            }
            Items.Add(new ControlData<T> {
                Key = name,
                Value = replacement
            });
            return true;
        }

        // For compatibility with collection initializers
        public void Add<T> (T value) {
            Add(null, value);
        }

        public void Add<T> (string name, T value) {
            if (Find<T>(name, out _))
                throw new ArgumentException("Key already exists");
            Items.Add(new ControlData<T> {
                Key = name,
                Value = value
            });
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator () {
            if (Items.Count == 0)
                yield break;

            foreach (var item in Items)
                yield return new KeyValuePair<string, object>(item.Key, item.BoxedValue);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return GetEnumerator();
        }
    }

    internal abstract class ControlDataBase {
        public string Key;

        public abstract object BoxedValue { get; }
    }

    internal sealed class ControlData<T> : ControlDataBase {
        public T Value;

        public override object BoxedValue => Value;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class ItemListManager<T> {
        public ItemList<T> Items;
        // FIXME: Handle set
        public IEqualityComparer<T> Comparer;

        private T _SelectedItem;
        private int _SelectedIndexVersion;
        private int _SelectedIndex = -1;

        public ItemListManager (IEqualityComparer<T> comparer) {
            Comparer = comparer;
            Items = new ItemList<T>(comparer);
        }

        public T SelectedItem {
            get => _SelectedItem;
            set {
                if (Comparer.Equals(_SelectedItem, value))
                    return;
                _SelectedItem = value;
                _SelectedIndex = Items.IndexOf(ref value, Comparer);
                _SelectedIndexVersion = Items.Version;
            }
        }

        public int SelectedIndex {
            get {
                if (_SelectedIndexVersion == Items.Version)
                    return _SelectedIndex;
                _SelectedIndexVersion = Items.Version;
                return _SelectedIndex = Items.IndexOf(ref _SelectedItem, Comparer);
            }
            set {
                if (_SelectedIndex == value)
                    return;

                _SelectedIndex = value;
                if (value == -1) {
                    _SelectedItem = default(T);
                } else if ((value < 0) || (value >= Items.Count)) {
                    throw new ArgumentOutOfRangeException("value");
                } else {
                    _SelectedItem = Items[value];
                }
                _SelectedIndexVersion = Items.Version;
            }
        }

        public Control SelectedControl =>
            Items.GetControlForValue(ref _SelectedItem, out Control result)
                ? result
                : null;

        public bool TryAdjustSelection (int delta, out T newItem, bool clamp = true) {
            newItem = default(T);
            if (Items.Count == 0)
                return false;

            var selectedIndex = SelectedIndex;
            if (selectedIndex < 0)
                selectedIndex = delta > 0 ? 0 : Items.Count - 1;
            else
                selectedIndex += delta;
            if (clamp)
                selectedIndex = Arithmetic.Clamp(selectedIndex, 0, Items.Count - 1);

            if ((selectedIndex >= 0) && (selectedIndex < Items.Count)) {
                newItem = Items[selectedIndex];
                return true;
            } else {
                return false;
            }
        }

        public bool TrySetSelectedItem (ref T value) {
            if (Comparer.Equals(value, _SelectedItem))
                return false;
            var indexOf = Items.IndexOf(ref value, Comparer);
            if (indexOf < 0)
                return false;
            _SelectedItem = value;
            _SelectedIndex = indexOf;
            _SelectedIndexVersion = Items.Version;
            return true;
        }
    }

    public class ItemList<T> : IEnumerable<T> {
        private List<T> Items = new List<T>();
        private readonly Dictionary<T, Control> ControlForValue;
        private readonly Dictionary<Control, T> ValueForControl = 
            new Dictionary<Control, T>(new ReferenceComparer<Control>());

        public ItemList (IEqualityComparer<T> comparer) 
            : base () {
            ControlForValue = new Dictionary<T, Control>(comparer);
        }

        public int Version { get; internal set; }
        public int Count => Items.Count;

        public T this[int index] {
            get => Items[index];
            set {
                Items[index] = value;
                Invalidate();
            }
        }

        public void Invalidate () {
            Version++;
        }

        public void Clear () {
            Items.Clear();
            Invalidate();
        }

        public void AddRange (IEnumerable<T> collection) {
            Items.AddRange(collection);
            Invalidate();
        }

        public void Add (T value) {
            Items.Add(value);
            Invalidate();
        }

        public void Add (ref T value) {
            Items.Add(value);
            Invalidate();
        }

        public bool Remove (T value) {
            Invalidate();
            return Items.Remove(value);
        }

        public bool Remove (ref T value) {
            Invalidate();
            return Items.Remove(value);
        }

        public void RemoveAt (int index) {
            Items.RemoveAt(index);
            Invalidate();
        }

        public void Sort (Comparison<T> comparer) {
            Items.Sort(comparer);
            Invalidate();
        }

        public void Sort (IComparer<T> comparer) {
            Items.Sort(comparer);
            Invalidate();
        }

        public bool GetControlForValue (T value, out Control result) {
            if (value == null) {
                result = null;
                return false;
            }
            return ControlForValue.TryGetValue(value, out result);
        }

        public bool GetControlForValue (ref T value, out Control result) {
            if (value == null) {
                result = null;
                return false;
            }
            return ControlForValue.TryGetValue(value, out result);
        }

        public bool GetValueForControl (Control control, out T result) {
            if (control == null) {
                result = default(T);
                return false;
            }
            return ValueForControl.TryGetValue(control, out result);
        }

        public int IndexOf (T value, IEqualityComparer<T> comparer) {
            return IndexOf(ref value, comparer);
        }

        public int IndexOf (ref T value, IEqualityComparer<T> comparer) {
            for (int i = 0, c = Count; i < c; i++)
                if (comparer.Equals(value, this[i]))
                    return i;

            return -1;
        }

        public bool Contains (T value, IEqualityComparer<T> comparer) {
            return Contains(ref value, comparer);
        }

        public bool Contains (ref T value, IEqualityComparer<T> comparer) {
            return IndexOf(ref value, comparer) >= 0;
        }

        private Control CreateControlForValue (
            ref T value, Control existingControl,
            CreateControlForValueDelegate<T> createControlForValue
        ) {
            Control newControl;
            if (value is Control)
                newControl = (Control)(object)value;
            else if (createControlForValue != null)
                newControl = createControlForValue(ref value, existingControl);
            else
                throw new ArgumentNullException("createControlForValue");

            if (value != null)
                ControlForValue[value] = newControl;

            ValueForControl[newControl] = value;
            return newControl;
        }

        public void GenerateControls (
            ControlCollection output, 
            CreateControlForValueDelegate<T> createControlForValue,
            int offset = 0, int count = int.MaxValue
        ) {
            // FIXME: This is inefficient, it would be cool to reuse existing controls
            //  even if the order of values changes
            ControlForValue.Clear();

            count = Math.Min(Count, count);
            if (offset < 0) {
                count += offset;
                offset = 0;
            }

            count = Math.Max(Math.Min(Count - offset, count), 0);

            while (output.Count > count) {
                var ctl = output[output.Count - 1];
                if (ValueForControl.TryGetValue(ctl, out T temp))
                    ControlForValue.Remove(temp);
                ValueForControl.Remove(ctl);
                output.RemoveAt(output.Count - 1);
            }

            for (int i = 0; i < count; i++) {
                var value = this[i + offset];
                // FIXME
                if (value == null)
                    continue;

                var existingControl = (i < output.Count)
                    ? output[i]
                    : null;

                var newControl = CreateControlForValue(ref value, existingControl, createControlForValue);

                if (newControl != existingControl) {
                    if (i < output.Count)
                        output[i] = newControl;
                    else
                        output.Add(newControl);
                }
            }
        }

        public IEnumerator<T> GetEnumerator () {
            return ((IEnumerable<T>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<T>)Items).GetEnumerator();
        }
    }
}

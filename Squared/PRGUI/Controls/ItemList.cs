using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class ItemListManager<T> {
        private sealed class IndexComparer : IRefComparer<int>, IEqualityComparer<int> {
            public static IndexComparer Instance = new IndexComparer();

            public int Compare (ref int lhs, ref int rhs) {
                return lhs.CompareTo(rhs);
            }

            public bool Equals (int lhs, int rhs) {
                return lhs == rhs;
            }

            public int GetHashCode (int value) {
                return value.GetHashCode();
            }
        }

        public event Action SelectionChanged;

        public ItemList<T> Items;
        // FIXME: Handle set
        public IEqualityComparer<T> Comparer;

        private int _SelectionVersion;
        /// <summary>
        /// Don't mutate this! If you do you deserve the problems that will result!
        /// </summary>
        internal DenseList<int> _SelectedIndices;

        private int _MaxSelectedCount = 1;
        public int MaxSelectedCount {
            get => _MaxSelectedCount;
            set {
                if (_MaxSelectedCount == value)
                    return;
                if (value < 0)
                    value = 0;

                _MaxSelectedCount = value;
                if (value <= 1) {
                    _SelectedIndices.Clear();
                    if (value > 0)
                        _SelectedIndices.Add(MostRecentItemInteractedWith);
                }
                // FIXME: Prune extra selected items on change
            }
        }

        public ItemListManager (IEqualityComparer<T> comparer) {
            Comparer = comparer;
            Items = new ItemList<T>(comparer);
            _SelectedIndices.EnsureList();
        }

        public bool HasSelectedItem { get; private set; }

        public int SelectedItemCount => _SelectedIndices.Count;

        public bool IsSelectedIndex (int index) {
            if (index < MinSelectedIndex)
                return false;
            else if (index > MaxSelectedIndex)
                return false;
            return _SelectedIndices.IndexOf(index, IndexComparer.Instance) >= 0;
        }

        public int IndexOfControl (Control child) {
            if (!Items.GetIndexOfControl(child, out int index))
                return -1;
            return index;
        }

        public void Clear () {
            var previousSelection = SelectedIndex;
            Items.Clear();
            _SelectedIndices.Clear();
            if (previousSelection != SelectedIndex)
                OnSelectionChanged(true);
            // FIXME: Should we do this?
            // HasSelectedItem = false;
        }

        private void OnSelectionChanged (bool fireEvent) {
            _SelectedIndices.Sort(IndexComparer.Instance);
            if (fireEvent && (SelectionChanged != null))
                SelectionChanged();
        }

        public T SelectedItem {
            get => 
                (_SelectedIndices.Count > 0) && (_SelectedIndices[0] >= 0) && (_SelectedIndices[0] < Items.Count)
                    ? Items[_SelectedIndices[0]] 
                    : default(T);
            set {
                if ((_SelectedIndices.Count == 1) && Comparer.Equals(Items[_SelectedIndices[0]], value))
                    return;
                var newIndex = Items.IndexOf(ref value, Comparer);
                if (newIndex < 0)
                    throw new ArgumentException("Item not found in collection", "value");
                MostRecentItemInteractedWith = newIndex;
                _SelectedIndices.Clear();
                _LastGrowDirection = 0;
                _SelectionVersion++;
                if (newIndex >= 0) {
                    HasSelectedItem = true;
                    AddSelectedIndex(newIndex);
                }
                OnSelectionChanged(true);
            }
        }

        public int SelectedIndex {
            get {
                if (_SelectedIndices.Count < 1)
                    return -1;
                var index = _SelectedIndices[0];
                if (index >= Items.Count)
                    return -1;
                return index;
            }
            set {
                if (!TrySetSelectedIndex(value, true))
                    throw new Exception("Failed to set selected index");
            }
        }

        public bool TrySetSelectedIndex (int index, bool fireEvent) {
            if ((index < 0) && (_SelectedIndices.Count == 0))
                return true;
            if ((_SelectedIndices.Count == 1) && (_SelectedIndices[0] == index))
                return true;
            else if (index >= Items.Count)
                return false;
            MostRecentItemInteractedWith = index;
            _SelectedIndices.Clear();
            _SelectionVersion++;
            _LastGrowDirection = 0;
            if (index >= 0) {
                HasSelectedItem = true;
                AddSelectedIndex(index);
            }
            OnSelectionChanged(true);
            return true;
        }

        public Control ControlForIndex (int index) {
            if ((index < 0) || (index >= Items.Count))
                throw new IndexOutOfRangeException();
            var item = Items[index];
            Items.GetControlForValue(ref item, out Control result);
            return result;
        }

        public Control SelectedControl =>
            Items.GetControlForValue(SelectedItem, out Control result)
                ? result
                : null;

        /// <summary>
        /// The index of the item most recently interacted with (via resize, toggle, etc)
        /// </summary>
        public int MostRecentItemInteractedWith { get; set; }

        private int _LastGrowDirection = 0;
        public int LastGrowDirection => _LastGrowDirection;

        public int MinSelectedIndex {
            get {
                var result = _SelectedIndices.FirstOrDefault(-1);
                return result;
            }
        }
        public int MaxSelectedIndex {
            get {
                var result = _SelectedIndices.LastOrDefault(-1);
                return result;
            }
        }

        public bool TryExpandOrShrinkSelectionToItem (ref T item, bool fireEvent = true) {
            var newIndex = Items.IndexOf(ref item, Comparer);
            if (newIndex < 0)
                return false;

            MostRecentItemInteractedWith = newIndex;

            T temp;
            var minIndex = _SelectedIndices.FirstOrDefault();
            var maxIndex = _SelectedIndices.LastOrDefault();
            var insideSelection = (newIndex >= minIndex) && (newIndex <= maxIndex);
            var shrinkDirection = -_LastGrowDirection;
            var shrinking = (_LastGrowDirection != 0) && (
                insideSelection || (shrinkDirection != _LastGrowDirection)
            );

            if (shrinking) {
                if (shrinkDirection == 0)
                    return false;
                else if (shrinkDirection < 0)
                    ConstrainSelection(minIndex, newIndex);
                else
                    ConstrainSelection(maxIndex, newIndex);
            }

            if (!insideSelection) {
                var deltaSign = newIndex < minIndex
                    ? -1
                    : 1;
                minIndex = _SelectedIndices.FirstOrDefault();
                maxIndex = _SelectedIndices.LastOrDefault();
                int delta = deltaSign < 0
                    ? newIndex - minIndex
                    : newIndex - maxIndex;

                return TryResizeSelection(delta, out temp, grow: true, fireEvent: fireEvent);
            } else {
                return true;
            }
        }

        public void ConstrainSelection (int a, int b) {
            if ((a < 0) || (b < 0))
                throw new ArgumentOutOfRangeException();

            int minIndex = Math.Min(a, b),
                maxIndex = Math.Max(a, b);

            for (int i = _SelectedIndices.Count - 1; i >= 0; i--) {
                // FIXME: This should be impossible
                if (i >= _SelectedIndices.Count)
                    break;
                var index = _SelectedIndices[i];
                if ((index >= minIndex) && (index <= maxIndex))
                    continue;
                _SelectedIndices.RemoveAt(i);
            }

            _SelectionVersion++;
        }

        public void SelectAll (bool fireEvent = true) {
            _SelectedIndices.Clear();
            for (int i = 0, c = Math.Min(Items.Count, MaxSelectedCount); i < c; i++)
                _SelectedIndices.Add(i);
            _SelectionVersion++;
            OnSelectionChanged(fireEvent);
        }

        public bool TryToggleInDirection (int delta, bool fireEvent = true) {
            var newIndex = MostRecentItemInteractedWith + delta;
            if (newIndex < 0)
                return false;
            if (newIndex >= Items.Count)
                return false;
            if (newIndex == MostRecentItemInteractedWith)
                return false;
            MostRecentItemInteractedWith = newIndex;
            var item = Items[newIndex];
            return TryToggleItemSelected(ref item, fireEvent);
        }

        public bool TryMoveSelection (int delta, bool fireEvent) {
            int previousIndex;
            if ((MaxSelectedCount > 1) && (MostRecentItemInteractedWith >= 0))
                previousIndex = MostRecentItemInteractedWith;
            else
                previousIndex = SelectedIndex;
            int newIndex = Arithmetic.Clamp(previousIndex + delta, 0, Items.Count - 1);
            if (newIndex == previousIndex)
                return false;
            return TrySetSelectedIndex(newIndex, fireEvent);
        }

        public bool TryResizeSelection (int delta, out T lastNewItem, bool grow, bool fireEvent = true) {
            if (delta == 0) {
                lastNewItem = SelectedItem;
                return false;
            }

            if (Items.Count == 0) {
                lastNewItem = default(T);
                return TrySetSelectedIndex(-1, fireEvent);
            }

            var deltaSign = Math.Sign(delta);

            _SelectionVersion++;
            if ((_SelectedIndices.Count > 0) && (MaxSelectedCount > 1)) {
                int pos = (delta > 0)
                    ? _SelectedIndices.LastOrDefault()
                    : _SelectedIndices.FirstOrDefault(),
                    neg = (delta > 0)
                        ? _SelectedIndices.FirstOrDefault()
                        : _SelectedIndices.LastOrDefault();
                int leadingEdge, newLeadingEdge;

                if (grow) {
                    leadingEdge = pos;
                    newLeadingEdge = pos + delta;
                } else {
                    leadingEdge = neg;
                    newLeadingEdge = neg + delta;
                }
                leadingEdge = Arithmetic.Clamp(leadingEdge, 0, Items.Count - 1);
                newLeadingEdge = Arithmetic.Clamp(newLeadingEdge, 0, Items.Count - 1);

                for (int i = leadingEdge + deltaSign, c = Items.Count; (i != newLeadingEdge) && (i >= 0) && (i < c); i += deltaSign) {
                    if (grow)
                        AddSelectedIndex(i);
                    else {
                        var indexOf = _SelectedIndices.IndexOf(i, IndexComparer.Instance);
                        if (indexOf < 0)
                            break;
                        _SelectedIndices.RemoveAt(indexOf);
                    }
                }

                if (grow) {
                    AddSelectedIndex(newLeadingEdge);
                    OnSelectionChanged(false);
                } else {
                    var indexOf = _SelectedIndices.IndexOf(newLeadingEdge, IndexComparer.Instance);
                    if (indexOf >= 0)
                        _SelectedIndices.RemoveAt(indexOf);
                    else
                        ;
                }

                if ((_LastGrowDirection == 0) || grow)
                    _LastGrowDirection = deltaSign;
                lastNewItem = Items[newLeadingEdge];
            } else {
                if (MaxSelectedCount > 1) {
                    int start = (delta < 0)
                        ? Items.Count - delta
                        : 0;
                    int end = (delta < 0)
                        ? Items.Count - 1
                        : delta;
                    lastNewItem = Items[end];

                    for (int i = start; i != end; i += deltaSign)
                        AddSelectedIndex(i);
                    AddSelectedIndex(end);
                    OnSelectionChanged(false);
                } else {
                    var newIndex = Arithmetic.Clamp(SelectedIndex + delta, 0, Items.Count - 1);
                    _SelectedIndices.Clear();
                    AddSelectedIndex(newIndex);
                    OnSelectionChanged(false);
                    lastNewItem = SelectedItem;
                }
            }

            MostRecentItemInteractedWith = Items.IndexOf(lastNewItem, Comparer);
            OnSelectionChanged(fireEvent);
            return true;
        }

        private void AddSelectedIndex (int index) {
            if ((index < 0) || (index >= Items.Count))
                throw new IndexOutOfRangeException();

            if (_SelectedIndices.Count >= MaxSelectedCount)
                return;

            if (_SelectedIndices.IndexOf(index) >= 0)
#if DEBUG
                throw new ArgumentException("index already selected");
#else
                return;
#endif

            _SelectedIndices.Add(index);
        }

        public void ClearSelection () {
            _SelectionVersion++;
            _SelectedIndices.Clear();
            OnSelectionChanged(true);
        }

        public bool TryToggleItemSelected (ref T item, bool fireEvent) {
            if (Items.Count == 0) {
                TrySetSelectedIndex(-1, fireEvent);
                return false;
            }

            var indexOf = Items.IndexOf(ref item, Comparer);
            if (indexOf < 0)
                return false;

            _SelectionVersion++;
            _LastGrowDirection = 0;
            var selectionIndexOf = _SelectedIndices.IndexOf(indexOf, IndexComparer.Instance);
            if (selectionIndexOf < 0) {
                AddSelectedIndex(indexOf);
                MostRecentItemInteractedWith = indexOf;
            } else
                _SelectedIndices.RemoveAt(selectionIndexOf);

            _SelectedIndices.Sort(IndexComparer.Instance);
            OnSelectionChanged(fireEvent);
            return true;
        }

        public bool TrySetSelectedItem (ref T value, bool fireEvent) {
            var indexOf = Items.IndexOf(ref value, Comparer);
            if (indexOf < 0)
                return false;

            if (
                (indexOf >= 0) &&
                (indexOf == SelectedIndex) && 
                (_SelectedIndices.Count == 1)
            )
                return true;

            return TrySetSelectedIndex(indexOf, fireEvent);
        }
    }

    public class ItemList<T> : IEnumerable<T> {
        private struct ValueToken {
            public T Value;
        }

        private IEqualityComparer<T> Comparer;
        private List<T> Items = new List<T>();
        private readonly Dictionary<T, Control> ControlForValue;
        private HashSet<Control> InvalidatedControls = new HashSet<Control>(new ReferenceComparer<Control>());
        private DenseList<Control> ResultBuffer = new DenseList<Control>(),
            SpareBuffer = new DenseList<Control>();

        public ItemList (IEqualityComparer<T> comparer) 
            : base () {
            Comparer = comparer;
            ControlForValue = new Dictionary<T, Control>(comparer);
        }

        private bool PurgePending;

        public int Version { get; internal set; }
        public int Count => Items.Count;

        public T this[int index] {
            get => Items[index];
            set {
                var oldValue = Items[index];
                if (Comparer.Equals(oldValue, value))
                    return;
                Invalidate(oldValue);
                Items[index] = value;
                Invalidate(value);
            }
        }

        public Dictionary<T, Control>.ValueCollection Controls => ControlForValue.Values;

        /// <summary>
        /// Forces all child controls to be re-created from scratch
        /// </summary>
        public void Purge () {
            PurgePending = true;
        }

        /// <summary>
        /// Flags the sequence as having changed so controls will be updated
        /// </summary>
        public void Invalidate () {
            Version++;
        }

        /// <summary>
        /// Flags a single item as having changed so its control will be updated
        /// </summary>
        public void Invalidate (T item) {
            if (ControlForValue.TryGetValue(item, out Control control))
                InvalidatedControls.Add(control);
        }

        public void Clear (bool invalidateControls = false) {
            if (invalidateControls) {
                ControlForValue.Clear();
                InvalidatedControls.Clear();
            }

            Items.Clear();
            Invalidate();
        }

        public void AddRange (T[] collection) {
            foreach (var item in collection)
                Items.Add(item);
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
            if (control.Data.TryGet(null, out ValueToken vt)) {
                result = vt.Value;
                return true;
            } else {
                result = default(T);
                return false;
            }
        }

        internal bool GetIndexOfControl (Control child, out int index) {
            if (child == null) {
                index = -1;
                return false;
            }

            if (!child.Data.TryGet("_Index", out index)) {
                index = -1;
                return false;
            }

            return true;
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

        private void CreateControlForValueEpilogue (
            ref T value, Control newControl
        ) {
            if (value != null)
                ControlForValue[value] = newControl;

            if (!GetValueForControl(newControl, out T existingValue) || !Comparer.Equals(existingValue, value))
                newControl.Data.Set(new ValueToken { Value = value });
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

            CreateControlForValueEpilogue(ref value, newControl);
            return newControl;
        }

        private Control CreateControlForValue<TUserData> (
            ref T value, Control existingControl, ref TUserData userData,
            CreateControlForValueDelegate<T, TUserData> createControlForValue
        ) {
            Control newControl;
            if (value is Control)
                newControl = (Control)(object)value;
            else if (createControlForValue != null)
                newControl = createControlForValue(ref value, existingControl, ref userData);
            else
                throw new ArgumentNullException("createControlForValue");

            CreateControlForValueEpilogue(ref value, newControl);
            return newControl;
        }

        public void GenerateInvalidatedControls (
            CreateControlForValueDelegate<T> createControlForValue
        ) {
            var originalCount = InvalidatedControls.Count;
            if (originalCount == 0)
                return;

            foreach (var ic in InvalidatedControls) {
                if (!GetValueForControl(ic, out T value))
                    // FIXME
                    continue;
                if (createControlForValue(ref value, ic) != ic)
                    throw new Exception("A new control was created when updating an invalidated control");
            }
            if (InvalidatedControls.Count != originalCount)
                throw new Exception("New controls were invalidated while previous ones were being updated");
            InvalidatedControls.Clear();
        }

        public void GenerateControls (
            ControlCollection output, 
            CreateControlForValueDelegate<T> createControlForValue,
            int offset = 0, int count = int.MaxValue, 
            int skipLeadingControls = 0, int skipTrailingControls = 0
        ) {
            var temp = NoneType.None;
            GenerateControlsImpl<NoneType>(output, createControlForValue, ref temp, offset, count, skipLeadingControls, skipTrailingControls);
        }

        public void GenerateControls<TUserData> (
            ControlCollection output, 
            CreateControlForValueDelegate<T, TUserData> createControlForValue,
            ref TUserData userData,
            int offset = 0, int count = int.MaxValue, 
            int skipLeadingControls = 0, int skipTrailingControls = 0
        ) {
            GenerateControlsImpl<TUserData>(output, createControlForValue, ref userData, offset, count, skipLeadingControls, skipTrailingControls);
        }

        private void GenerateControlsImpl<TUserData> (
            ControlCollection output, 
            Delegate createControlForValue,
            ref TUserData userData,
            int offset = 0, int count = int.MaxValue, 
            int skipLeadingControls = 0, int skipTrailingControls = 0
        ) {
            CreateControlForValueDelegate<T> ccfvd = null;
            CreateControlForValueDelegate<T, TUserData> ccfvdud = null;
            if (typeof(TUserData) == typeof(NoneType))
                ccfvd = (CreateControlForValueDelegate<T>)createControlForValue;
            else
                ccfvdud = (CreateControlForValueDelegate<T, TUserData>)createControlForValue;

            var oldFocus = output.Context?.Focused;
            var wasItemFocused = GetValueForControl(oldFocus, out T oldFocusedValue);

            count = Math.Min(Count, count);
            if (offset < 0) {
                count += offset;
                offset = 0;
            }

            count = Math.Max(Math.Min(Count - offset, count), 0);
            int i = 0, c = output.Count - skipLeadingControls - skipTrailingControls;

            ResultBuffer.Clear();
            SpareBuffer.Clear();

            // Add the head to our result buffer
            for (i = 0; i < skipLeadingControls; i++)
                if (i < output.Count)
                    ResultBuffer.Add(output[i]);

            // First pass: Collect controls for values we previously had
            for (i = 0; i < count; i++) {
                var value = this[i + offset];
                // FIXME
                if (value == null)
                    continue;

                Control controlToPutHere;
                if (GetControlForValue(ref value, out controlToPutHere)) {
                    // Remove the control from the table so it doesn't get reused
                    ControlForValue.Remove(value);
                    ResultBuffer.Add(controlToPutHere);
                } else {
                    // We don't have an existing control so put a null here, we'll fill this slot later
                    ResultBuffer.Add(null);
                }
            }

            // Any controls left in the value -> control table are unused since we didn't encounter
            //  that value in our first pass
            foreach (var ctl in ControlForValue.Values)
                SpareBuffer.Add(ctl);
            ControlForValue.Clear();

            // Second pass: fill gaps and generate all the controls
            for (i = 0; i < count; i++) {
                int j = i + skipLeadingControls;
                var value = this[i + offset];
                // FIXME
                if (value == null)
                    continue;

                // If we have any spare controls and this is a gap, fill it with one
                if ((ResultBuffer[j] == null) && (SpareBuffer.Count > 0)) {
                    ResultBuffer[j] = SpareBuffer.Last();
                    SpareBuffer.RemoveTail(1);
                }

                if (ccfvdud != null)
                    ResultBuffer[j] = CreateControlForValue(ref value, ResultBuffer[j], ref userData, ccfvdud);
                else
                    ResultBuffer[j] = CreateControlForValue(ref value, ResultBuffer[j], ccfvd);
                // Now record the control for this value since we've filled gaps
                ControlForValue[value] = ResultBuffer[j];
            }

            // Now add the tail to our result buffer
            for (i = 0; i < skipTrailingControls; i++) {
                int j = (output.Count - skipTrailingControls) + i;
                if (j < output.Count)
                    ResultBuffer.Add(output[j]);
            }

            // Update the index table
            // FIXME: Optimize this
            for (i = 0; i < ResultBuffer.Count; i++)
                ResultBuffer[i].Data.Set("_Index", i + offset);

            output.ReplaceWith(ref ResultBuffer);

            ResultBuffer.Clear();
            SpareBuffer.Clear();

            if (wasItemFocused && GetControlForValue(oldFocusedValue, out Control newFocusedControl))
                output.Context.TrySetFocus(newFocusedControl, false, false);

            PurgePending = false;
        }

        public IEnumerator<T> GetEnumerator () {
            return ((IEnumerable<T>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<T>)Items).GetEnumerator();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.PRGUI {
    public sealed class ControlCollection : IEnumerable<Control> {
        public struct OrderRange {
            public int Min, Max;

            internal void Update (int order) {
                Min = Math.Min(Min, order);
                Max = Math.Max(Max, order);
            }
        }

        private OrderRange PaintOrderRange, TabOrderRange;
        private List<Control> PaintOrderedItems = new List<Control>(),
            TabOrderedItemsResult = new List<Control>();
        private List<IndexedControl> TabOrderedItems = new List<IndexedControl>();
        private List<Control> Items = new List<Control>();
        private Dictionary<Control, int> IndexTable = 
            new Dictionary<Control, int>(new ReferenceComparer<Control>());
        private DenseList<Control> DeadControlScratchBuffer = new DenseList<Control>();

        public int Count => Items.Count;
        public Control Host { get; private set; }
        private WeakReference<Control> _WeakHost;
        private WeakReference<Control> WeakHost {
            get {
                if (Host == null)
                    return null;
                if (_WeakHost == null)
                    _WeakHost = new WeakReference<Control>(Host);
                return _WeakHost;
            }
        }
        public UIContext Context { get; private set; }

        internal ControlCollection (UIContext parent) {
            Host = null;
            Context = parent;
        }

        public ControlCollection (Control parent, UIContext context) {
            Host = parent;
            Context = context ?? parent.Context;
        }

        public void CopyTo (List<Control> destination) {
            foreach (var item in Items) {
                if (item == null)
                    throw new ArgumentNullException("Items[...]");
                destination.Add(item);
            }
        }

        public void AddRange (IEnumerable<Control> source) {
            foreach (var control in source)
                Add(control);
        }

        public void AddRange (List<Control> source, int offset, int count) {
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");
            else if (offset >= source.Count)
                throw new ArgumentOutOfRangeException("offset");
            else if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            else if (count > source.Count)
                throw new ArgumentOutOfRangeException("count");

            for (int i = 0; i < count; i++) {
                int j = i + offset;
                if (j >= source.Count)
                    throw new ArgumentOutOfRangeException("count");

                var control = source[j];
                Add(control);
            }
        }

        private void Invalidate () {
            PaintOrderLastValidFrame = TabOrderLastValidFrame = -1;
        }

        public void Adopt (Control control) {
            if (control == null)
                throw new ArgumentNullException("control");

            if (Contains(control))
                return;

            if (control.TryGetParent(out Control parent))
                (parent as IControlContainer).Children.Remove(control);

            Add(control);
        }

        public void Insert (int index, Control control) {
            if (control == null)
                throw new ArgumentNullException("control");
            if (IndexTable.ContainsKey(control))
                throw new InvalidOperationException("Control already in collection");

            if (Host != null)
                control.SetParent(WeakHost);
            else
                control.Context = Context;

            Items.Insert(index, control);
            UpdateIndexTable(index);
            Invalidate();
        }

        public int AddOrGetIndex (Control control) {
            var index = IndexOf(control);
            if (index >= 0)
                return index;
            Add(control);
            return Items.Count - 1;
        }

        public void Add (Control control) {
            if (control == null)
                throw new ArgumentNullException("control");
            if (IndexTable.ContainsKey(control))
                throw new InvalidOperationException("Control already in collection");

            if (Host != null)
                control.SetParent(WeakHost);
            else
                control.Context = Context;

            var newIndex = Items.Count;
            Items.Add(control);
            IndexTable[control] = newIndex;
            Invalidate();
        }

        private void UpdateIndexTable (int startIndex) {
            if (startIndex < 0)
                throw new ArgumentOutOfRangeException("startIndex");

            while (startIndex < Items.Count) {
                var control = Items[startIndex];
                IndexTable[control] = startIndex;
                startIndex++;
            }
        }

        private void FireControlRemoveEvents (Control control) {
            Context?.NotifyControlBecomingInvalidFocusTarget(control, true);
            control.UnsetParent(Host);
        }

        public void Remove (Control control) {
            if (control == null)
                return;

            if (IndexTable.TryGetValue(control, out int deleteAtIndex)) {
                FireControlRemoveEvents(control);
                Items.RemoveAt(deleteAtIndex);
                IndexTable.Remove(control);
                UpdateIndexTable(deleteAtIndex);
            }

            Invalidate();
        }

        public void RemoveAt (int index) {
            var control = Items[index];
            FireControlRemoveEvents(control);
            Items.RemoveAt(index);
            IndexTable.Remove(control);
            UpdateIndexTable(index);
            Invalidate();
        }

        public int IndexOf (Control control) {
            if (control == null)
                return -1;
            if (!IndexTable.TryGetValue(control, out int result))
                return -1;
            return result;
        }

        public void Clear () {
            IndexTable.Clear();

            foreach (var control in Items)
                FireControlRemoveEvents(control);

            Items.Clear();
            Invalidate();
        }

        public List<Control>.Enumerator GetEnumerator () {
            return Items.GetEnumerator();
        }

        public bool Contains (Control control) {
            if (control == null)
                return false;

            return IndexTable.ContainsKey(control);
        }

        public Control this[int index] {
            get {
                return Items[index];
            }
            set {
                if (value == null)
                    throw new ArgumentNullException("value");
                if (Items[index] == value)
                    return;

                var previous = Items[index];
                IndexTable.Remove(previous);
                Items[index].UnsetParent(Host);
                value.SetParent(WeakHost);
                IndexTable[value] = index;
                Items[index] = value;
            }
        }

        private int TabOrderLastValidFrame = -1, PaintOrderLastValidFrame = -1;

        // TODO: Switch these lists to some sort of intrusive linked list system
        private bool PrepareToUpdateSortedList<T> (ref int lastValidFrame, int currentFrame, List<T> targetList, ref OrderRange range) {
            if ((targetList.Count == Count) && (lastValidFrame == currentFrame))
                return false;

            range = new OrderRange {
                Min = (Items.Count > 0) ? int.MaxValue : 0,
                Max = (Items.Count > 0) ? int.MinValue : 0
            };
            targetList.Clear();
            targetList.Capacity = Math.Max(Math.Max(targetList.Capacity, Items.Count), 16);
            lastValidFrame = currentFrame;
            return true;
        }

        private bool UpdatingTabOrder, UpdatingDisplayOrder;

        internal List<Control> InTabOrder (int frameIndex, bool suitableTargetsOnly) {
            if (PrepareToUpdateSortedList(ref TabOrderLastValidFrame, frameIndex, TabOrderedItems, ref TabOrderRange)) {
                if (UpdatingTabOrder)
                    throw new Exception("Already updating tab order list");
                UpdatingTabOrder = true;
                try {
                    for (int i = 0; i < Items.Count; i++) {
                        var item = Items[i];
                        if (!suitableTargetsOnly || item.IsValidFocusTarget)
                            TabOrderedItems.Add(new IndexedControl { 
                                Control = item,
                                Index = i,
                                SortOrder = item.TabOrder
                            });
                    }
                    TabOrderedItems.Sort(TabOrderComparer.Instance);
                    TabOrderedItemsResult.Clear();
                    foreach (var item in TabOrderedItems)
                        TabOrderedItemsResult.Add(item.Control);
                } finally {
                    UpdatingTabOrder = false;
                }
            }
            return TabOrderedItemsResult;
        }

        public void Sort (IComparer<Control> comparer) {
            Items.Sort(comparer);
            Invalidate();
        }

        internal List<Control> InDisplayOrder (int frameIndex) {
            return InDisplayOrder(frameIndex, out OrderRange temp);
        }

        internal List<Control> InDisplayOrder (int frameIndex, out OrderRange range) {
            if (PrepareToUpdateSortedList(ref PaintOrderLastValidFrame, frameIndex, PaintOrderedItems, ref PaintOrderRange)) {
                if (UpdatingDisplayOrder)
                    throw new Exception("Already updating display order list");
                UpdatingDisplayOrder = true;
                try {
                    foreach (var item in Items) {
                        // TODO: Exclude invisible controls
                        PaintOrderRange.Update(item.DisplayOrder);
                        PaintOrderedItems.Add(item);
                    }
                    PaintOrderedItems.Sort(
                        (Host == null) ? (IComparer<Control>)IndexPreservingPaintOrderComparer.Instance : PaintOrderComparer.Instance
                    );
                } finally {
                    UpdatingDisplayOrder = false;
                }
            }
            range = PaintOrderRange;
            return PaintOrderedItems;
        }

        IEnumerator<Control> IEnumerable<Control>.GetEnumerator () {
            return ((IEnumerable<Control>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable)Items).GetEnumerator();
        }

        public int PickNewHighestDisplayOrder (Control ctl, bool topmost) {
            int result = int.MinValue;
            foreach (var item in Items) {
                if (item == ctl)
                    continue;
                var itemIsTopmost = item.DisplayOrder >= (int.MaxValue - 16);
                if (!topmost && itemIsTopmost)
                    continue;
                result = Math.Max(result, item.DisplayOrder + 1);
            }
            if (result <= int.MinValue)
                return Math.Max(ctl.DisplayOrder, 0);
            else
                return result;
        }

        public void ReplaceWith (ref DenseList<Control> newControls) {
            for (int i = 0; i < newControls.Count; i++) {
                var newControl = newControls[i];
                if (newControl == null)
                    throw new ArgumentNullException($"newControls[{i}]");
            }

            for (int i = 0; i < newControls.Count; i++) {
                var newControl = newControls[i];
                if (newControl == null)
                    throw new ArgumentNullException($"newControls[{i}]");
                var isNew = !IndexTable.TryGetValue(newControl, out int oldIndex);
                if (!isNew) {
                    if (oldIndex == i)
                        continue;

                    // HACK: Erase the slot that previously held this control
                    if (oldIndex >= 0)
                        Items[oldIndex] = null;
                }

                if (i < Count) {
                    var oldControl = Items[i];
                    // HACK: Flag this control as potentially dead
                    if (oldControl != null)
                        IndexTable[oldControl] = -1;

                    Items[i] = newControl;
                } else {
                    Items.Add(newControl);
                }

                IndexTable[newControl] = i;
                if (isNew)
                    newControl.SetParent(WeakHost);
            }

            // Find dead controls that we removed and never re-added
            foreach (var kvp in IndexTable) {
                if (kvp.Value <= -1)
                    DeadControlScratchBuffer.Add(kvp.Key);
            }

            while (Count > newControls.Count) {
                var i = Count - 1;
                var extraControl = Items[i];

                // We may have moved this extra control, in which case we don't want to go through
                //  the remove path that fires events.
                if ((extraControl != null) && (IndexTable[extraControl] == i))
                    DeadControlScratchBuffer.Add(extraControl);

                // We moved it, so we just need to remove whatever's at this location
                Items.RemoveAt(i);
            }

#if DEBUG
            foreach (var item in Items) {
                if (item == null)
                    throw new Exception("Bad state");
            }
#endif

            // Fire the relevant notifications for the dead controls and purge them from the table
            foreach (var deadControl in DeadControlScratchBuffer) {
                IndexTable.Remove(deadControl);
                FireControlRemoveEvents(deadControl);
            }

#if DEBUG
            foreach (var kvp in IndexTable) {
                if (kvp.Value <= -1)
                    throw new Exception("Bad state");
            }
#endif

            Invalidate();
            DeadControlScratchBuffer.Clear();
        }
    }

    internal struct IndexedControl {
        public int SortOrder, Index;
        public Control Control;
    }

    internal sealed class TabOrderComparer : IComparer<IndexedControl> {
        public static readonly TabOrderComparer Instance = new TabOrderComparer();

        public int Compare (IndexedControl x, IndexedControl y) {
            var result = x.SortOrder.CompareTo(y.SortOrder);
            if (result == 0)
                result = x.Index.CompareTo(y.Index);
            return result;
        }
    }

    internal sealed class IndexPreservingPaintOrderComparer : IComparer<Control> {
        public static readonly IndexPreservingPaintOrderComparer Instance = new IndexPreservingPaintOrderComparer();

        public int Compare (Control x, Control y) {
            return x.DisplayOrder.CompareTo(y.DisplayOrder);
        }
    }

    internal sealed class PaintOrderComparer : IComparer<Control> {
        public static readonly PaintOrderComparer Instance = new PaintOrderComparer();

        public int Compare (Control x, Control y) {
            var result = x.DisplayOrder.CompareTo(y.DisplayOrder);
            if (result == 0) {
                result = x.TypeID.CompareTo(y.TypeID);
            }
            return result;
        }
    }
}

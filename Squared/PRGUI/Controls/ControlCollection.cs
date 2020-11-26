using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Util;

namespace Squared.PRGUI {
    public class ControlCollection : IEnumerable<Control> {
        private List<Control> PaintOrderedItems = new List<Control>(),
            TabOrderedItems = new List<Control>();
        private List<Control> Items = new List<Control>();
        private Dictionary<Control, int> IndexTable = 
            new Dictionary<Control, int>(new ReferenceComparer<Control>());

        public int Count => Items.Count;
        public Control Parent { get; private set; }
        public UIContext Context { get; private set; }

        internal ControlCollection (UIContext parent) {
            Parent = null;
            Context = parent;
        }

        public ControlCollection (Control parent) {
            Parent = parent;
            Context = parent.Context;
        }

        public void CopyTo (List<Control> destination) {
            foreach (var item in Items)
                destination.Add(item);
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

        public void Add (Control control) {
            if (IndexTable.ContainsKey(control))
                throw new InvalidOperationException("Control already in collection");

            if (Parent != null)
                control.SetParent(Parent);
            else
                control.SetContext(Context);

            var newIndex = Items.Count;
            Items.Add(control);
            IndexTable[control] = newIndex;
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

        public void Remove (Control control) {
            Context?.NotifyControlBeingRemoved(control);
            control.UnsetParent(Parent);

            if (IndexTable.TryGetValue(control, out int deleteAtIndex)) {
                Items.RemoveAt(deleteAtIndex);
                IndexTable.Remove(control);
                UpdateIndexTable(deleteAtIndex);
            } else {
                ;
            }
        }

        public void RemoveAt (int index) {
            var control = Items[index];
            Context?.NotifyControlBeingRemoved(control);
            Items.RemoveAt(index);
            control.UnsetParent(Parent);
            IndexTable.Remove(control);
            UpdateIndexTable(index);
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

            foreach (var control in Items) {
                Context?.NotifyControlBeingRemoved(control);
                control.UnsetParent(Parent);
            }

            Items.Clear();
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
                if (Items[index] == value)
                    return;

                var previous = Items[index];
                IndexTable.Remove(previous);
                Items[index].UnsetParent(Parent);
                value.SetParent(Parent);
                IndexTable[value] = index;
                Items[index] = value;
            }
        }

        private int TabOrderLastValidFrame = -1, PaintOrderLastValidFrame = -1;

        private bool PrepareToUpdateSortedList (ref int lastValidFrame, int currentFrame, List<Control> targetList) {
            if ((targetList.Count == Count) && (lastValidFrame == currentFrame))
                return false;

            targetList.Clear();
            targetList.Capacity = Math.Max(Math.Max(targetList.Capacity, Items.Count), 16);
            lastValidFrame = currentFrame;
            return true;
        }

        internal List<Control> InTabOrder (int frameIndex, bool suitableTargetsOnly) {
            if (PrepareToUpdateSortedList(ref TabOrderLastValidFrame, frameIndex, TabOrderedItems)) {
                foreach (var item in Items)
                    if (!suitableTargetsOnly || item.IsValidFocusTarget)
                        TabOrderedItems.Add(item);
                TabOrderedItems.Sort(Control.TabOrderComparer.Instance);
            }
            return TabOrderedItems;
        }

        internal List<Control> InPaintOrder (int frameIndex) {
            if (PrepareToUpdateSortedList(ref PaintOrderLastValidFrame, frameIndex, PaintOrderedItems)) {
                foreach (var item in Items)
                    PaintOrderedItems.Add(item);
                PaintOrderedItems.Sort(Control.PaintOrderComparer.Instance);
            }
            return PaintOrderedItems;
        }

        IEnumerator<Control> IEnumerable<Control>.GetEnumerator () {
            return ((IEnumerable<Control>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable)Items).GetEnumerator();
        }
    }
}

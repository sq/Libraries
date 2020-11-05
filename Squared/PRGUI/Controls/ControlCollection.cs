using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI {
    public class ControlCollection : IEnumerable<Control> {
        private List<Control> PaintOrderedItems = new List<Control>(),
            TabOrderedItems = new List<Control>();
        private List<Control> Items = new List<Control>();

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

        public void Add (Control control) {
            if (Items.Contains(control))
                throw new InvalidOperationException("Control already in collection");

            if (Parent != null)
                control.SetParent(Parent);
            else
                control.SetContext(Context);
            Items.Add(control);
        }

        public void Remove (Control control) {
            control.UnsetParent(Parent);
            Items.Remove(control);
        }

        public void RemoveAt (int index) {
            var control = Items[index];
            Items.RemoveAt(index);
            control.UnsetParent(Parent);
        }

        public int IndexOf (Control control) {
            return Items.IndexOf(control);
        }

        public void Clear () {
            foreach (var control in Items)
                control.UnsetParent(Parent);

            Items.Clear();
        }

        public List<Control>.Enumerator GetEnumerator () {
            return Items.GetEnumerator();
        }

        public bool Contains (Control control) {
            return Items.Contains(control);
        }

        public Control this[int index] {
            get {
                return Items[index];
            }
            set {
                if (Items[index] == value)
                    return;

                Items[index].UnsetParent(Parent);
                value.SetParent(Parent);
                Items[index] = value;
            }
        }

        internal List<Control> InTabOrder (bool suitableTargetsOnly) {
            TabOrderedItems.Clear();
            TabOrderedItems.Capacity = Math.Max(Math.Max(TabOrderedItems.Capacity, Items.Count), 16);
            foreach (var item in Items)
                if (!suitableTargetsOnly || item.IsValidFocusTarget)
                    TabOrderedItems.Add(item);
            TabOrderedItems.Sort(Control.TabOrderComparer.Instance);
            return TabOrderedItems;
        }

        internal List<Control> InPaintOrder () {
            PaintOrderedItems.Clear();
            PaintOrderedItems.Capacity = Math.Max(Math.Max(PaintOrderedItems.Capacity, Items.Count), 16);
            foreach (var item in Items)
                PaintOrderedItems.Add(item);
            PaintOrderedItems.Sort(Control.PaintOrderComparer.Instance);
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

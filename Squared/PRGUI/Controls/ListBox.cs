using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public interface IListBox {
        int Count { get; }
        int SelectedIndex { get; set; }
        object SelectedItem { get; set; }
        bool Virtual { get; set; }
    }

    public class ListBox<T> : 
        Container, Accessibility.IReadingTarget, Accessibility.IAcceleratorSource, 
        IValueControl<T>, ISelectionBearer, IListBox,
        IPartiallyIntangibleControl, IFuzzyHitTestTarget 
    {
        public static bool SelectOnMouseDown = false;

        public const int ControlMinimumHeight = 75, ControlMinimumWidth = 150;

        protected ItemListManager<T> Manager;

        public IEqualityComparer<T> Comparer {
            get => Manager.Comparer;
            set => Manager.Comparer = value;
        }
        public ItemList<T> Items => Manager.Items;

        int IListBox.Count => Items.Count;
        object IListBox.SelectedItem {
            get => SelectedItem;
            set => SelectedItem = (T)value;
        }

        private CreateControlForValueDelegate<T> _CreateControlForValue,
            DefaultCreateControlForValue;
        public Func<T, AbstractString> FormatValue = null;

        public CreateControlForValueDelegate<T> CreateControlForValue {
            get => _CreateControlForValue;
            set {
                if (_CreateControlForValue == value)
                    return;
                _CreateControlForValue = value;
                NeedsUpdate = true;
            }
        }

        public const float AutoscrollMarginSize = 24f;

        public string Description;

        public int SelectedIndex {
            get => Manager.SelectedIndex;
            set => Manager.SelectedIndex = value;
        }

        private bool SelectedItemHasChangedSinceLastUpdate = true;

        public T SelectedItem {
            get => Manager.SelectedItem;
            set {
                if (!Manager.TrySetSelectedItem(ref value))
                    return;
                DesiredScrollOffset = null;
                SelectedItemHasChangedSinceLastUpdate = true;
                FireEvent(UIEvents.ValueChanged, SelectedItem);
            }
        }

        private bool _Virtual = false;
        public bool Virtual {
            get => _Virtual;
            set {
                if (_Virtual == value)
                    return;

                Children.Clear();
                NeedsUpdate = true;
                _Virtual = value;
            }
        }

        private int VirtualItemOffset = 0;
        private float VirtualItemHeight = 1; // HACK, will be adjusted each frame
        private int VirtualViewportSize = 2; // HACK, will be adjusted up/down each frame

        private int _Version;
        private bool NeedsUpdate = true;

        protected int PageSize { get; private set; }

        bool ISelectionBearer.HasSelection => Manager.SelectedIndex >= 0;
        // FIXME: We should expand the width here
        RectF? ISelectionBearer.SelectionRect => Manager.SelectedControl?.GetRect();
        Control ISelectionBearer.SelectedControl => Manager.SelectedControl;

        public ListBox ()
            : this (null) {
        }

        public ListBox (IEqualityComparer<T> comparer = null) 
            : base () {
            PageSize = 1;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            ContainerFlags = ControlFlags.Container_Column | ControlFlags.Container_Align_Start;
            ClipChildren = true;
            ShowHorizontalScrollbar = false;
            Scrollable = true;
            // FIXME
            Manager = new ItemListManager<T>(comparer ?? EqualityComparer<T>.Default);
            DefaultCreateControlForValue = _DefaultCreateControlForValue;
        }

        private Control _DefaultCreateControlForValue (ref T value, Control existingControl) {
            var st = (existingControl as StaticText) ?? new StaticText();
            var text =
                (FormatValue != null)
                    ? FormatValue(value)
                    : value.ToString();
            st.Text = text;
            st.Wrap = false;
            st.AutoSizeWidth = false;
            st.Data.Set<T>(ref value);
            return st;
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            if (minimumWidth.HasValue)
                minimumWidth = Math.Max(minimumWidth.Value, ControlMinimumWidth * Context.Decorations.SizeScaleRatio.X);
            else
                minimumWidth = ControlMinimumWidth * Context.Decorations.SizeScaleRatio.X;
            if (minimumHeight.HasValue)
                minimumHeight = Math.Max(minimumHeight.Value, ControlMinimumHeight * Context.Decorations.SizeScaleRatio.Y);
            else
                minimumHeight = ControlMinimumHeight * Context.Decorations.SizeScaleRatio.Y;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.ListBox;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            bool scrollOffsetChanged = false;

            if (Virtual) {
                var selectedIndex = SelectedIndex;

                while (true) {
                    var newItemOffset = Math.Max((int)(ScrollOffset.Y / VirtualItemHeight) - 1, 0);
                    var newEndItemOffset = Math.Min(newItemOffset + VirtualViewportSize, Items.Count - 1);

                    int delta = 0;
                    if (selectedIndex >= 0) {
                        // HACK: We need to offset by more than 1 because of the virtual viewport padding
                        if (selectedIndex < newItemOffset)
                            delta = -(newItemOffset - selectedIndex) - 2;
                        else if (selectedIndex >= newEndItemOffset)
                            delta = (selectedIndex - newEndItemOffset) + 4;
                    }

                    if (SelectedItemHasChangedSinceLastUpdate && (delta != 0) && !scrollOffsetChanged) {
                        if (delta != 0) {
                            var newOffset = ScrollOffset;
                            newOffset.Y += (delta * VirtualItemHeight);
                            if (TrySetScrollOffset(newOffset, false)) {
                                scrollOffsetChanged = true;
                                continue;
                            }
                        }
                    }

                    if (newItemOffset != VirtualItemOffset) {
                        VirtualItemOffset = newItemOffset;
                        NeedsUpdate = true;
                    }
                    var newScrollOffset = -VirtualItemOffset * VirtualItemHeight;
                    if (newScrollOffset != VirtualScrollOffset.Y) {
                        VirtualScrollOffset.Y = newScrollOffset;
                        scrollOffsetChanged = true;
                        NeedsUpdate = true;
                    }

                    break;
                }
            } else {
                if (VirtualScrollOffset != Vector2.Zero) {
                    scrollOffsetChanged = true;
                    VirtualScrollOffset = Vector2.Zero;
                }
                VirtualScrollRegion = Vector2.Zero;
                NeedsUpdate |= (Items.Count != Children.Count);
            }

            if (Items.Version != _Version)
                NeedsUpdate = true;

            bool hadKeyboardSelection = false;
            if (NeedsUpdate) {
                hadKeyboardSelection = Children.Contains(Context.KeyboardSelection);
                var priorControl = Manager.SelectedControl;
                // FIXME: Why do virtual list items flicker for a frame before appearing?
                Items.GenerateControls(
                    Children, CreateControlForValue ?? DefaultCreateControlForValue,
                    offset: Virtual ? VirtualItemOffset : 0, count: Virtual ? VirtualViewportSize : int.MaxValue
                );
                _Version = Items.Version;
                // HACK: Without doing this, old content bounds can be kept that are too big/too small
                HasContentBounds = false;
            }

            if (SelectedItemHasChangedSinceLastUpdate || NeedsUpdate || hadKeyboardSelection) {
                var newControl = Manager.SelectedControl;
                OnSelectionChange(newControl, SelectedItemHasChangedSinceLastUpdate);
                if (hadKeyboardSelection)
                    Context.OverrideKeyboardSelection(newControl, forUser: false);
            }

            NeedsUpdate = false;
            SelectedItemHasChangedSinceLastUpdate = false;

            if (scrollOffsetChanged)
                OnDisplayOffsetChanged();

            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            if (result.IsInvalid)
                return result;

            foreach (var child in Children) {
                var lk = child.LayoutKey;
                var m = context.Layout.GetMargins(lk);
                m.Top = m.Bottom = 0;
                context.Layout.SetMargins(lk, m);
            }
            return result;
        }

        protected override void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(context, ref relayoutRequested);

            if (Children.Count > 0) {
                VirtualItemHeight = Children[0].GetRect(applyOffset: false).Height;
                // HACK: Traditional listboxes on windows scroll multiple item(s) at a time on mousewheel
                //  instead of scrolling on a per-pixel basis
                ScrollSpeedMultiplier = (VirtualItemHeight / 14);
            }

            var box = GetRect(applyOffset: false, contentRect: true);
            var newViewportSize = Math.Max((int)(box.Height / VirtualItemHeight) + 4, 8);
            if (newViewportSize != VirtualViewportSize) {
                VirtualViewportSize = newViewportSize;
                NeedsUpdate = true;
                relayoutRequested = true;
            }
            VirtualScrollRegion.Y = (Items.Count * VirtualItemHeight) - box.Height;
        }

        public void Invalidate () {
            NeedsUpdate = true;
        }

        // HACK
        private bool _OverrideHitTestResults = true;

        protected override bool OnHitTest (RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            var ok = base.OnHitTest(box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            // HACK: Ensure that hit-test does not pass through to our individual items. We want to handle all events for them
            if (ok && _OverrideHitTestResults)
                result = this;
            return ok;
        }

        private void UpdateTextDecorators (Control selectedControl) {
            // FIXME: Optimize this for large lists
            foreach (var child in Children) {
                child.Appearance.TextDecorator = ((child == selectedControl) && (child.Appearance.BackgroundColor.pLinear == null))
                    ? Context?.Decorations.Selection 
                    : null;
            }
        }

        private void OnSelectionChange (Control newControl, bool fireEvent) {
            UpdateTextDecorators(newControl);

            // FIXME
            // if ((fireEvent) && (previous != newControl))
            if (fireEvent)
                FireEvent(UIEvents.SelectionChanged, newControl);
        }

        private Control LocateContainingChild (Control control) {
            var current = control;
            while (current != null) {
                if (!control.TryGetParent(out Control parent))
                    return null;
                if (parent == this)
                    return current;
                else
                    current = parent;
            }

            return null;
        }

        private Control ChildFromGlobalPosition (LayoutContext context, Vector2 globalPosition) {
            try {
                var rect = this.GetRect(contentRect: true);
                if (!rect.Contains(globalPosition))
                    return null;

                globalPosition.X = rect.Left + 6;
                _OverrideHitTestResults = false;

                var child = HitTest(globalPosition, false, false);
                if ((child ?? this) == this) {
                    globalPosition.X = rect.Center.X;
                    child = HitTest(globalPosition, false, false);
                }

                if (child == this)
                    return null;
                else
                    return LocateContainingChild(child);
            } finally {
                _OverrideHitTestResults = true;
            }
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (ProcessMouseEventForScrollbar(name, args))
                return true;

            var control = ChildFromGlobalPosition(Context.Layout, args.RelativeGlobalPosition);
            // FIXME: If we handle Click then drag-to-scroll won't select an item,
            //  but having it not select on mousedown feels bad
            if (
                ((name == UIEvents.MouseDown) && SelectOnMouseDown) ||
                (name == UIEvents.Click)
            ) {
                if (
                    args.Box.Contains(args.RelativeGlobalPosition) && 
                    Items.GetValueForControl(control, out T newItem)
                ) {
                    SelectedItem = newItem;
                    return (name == UIEvents.Click);
                }
            }

            return false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);
        }

        private void UpdateKeyboardSelection (T item, bool forUser) {
            // HACK: Tell the context that the current item is the keyboard selection,
            //  so that autoscroll and tooltips will happen for it.
            Context.OverrideKeyboardSelection(Manager.SelectedControl, forUser);
        }

        private void SelectItemViaKeyboard (T item) {
            SelectedItem = item;
            UpdateKeyboardSelection(item, true);
        }

        public void AdjustSelection (int delta) {
            if (Manager.TryAdjustSelection(delta, out T newItem))
                SelectItemViaKeyboard(newItem);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            if (args.Key != null && UIContext.ModifierKeys.Contains(args.Key.Value))
                return false;
            if (name != UIEvents.KeyPress)
                return true;

            switch (args.Key) {
                case Keys.Home:
                    SelectItemViaKeyboard(Items.FirstOrDefault());
                    return true;
                case Keys.End:
                    SelectItemViaKeyboard(Items.LastOrDefault());
                    return true;
                case Keys.PageUp:
                case Keys.PageDown:
                    AdjustSelection(args.Key == Keys.PageUp ? -PageSize : PageSize);
                    return true;
                case Keys.Up:
                case Keys.Down:
                    AdjustSelection(args.Key == Keys.Up ? -1 : 1);
                    return true;
                default:
                    return false;
            }
        }

        protected override void OnRasterizeChildren (UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
            var selectionDecorator = context.DecorationProvider.ListSelection;
            var selectedControl = Manager.SelectedControl;
            if (
                (selectionDecorator != null) && (selectedControl != null)
            ) {
                var selectionBox = selectedControl.GetRect();
                selectionBox.Top += selectionDecorator.Margins.Top;
                selectionBox.Left = settings.ContentBox.Left + selectionDecorator.Margins.Left;
                selectionBox.Height -= selectionDecorator.Margins.Y;
                selectionBox.Width = settings.ContentBox.Width - selectionDecorator.Margins.X;

                // HACK: Selection boxes are normally rasterized on the content layer, but we want to rasterize
                //  the selection on the Below layer beneath items' decorations and content.
                var oldPass = context.Pass;
                context.Pass = RasterizePasses.Content;
                var selectionSettings = new DecorationSettings {
                    Box = selectionBox,
                    ContentBox = selectionBox,
                    State = settings.State
                };
                // FIXME
                selectionDecorator.Rasterize(context, ref passSet.Below, selectionSettings);
                context.Pass = oldPass;

                passSet.Below.Layer += 1;
            }

            base.OnRasterizeChildren(context, ref passSet, settings);
        }

        private int lastOffset1 = -1,
            lastOffset2 = -1;

        protected override void RasterizeChildrenInOrder (
            ref UIOperationContext context, ref RasterizePassSet passSet, 
            int layer1, int layer2, int layer3, 
            ref int maxLayer1, ref int maxLayer2, ref int maxLayer3
        ) {
            if (Virtual) {
                base.RasterizeChildrenInOrder(
                    ref context, ref passSet, layer1, layer2, layer2,
                    ref maxLayer1, ref maxLayer2, ref maxLayer3
                );
                PageSize = Math.Max(VirtualViewportSize - 4, 2);
            } else {
                var selectedControl = Manager.SelectedControl;
                var displayPageSize = RasterizeChildrenFromCenter(
                    ref context, ref passSet, 
                    GetRect(), Children, selectedControl,
                    layer1, layer2, layer3, 
                    ref maxLayer1, ref maxLayer2, ref maxLayer3,
                    ref lastOffset1, ref lastOffset2
                );

                // FIXME: If we're partially offscreen this value will be too small
                PageSize = Math.Max(1, displayPageSize / 2);
            }
        }

        private void CalculateScrollable (UIContext context) {
            context.UpdateSubtreeLayout(this);
            if (GetContentBounds(context, out RectF contentBounds))
                Scrollable = contentBounds.Height >= Height.Maximum;
        }

        StringBuilder TextBuilder = new StringBuilder();

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                TextBuilder.Clear();
                if (Description != null)
                    TextBuilder.Append(Description);
                else {
                    var ttc = TooltipContent.Get(this).ToString();
                    if (ttc != null)
                        TextBuilder.Append(ttc);
                }

                if (SelectedItem is Accessibility.IReadingTarget irt) {
                    TextBuilder.Append(": ");
                    var existingLength = TextBuilder.Length;
                    irt.FormatValueInto(TextBuilder);
                    if (TextBuilder.Length == existingLength)
                        TextBuilder.Append(irt.Text.ToString());
                } else {
                    // FIXME: Fallback to something else here?
                }

                return TextBuilder;
            }
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            if (SelectedItem == null)
                return;

            if (SelectedItem is Accessibility.IReadingTarget irt) {
                irt.FormatValueInto(sb);
                if (sb.Length == 0)
                    sb.Append(irt.Text);
            } else {
                sb.Append(SelectedItem.ToString());
            }
        }

        IEnumerable<Accessibility.AcceleratorInfo> Accessibility.IAcceleratorSource.Accelerators {
            get {
                var si = SelectedIndex;
                if (si > 0) {
                    Items.GetControlForValue(Items[si - 1], out Control prev);
                    yield return new Accessibility.AcceleratorInfo(prev, Keys.Up);
                }
                if (si < (Items.Count - 1)) {
                    Items.GetControlForValue(Items[si + 1], out Control next);
                    yield return new Accessibility.AcceleratorInfo(next, Keys.Down);
                }
            }
        }

        T IValueControl<T>.Value {
            get => SelectedItem;
            set => SelectedItem = value;
        }

        public int Count => Items.Count;

        public T this [int index] {
            get => Items[index];
            set {
                Items[index] = value;
                Invalidate();
            }
        }

        public void RemoveAt (int index) {
            Items.RemoveAt(index);
            Invalidate();
        }

        public void Clear () {
            Items.Clear();
            Invalidate();
        }

        public void Add (T value) {
            Items.Add(value);
            Invalidate();
        }

        bool IPartiallyIntangibleControl.IsIntangibleAtPosition (Vector2 position) => false;

        int IFuzzyHitTestTarget.WalkTree (
            List<FuzzyHitTest.Result> output, ref FuzzyHitTest.Result thisControl, Vector2 position, Func<Control, bool> predicate, float maxDistanceSquared
        ) => 0;
        bool IFuzzyHitTestTarget.WalkChildren => false;
    }
}

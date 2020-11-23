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
    public class ListBox<T> : Container, Accessibility.IReadingTarget, Accessibility.IAcceleratorSource, IValueControl<T> {
        public const int ControlMinimumHeight = 75, ControlMinimumWidth = 150;

        public IEqualityComparer<T> Comparer;
        public ItemList<T> Items { get; private set; }

        public CreateControlForValueDelegate<T> CreateControlForValue = null;
        public Func<T, AbstractString> FormatValue = null;

        public const float AutoscrollMarginSize = 24f;

        public string Description;

        public int SelectedIndex => (_SelectedItem != null)
            ? Items.IndexOf(_SelectedItem)
            : -1;

        private T _SelectedItem = default(T);
        public T SelectedItem {
            get => _SelectedItem;
            set {
                if (Comparer.Equals(value, _SelectedItem))
                    return;
                if (!Items.Contains(value))
                    throw new ArgumentException("Value not in items list");
                _SelectedItem = value;
                FireEvent(UIEvents.ValueChanged, _SelectedItem);
            }
        }

        private bool NeedsUpdate = true;

        protected int PageSize { get; private set; }

        public ListBox ()
            : this (null) {
        }

        public ListBox (IEqualityComparer<T> comparer = null) 
            : base () {
            PageSize = 1;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Wrap | ControlFlags.Container_Align_Start;
            ClipChildren = true;
            ShowHorizontalScrollbar = false;
            Comparer = comparer ?? EqualityComparer<T>.Default;
            Scrollable = true;
            // FIXME
            Items = new ItemList<T>(Comparer);
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            if (minimumWidth.HasValue)
                minimumWidth = Math.Max(minimumWidth.Value, ControlMinimumWidth);
            else
                minimumWidth = ControlMinimumWidth;
            if (minimumHeight.HasValue)
                minimumHeight = Math.Max(minimumHeight.Value, ControlMinimumHeight);
            else
                minimumHeight = ControlMinimumHeight;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.ListBox;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // FIXME
            NeedsUpdate |= (Items.Count != Children.Count);
            if (NeedsUpdate) {
                NeedsUpdate = false;
                Items.GenerateControls(Children, CreateControlForValue, FormatValue);
            }

            var result = base.OnGenerateLayoutTree(context, parent, existingKey);
            foreach (var child in Children) {
                var lk = child.LayoutKey;
                var cf = context.Layout.GetLayoutFlags(lk);
                context.Layout.SetLayoutFlags(lk, cf | ControlFlags.Layout_ForceBreak);
                var m = context.Layout.GetMargins(lk);
                m.Top = m.Bottom = 0;
                context.Layout.SetMargins(lk, m);
            }
            return result;
        }

        public void Invalidate () {
            NeedsUpdate = true;
        }

        // HACK
        private bool _OverrideHitTestResults = true;

        protected override bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            var ok = base.OnHitTest(context, box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            // HACK: Ensure that hit-test does not pass through to our individual items. We want to handle all events for them
            if (ok && _OverrideHitTestResults)
                result = this;
            return ok;
        }

        private void OnSelectionChange (Control previous, Control newControl) {
            foreach (var child in Children) {
                child.CustomTextDecorator = ((child == newControl) && (child.BackgroundColor.pLinear == null))
                    ? Context?.Decorations.Selection 
                    : null;
            }

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
                var rect = this.GetRect(context, contentRect: true);
                if (!rect.Contains(globalPosition))
                    return null;

                globalPosition.X = rect.Left + 6;
                _OverrideHitTestResults = false;

                var child = HitTest(context, globalPosition, false, false);
                if ((child ?? this) == this) {
                    globalPosition.X = rect.Center.X;
                    child = HitTest(context, globalPosition, false, false);
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
            // Console.WriteLine($"menu.{name}");

            var control = ChildFromGlobalPosition(Context.Layout, args.RelativeGlobalPosition);

            if (name == UIEvents.MouseDown) {
                if (!args.Box.Contains(args.RelativeGlobalPosition))
                    control = null;
                else if (Items.GetValueForControl(control, out T newItem))
                    SelectedItem = newItem;
            } else if (
                (name == UIEvents.MouseMove) &&
                (control != null) &&
                (args.Buttons != MouseButtons.None)
            ) {
                if (Items.GetValueForControl(control, out T newItem))
                    SelectedItem = newItem;
            }

            return true;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);
        }

        private void SelectItemViaKeyboard (T item) {
            SelectedItem = item;
            // HACK: Tell the context that the current item is the keyboard selection,
            //  so that autoscroll and tooltips will happen for it.
            if (Items.GetControlForValue(ref item, out Control control))
                Context.OverrideKeyboardSelection(control);
        }

        public bool AdjustSelection (int direction) {
            if (Children.Count == 0)
                return false;

            var selectedIndex = Items.IndexOf(_SelectedItem);
            if (selectedIndex < 0)
                selectedIndex = direction > 0 ? 0 : Items.Count - 1;
            else
                selectedIndex += direction;

            int steps = Children.Count;
            while (steps-- > 0) {
                selectedIndex = Arithmetic.Wrap(selectedIndex, 0, Items.Count - 1);
                var item = Items[selectedIndex];
                SelectItemViaKeyboard(item);
            }

            return false;
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
            if (Items.GetControlForValue(ref _SelectedItem, out Control selectedControl)) {
                var selectionBox = selectedControl.GetRect(context.Layout, true, false);
                selectionBox.Left = settings.ContentBox.Left;
                selectionBox.Width = settings.ContentBox.Width;

                // HACK: Selection boxes are normally rasterized on the content layer, but we want to rasterize
                //  the selection on the Below layer beneath items' decorations and content.
                var oldPass = context.Pass;
                context.Pass = RasterizePasses.Content;
                var selectionSettings = new DecorationSettings {
                    Box = selectionBox,
                    ContentBox = selectionBox,
                    State = ControlStates.Hovering | ControlStates.Focused
                };
                // FIXME
                context.DecorationProvider.MenuSelection?.Rasterize(context, ref passSet.Below, selectionSettings);
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
            Items.GetControlForValue(ref _SelectedItem, out Control selectedControl);
            var displayPageSize = RasterizeChildrenFromCenter(
                ref context, ref passSet, 
                GetRect(context.Layout), Children, selectedControl,
                layer1, layer2, layer3, 
                ref maxLayer1, ref maxLayer2, ref maxLayer3,
                ref lastOffset1, ref lastOffset2
            );

            // FIXME: If we're partially offscreen this value will be too small
            PageSize = Math.Max(1, displayPageSize / 2);
        }

        private void CalculateScrollable (UIContext context) {
            context.UpdateSubtreeLayout(this);
            if (GetContentBounds(context, out RectF contentBounds)) {
                Scrollable = contentBounds.Height >= MaximumHeight;
                // HACK: Changing the scrollable flag invalidates our layout info, so recalculate it
                // If we don't do this the menu will overhang on the right side
                if (Scrollable)
                    context.UpdateSubtreeLayout(this);
            }
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

                if (SelectedItem != null) {
                    var irt = SelectedItem as Accessibility.IReadingTarget;
                    if (irt != null) {
                        TextBuilder.Append(": ");
                        var existingLength = TextBuilder.Length;
                        irt.FormatValueInto(TextBuilder);
                        if (TextBuilder.Length == existingLength)
                            TextBuilder.Append(irt.Text.ToString());
                    } else {
                        // FIXME: Fallback to something else here?
                    }
                }

                return TextBuilder;
            }
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            if (SelectedItem == null)
                return;

            var irt = SelectedItem as Accessibility.IReadingTarget;
            if (irt != null) {
                irt.FormatValueInto(sb);
                if (sb.Length == 0)
                    sb.Append(irt.Text);
            } else {
                sb.Append(SelectedItem.ToString());
            }
        }

        public int Count => Children.Count;

        IEnumerable<KeyValuePair<Control, string>> Accessibility.IAcceleratorSource.Accelerators {
            get {
                if (SelectedIndex > 0)
                    yield return new KeyValuePair<Control, string>(this[SelectedIndex - 1], "Up");
                if (SelectedIndex < Children.Count)
                    yield return new KeyValuePair<Control, string>(this[SelectedIndex + 1], "Down");
            }
        }

        T IValueControl<T>.Value {
            get => _SelectedItem;
            set => SelectedItem = value;
        }

        public Control this [int index] {
            get => Children[index];
            set => Children[index] = value;
        }
        public void RemoveAt (int index) => Children.RemoveAt(index);
        public void Clear () => Children.Clear();
        public void Add (Control child) => Children.Add(child);
    }
}

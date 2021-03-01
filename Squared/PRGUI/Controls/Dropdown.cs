using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public delegate Control CreateControlForValueDelegate<T> (ref T value, Control existingControl);

    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget, IMenuListener, IValueControl<T>, IHasDescription {
        private readonly Menu ItemsMenu = new Menu {
            DeselectOnMouseLeave = false
        };

        public string Description { get; set; }

        public string Label;
        public CreateControlForValueDelegate<T> CreateControlForValue = null;
        public Func<T, AbstractString> FormatValue = null;
        private CreateControlForValueDelegate<T> DefaultCreateControlForValue;

        private int _Version;
        protected ItemListManager<T> Manager;

        public IEqualityComparer<T> Comparer {
            get => Manager.Comparer;
            set => Manager.Comparer = value;
        }
        public ItemList<T> Items => Manager.Items;

        private bool NeedsUpdate = true;
        private bool MenuJustClosed = false;

        public int SelectedIndex {
            get => Manager.SelectedIndex;
            set => Manager.SelectedIndex = value;
        }

        public T SelectedItem {
            get => Manager.SelectedItem;
            set {
                SetSelectedItem(value, false);
            }
        }

        public void SetSelectedItem (T value, bool forUserInput) {
            if (!Manager.TrySetSelectedItem(ref value, forUserInput))
                return;
            Invalidate();
            NeedsUpdate = true;
            FireEvent(UIEvents.ValueChanged, SelectedItem);
            if (forUserInput)
                FireEvent(UIEvents.ValueChangedByUser, SelectedItem);
        }

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                // FIXME: Reverse this order?
                if (!string.IsNullOrWhiteSpace(Label) && Label.Contains('{'))
                    return string.Format(Label, GetValueText());
                else if (Description != null)
                    return $"{Description}: {GetValueText()}";
                else if (TooltipContent)
                    return TooltipContent.Get(this);

                if (SelectedItem is Accessibility.IReadingTarget irt)
                    return irt.Text;
                else
                    return GetValueText();
            }
        }

        T IValueControl<T>.Value {
            get => SelectedItem;
            set => SelectedItem = value;
        }

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            if (SelectedItem is Accessibility.IReadingTarget irt)
                irt.FormatValueInto(sb);
            else
                sb.Append(GetValueText());
        }

        AbstractString GetValueText () {
            if (FormatValue != null)
                return FormatValue(SelectedItem);
            else if (SelectedItem is StaticTextBase stb)
                return stb.Text;
            else
                return SelectedItem?.ToString();
        }

        public Dropdown ()
            : this (null) {
        }

        public Dropdown (IEqualityComparer<T> comparer = null) 
            : base () {
            AcceptsFocus = true;
            AcceptsMouseInput = true;
            Manager = new ItemListManager<T>(comparer ?? EqualityComparer<T>.Default);
            DefaultCreateControlForValue = _DefaultCreateControlForValue;
        }

        protected override void ComputePadding (UIOperationContext context, IDecorator decorations, out Margins result) {
            base.ComputePadding(context, decorations, out result);
            var paddingScale = context.DecorationProvider.SpacingScaleRatio * context.DecorationProvider.PaddingScaleRatio;
            var width = context.DecorationProvider.DropdownArrow.Padding.X * context.DecorationProvider.SizeScaleRatio.X;
            result.Right += width / paddingScale.X;
            // FIXME: Use the Y value?
        }

        private Control _DefaultCreateControlForValue (ref T value, Control existingControl) {
            var st = (existingControl as StaticText) ?? new StaticText();
            var text =
                (FormatValue != null)
                    ? FormatValue(value)
                    : value.ToString();
            st.Text = text;
            st.Data.Set<T>(ref value);
            return st;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Dropdown;
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            if (NeedsUpdate || (Items.Version != _Version))
                Update();

            return base.OnGenerateLayoutTree(ref context, parent, existingKey);
        }

        protected void Update () {
            _Version = Items.Version;
            NeedsUpdate = false;
            if (!Manager.HasSelectedItem && (Items.Count > 0))
                // FIXME: ForUser?
                SetSelectedItem(Items[0], true);

            if (!string.IsNullOrWhiteSpace(Label)) {
                // HACK
                if (Label.Contains('{'))
                    Text = string.Format(Label.ToString(), GetValueText());
                else
                    Text = Label;
            } else
                Text = GetValueText();
        }

        protected Control UpdateMenu () {
            Control result = null;

            if (Description != null)
                ItemsMenu.Description = $"Menu {Description}";

            Items.GenerateControls(
                ItemsMenu.Children, CreateControlForValue ?? DefaultCreateControlForValue
            );

            return result;
        }

        private void ShowMenu () {
            // When the menu closes it will set this flag. If it was closed because a click occurred
            //  on us (outside of the menu), we will hit this point with the flag set and know not
            //  to reopen the menu
            // FIXME: Maybe we should clear it here?
            if (MenuJustClosed)
                return;

            UpdateMenu();

            // FIXME: The ideal behavior is for another click when open to close the dropdown
            if (ItemsMenu.IsActive)
                return;

            var box = GetRect(contentRect: false);
            ItemsMenu.Width.Minimum = box.Width;
            var selectedControl = Manager.SelectedControl;
            ItemsMenu.Show(
                Context, this, 
                anchorAlignment: new Vector2(0.5f, 1f), localAlignment: new Vector2(0.5f, 0f), 
                selectedItem: selectedControl
            );
            MenuJustClosed = false;
        }

        protected override bool OnEvent<TArgs> (string name, TArgs args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (args is KeyEventArgs)
                return OnKeyEvent(name, (KeyEventArgs)(object)args);
            else
                return base.OnEvent(name, args);
        }

        private bool OnKeyEvent (string name, KeyEventArgs args) {
            switch (name) {
                case UIEvents.KeyPress:
                    Context.OverrideKeyboardSelection(this, true);
                    var oldSelection = SelectedItem;
                    var oldIndex = Items.IndexOf(ref oldSelection, Comparer);
                    switch (args.Key) {
                        case Keys.Up:
                        case Keys.Down:
                            if (Manager.TryMoveSelection(
                                (args.Key == Keys.Up) ? -1 : 1, false
                            ))
                                SetSelectedItem(Manager.SelectedItem, true);
                            return true;
                        case Keys.Space:
                            ShowMenu();
                            return true;
                        default:
                            return false;
                    }
            }

            return false;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                ShowMenu();
                return true;
            }

            return false;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (ItemsMenu.IsActive)
                settings.State |= ControlStates.Pressed;
            base.OnRasterize(context, ref renderer, settings, decorations);

            context.DecorationProvider.DropdownArrow?.Rasterize(context, ref renderer, settings);

            // FIXME: There is probably a better place to clear this flag
            MenuJustClosed = false;
        }

        public override string ToString () {
            return $"Dropdown #{GetHashCode():X8} '{GetTrimmedText(GetValueText().ToString())}'";
        }

        void IMenuListener.Shown (Menu menu) {
        }

        void IMenuListener.Closed (Menu menu) {
            NeedsUpdate = true;
            MenuJustClosed = true;
        }

        void IMenuListener.ItemSelected (Menu menu, Control item) {
        }

        void IMenuListener.ItemChosen (Menu menu, Control item) {
            if (Items.GetValueForControl(item, out T value))
                SetSelectedItem(value, true);
        }
    }
}

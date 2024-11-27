﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls.SpecialInterfaces {
    public interface IHasCreateControlForValueProperty<T> {
        public CreateControlForValueDelegate<T> CreateControlForValue { get; set; }
    }
}

namespace Squared.PRGUI.Controls {
    public delegate Control CreateControlForValueDelegate<T> (ref T value, Control existingControl);
    public delegate Control CreateControlForValueDelegate<T, TUserData> (ref T value, Control existingControl, ref TUserData userData);

    public class Dropdown<T> : StaticTextBase, Accessibility.IReadingTarget, 
        IMenuListener, IValueControl<T>, IHasDescription, 
        SpecialInterfaces.IHasCreateControlForValueProperty<T>
    {
        private readonly Menu ItemsMenu = new Menu {
            DeselectOnMouseLeave = false
        };

        public bool EnableFiltering {
            get => ItemsMenu.EnableFiltering;
            set => ItemsMenu.EnableFiltering = value;
        }

        public string Description { get; set; }

        public string Label;
        public CreateControlForValueDelegate<T> CreateControlForValue { get; set; }
        public Func<T, AbstractString> FormatValue = null;
        private CreateControlForValueDelegate<T> DefaultCreateControlForValue;

        private int _Version;
        protected ItemListManager<T> Manager;

        new public IEqualityComparer<T> Comparer {
            get => Manager.Comparer;
            set => Manager.Comparer = value;
        }
        public ItemList<T> Items => Manager.Items;

        /// <summary>
        /// The default value for the dropdown, used if there is currently no user selection.
        /// A freshly created dropdown will use this the first time it checks its value (for display, etc.)
        /// </summary>
        public T DefaultValue = default(T);

        // HACK: If T is a class and the default value is null,
        //  just pick our first item, since that's sensible.
        // For value types default(T) is the best we can do.
        T EffectiveDefaultValue => (DefaultValue == null) && (Items.Count > 0)
            ? Items[0]
            : DefaultValue;

        private bool NeedsUpdate = true;
        private bool MenuJustClosed = false;

        public int SelectedIndex {
            get => Manager.SelectedIndex;
            set => Manager.SelectedIndex = value;
        }

        public T SelectedItem {
            get => Manager.HasSelectedItem ? Manager.SelectedItem : EffectiveDefaultValue;
            set {
                SetSelectedItem(value, false);
            }
        }

        new public bool ScaleToFit {
            get => base.ScaleToFit;
            set => base.ScaleToFit = value;
        }
        new public bool ScaleToFitX {
            get => base.ScaleToFitX;
            set => base.ScaleToFitX = value;
        }
        new public bool ScaleToFitY {
            get => base.ScaleToFitY;
            set => base.ScaleToFitY = value;
        }
        new public float? MinScale {
            get => base.MinScale;
            set => base.MinScale = value;
        }

        public bool HasSelectedItem => Manager.HasSelectedItem;

        public void SetSelectedItem (T value, bool forUserInput) {
            // NOTE: If the current selected item is 'value', this will return false,
            //  which skips firing the change events and invalidating the dropdown.
            if (!Manager.TrySetSelectedItem(ref value, forUserInput))
                return;

            FireSelectionChangeEvent(forUserInput);
        }

        private void FireSelectionChangeEvent (bool forUserInput) {
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
                    return TooltipContent.GetPlainText(this);

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
            Content.CharacterWrap = false;
            Content.WordWrap = false;
        }

        protected override void ComputeAppearanceSpacing (
            ref UIOperationContext context, IDecorator decorations, 
            out Margins scaledMargins, out Margins scaledPadding, out Margins unscaledPadding
        ) {
            base.ComputeAppearanceSpacing(ref context, decorations, out scaledMargins, out scaledPadding, out unscaledPadding);
            var decorationProvider = context.DecorationProvider;
            ComputeEffectiveScaleRatios(decorationProvider, out Vector2 paddingScale, out Vector2 marginScale, out Vector2 sizeScale);
            var width = (decorationProvider.DropdownArrow.Padding.X * paddingScale.X) +
                (decorationProvider.DropdownArrow.UnscaledPadding.X);
            unscaledPadding.Right += width;
            // FIXME: Use the Y value?
        }

        private Control _DefaultCreateControlForValue (ref T value, Control existingControl) {
            if (!typeof(T).IsValueType) {
                if (value is Control c)
                    return c;
            }

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

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            if (NeedsUpdate || (Items.Version != _Version))
                Update();

            return ref base.OnGenerateLayoutTree(ref context, parent, existingKey);
        }

        protected void Update () {
            _Version = Items.Version;
            NeedsUpdate = false;
            if (!Manager.HasSelectedItem && (Items.Count > 0))
                // FIXME: ForUser?
                SetSelectedItem(DefaultValue, true);

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
                Context, anchor: this, 
                anchorPoint: new Vector2(0.5f, 1f), 
                controlAlignmentPoint: new Vector2(0.5f, 0f), 
                selectedItem: selectedControl
            );
            MenuJustClosed = false;
        }

        protected override bool OnEvent<TArgs> (string name, TArgs args) {
            if (args is MouseEventArgs ma)
                return OnMouseEvent(name, ma);
            else if (KeyEventArgs.From(ref args, out var ka))
                return OnKeyEvent(name, ka);
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
                                (args.Key == Keys.Up) ? -1 : 1, true
                            ))
                                FireSelectionChangeEvent(true);
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

        protected override ControlStates GetCurrentState (ref UIOperationContext context) {
            var result = base.GetCurrentState(ref context);
            if (ItemsMenu.IsActive)
                result |= ControlStates.Pressed;
            return result;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref passSet, settings, decorations);

            context.DecorationProvider.DropdownArrow?.Rasterize(ref context, ref passSet, ref settings);

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

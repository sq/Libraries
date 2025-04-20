﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Controls.SpecialInterfaces;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI.Imperative {
    public delegate void ContainerContentsDelegate (ref ContainerBuilder builder);
    
    public struct ContainerBuilder {
        public bool IsNewInstance { get; private set; }
        public UIContext Context { get; internal set; }
        public IControlContainer Container { get; private set; }
        public ControlCollection Children { get; private set; }
        public Control Control { get; internal set; }

        public ControlFlags? OverrideLayoutFlags;
        public ControlFlags ExtraLayoutFlags;
        public Func<IGlyphSource> DefaultGlyphSourceProvider;

        internal DenseList<Control> PreviousRemovedControls, CurrentRemovedControls;
        private int NextIndex;

        private Control WaitingForFocusBeneficiary;

        internal ContainerBuilder (UIContext context, Control control, bool isNewInstance) {
            if (control == null)
                throw new ArgumentNullException("control");
            if (control is not IControlContainer icc)
                throw new InvalidCastException("control must implement IControlContainer");

            Context = context;
            Control = control;
            Container = icc;
            NextIndex = Container?.ChildrenToSkipWhenBuilding ?? 0;
            Children = Container.Children;
            PreviousRemovedControls = new DenseList<Control>();
            CurrentRemovedControls = new DenseList<Control>();
            ExtraLayoutFlags = default(ControlFlags);
            OverrideLayoutFlags = null;
            WaitingForFocusBeneficiary = null;
            IsNewInstance = isNewInstance;
            DefaultGlyphSourceProvider = null;
        }

        public ContainerBuilder (Control container, bool isNewInstance = true)
            : this (container.Context, container, isNewInstance) {
        }

        public void Reset () {
            WaitingForFocusBeneficiary = null;
            NextIndex = Container?.ChildrenToSkipWhenBuilding ?? 0;
            IsNewInstance = false;
        }

        public void Finish () {
            var temp = PreviousRemovedControls;
            PreviousRemovedControls = CurrentRemovedControls;
            CurrentRemovedControls = temp;
            CurrentRemovedControls.Clear();

            if (Children != null) {
                // Trim off any excess controls
                for (int i = Children.Count - 1; i >= NextIndex; i--) {
                    PreviousRemovedControls.Add(Children[i]);
                    Children.RemoveAt(i);
                }
            }

            WaitingForFocusBeneficiary = null;
        }

        public ControlBuilder<TControl> Data<TControl, TData> (TData data)
            where TControl : Control, new() {
            return Data<TControl, TData>(null, data);
        }

        private TControl EvaluateMatch<TControl, TData> (Control c, string key, TData data)
            where TControl : Control {
            var result = c as TControl;
            if (result == null)
                return null;

            var currentData = result.Data.Get<TData>(key);
            if (object.Equals(currentData, data))
                return result;
            else
                return null;
        }

        private TControl FindRemovedWithData<TControl, TData> (ref DenseList<Control> list, string key, TData data)
            where TControl : Control {
            // Look for a match in the controls we have previously removed
            for (int i = 0, c = list.Count; i < c; i++) {
                var tctl = EvaluateMatch<TControl, TData>(list[i], key, data);
                if (tctl == null)
                    continue;

                list.RemoveAt(i);
                return tctl;
            }
            return null;
        }

        public ControlBuilder<TControl> Data<TControl, TData> (string key, TData data)
            where TControl : Control, new() {

            TControl instance = null;
            int foundWhere = 0;
            for (int i = NextIndex, c = Children.Count; i < c; i++) {
                var tctl = EvaluateMatch<TControl, TData>(Children[i], key, data);
                if (tctl == null)
                    continue;
                instance = tctl;
                foundWhere = i;
            }

            bool isNew;
            if ((foundWhere == NextIndex) && (instance != null)) {
                isNew = false;
                instance.Data.Set<TData>(key, data);
                NextIndex++;
            } else {
                if (instance != null) {
                    // Found a match in the current list
                    Children.RemoveAt(foundWhere);
                } else
                    instance = FindRemovedWithData<TControl, TData>(ref CurrentRemovedControls, key, data);

                if (instance == null)
                    instance = FindRemovedWithData<TControl, TData>(ref PreviousRemovedControls, key, data);

                // Failed to find a match anywhere, so make a new one
                if (instance == null)
                    instance = new TControl();

                instance.Data.Set<TData>(key, data);
                ApplyAppearance(instance);
                ApplyLayoutFlags(instance, null);
                AddInternal(instance);
                isNew = true;
            }

            return new ControlBuilder<TControl>(instance, isNew);
        }

        public ControlBuilder<StaticText> Label (AbstractString text, AbstractTooltipContent tooltip = default, ControlFlags? layoutFlags = null) {
            var result = this.Text<StaticText>(text, tooltip, layoutFlags);
            WaitingForFocusBeneficiary = result.Control;
            return result;
        }

        public ControlBuilder<StaticText> Text (AbstractString text, AbstractTooltipContent tooltip = default, ControlFlags? layoutFlags = null) {
            return this.Text<StaticText>(text, tooltip, layoutFlags);
        }

        public ControlBuilder<Spacer> Spacer (bool forceBreak = false) {
            return this.New<Spacer>()
                .SetForceBreak(forceBreak);
        }

        public ControlBuilder<StaticImage> Image (AbstractTextureReference texture, AbstractTooltipContent tooltip = default, ControlFlags? layoutFlags = null) {
            return this.Image<StaticImage>(texture, tooltip, layoutFlags);
        }

        public ControlBuilder<TControl> Image<TControl> (AbstractTextureReference texture, AbstractTooltipContent tooltip = default, ControlFlags? layoutFlags = null)
            where TControl : StaticImage, new() {
            var result = New<TControl>(layoutFlags);
            result.SetImage(texture);
            if (tooltip != default)
                result.SetTooltip(tooltip);
            return result;
        }

        public ControlBuilder<TControl> Text<TControl> (AbstractString text, AbstractTooltipContent tooltip = default, ControlFlags? layoutFlags = null)
            where TControl : Control, IHasText, new() {
            var result = New<TControl>(layoutFlags);
            result.SetText(text);
            if (tooltip != default)
                result.SetTooltip(tooltip);
            return result;
        }

        private void ApplyAppearance (Control control) {
            if (
                (DefaultGlyphSourceProvider != null) && 
                (control.Appearance.GlyphSource == null) &&
                (control.Appearance.GlyphSourceProvider == null)
            )
                control.Appearance.GlyphSourceProvider = DefaultGlyphSourceProvider;
        }

        private void ApplyLayoutFlags (Control control, ControlFlags? customFlags) {
            if (OverrideLayoutFlags.HasValue)
                control.LayoutFlags = OverrideLayoutFlags.Value;

            if (customFlags.HasValue)
                control.LayoutFlags = customFlags.Value;
            control.LayoutFlags |= ExtraLayoutFlags;
        }

        private void ApplyContainerFlags (Control control, ControlFlags? containerFlags) {
            if (!containerFlags.HasValue)
                return;

            if (control is Container c)
                c.ContainerFlags = containerFlags.Value;
            else if (control is ControlGroup cg)
                cg.ContainerFlags = containerFlags.Value;
            else
                throw new InvalidOperationException("This control does not accept container flags");
        }

        public ControlBuilder<TControl> New<TControl> (ControlFlags? layoutFlags = null, AbstractTooltipContent tooltip = default)
            where TControl : Control, new() {
            TControl instance = null;
            if (NextIndex < Children.Count)
                instance = Children[NextIndex] as TControl;

            if (instance?.GetType() != typeof(TControl))
                instance = null;

            bool isNew = false;
            if (instance == null) {
                instance = new TControl();
                isNew = true;
            }

            ApplyAppearance(instance);
            ApplyLayoutFlags(instance, layoutFlags);
            AddInternal(instance);

            var result = new ControlBuilder<TControl>(instance, isNew);
            if (tooltip != default)
                result.SetTooltip(tooltip);
            return result;
        }

        public ContainerBuilder NewContainer (ControlFlags? layoutFlags = null, ControlFlags? containerFlags = null) {
            return this.NewContainer<Container>(layoutFlags, containerFlags);
        }

        public ContainerBuilder NewGroup (ControlFlags? layoutFlags = null, ControlFlags? containerFlags = null) {
            return this.NewContainer<ControlGroup>(layoutFlags, containerFlags);
        }

        public ContainerBuilder NewContainer<TControl> (
            ControlFlags? layoutFlags = null, ControlFlags? containerFlags = null
        )
            where TControl : Control, IControlContainer, new() {
            TControl instance = null;
            if (NextIndex < Children.Count) {
                instance = Children[NextIndex] as TControl;
            }

            if (instance?.GetType() != typeof(TControl))
                instance = null;

            ContainerBuilder result;
            ContainerBase container;
            if (instance == null) {
                instance = new TControl();
                instance.Context = Control.Context;
                result = new ContainerBuilder(instance, true);
            } else if ((container = (instance as ContainerBase)) != null) {
                container.EnsureDynamicBuilderInitialized(out result);
            } else {
                result = new ContainerBuilder(instance, false);
            }

            result.DefaultGlyphSourceProvider = DefaultGlyphSourceProvider;
            ApplyAppearance(instance);
            ApplyLayoutFlags(instance, layoutFlags);
            ApplyContainerFlags(instance, containerFlags);
            AddInternal(instance);

            // FIXME: The child builder will create a temporary list
            return result;
        }

        // FIXME: ContainerFlags
        public ContainerBuilder TitledContainer (string title, bool? collapsible = null, ControlFlags? layoutFlags = null) {
            var result = New<TitledContainer>(layoutFlags);
            result.SetTitle(title);
            if (collapsible != null)
                result.SetCollapsible(collapsible.Value);
            return result.Children();
        }

        private void AddInternal<TControl> (TControl instance)
            where TControl : Control {
            var t = typeof(TControl);

            if ((WaitingForFocusBeneficiary != null) && (WaitingForFocusBeneficiary != instance)) {
                WaitingForFocusBeneficiary.FocusBeneficiary = instance;
                WaitingForFocusBeneficiary = null;
            }

            var index = NextIndex++;
            if (index >= Children.Count)
                Children.Add(instance);
            else {
                var previous = Children[index];
                if (previous == instance)
                    return;

                Children[index] = instance;
                CurrentRemovedControls.Add(previous);
            }
        }

        public ContainerBuilder Add (Control child) {
            AddInternal(child);
            return this;
        }

        public ContainerBuilder AddRange (params Control[] children) {
            foreach (var child in children)
                AddInternal(child);
            return this;
        }

        public ControlBuilder<ContainerBase> Properties {
            get => new ControlBuilder<ContainerBase>((ContainerBase)Control, IsNewInstance);
        }

        public bool GetEvent<TSource> (string eventName, out TSource source)
            where TSource : Control 
        {
            Control temp;
            if (Context.GetUnhandledChildEvent(Control, eventName, out temp)) {
                source = temp as TSource;
                return (source != null);
            }

            source = default(TSource);
            return false;
        }

        public bool GetEvent (string eventName) {
            return Context.GetUnhandledEvent(Control, eventName);
        }
    }

    public static class ControlBuilder {
        public static ControlBuilder<TControl> New<TControl> (TControl instance = null) 
            where TControl : Control, new()
        {
            bool isNew = false;
            if (instance == null) {
                isNew = true;
                instance = new TControl();
            }
            return new ControlBuilder<TControl>(instance, isNew);
        }
    }

    public struct ControlBuilder<TControl>
        where TControl : Control {

        public bool IsNew { get; internal set; }
        public TControl Control { get; internal set; }
        UIContext Context => Control.Context;

        public ControlBuilder (TControl control, bool isNew) {
            if (control == null)
                throw new ArgumentNullException("control");
            IsNew = isNew;
            Control = control;
        }

        public ContainerBuilder Children () {
            var cast = Control as IControlContainer;
            if (cast == null)
                throw new InvalidCastException("Control is not a container");
            return new ContainerBuilder(Control, false);
        }

        public bool GetEvent (string eventName) {
            if (Context == null)
                return false;
            return Context.GetUnhandledEvent(Control, eventName);
        }

        public ControlBuilder<TControl> GetEvent (string eventName, out bool result) {
            if (Context != null)
                result = Context.GetUnhandledEvent(Control, eventName);
            else
                result = false;

            return this;
        }

        public ControlBuilder<TControl> OnClick (Action onClick) {
            if (onClick == null)
                return this;
            GetEvent(UIEvents.Click, out bool clicked);
            if (clicked)
                onClick();
            return this;
        }

        public ControlBuilder<TControl> OnClick (Action<TControl> onClick) {
            if (onClick == null)
                return this;
            GetEvent(UIEvents.Click, out bool clicked);
            if (clicked)
                onClick(Control);
            return this;
        }


        public static implicit operator TControl (ControlBuilder<TControl> builder) {
            return builder.Control;
        }

        public ControlBuilder<TControl> ClearData () {
            Control.Data.Clear();
            return this;
        }

        public ControlBuilder<TControl> RemoveData<T> (string name = null) {
            Control.Data.Remove<T>(name);
            return this;
        }

        public ControlBuilder<TControl> SetData<T> (T value) {
            Control.Data.Set(value);
            return this;
        }

        public ControlBuilder<TControl> SetData<T> (string name, T value) {
            Control.Data.Set(name, value);
            return this;
        }

        public ControlBuilder<TControl> StoreInstance (ref TControl result, out bool changed) {
            changed = (result != Control);
            result = Control;
            return this;
        }
        public bool StoreInstance (ref TControl result) {
            StoreInstance(ref result, out bool changed);
            return changed;
        }

        public ControlBuilder<TControl> ClearLayoutFlags (ControlFlags value) {
            Control.LayoutFlags &= ~value;
            return this;
        }
        public ControlBuilder<TControl> AddLayoutFlags (ControlFlags value) {
            Control.LayoutFlags |= value;
            return this;
        }
        public ControlBuilder<TControl> SetLayoutFlags (ControlFlags value) {
            Control.LayoutFlags = value;
            return this;
        }
        public ControlBuilder<TControl> SetForceBreak (bool value) {
            Control.Layout.ForceBreak = value;
            return this;
        }

        public ControlBuilder<TControl> SetLayout (
            Flags.AnchorFlags? anchor = null,
            Flags.FillFlags? fill = null,
            Vector2? floatingPosition = null,
            bool? floating = null, bool? stacked = null, 
            bool? forceBreak = null
        ) {
            Control.Layout.Anchor = anchor ?? Control.Layout.Anchor;
            Control.Layout.Fill = fill ?? Control.Layout.Fill;
            if (floatingPosition.HasValue)
                Control.Layout.Floating = true;
            else
                Control.Layout.Floating = floating ?? Control.Layout.Floating;
            Control.Layout.FloatingPosition = floatingPosition;
            Control.Layout.Stacked = stacked ?? Control.Layout.Stacked;
            Control.Layout.ForceBreak = forceBreak ?? Control.Layout.ForceBreak;
            return this;
        }

        public ControlBuilder<TControl> SetContainer (
            Flags.ChildAlignment? alignment = null,
            Flags.ChildArrangement? arrangement = null,
            bool? constrainSize = null, bool? noExpansion = null,
            bool? preventCrush = null, bool? wrap = null,
            bool? autoBreak = null
        ) {
            if (Control is not ContainerBase cast)
                return this;

            cast.Container.Alignment = alignment ?? cast.Container.Alignment;
            cast.Container.Arrangement = arrangement ?? cast.Container.Arrangement;
            cast.Container.ConstrainSize = constrainSize ?? cast.Container.ConstrainSize;
            cast.Container.NoExpansion = noExpansion ?? cast.Container.NoExpansion;
            cast.Container.PreventCrush = preventCrush ?? cast.Container.PreventCrush;
            cast.Container.Wrap = wrap ?? cast.Container.Wrap;
            cast.Container.AutoBreak = autoBreak ?? cast.Container.AutoBreak;
            return this;
        }

        public ControlBuilder<TControl> ClearContainerFlags (ControlFlags value) {
            if (Control is ContainerBase cast)
                cast.ContainerFlags &= ~value;
            return this;
        }
        public ControlBuilder<TControl> AddContainerFlags (ControlFlags value) {
            if (Control is ContainerBase cast)
                cast.ContainerFlags |= value;
            return this;
        }
        public ControlBuilder<TControl> SetContainerFlags (ControlFlags value) {
            if (Control is ContainerBase cast)
                cast.ContainerFlags = value;
            return this;
        }
        public ControlBuilder<TControl> SetScrollable (bool value) {
            if (Control is IScrollableControl cast)
                cast.Scrollable = value;
            return this;
        }
        public ControlBuilder<TControl> SetShowScrollbars (bool? horizontal, bool? vertical) {
            if (Control is Container cast) {
                cast.ShowHorizontalScrollbar = horizontal;
                cast.ShowVerticalScrollbar = vertical;
            }
            return this;
        }

        public ControlBuilder<TControl> SetAppearance (ref ControlAppearance value) {
            Control.Appearance = value;
            return this;
        }
        public ControlBuilder<TControl> SetAppearance (ControlAppearance value) {
            Control.Appearance = value;
            return this;
        }
        public ControlBuilder<TControl> SetUndecorated (bool value) {
            Control.Appearance.Undecorated = value;
            return this;
        }
        public ControlBuilder<TControl> SetDecorator (IDecorator value) {
            Control.Appearance.Decorator = value;
            return this;
        }
        public ControlBuilder<TControl> SetUndecoratedText (bool value) {
            Control.Appearance.UndecoratedText = value;
            return this;
        }
        public ControlBuilder<TControl> SetTextDecorator (IDecorator value) {
            Control.Appearance.TextDecorator = value;
            return this;
        }
        public ControlBuilder<TControl> SetBackgroundColor (ColorVariable value) {
            Control.Appearance.BackgroundColor = value;
            return this;
        }
        public ControlBuilder<TControl> SetBackgroundImage (BackgroundImageSettings value) {
            Control.Appearance.BackgroundImage = value;
            return this;
        }
        public ControlBuilder<TControl> SetTextColor (ColorVariable value) {
            Control.Appearance.TextColor = value;
            return this;
        }

        public ControlBuilder<TControl> SetAlignment (Vector2 value) {
            if (Control is StaticImage si)
                si.Alignment = value;
            else if (Control is Window w)
                w.Alignment = value;
            return this;
        }

        public ControlBuilder<TControl> SetVisible (bool value) {
            Control.Visible = value;
            return this;
        }
        public ControlBuilder<TControl> SetEnabled (bool value) {
            Control.Enabled = value;
            return this;
        }
        public ControlBuilder<TControl> SetIntangible (bool value) {
            Control.Intangible = value;
            return this;
        }

        public ControlBuilder<TControl> SetTabOrder (int value) {
            Control.TabOrder = value;
            return this;
        }
        public ControlBuilder<TControl> SetDisplayOrder (int value) {
            Control.DisplayOrder = value;
            return this;
        }

        public ControlBuilder<TControl> SetFocusBeneficiary (Control value) {
            Control.FocusBeneficiary = value;
            return this;
        }

        public ControlBuilder<TControl> SetPadding (Margins value) {
            Control.Padding = value;
            return this;
        }
        public ControlBuilder<TControl> SetMargins (Margins value) {
            Control.Margins = value;
            return this;
        }

        public ControlBuilder<TControl> SetSize (ControlDimension? width = null, ControlDimension? height = null) {
            if (width.HasValue)
                Control.Width = width.Value;
            if (height.HasValue)
                Control.Height = height.Value;
            return this;
        }
        public ControlBuilder<TControl> SetFixedSize (float? width = null, float? height = null) {
            Control.Width.Fixed = width;
            Control.Height.Fixed = height;
            return this;
        }
        public ControlBuilder<TControl> SetMinimumSize (float? width = null, float? height = null) {
            Control.Width.Minimum = width;
            Control.Height.Minimum = height;
            return this;
        }
        public ControlBuilder<TControl> SetMaximumSize (float? width = null, float? height = null) {
            Control.Width.Maximum = width;
            Control.Height.Maximum = height;
            return this;
        }
        public ControlBuilder<TControl> SetPercentageSize (float? width = null, float? height = null, bool? isMaximum = null) {
            Control.Width.Percentage = width;
            Control.Height.Percentage = height;
            if (isMaximum.HasValue && width.HasValue)
                Control.Width.PercentageIsMaximum = isMaximum.Value;
            if (isMaximum.HasValue && height.HasValue)
                Control.Height.PercentageIsMaximum = isMaximum.Value;
            return this;
        }

        public ControlBuilder<TControl> SetTooltip (AbstractTooltipContent value, string format = null) {
            if (Control is Slider cast)
                cast.TooltipFormat = format;
            Control.TooltipContent = value;
            return this;
        }

        public ControlBuilder<TControl> SetTitle (string value) {
            if (Control is TitledContainer cast)
                cast.Title = value;
            return this;
        }
        public ControlBuilder<TControl> SetCollapsible (bool value) {
            if (Control is Window cast1)
                cast1.Collapsible = value;
            else if (Control is TitledContainer cast2)
                cast2.Collapsible = value;
            return this;
        }

        public ControlBuilder<TControl> SetDebugLabel (string value) {
            Control.DebugLabel = value;
            return this;
        }
        public ControlBuilder<TControl> SetIntegral (bool value) {
            if (Control is Slider cast1)
                cast1.Integral = value;
            else if (Control is EditableText cast2)
                cast2.IntegerOnly = value;
            return this;
        }

        public ControlBuilder<TControl> SetIncrement<TValue> (TValue value)
            where TValue : struct, IComparable<TValue>
        {
            if (Control is ParameterEditor<TValue> cast1) {
                cast1.Increment = value;
            } else if (Control is Slider cast2) {
                cast2.KeyboardSpeed = Convert.ToSingle(value);
            }
            return this;
        }

        public ControlBuilder<TControl> SetRange<TValue> (TValue? min = null, TValue? max = null, bool? clamp = null)
            where TValue : struct, IComparable<TValue>
        {
            if (Control is ParameterEditor<TValue> cast1) {
                cast1.Minimum = min;
                cast1.Maximum = max;
                if (clamp.HasValue)
                    cast1.ClampToRange = clamp.Value;
            }

            if (Control is Slider cast2) {
                if (min.HasValue)
                    cast2.Minimum = Convert.ToSingle(min.Value);
                if (max.HasValue)
                    cast2.Maximum = Convert.ToSingle(max.Value);
            }

            return this;
        }

        public ControlBuilder<TControl> SetValue<TValue> (TValue value) {
            if (Control is IValueControl<TValue> ivc)
                ivc.Value = value;
            return this;
        }

        public bool Value<TValue> (ref TValue value) {
            Value(ref value, out bool temp);
            return temp;
        }

        public ControlBuilder<TControl> Value<TValue> (ref TValue value, out bool changed) {
            var cast = (Control as IValueControl<TValue>);
            if (cast == null) {
                var t = typeof(TValue);
                // HACK: For sliders, attempt to convert to float
                if (
                    t.IsPrimitive &&
                    (t != typeof(float)) &&
                    (Control is IValueControl<float>)
                ) {
                    var temp = Convert.ToSingle(value);
                    Value<float>(ref temp, out changed);
                    if (changed)
                        value = (TValue)Convert.ChangeType(temp, t);
                    return this;
                }

                throw new InvalidCastException("Control is not a compatible value control");
            }

            GetEvent(UIEvents.ValueChangedByUser, out changed);
            if (!changed)
                GetEvent(UIEvents.CheckedChanged, out changed);
            if (changed)
                value = cast.Value;
            else 
                cast.Value = value;
            return this;
        }

        public ControlBuilder<TControl> GetValue<TValue> (out TValue value) {
            var cast = (IValueControl<TValue>)Control;
            value = cast.Value;
            return this;
        }

        public ControlBuilder<TControl> Text (ref string value, out bool changed) {
            var cast1 = (Control as StaticTextBase);
            var cast2 = (Control as EditableText);
            GetEvent(UIEvents.ValueChanged, out changed);
            if (changed) {
                value = cast2.Text;
            } else {
                cast1?.SetTextInternal(value, true);
                cast2?.SetText(value, false);
            }
            return this;
        }

        public ControlBuilder<TControl> Text (ref AbstractString value, out bool changed) {
            var cast1 = (Control as StaticTextBase);
            var cast2 = (Control as EditableText);
            GetEvent(UIEvents.ValueChanged, out changed);
            if (changed) {
                value = cast2.Text;
            } else {
                cast1?.SetTextInternal(value);
                cast2?.SetText(value, false);
            }
            return this;
        }

        public ControlBuilder<TControl> SetGlyphSource (IGlyphSource value) {
            Control.Appearance.GlyphSource = value;
            return this;
        }
        public ControlBuilder<TControl> SetGlyphSource (Func<IGlyphSource> provider) {
            Control.Appearance.GlyphSourceProvider = provider;
            return this;
        }
    }

    public static class ControlBuilderExtensions {
        public static ControlBuilder<T> SetClampToRange<T> (this ControlBuilder<T> self, bool value)
            where T : Control, IParameterEditor
        {
            self.Control.ClampToMinimum = value;
            self.Control.ClampToMaximum = value;
            return self;
        }        

        public static ControlBuilder<T> SetClampToRange<T> (this ControlBuilder<T> self, bool min = true, bool max = true)
            where T : Control, IParameterEditor
        {
            self.Control.ClampToMinimum = min;
            self.Control.ClampToMaximum = max;
            return self;
        }        

        public static ControlBuilder<T> SetDescription<T> (this ControlBuilder<T> self, string value)
            where T : Control, IHasDescription
        {
            self.Control.Description = value;
            return self;
        }

        public static ControlBuilder<T> SetAllowCopy<T> (this ControlBuilder<T> self, bool value)
            where T : EditableText
        {
            self.Control.AllowCopy = value;
            return self;
        }

        public static ControlBuilder<T> SetPassword<T> (this ControlBuilder<T> self, bool value)
            where T : EditableText
        {
            self.Control.Password = value;
            return self;
        }

        public static ControlBuilder<T> SetKeyboardSpeed<T> (this ControlBuilder<T> self, float value)
            where T : Slider
        {
            self.Control.KeyboardSpeed = value;
            return self;
        }
        
        public static ControlBuilder<T> SetImage<T> (this ControlBuilder<T> self, AbstractTextureReference value)
            where T : StaticImage
        {
            self.Control.Image = value;
            return self;
        }
        
        public static ControlBuilder<T> SetText<T> (this ControlBuilder<T> self, AbstractString value)
            where T : Control, IHasText 
        {
            self.Control.SetText(value);
            return self;
        }

        public static ControlBuilder<T> SetText<T> (this ControlBuilder<T> self, ImmutableAbstractString value, bool onlyIfTextChanged)
            where T : Control, IHasText 
        {
            self.Control.SetText(value, onlyIfTextChanged);
            return self;
        }

        public static ControlBuilder<T> GetText<T> (this ControlBuilder<T> self, StringBuilder output)
            where T : Control, IHasText 
        {
            output.Clear();
            self.Control.GetText(output);
            return self;
        }

        public static ControlBuilder<T> GetText<T> (this ControlBuilder<T> self, out AbstractString result)
            where T : Control, IHasText 
        {
            result = self.Control.GetText();
            return self;
        }

        public static ControlBuilder<T> SetRichText<T> (this ControlBuilder<T> self, bool value)
            where T : StaticTextBase
        {
            self.Control.RichText = value;
            return self;
        }

        public static ControlBuilder<T> SetRichText<T> (this ControlBuilder<T> self, bool value, RichTextConfiguration configuration)
            where T : StaticTextBase
        {
            self.Control.RichText = value;
            if (self.Control is StaticText st)
                st.RichTextConfiguration = configuration;
            else if (self.Control is HyperText ht)
                ht.RichTextConfiguration = configuration;
            return self;
        }

        public static ControlBuilder<T> SetTextAlignment<T> (this ControlBuilder<T> self, HorizontalAlignment value)
            where T : StaticTextBase
        {
            self.Control.TextAlignment = value;
            return self;
        }

        public static ControlBuilder<T> SetScaleToFit<T> (this ControlBuilder<T> self, bool value)
            where T : Control, IHasScaleToFit
        {
            self.Control.ScaleToFit = value;
            return self;
        }

        public static ControlBuilder<T> SetScale<T> (this ControlBuilder<T> self, float value)
            where T : Control, IHasScale
        {
            self.Control.Scale = value;
            return self;
        }

        public static ControlBuilder<T> SetWrap<T> (this ControlBuilder<T> self, bool value)
            where T : StaticText
        {
            self.Control.Wrap = value;
            return self;
        }

        public static ControlBuilder<T> SetWrap<T> (this ControlBuilder<T> self, bool wordWrap, bool characterWrap)
            where T : StaticText
        {
            self.Control.Content.WordWrap = wordWrap;
            self.Control.Content.CharacterWrap = characterWrap;
            return self;
        }

        public static ControlBuilder<T> SetMultiline<T> (this ControlBuilder<T> self, bool value)
            where T : StaticText
        {
            self.Control.Multiline = value;
            return self;
        }

        public static ControlBuilder<T> SetAutoSize<T> (this ControlBuilder<T> self, bool value)
            where T : StaticTextBase 
        {
            self.Control.AutoSizeWidth = value;
            self.Control.AutoSizeHeight = value;
            return self;
        }

        public static ControlBuilder<T> SetAutoSize<T> (this ControlBuilder<T> self, bool width, bool height)
            where T : StaticTextBase 
        {
            self.Control.AutoSizeWidth = width;
            self.Control.AutoSizeHeight = height;
            return self;
        }

        public static ControlBuilder<T> SetCreateControlForValue<T, U> (this ControlBuilder<T> self, CreateControlForValueDelegate<U> value)
            where T : Control, IHasCreateControlForValueProperty<U>
        {
            self.Control.CreateControlForValue = value;
            return self;
        }

        public static ControlBuilder<ListBox<T>> SetSelectedItem<T> (this ControlBuilder<ListBox<T>> self, T value) {
            self.Control.SelectedItem = value;
            return self;
        }

        public static ControlBuilder<T> SetSelectedIndex<T> (this ControlBuilder<T> self, int value)
            where T : Control, IListBox
        {
            self.Control.SelectedIndex = value;
            return self;
        }

        public static ControlBuilder<T> SetVirtual<T> (this ControlBuilder<T> self, bool value)
            where T : Control, IListBox
        {
            self.Control.Virtual = value;
            return self;
        }

        public static ControlBuilder<T> SetColumnCount<T> (this ControlBuilder<T> self, int value)
            where T : ContainerBase 
        {
            self.Control.ColumnCount = value;
            return self;
        }

        public static ControlBuilder<T> SetClipChildren<T> (this ControlBuilder<T> self, bool value)
            where T : Control, IControlContainer
        {
            self.Control.ClipChildren = value;
            return self;
        }

        public static ControlBuilder<T> SetDynamicContents<T> (this ControlBuilder<T> self, ContainerContentsDelegate value, bool? cached = null)
            where T : ContainerBase 
        {
            self.Control.DynamicContents = value;
            if (cached.HasValue)
                self.Control.CacheDynamicContent = cached.Value;
            return self;
        }

        public static ControlBuilder<T> SetCacheDynamicContent<T> (this ControlBuilder<T> self, bool value)
            where T : ContainerBase 
        {
            self.Control.CacheDynamicContent = value;
            return self;
        }

        public static ControlBuilder<T> InvalidateDynamicContent<T> (this ControlBuilder<T> self)
            where T : ContainerBase 
        {
            self.Control.InvalidateDynamicContent();
            return self;
        }

        public static ControlBuilder<T> AddChildren<T> (this ControlBuilder<T> self, IEnumerable<Control> children)
            where T : Control, IControlContainer
        {
            self.Control.Children.AddRange(children);
            return self;
        }
    }
}

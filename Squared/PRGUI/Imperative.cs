using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Controls;
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
        public UIContext Context { get; internal set; }
        public IControlContainer Container { get; private set; }
        public ControlCollection Children { get; private set; }
        public Control Control { get; internal set; }

        public ControlFlags? OverrideLayoutFlags;
        public ControlFlags ExtraLayoutFlags;

        internal DenseList<Control> PreviousRemovedControls, CurrentRemovedControls;
        private int NextIndex;

        private Control WaitingForFocusBeneficiary;

        internal ContainerBuilder (UIContext context, Control control) {
            if (control == null)
                throw new ArgumentNullException("control");
            if (!(control is IControlContainer))
                throw new InvalidCastException("control must implement IControlContainer");

            Context = context;
            Control = control;
            Container = (IControlContainer)control;
            NextIndex = Container?.ChildrenToSkipWhenBuilding ?? 0;
            Children = Container.Children;
            PreviousRemovedControls = new DenseList<Control>();
            CurrentRemovedControls = new DenseList<Control>();
            ExtraLayoutFlags = default(ControlFlags);
            OverrideLayoutFlags = null;
            WaitingForFocusBeneficiary = null;
        }

        public ContainerBuilder (Control container)
            : this (container.Context, container) {
        }

        public void Reset () {
            WaitingForFocusBeneficiary = null;
            NextIndex = Container?.ChildrenToSkipWhenBuilding ?? 0;
        }

        public void Finish () {
            var temp = PreviousRemovedControls;
            PreviousRemovedControls = CurrentRemovedControls;
            CurrentRemovedControls = temp;
            CurrentRemovedControls.Clear();

            // Trim off any excess controls
            for (int i = Children.Count - 1; i >= NextIndex; i--) {
                PreviousRemovedControls.Add(Children[i]);
                Children.RemoveAt(i);
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

            if ((foundWhere == NextIndex) && (instance != null)) {
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
                ApplyLayoutFlags(instance, null);
                AddInternal(instance);
            }

            return new ControlBuilder<TControl>(instance);
        }

        public ControlBuilder<StaticText> Label (AbstractString text, ControlFlags? layoutFlags = null) {
            var result = this.Text<StaticText>(text, layoutFlags);
            WaitingForFocusBeneficiary = result.Control;
            return result;
        }

        public ControlBuilder<StaticText> Text (AbstractString text, ControlFlags? layoutFlags = null) {
            return this.Text<StaticText>(text, layoutFlags);
        }

        public ControlBuilder<Spacer> Spacer (bool forceBreak = false) {
            return this.New<Spacer>()
                .SetForceBreak(forceBreak);
        }

        public ControlBuilder<StaticImage> Image (AbstractTextureReference texture, ControlFlags? layoutFlags = null) {
            return this.Image<StaticImage>(texture, layoutFlags);
        }

        public ControlBuilder<TControl> Image<TControl> (AbstractTextureReference texture, ControlFlags? layoutFlags = null)
            where TControl : Control, new() {
            var result = New<TControl>(layoutFlags);
            result.SetImage(texture);
            return result;
        }

        public ControlBuilder<TControl> Text<TControl> (AbstractString text, ControlFlags? layoutFlags = null)
            where TControl : Control, new() {
            var result = New<TControl>(layoutFlags);
            result.SetText(text);
            return result;
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

        public ControlBuilder<TControl> New<TControl> (ControlFlags? layoutFlags = null)
            where TControl : Control, new() {
            TControl instance = null;
            if (NextIndex < Children.Count)
                instance = Children[NextIndex] as TControl;

            if (instance == null)
                instance = new TControl();

            ApplyLayoutFlags(instance, layoutFlags);
            AddInternal(instance);

            return new ControlBuilder<TControl>(instance);
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

            ContainerBuilder result;
            ContainerBase container;
            if (instance == null) {
                instance = new TControl();
                instance.Context = Control.Context;
                result = new ContainerBuilder(instance);
            } else if ((container = (instance as ContainerBase)) != null) {
                container.EnsureDynamicBuilderInitialized(out result);
            } else {
                result = new ContainerBuilder(instance);
            }

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

        public ControlBuilder<Control> Properties {
            get => new ControlBuilder<Control>(Control);
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
            return new ControlBuilder<TControl>(instance ?? new TControl());
        }
    }

    public struct ControlBuilder<TControl>
        where TControl : Control {

        public TControl Control { get; internal set; }
        UIContext Context => Control.Context;

        public ControlBuilder (TControl control) {
            if (control == null)
                throw new ArgumentNullException("control");
            Control = control;
        }

        public ContainerBuilder Children () {
            var cast = Control as IControlContainer;
            if (cast == null)
                throw new InvalidCastException();
            return new ContainerBuilder(Control);
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

        public ControlBuilder<TControl> Subscribe (string eventName, EventSubscriber subscriber) {
            if ((eventName == null) || (subscriber == null))
                throw new ArgumentNullException();
            Context.EventBus.Subscribe(Control, eventName, subscriber);
            return this;
        }

        public ControlBuilder<TControl> Subscribe<T> (string eventName, TypedEventSubscriber<T> subscriber)
            where T : class 
        {
            if ((eventName == null) || (subscriber == null))
                throw new ArgumentNullException();
            Context.EventBus.Subscribe<T>(Control, eventName, subscriber);
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
        public ControlBuilder<TControl> SetClipChildren (bool value) {
            if (Control is IControlContainer cast)
                cast.ClipChildren = value;
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

        public ControlBuilder<TControl> SetImage (AbstractTextureReference value) {
            if (Control is StaticImage si)
                si.Image = value;
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

        public ControlBuilder<TControl> SetDescription (string value) {
            if (Control is IHasDescription cast)
                cast.Description = value;
            return this;
        }
        public ControlBuilder<TControl> SetAllowCopy (bool value) {
            if (Control is EditableText cast)
                cast.AllowCopy = value;
            return this;
        }
        public ControlBuilder<TControl> SetPassword (bool value) {
            if (Control is EditableText cast)
                cast.Password = value;
            return this;
        }

        public ControlBuilder<TControl> SetKeyboardSpeed(float value) {
            if (Control is Slider cast1)
                cast1.KeyboardSpeed = value;
            // FIXME: Parameter editor
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
        public ControlBuilder<TControl> SetRange<TValue> (TValue? min = null, TValue? max = null)
            where TValue : struct, IComparable<TValue>
        {
            if (Control is ParameterEditor<TValue> cast1) {
                cast1.Minimum = min;
                cast1.Maximum = max;
            }

            if (Control is Slider cast2) {
                if (min.HasValue)
                    cast2.Minimum = Convert.ToSingle(min);
                if (max.HasValue)
                    cast2.Maximum = Convert.ToSingle(max);
            }

            return this;
        }

        public ControlBuilder<TControl> SetValue<TValue> (TValue value) {
            var cast = (Control as IValueControl<TValue>);
            cast.Value = value;
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

        public ControlBuilder<TControl> SetText (AbstractString value) {
            var cast1 = (Control as StaticTextBase);
            var cast2 = (Control as EditableText);
            cast1?.SetTextInternal(value, true);
            cast2?.SetText(value, false);
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
        public ControlBuilder<TControl> GetText (StringBuilder result) {
            result.Clear();
            if (Control is EditableText cast2)
                cast2.GetText(result);
            else if (Control is StaticTextBase cast1)
                cast1.Text.CopyTo(result);
            return this;
        }
        public ControlBuilder<TControl> GetText (out AbstractString value) {
            if (Control is EditableText cast2)
                value = cast2.Text;
            else if (Control is StaticTextBase cast1)
                value = cast1.Text;
            else
                value = default(AbstractString);
            return this;
        }

        public ControlBuilder<TControl> SetGlyphSource (IGlyphSource value) {
            if (Control is StaticTextBase stb)
                stb.GlyphSource = value;
            return this;
        }
        public ControlBuilder<TControl> SetRichText (bool value) {
            if (Control is StaticTextBase stb)
                stb.RichText = value;
            return this;
        }
        public ControlBuilder<TControl> SetTextAlignment (HorizontalAlignment value) {
            if (Control is StaticTextBase stb)
                stb.TextAlignment = value;
            return this;
        }
        public ControlBuilder<TControl> SetScaleToFit (bool value) {
            if (Control is StaticText stb)
                stb.ScaleToFit = value;
            else if (Control is StaticImage si)
                si.ScaleToFit = value;
            return this;
        }
        public ControlBuilder<TControl> SetScale (float value) {
            if (Control is StaticTextBase stb)
                stb.Scale = value;
            else if (Control is StaticImage si)
                si.Scale = new Vector2(value);
            return this;
        }
        public ControlBuilder<TControl> SetWrap (bool value) {
            if (Control is StaticText stb)
                stb.Wrap = value;
            return this;
        }
        public ControlBuilder<TControl> SetWrap (bool wordWrap, bool characterWrap) {
            if (Control is StaticText stb) {
                stb.Content.WordWrap = wordWrap;
                stb.Content.CharacterWrap = characterWrap;
            }
            return this;
        }
        public ControlBuilder<TControl> SetMultiline (bool value) {
            if (Control is StaticText stb)
                stb.Multiline = value;
            return this;
        }
        public ControlBuilder<TControl> SetAutoSize (bool value) {
            if (Control is StaticTextBase stb)
                stb.AutoSize = value;
            // FIXME
            /*
            else if (Control is StaticImage si)
                si.AutoSize = value;
            */
            return this;
        }
        public ControlBuilder<TControl> SetAutoSize (bool width, bool height) {
            if (Control is StaticTextBase stb) {
                stb.AutoSizeWidth = width;
                stb.AutoSizeHeight = height;
            // FIXME
            /*
            } else if (Control is StaticImage si) {
                si.AutoSizeWidth = width;
                si.AutoSizeHeight = height;
            */
            }
            return this;
        }

        public ControlBuilder<TControl> SetVirtual (bool value) {
            if (Control is IListBox lb)
                lb.Virtual = value;
            return this;
        }
        public ControlBuilder<TControl> SetSelectedIndex (int value) {
            if (Control is IListBox lb)
                lb.SelectedIndex = value;
            return this;
        }
        public ControlBuilder<TControl> SetSelectedItem<T> (T value) {
            if (Control is ListBox<T> lb)
                lb.SelectedItem = value;
            else if (Control is IListBox ilb)
                ilb.SelectedItem = value;
            return this;
        }
        public ControlBuilder<TControl> SetCreateControlForValue<T> (CreateControlForValueDelegate<T> value) {
            if (Control is ListBox<T> lb)
                lb.CreateControlForValue = value;
            return this;
        }

        public ControlBuilder<TControl> InvalidateDynamicContent () {
            if (Control is ContainerBase c)
                c.InvalidateDynamicContent();
            return this;
        }
        public ControlBuilder<TControl> SetCacheDynamicContent (bool value) {
            if (Control is ContainerBase c)
                c.CacheDynamicContent = value;
            return this;
        }
        public ControlBuilder<TControl> SetDynamicContents (ContainerContentsDelegate value) {
            if (Control is ContainerBase c)
                c.DynamicContents = value;
            return this;
        }
        public ControlBuilder<TControl> SetColumnCount (int value, bool? autoBreak = null) {
            if (Control is ContainerBase c) {
                c.ColumnCount = value;
                if (autoBreak != null)
                    c.AutoBreakColumnItems = autoBreak.Value;
            }
            return this;
        }

        public ControlBuilder<TControl> AddChildren (params Control[] children) {
            var cast = (IControlContainer)Control;
            cast.Children.AddRange(children);
            return this;
        }
    }
}

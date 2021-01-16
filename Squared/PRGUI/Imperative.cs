using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Imperative {
    public delegate void ContainerContentsDelegate (ref ContainerBuilder builder);
    
    public struct ContainerBuilder {
        public UIContext Context { get; internal set; }
        public IControlContainer Container { get => (IControlContainer)Control; }
        public Control Control { get; internal set; }

        private DenseList<Control> PreviousRemovedControls, CurrentRemovedControls;
        private int NextIndex;

        internal ContainerBuilder (UIContext context, Control control) {
            if (control == null)
                throw new ArgumentNullException("control");
            if (!(control is IControlContainer))
                throw new InvalidCastException("control must implement IControlContainer");

            NextIndex = 0;
            Context = context;
            Control = control;
            PreviousRemovedControls = new DenseList<Control>();
            CurrentRemovedControls = new DenseList<Control>();
        }

        public ContainerBuilder (Control container)
            : this (container.Context, container) {
        }

        public void Reset () {
            NextIndex = 0;
        }

        public void Finish () {
            var temp = PreviousRemovedControls;
            PreviousRemovedControls = CurrentRemovedControls;
            CurrentRemovedControls = temp;
            CurrentRemovedControls.Clear();

            // Trim off any excess controls
            for (int i = Container.Children.Count - 1; i >= NextIndex; i--) {
                PreviousRemovedControls.Add(Container.Children[i]);
                Container.Children.RemoveAt(i);
            }
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
            for (int i = NextIndex, c = Container.Children.Count; i < c; i++) {
                var tctl = EvaluateMatch<TControl, TData>(Container.Children[i], key, data);
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
                    Container.Children.RemoveAt(foundWhere);
                } else
                    instance = FindRemovedWithData<TControl, TData>(ref CurrentRemovedControls, key, data);

                if (instance == null)
                    instance = FindRemovedWithData<TControl, TData>(ref PreviousRemovedControls, key, data);

                // Failed to find a match anywhere, so make a new one
                if (instance == null)
                    instance = new TControl();

                instance.Data.Set<TData>(key, data);
                AddInternal(instance);
            }

            return new ControlBuilder<TControl>(instance);
        }

        public ControlBuilder<StaticText> Text (AbstractString text) {
            return this.Text<StaticText>(text);
        }

        public ControlBuilder<TControl> Text<TControl> (AbstractString text)
            where TControl : Control, new() {
            var result = New<TControl>();
            result.SetText(text);
            return result;
        }

        public ControlBuilder<TControl> New<TControl> ()
            where TControl : Control, new() {
            TControl instance = null;
            if (NextIndex < Container.Children.Count)
                instance = Container.Children[NextIndex] as TControl;

            if (instance == null)
                instance = new TControl();

            AddInternal(instance);

            return new ControlBuilder<TControl>(instance);
        }

        public ContainerBuilder NewContainer () {
            return this.NewContainer<Container>();
        }

        public ContainerBuilder NewContainer<TControl> ()
            where TControl : Control, IControlContainer, new() {
            TControl instance = null;
            if (NextIndex < Container.Children.Count)
                instance = Container.Children[NextIndex] as TControl;

            ContainerBuilder result;
            Container container;
            if (instance == null) {
                instance = new TControl();
                result = new ContainerBuilder(instance);
            } else if ((container = (instance as Container)) != null) {
                // HACK: A lot of gross stuff going on here to try and reuse lists
                if ((container.DynamicContents == null) && (container.DynamicBuilder.Container == container)) {
                    container.DynamicBuilder.PreviousRemovedControls.EnsureList();
                    container.DynamicBuilder.CurrentRemovedControls.EnsureList();
                    result = container.DynamicBuilder;
                } else {
                    result = new ContainerBuilder(instance);
                    container.DynamicBuilder = result;
                }
            } else {
                result = new ContainerBuilder(instance);
            }

            AddInternal(instance);

            // FIXME: The child builder will create a temporary list
            return result;
        }

        public ContainerBuilder TitledContainer (string title, bool? collapsible = null) {
            var result = New<TitledContainer>();
            result.SetTitle(title);
            if (collapsible != null)
                result.SetCollapsible(collapsible.Value);
            return result.Children();
        }

        private void AddInternal<TControl> (TControl instance)
            where TControl : Control {
            var t = typeof(TControl);

            var index = NextIndex++;
            if (index >= Container.Children.Count)
                Container.Children.Add(instance);
            else {
                var previous = Container.Children[index];
                if (previous == instance)
                    return;

                Container.Children[index] = instance;
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

        public ControlBuilder<TControl> GetEvent (string eventName, out bool result) {
            result = Context.GetUnhandledEvent(Control, eventName);

            return this;
        }

        public ControlBuilder<TControl> OnClick (Action onClick) {
            GetEvent(UIEvents.Click, out bool clicked);
            if (clicked)
                onClick();
            return this;
        }

        public ControlBuilder<TControl> OnClick (Action<TControl> onClick) {
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

        public ControlBuilder<TControl> SetLayoutFlags (ControlFlags value) {
            Control.LayoutFlags = value;
            return this;
        }
        public ControlBuilder<TControl> SetForceBreak (bool value) {
            var flags = Control.LayoutFlags & ~ControlFlags.Layout_ForceBreak;
            if (value)
                flags |= ControlFlags.Layout_ForceBreak;
            Control.LayoutFlags = flags;
            return this;
        }

        public ControlBuilder<TControl> SetContainerFlags (ControlFlags value) {
            if (Control is IControlContainer cast)
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

        public ControlBuilder<TControl> SetAppearance (ref ControlAppearance value) {
            Control.Appearance = value;
            return this;
        }
        public ControlBuilder<TControl> SetAppearance (ControlAppearance value) {
            Control.Appearance = value;
            return this;
        }
        public ControlBuilder<TControl> SetDecorator (IDecorator value) {
            Control.Appearance.Decorator = value;
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
        public ControlBuilder<TControl> SetPaintOrder (int value) {
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

        public ControlBuilder<TControl> SetTooltip (AbstractTooltipContent value) {
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

        public ControlBuilder<TControl> SetDescription (string value) {
            if (Control is EditableText cast)
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

        public ControlBuilder<TControl> SetIntegral (bool value) {
            if (Control is Slider cast1)
                cast1.Integral = value;
            else if (Control is EditableText cast2)
                cast2.IntegerOnly = value;
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
                    cast2.Minimum = (float)(object)min;
                if (max.HasValue)
                    cast2.Maximum = (float)(object)max;
            }

            return this;
        }

        public ControlBuilder<TControl> SetValue<TValue> (TValue value) {
            var cast = (Control as IValueControl<TValue>);
            cast.Value = value;
            return this;
        }
        public ControlBuilder<TControl> Value<TValue> (ref TValue value, out bool changed) {
            var cast = (Control as IValueControl<TValue>);
            GetEvent(UIEvents.ValueChanged, out changed);
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
            cast1?.SetText(value);
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
                cast1?.SetText(value);
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
                cast1?.SetText(value);
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
            return this;
        }
        public ControlBuilder<TControl> SetWrap (bool value) {
            if (Control is StaticText stb)
                stb.Wrap = value;
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
            return this;
        }
        public ControlBuilder<TControl> SetAutoSize (bool width, bool height) {
            if (Control is StaticTextBase stb) {
                stb.AutoSizeWidth = width;
                stb.AutoSizeHeight = height;
            }
            return this;
        }

        public ControlBuilder<TControl> InvalidateDynamicContent () {
            if (Control is Container c)
                c.InvalidateDynamicContent();
            return this;
        }
        public ControlBuilder<TControl> SetCacheDynamicContent (bool value) {
            if (Control is Container c)
                c.CacheDynamicContent = value;
            return this;
        }
        public ControlBuilder<TControl> SetDynamicContents (ContainerContentsDelegate value) {
            if (Control is Container c)
                c.DynamicContents = value;
            return this;
        }

        public ControlBuilder<TControl> AddChildren (params Control[] children) {
            var cast = (IControlContainer)Control;
            cast.Children.AddRange(children);
            return this;
        }
    }
}

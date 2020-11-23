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

            if (instance == null)
                instance = new TControl();

            AddInternal(instance);

            return new ContainerBuilder(instance);
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

        private bool UnhandledEvent (string eventName) {
            var key = new UIContext.UnhandledEvent { Source = Control, Name = eventName };
            var comparer = UIContext.UnhandledEvent.Comparer.Instance;
            var result = Context.UnhandledEvents.Contains(key, comparer) || 
                Context.PreviousUnhandledEvents.Contains(key, comparer);
            return result;
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

        public ControlBuilder<TControl> SetContainerFlags (ControlFlags value) {
            var cast = (Control as IControlContainer);
            if (cast != null)
                cast.ContainerFlags = value;
            return this;
        }
        public ControlBuilder<TControl> SetClipChildren (bool value) {
            var cast = (Control as IControlContainer);
            if (cast != null)
                cast.ClipChildren = value;
            return this;
        }
        public ControlBuilder<TControl> SetScrollable (bool value) {
            var cast = (Control as IScrollableControl);
            if (cast != null)
                cast.Scrollable = value;
            return this;
        }

        public ControlBuilder<TControl> SetDecorator (IDecorator value) {
            Control.CustomDecorator = value;
            return this;
        }

        public ControlBuilder<TControl> SetBackgroundColor (ColorVariable value) {
            Control.BackgroundColor = value;
            return this;
        }
        public ControlBuilder<TControl> SetBackgroundImage (BackgroundImageSettings value) {
            Control.BackgroundImage = value;
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
            Control.PaintOrder = value;
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

        public ControlBuilder<TControl> SetFixedSize (float? width = null, float? height = null) {
            Control.FixedWidth = width;
            Control.FixedHeight = height;
            return this;
        }
        public ControlBuilder<TControl> SetMinimumSize (float? width = null, float? height = null) {
            Control.MinimumWidth = width;
            Control.MinimumHeight = height;
            return this;
        }
        public ControlBuilder<TControl> SetMaximumSize (float? width = null, float? height = null) {
            Control.MaximumWidth = width;
            Control.MaximumHeight = height;
            return this;
        }

        public ControlBuilder<TControl> SetTooltip (AbstractTooltipContent value) {
            Control.TooltipContent = value;
            return this;
        }

        public ControlBuilder<TControl> SetTitle (string value) {
            // FIXME
            var cast = (Control as TitledContainer);
            if (cast != null)
                cast.Title = value;
            return this;
        }
        public ControlBuilder<TControl> SetCollapsible (bool value) {
            // FIXME
            var cast1 = (Control as Window);
            var cast2 = (Control as TitledContainer);
            if (cast1 != null)
                cast1.Collapsible = value;
            else if (cast2 != null)
                cast2.Collapsible = value;
            return this;
        }

        public ControlBuilder<TControl> SetDescription (string value) {
            var cast = (Control as EditableText);
            if (cast != null)
                cast.Description = value;
            return this;
        }

        public ControlBuilder<TControl> SetIntegral (bool value) {
            var cast1 = (Control as Slider);
            var cast2 = (Control as EditableText);
            if (cast1 != null)
                cast1.Integral = value;
            else if (cast2 != null)
                cast2.IntegerOnly = value;
            return this;
        }
        public ControlBuilder<TControl> SetRange<TValue> (TValue? min = null, TValue? max = null)
            where TValue : struct, IComparable<TValue>
        {
            var cast1 = (Control as ParameterEditor<TValue>);
            if (cast1 != null) {
                cast1.Minimum = min;
                cast1.Maximum = max;
            }

            var cast2 = (Control as Slider);
            if (cast2 != null) {
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
        public ControlBuilder<TControl> SetValue<TValue> (ref TValue value, out bool changed) {
            var cast = (Control as IValueControl<TValue>);
            if (
                UnhandledEvent(UIEvents.ValueChanged) || 
                UnhandledEvent(UIEvents.CheckedChanged)
            ) {
                changed = true;
                value = cast.Value;
            } else {
                changed = false;
                cast.Value = value;
            }
            return this;
        }
        public ControlBuilder<TControl> SetText (AbstractString value) {
            var cast1 = (Control as StaticTextBase);
            var cast2 = (Control as EditableText);
            cast1?.SetText(value);
            cast2?.SetText(value, false);
            return this;
        }

        public ControlBuilder<TControl> SetRichText (bool value) {
            var stb = (Control as StaticTextBase);
            if (stb != null)
                stb.RichText = value;
            return this;
        }
        public ControlBuilder<TControl> SetTextAlignment (HorizontalAlignment value) {
            var stb = (Control as StaticTextBase);
            if (stb != null)
                stb.TextAlignment = value;
            return this;
        }
        public ControlBuilder<TControl> SetTextColor (ColorVariable value) {
            var stb = (Control as StaticTextBase);
            if (stb != null)
                stb.TextColor = value;
            return this;
        }
        public ControlBuilder<TControl> SetScaleToFit (bool value) {
            var stb = (Control as StaticText);
            if (stb != null)
                stb.ScaleToFit = value;
            return this;
        }
        public ControlBuilder<TControl> SetWrap (bool value) {
            var stb = (Control as StaticText);
            if (stb != null)
                stb.Wrap = value;
            return this;
        }
        public ControlBuilder<TControl> SetAutoSize (bool value) {
            var stb = (Control as StaticTextBase);
            if (stb != null)
                stb.AutoSize = value;
            return this;
        }
        public ControlBuilder<TControl> SetAutoSize (bool width, bool height) {
            var stb = (Control as StaticTextBase);
            if (stb != null) {
                stb.AutoSizeWidth = width;
                stb.AutoSizeHeight = height;
            }
            return this;
        }

        public ControlBuilder<TControl> AddChildren (params Control[] children) {
            var cast = (Control as IControlContainer);
            cast.Children.AddRange(children);
            return this;
        }
    }
}

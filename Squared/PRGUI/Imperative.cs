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
    public delegate void ContainerContentsDelegate<T> (ref ContainerBuilder<T> builder)
        where T : Control, IControlContainer, new();

    public static class ContainerBuilder {
        public static ContainerBuilder<TContainer> New<TContainer> ()
            where TContainer : Control, IControlContainer, new() {
            return new ContainerBuilder<TContainer>(new TContainer());
        }
    }

    public struct ContainerBuilder<TContainer>
        where TContainer : Control, IControlContainer
    {
        public UIContext Context { get; internal set; }
        public IControlContainer Container { get => Control; }
        public TContainer Control { get; internal set; }

        private DenseList<Control> RemovedControls;
        private int NextIndex;

        internal ContainerBuilder (UIContext context, TContainer container) {
            if (container == null)
                throw new ArgumentNullException("container");

            NextIndex = 0;
            Context = context;
            Control = container;
            RemovedControls = new DenseList<Control>();
        }

        public ContainerBuilder (TContainer container)
            : this (container.Context, container) {
        }

        public void Reset () {
            RemovedControls.Clear();
            NextIndex = 0;
        }

        public void Finish () {
            // Trim off any excess controls
            for (int i = Container.Children.Count - 1; i >= NextIndex; i--) {
                RemovedControls.Add(Container.Children[i]);
                Container.Children.RemoveAt(i);
            }
        }

        public static implicit operator TContainer (ContainerBuilder<TContainer> builder) {
            return builder.Control;
        }

        public ControlBuilder<TControl> Data<TControl, TData> (TData data)
            where TControl : Control, new() {
            return Data<TControl, TData>(null, data);
        }

        private TControl EvaluateMatch<TControl, TData> (Control c, string key, TData data)
            where TControl : Control, new() {
            var result = c as TControl;
            if (result == null)
                return null;

            if (object.Equals(result.Data.Get<TData>(key), data))
                return result;
            else
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
            }

            if ((foundWhere == NextIndex) && (instance != null)) {
                instance.Data.Set<TData>(key, data);
                NextIndex++;
            } else {
                if (instance != null) {
                    // Found a match in the current list
                    Container.Children.RemoveAt(foundWhere);
                } else {
                    // Look for a match in the controls we have previously removed
                    for (int i = 0, c = RemovedControls.Count; i < c; i++) {
                        var tctl = EvaluateMatch<TControl, TData>(RemovedControls[i], key, data);
                        if (tctl == null)
                            continue;

                        RemovedControls.RemoveAt(i);
                        instance = tctl;
                        break;
                    }
                }

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

        public ContainerBuilder<Container> NewContainer () {
            return this.NewContainer<Container>();
        }

        public ContainerBuilder<TControl> NewContainer<TControl> ()
            where TControl : Control, IControlContainer, new() {
            TControl instance = null;
            if (NextIndex < Container.Children.Count)
                instance = Container.Children[NextIndex] as TControl;

            if (instance == null)
                instance = new TControl();

            AddInternal(instance);

            return new ContainerBuilder<TControl>(instance);
        }

        public ContainerBuilder<TitledContainer> TitledContainer (string title, bool? collapsible = null) {
            var result = NewContainer<TitledContainer>();
            result.Properties.SetTitle(title);
            if (collapsible != null)
                result.Properties.SetCollapsible(collapsible.Value);
            return result;
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
                RemovedControls.Add(previous);
            }
        }

        public ContainerBuilder<TContainer> Add (Control child) {
            AddInternal(child);
            return this;
        }

        public ContainerBuilder<TContainer> AddRange (params Control[] children) {
            foreach (var child in children)
                AddInternal(child);
            return this;
        }

        public ControlBuilder<TContainer> Properties {
            get => new ControlBuilder<TContainer>(Control);
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

        public ControlBuilder (TControl control) {
            if (control == null)
                throw new ArgumentNullException("control");
            Control = control;
        }

        public ContainerBuilder<TContainer> Children<TContainer> ()
            where TContainer : TControl, IControlContainer {
            var cast = Control as TContainer;
            if (cast == null)
                throw new InvalidCastException();
            return new ContainerBuilder<TContainer>(cast);
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
            cast.ContainerFlags = value;
            return this;
        }
        public ControlBuilder<TControl> SetClipChildren (bool value) {
            var cast = (Control as IControlContainer);
            cast.ClipChildren = value;
            return this;
        }
        public ControlBuilder<TControl> SetScrollable (bool value) {
            var cast = (Control as IScrollableControl);
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

        public ControlBuilder<TControl> SetIntegral (bool value) {
            var cast = (Control as Slider);
            cast.Integral = value;
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

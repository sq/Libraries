using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Util;

namespace Squared.PRGUI.Imperative {
    public struct ContainerBuilder<TContainer>
        where TContainer : Control, IControlContainer
    {
        public UIContext Context { get; internal set; }
        public IControlContainer Container { get => Control; }
        public TContainer Control { get; internal set; }

        private List<Control> ChildBuffer;
        private Dictionary<Type, List<Control>> ControlsByType;
        private int NextIndex;

        internal ContainerBuilder (UIContext context, TContainer container) {
            if (container == null)
                throw new ArgumentNullException("container");

            NextIndex = 0;
            Context = context;
            Control = container;
            ControlsByType = new Dictionary<Type, List<Control>>(new ReferenceComparer<Type>());
            ChildBuffer = new List<Control>(container.Children);
            container.Children.Clear();

            foreach (var child in ChildBuffer) {
                var t = child.GetType();
                List<Control> byType;
                if (!ControlsByType.TryGetValue(t, out byType))
                    ControlsByType[t] = byType = new List<Control>();
                byType.Add(child);
            }
        }

        public ContainerBuilder (TContainer container)
            : this (container.Context, container) {
        }

        public static implicit operator TContainer (ContainerBuilder<TContainer> builder) {
            return builder.Control;
        }

        public ControlBuilder<TControl> Data<TControl, TData> (TData data)
            where TControl : Control {
            return Data<TControl, TData>(null, data);
        }

        public ControlBuilder<TControl> Data<TControl, TData> (string key, TData data)
            where TControl : Control {

            TControl instance = null;
            if (ControlsByType.TryGetValue(typeof(TControl), out List<Control> list)) {
                // FIXME: This is slow. Use a dictionary?
                foreach (var ctl in list) {
                    if (ctl.Data.Get<TData>(key).Equals(data)) {
                        instance = (TControl)ctl;
                        break;
                    }
                }
            }

            if (instance == null)
                instance = Activator.CreateInstance<TControl>();

            instance.Data.Set<TData>(key, data);

            AddInternal(instance);
            return new ControlBuilder<TControl>(instance);
        }

        public ControlBuilder<TControl> New<TControl> ()
            where TControl : Control {
            var instance = (NextIndex < ChildBuffer.Count)
                ? ChildBuffer[NextIndex] as TControl
                : Activator.CreateInstance<TControl>();

            AddInternal(instance);
            return new ControlBuilder<TControl>(instance);
        }

        private void AddInternal<TControl> (TControl instance)
            where TControl : Control {
            var t = typeof(TControl);

            List<Control> list;
            if (!ControlsByType.TryGetValue(t, out list))
                ControlsByType[t] = list = new List<Control>();

            Container.Children.Add(instance);
            list.Add(instance);
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
            where TContainer : Control, IControlContainer {
            var cast = Control as TContainer;
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
            cast.Title = value;
            return this;
        }
        public ControlBuilder<TControl> SetCollapsible (bool value) {
            // FIXME
            var cast = (Control as TitledContainer);
            cast.Collapsible = value;
            return this;
        }

        public ControlBuilder<TControl> SetText (string value) {
            var cast1 = (Control as StaticTextBase);
            cast1?.SetText(value);

            var cast2 = (Control as EditableText);
            if (cast2 != null)
                cast2.Text = value;

            return this;
        }

        public ControlBuilder<TControl> AddChildren (params Control[] children) {
            var cast = (Control as IControlContainer);
            cast?.Children.AddRange(children);
            return this;
        }
    }
}

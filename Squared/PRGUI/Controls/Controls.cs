﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public class Button : StaticText {
        public Button ()
            : base () {
            Content.Alignment = HorizontalAlignment.Center;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.Button;
        }
    }

    public class Checkbox : StaticText {
        public bool Checked;

        public Checkbox ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.Checkbox;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (Checked)
                settings.State |= ControlStates.Checked;
            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click) {
                Checked = !Checked;
                FireEvent(UIEvents.CheckedChanged);
            }

            return base.OnEvent(name, args);
        }
    }

    public class RadioButton : StaticText {
        private bool _Checked, _SubscriptionPending;
        public string GroupId;

        private EventSubscription Subscription;

        public RadioButton ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
        }

        public bool Checked {
            get => _Checked;
            set {
                if (value == _Checked)
                    return;

                if (value == false) {
                    Unsubscribe();
                    _Checked = false;
                    FireEvent(UIEvents.CheckedChanged);
                } else {
                    Subscribe();
                    _Checked = true;
                    FireEvent(UIEvents.RadioButtonSelected, GroupId);
                    FireEvent(UIEvents.CheckedChanged);
                }
            }
        }

        protected override void Initialize () {
            if (_SubscriptionPending) {
                Subscribe();
                if (_Checked)
                    FireEvent(UIEvents.RadioButtonSelected, GroupId);
            }
        }

        private void OnRadioButtonSelected (IEventInfo<string> e, string groupId) {
            if (e.Source == this)
                return;
            if (GroupId != groupId)
                return;
            Checked = false;
        }

        private void Subscribe () {
            _SubscriptionPending = false;
            Subscription.Dispose();
            if (Context == null) {
                _SubscriptionPending = true;
                return;
            }
            Subscription = Context.EventBus.Subscribe<string>(null, UIEvents.RadioButtonSelected, OnRadioButtonSelected);
        }

        private void Unsubscribe () {
            Subscription.Dispose();
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.RadioButton;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if (Checked)
                settings.State |= ControlStates.Checked;
            base.OnRasterize(context, ref renderer, settings, decorations);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.Click)
                Checked = true;

            return base.OnEvent(name, args);
        }
    }

    public class Tooltip : StaticText {
        public Tooltip ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            AcceptsMouseInput = false;
            AcceptsFocus = false;
            AutoSize = true;
            Intangible = true;
            LayoutFlags = ControlFlags.Layout_Floating;
            PaintOrder = 9999;
            Wrap = true;
            Multiline = true;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.Tooltip;
        }
    }

    public class Window : Container {
        public Vector2 Position {
            get {
                return new Vector2(Margins.Left, Margins.Top);
            }
            set {
                Margins.Left = value.X;
                Margins.Top = value.Y;
            }
        }

        public string Title;
        public bool Movable;

        private bool Dragging;
        private Vector2 DragStartMousePosition, DragStartWindowPosition;
        private RectF MostRecentTitleBox;

        protected DynamicStringLayout TitleLayout = new DynamicStringLayout {
            LineLimit = 1
        };

        public Window ()
            : base () {
            AcceptsMouseInput = true;
            ContainerFlags |= ControlFlags.Container_Constrain_Size;
            LayoutFlags |= ControlFlags.Layout_Floating;
        }

        protected IDecorator UpdateTitle (UIOperationContext context, DecorationSettings settings, out Material material, ref pSRGBColor? color) {
            var decorations = context.DecorationProvider?.WindowTitle;
            if (decorations == null) {
                material = null;
                return null;
            }
            decorations.GetTextSettings(context, settings.State, out material, out IGlyphSource font, ref color);
            TitleLayout.Text = Title;
            TitleLayout.GlyphSource = font;
            TitleLayout.Color = color?.ToColor() ?? Color.White;
            TitleLayout.LineBreakAtX = settings.ContentBox.Width;
            return decorations;
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            var titleDecorations = context.DecorationProvider?.WindowTitle;
            if (titleDecorations == null)
                return result;

            pSRGBColor? color = null;
            titleDecorations.GetTextSettings(context, default(ControlStates), out Material temp, out IGlyphSource font, ref color);
            result.Top += titleDecorations.Margins.Bottom;
            result.Top += titleDecorations.Padding.Top;
            result.Top += titleDecorations.Padding.Bottom;
            result.Top += font.LineSpacing;
            return result;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Below)
                return;

            // Handle the corner case where the canvas size has changed since we were last moved and ensure we are still on screen
            UpdatePosition(Position, context.UIContext, settings.Box);

            IDecorator titleDecorator;
            pSRGBColor? titleColor = null;
            if (
                (titleDecorator = UpdateTitle(context, settings, out Material titleMaterial, ref titleColor)) != null
            ) {
                var layout = TitleLayout.Get();
                var titleBox = settings.Box;
                titleBox.Height = titleDecorator.Padding.Top + titleDecorator.Padding.Bottom + TitleLayout.GlyphSource.LineSpacing;
                MostRecentTitleBox = titleBox;

                var titleContentBox = titleBox;
                titleContentBox.Left += titleDecorator.Padding.Left;
                titleContentBox.Top += titleDecorator.Padding.Top;
                titleContentBox.Width -= titleDecorator.Padding.X;

                var offsetX = (titleContentBox.Width - layout.Size.X) / 2f;

                var subSettings = settings;
                subSettings.Box = titleBox;
                subSettings.ContentBox = titleContentBox;

                renderer.Layer += 1;
                titleDecorator.Rasterize(context, ref renderer, subSettings);

                renderer.Layer += 1;
                renderer.DrawMultiple(
                    layout.DrawCalls, new Vector2(titleContentBox.Left + offsetX, titleContentBox.Top),
                    samplerState: RenderStates.Text, multiplyColor: titleColor?.ToColor()
                );
            }
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            return false;
        }

        private void UpdatePosition (Vector2 newPosition, UIContext context, RectF box) {
            newPosition = new Vector2(
                Arithmetic.Clamp(newPosition.X, 0, context.CanvasSize.X - box.Width),
                Arithmetic.Clamp(newPosition.Y, 0, context.CanvasSize.Y - box.Height)
            ).Floor();

            Position = newPosition;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.MouseDown) {
                if (MostRecentTitleBox.Contains(args.GlobalPosition)) {
                    Dragging = true;
                    DragStartMousePosition = args.GlobalPosition;
                    DragStartWindowPosition = Position;
                    return true;
                } else {
                    Dragging = false;
                    return false;
                }
            } else if (
                (name == UIEvents.MouseDrag) ||
                (name == UIEvents.MouseUp)
            ) {
                if (!Dragging)
                    return false;

                var delta = args.GlobalPosition - DragStartMousePosition;
                var newPosition = DragStartWindowPosition + delta;

                UpdatePosition(newPosition, args.Context, args.Box);

                FireEvent(UIEvents.Moved);

                // args.Context.Invalidate();

                if (name == UIEvents.MouseUp)
                    Dragging = false;

                return true;
            } else
                return false;
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{Title}'";
        }
    }

    public class ControlCollection : IEnumerable<Control> {
        private List<Control> PaintOrderedItems = new List<Control>(),
            TabOrderedItems = new List<Control>();
        private List<Control> Items = new List<Control>();

        public int Count => Items.Count;
        public Control Parent { get; private set; }
        public UIContext Context { get; private set; }

        internal ControlCollection (UIContext parent) {
            Parent = null;
            Context = parent;
        }

        public ControlCollection (Control parent) {
            Parent = parent;
            Context = parent.Context;
        }

        public void Add (Control control) {
            if (Items.Contains(control))
                throw new InvalidOperationException("Control already in collection");

            if (Parent != null)
                control.SetParent(Parent);
            else
                control.SetContext(Context);
            Items.Add(control);
        }

        public void Remove (Control control) {
            control.UnsetParent(Parent);
            Items.Remove(control);
        }

        public void Clear () {
            foreach (var control in Items)
                control.UnsetParent(Parent);

            Items.Clear();
        }

        public List<Control>.Enumerator GetEnumerator () {
            return Items.GetEnumerator();
        }

        public Control this[int index] {
            get {
                return Items[index];
            }
            set {
                Items[index] = value;
            }
        }

        internal List<Control> InTabOrder (bool suitableTargetsOnly) {
            TabOrderedItems.Clear();
            foreach (var item in Items)
                if (!suitableTargetsOnly || (item.AcceptsFocus && item.Enabled))
                    TabOrderedItems.Add(item);
            TabOrderedItems.Sort(Control.TabOrderComparer.Instance);
            return TabOrderedItems;
        }

        internal List<Control> InPaintOrder () {
            PaintOrderedItems.Clear();
            PaintOrderedItems.AddRange(Items);
            PaintOrderedItems.Sort(Control.PaintOrderComparer.Instance);
            return PaintOrderedItems;
        }

        IEnumerator<Control> IEnumerable<Control>.GetEnumerator () {
            return ((IEnumerable<Control>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable)Items).GetEnumerator();
        }
    }

    public struct AbstractTooltipContent {
        public Func<Control, AbstractString> GetText;
        public AbstractString Text;

        public static implicit operator AbstractTooltipContent (Func<Control, AbstractString> func) {
            return new AbstractTooltipContent { GetText = func };
        }

        public static implicit operator AbstractTooltipContent (AbstractString text) {
            return new AbstractTooltipContent { Text = text };
        }

        public static implicit operator AbstractTooltipContent (string text) {
            return new AbstractTooltipContent { Text = text };
        }
    }
}
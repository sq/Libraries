using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class Menu : Container {
        private Control _SelectedItem;

        public Control SelectedItem {
            get {
                return _SelectedItem;
            }
            set {
                if (_SelectedItem == value)
                    return;
                OnSelectionChange(_SelectedItem, value);
                _SelectedItem = value;
            }
        }

        public Vector2 Position {
            get {
                return new Vector2(Margins.Left, Margins.Top);
            }
            set {
                Margins.Left = value.X;
                Margins.Top = value.Y;
            }
        }

        public Menu ()
            : base () {
            AcceptsMouseInput = true;
            ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Wrap | ControlFlags.Container_Align_Start;
            LayoutFlags |= ControlFlags.Layout_Floating;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider.Menu;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
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

        protected override bool OnHitTest (LayoutContext context, RectF box, Vector2 position, bool acceptsMouseInputOnly, bool acceptsFocusOnly, ref Control result) {
            var ok = base.OnHitTest(context, box, position, acceptsMouseInputOnly, acceptsFocusOnly, ref result);
            // HACK: Ensure that hit-test does not pass through to our individual items. We want to handle all events for them
            if (ok)
                result = this;
            return ok;
        }

        private void OnSelectionChange (Control previous, Control newControl) {
            foreach (var child in Children)
                child.CustomTextDecorations = (child == newControl)
                    ? Context.Decorations.Selection 
                    : null;
        }

        private Control ChildFromGlobalPosition (LayoutContext context, Vector2 globalPosition) {
            foreach (var child in Children)
                if (child.HitTest(context, globalPosition, false, false) == child)
                    return child;

            return null;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            var position = new Vector2(
                Arithmetic.Clamp(args.LocalPosition.X, 0, args.ContentBox.Width - 1),
                Arithmetic.Clamp(args.LocalPosition.Y, 0, args.ContentBox.Height - 1)
            );

            if ((Context.MouseOver != this) && (Context.MouseCaptured != this)) {
                SelectedItem = null;
            } else {
                var virtualGlobalPosition = args.GlobalPosition + ScrollOffset;
                virtualGlobalPosition.X = args.ContentBox.Center.X;
                var item = ChildFromGlobalPosition(Context.Layout, virtualGlobalPosition);
                if (item != null)
                    SelectedItem = item;
            }

            if (name == UIEvents.MouseDown) {
                return false;
            } else if (
                (name == UIEvents.MouseDrag) ||
                (name == UIEvents.MouseUp)
            ) {
                return false;
            } else
                return false;
        }

        protected bool OnClick (int clickCount) {
            return false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (name == UIEvents.MouseLeave)
                SelectedItem = null;
            else if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (name == UIEvents.Click)
                return OnClick(Convert.ToInt32(args));
            /*
            else if (name == UIEvents.KeyPress)
                return OnKeyPress((KeyEventArgs)(object)args);
            */
            return false;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            if ((SelectedItem != null) && (context.Pass == RasterizePasses.Below)) {
                var selectionBox = SelectedItem.GetRect(context.Layout, true, false);
                selectionBox.Left = settings.ContentBox.Left;
                selectionBox.Width = settings.ContentBox.Width;

                // HACK
                context.Pass = RasterizePasses.Content;
                var selectionSettings = new DecorationSettings {
                    Box = selectionBox,
                    ContentBox = selectionBox,
                    State = ControlStates.Hovering | ControlStates.Focused
                };
                context.DecorationProvider.Selection?.Rasterize(context, ref renderer, selectionSettings);
                context.Pass = RasterizePasses.Below;
            }

            base.OnRasterize(context, ref renderer, settings, decorations);
        }
    }
}

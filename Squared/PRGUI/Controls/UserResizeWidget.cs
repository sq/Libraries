using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;

namespace Squared.PRGUI.Controls {
    public class UserResizeWidget : Control, IIgnoresScrollingControl {
        public const float MinimumWidth = 16, MinimumHeight = 16;

        public bool AllowHorizontal = true,
            AllowVertical = true;

        protected bool IsResizing, HasInitialValues;
        protected ControlDimension InitialWidth, InitialHeight, ResizeStartWidth, ResizeStartHeight;
        protected Vector2 ResizeStartPosition, ResizeStartDimensions;

        public UserResizeWidget ()
            : base() {
            AcceptsFocus = false;
            AcceptsTextInput = false;
            AcceptsMouseInput = true;
            TooltipContent = "Drag to resize";
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            Layout.Stacked = true;
            Layout.Anchor.Right = true;
            Layout.Anchor.Bottom = true;
            Layout.Fill = false;
            var ckey = base.OnGenerateLayoutTree(ref context, parent, existingKey);
            context.Engine[ckey].Config.AlignToParentBox = true;
            return ckey;
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            width.Minimum = Math.Max(width.Minimum ?? MinimumWidth, MinimumWidth);
            height.Minimum = Math.Max(height.Minimum ?? MinimumHeight, MinimumHeight);
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (MouseEventArgs.From(ref args, out var mea))
                return OnMouseEvent(name, mea);
            else
                return base.OnEvent(name, args);
        }

        protected bool OnMouseEvent (string name, MouseEventArgs mea) {
            switch (name) {
                case UIEvents.MouseDown:
                    if (mea.Buttons == MouseButtons.Left) {
                        TryGetParent(out var parent);
                        if (!HasInitialValues) {
                            InitialWidth = parent.Width;
                            InitialHeight = parent.Height;
                            HasInitialValues = true;
                        }
                        ResizeStartPosition = mea.GlobalPosition;
                        ResizeStartDimensions = parent.GetRect().Size;
                        ResizeStartWidth = parent.Width;
                        ResizeStartHeight = parent.Height;
                        IsResizing = true;
                        UpdateResize(mea);
                        return true;
                    } else if (mea.Buttons == MouseButtons.Right) {
                        ResetResize();
                        return true;
                    }
                    break;
                case UIEvents.MouseMove:
                    if (IsResizing) {
                        UpdateResize(mea);
                        return true;
                    }
                    break;
                case UIEvents.MouseUp:
                    if (IsResizing)
                        UpdateResize(mea);
                    IsResizing = false;
                    return true;
            }

            return base.OnEvent(name, mea);
        }

        protected void ResetResize () {
            TryGetParent(out var parent);

            if (HasInitialValues) {
                parent.Width = InitialWidth;
                parent.Height = InitialHeight;
                HasInitialValues = false;
                InitialWidth = InitialHeight = default;
            }
        }

        protected void UpdateResize (MouseEventArgs mea) {
            TryGetParent(out var parent);
            var delta = mea.GlobalPosition - ResizeStartPosition;
            if (!AllowHorizontal || Math.Abs(delta.X) < 2)
                delta.X = 0;
            if (!AllowVertical || Math.Abs(delta.Y) < 2)
                delta.Y = 0;
            
            // FIXME: Use Control.GetSizeConstraints somehow

            ControlDimension w = ResizeStartWidth, h = ResizeStartHeight;
            if (delta.X != 0) {
                if (w.HasFixed)
                    w.Fixed = w.Constrain(w.Fixed + delta.X, false);
                else
                    w.Fixed = w.Constrain(ResizeStartDimensions.X + delta.X, false);
                parent.Width = w;
            }
            if (delta.Y != 0) {
                if (h.HasFixed)
                    h.Fixed = h.Constrain(h.Fixed + delta.Y, false);
                else
                    h.Fixed = h.Constrain(ResizeStartDimensions.Y + delta.Y, false);
                parent.Height = h;
            }
        }

        // FIXME: Use a decorator for this.
        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            float radius = 1.66f, outlineRadius = 1.4f;
            pSRGBColor fillColor = Appearance.TextColor.Get(context.NowL) ?? Vector4.One, 
                outlineColor = Appearance.BackgroundColor.Get(context.NowL) ?? new Vector4(0, 0, 0, 1);

            settings.ContentBox.SnapAndInset(out var tl, out var br, 0); // FIXME: inset by outlineRadius?
            for (int i = 0; i < 3; i++) {
                renderer.RasterizeLineSegment(
                    new Vector2(tl.X, br.Y), new Vector2(br.X, tl.Y), 
                    radius, radius, outlineRadius, fillColor, fillColor, outlineColor
                );
                tl += Vector2.One * (radius + outlineRadius) * 2;
            }
        }
    }
}

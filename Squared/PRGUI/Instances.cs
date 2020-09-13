﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Text;

namespace Squared.PRGUI {
    public abstract class Control {
        public IDecorator CustomDecorations;
        public Margins Margins, Padding;
        public ControlFlags LayoutFlags;
        public Vector2? FixedSize;

        public ControlStates State;

        internal ControlKey LayoutKey;

        public void GenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            LayoutKey = OnGenerateLayoutTree(context, parent);
        }

        protected Vector2? GetFixedInteriorSpace () {
            if (!FixedSize.HasValue)
                return null;

            return new Vector2(
                Math.Max(0, FixedSize.Value.X - Margins.Left - Margins.Right),
                Math.Max(0, FixedSize.Value.Y - Margins.Top - Margins.Bottom)
            );
        }

        protected virtual Control OnHitTest (LayoutContext context, RectF box, Vector2 position) {
            if (box.Contains(position))
                return this;

            return null;
        }

        public Control HitTest (LayoutContext context, Vector2 position) {
            var box = context.GetRect(LayoutKey);
            return OnHitTest(context, box, position);
        }

        protected virtual ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = context.Layout.CreateItem();

            var decorations = GetDecorations(context);
            var computedMargins = Margins;
            if (decorations != null)
                computedMargins += decorations.Margins;

            context.Layout.SetLayoutFlags(result, LayoutFlags);
            context.Layout.SetMargins(result, computedMargins);
            if (FixedSize.HasValue)
                context.Layout.SetSize(result, FixedSize.Value);

            if (!parent.IsInvalid)
                context.Layout.InsertAtEnd(parent, result);

            return result;
        }

        protected virtual IDecorator GetDefaultDecorations (UIOperationContext context) {
            return null;
        }

        protected IDecorator GetDecorations (UIOperationContext context) {
            return CustomDecorations ?? GetDefaultDecorations(context);
        }

        protected ControlStates GetCurrentState (UIOperationContext context) {
            var result = State;
            if (context.UIContext.Hovering == this)
                result |= ControlStates.Hovering;
            if (context.UIContext.Focused == this)
                result |= ControlStates.Focused;
            return result;
        }

        protected virtual void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            decorations?.Rasterize(context, box, state);
        }

        public void Rasterize (UIOperationContext context, Vector2 offset) {
            var box = context.Layout.GetRect(LayoutKey);
            box.Left += offset.X;
            box.Top += offset.Y;
            var decorations = GetDecorations(context);
            var state = GetCurrentState(context);
            OnRasterize(context, box, state, decorations);
        }
    }

    public class StaticText : Control {
        public DynamicStringLayout Content = new DynamicStringLayout();
        public bool AutoSize;

        protected Margins ComputePadding (IDecorator decorations) {
            var computedPadding = Padding;
            if (decorations != null)
                computedPadding += decorations.Padding;
            return computedPadding;
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = base.OnGenerateLayoutTree(context, parent);
            if (AutoSize) {
                var interiorSpace = GetFixedInteriorSpace();
                if (interiorSpace.HasValue)
                    Content.LineBreakAtX = interiorSpace.Value.X;
                else
                    Content.LineBreakAtX = null;

                var decorations = GetDecorations(context);
                var computedPadding = ComputePadding(decorations);
                var computedSize = Content.Get().Size + new Vector2(computedPadding.Left + computedPadding.Right, computedPadding.Top + computedPadding.Bottom);
                context.Layout.SetSize(result, computedSize);
            }

            return result;
        }

        protected override void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            base.OnRasterize(context, box, state, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            var interiorSpace = GetFixedInteriorSpace();
            if (interiorSpace.HasValue)
                Content.LineBreakAtX = interiorSpace.Value.X;
            else
                Content.LineBreakAtX = null;

            var computedPadding = ComputePadding(decorations);

            var layout = Content.Get();
            context.Renderer.DrawMultiple(
                layout.DrawCalls, offset: box.Position + new Vector2(computedPadding.Left, computedPadding.Top),
                material: decorations?.GetTextMaterial(context, state)
            );
        }
    }

    public class Button : StaticText {
        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.Button;
        }

        protected override void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            base.OnRasterize(context, box, state, decorations);
        }
    }

    public class Container : Control {
        public ControlFlags ContainerFlags = ControlFlags.Container_Row;

        public readonly List<Control> Children = new List<Control>();

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            var result = base.OnGenerateLayoutTree(context, parent);
            context.Layout.SetContainerFlags(result, ContainerFlags);
            foreach (var item in Children)
                item.GenerateLayoutTree(context, result);
            return result;
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.Container;
        }

        protected override void OnRasterize (UIOperationContext context, RectF box, ControlStates state, IDecorator decorations) {
            base.OnRasterize(context, box, state, decorations);

            // FIXME
            int layer = context.Renderer.Layer, maxLayer = layer;

            foreach (var item in Children) {
                context.Renderer.Layer = layer;
                item.Rasterize(context, Vector2.Zero);
                maxLayer = Math.Max(maxLayer, context.Renderer.Layer);
            }

            context.Renderer.Layer = maxLayer;
        }

        protected override Control OnHitTest (LayoutContext context, RectF box, Vector2 position) {
            // FIXME: Should we only perform the hit test if the position is within our boundaries?
            // This doesn't produce the right outcome when a container's computed size is zero
            foreach (var item in Children) {
                var result = item.HitTest(context, position);
                if (result != null)
                    return result;
            }

            return base.OnHitTest(context, box, position);
        }
    }
}
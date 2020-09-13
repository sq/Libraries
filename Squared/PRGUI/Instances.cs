using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;
using Squared.Render.Text;

namespace Squared.PRGUI {
    public abstract class Control {
        public IDecorationRenderer Decorations;
        public Margins Margins;
        public ControlFlags LayoutFlags;
        public Vector2? FixedSize;

        private ControlKey LayoutKey;

        public void GenerateLayoutTree (LayoutContext context, ControlKey parent) {
            LayoutKey = OnGenerateLayoutTree(context, parent);
        }

        public Vector2 TotalMargins {
            get {
                return new Vector2(Margins.Left + Margins.Right, Margins.Top + Margins.Bottom);
            }
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

        protected virtual ControlKey OnGenerateLayoutTree (LayoutContext context, ControlKey parent) {
            var result = context.CreateItem();
            context.SetLayoutFlags(result, LayoutFlags);
            context.InsertAtEnd(parent, result);
            context.SetMargins(result, Margins);
            if (FixedSize.HasValue)
                context.SetSize(result, FixedSize.Value);
            return result;
        }

        protected virtual IDecorationRenderer GetDefaultDecorations (RasterizeContext context) {
            return null;
        }

        protected IDecorationRenderer GetDecorations (RasterizeContext context) {
            return Decorations ?? GetDefaultDecorations(context);
        }

        protected virtual void OnRasterize (RasterizeContext context, RectF box) {
            GetDecorations(context)?.Rasterize(context, box);
        }

        public void Rasterize (RasterizeContext context, Vector2 offset) {
            var box = context.Layout.GetRect(LayoutKey);
            box.Left += offset.X;
            box.Top += offset.Y;
            OnRasterize(context, box);
        }
    }

    public class StaticText : Control {
        public DynamicStringLayout Content = new DynamicStringLayout();
        public bool AutoSize;

        protected override ControlKey OnGenerateLayoutTree (LayoutContext context, ControlKey parent) {
            var result = base.OnGenerateLayoutTree(context, parent);
            if (AutoSize) {
                var interiorSpace = GetFixedInteriorSpace();
                if (interiorSpace.HasValue)
                    Content.LineBreakAtX = interiorSpace.Value.X;
                else
                    Content.LineBreakAtX = null;

                var computedSize = Content.Get().Size + TotalMargins;
                context.SetSize(result, computedSize);
            }

            return result;
        }

        protected override void OnRasterize (RasterizeContext context, RectF box) {
            base.OnRasterize(context, box);

            if (context.Pass != RasterizePasses.Content)
                return;

            var interiorSpace = GetFixedInteriorSpace();
            if (interiorSpace.HasValue)
                Content.LineBreakAtX = interiorSpace.Value.X;
            else
                Content.LineBreakAtX = null;

            var layout = Content.Get();
            context.Renderer.DrawMultiple(layout.DrawCalls, offset: box.Position);
        }
    }

    public class Button : StaticText {
        protected override IDecorationRenderer GetDefaultDecorations (RasterizeContext context) {
            return context.DecorationProvider?.Button;
        }

        protected override void OnRasterize (RasterizeContext context, RectF box) {
            base.OnRasterize(context, box);
        }
    }

    public class Container : Control {
        public ControlFlags ContainerFlags;

        public readonly List<Control> Children = new List<Control>();

        protected override ControlKey OnGenerateLayoutTree (LayoutContext context, ControlKey parent) {
            var result = base.OnGenerateLayoutTree(context, parent);
            context.SetContainerFlags(result, ContainerFlags);
            foreach (var item in Children)
                item.GenerateLayoutTree(context, result);
            return result;
        }

        protected override IDecorationRenderer GetDefaultDecorations (RasterizeContext context) {
            return context.DecorationProvider?.Container;
        }

        protected override void OnRasterize (RasterizeContext context, RectF box) {
            base.OnRasterize(context, box);

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
            var result = base.OnHitTest(context, box, position);
            if (result == null)
                return result;

            foreach (var item in Children) {
                result = item.HitTest(context, position) ?? result;
                if (result != this)
                    return result;
            }

            return result;
        }
    }
}

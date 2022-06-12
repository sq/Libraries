using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Flags;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.PRGUI {
    public abstract partial class Control {
        // HACK: No point having fields for this on every control, so create storage on the fly when
        //  a control is composited
        internal class CompositionData {
            // HACK
            public RectF Box;
            public BitmapDrawCall DrawCall;
        }

        public static bool ShowDebugBoxes = false,
            ShowDebugBoxesForLeavesOnly = false,
            ShowDebugBreakMarkers = false,
            ShowDebugMargins = false,
            ShowDebugPadding = false;

        private CompositionData MostRecentCompositeData;
        protected Margins MostRecentComputedMargins;

        private static readonly ViewTransformModifier ApplyLocalTransformMatrix = _ApplyLocalTransformMatrix,
            ApplyGlobalTransformMatrix = _ApplyGlobalTransformMatrix;
        private static readonly Action<DeviceManager, object> BeforeComposite = _BeforeIssueComposite,
            AfterComposite = _AfterIssueComposite;

        protected virtual void OnPreRasterize (ref UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            UpdateAnimation(context.NowL);
        }

        protected virtual void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            decorations?.Rasterize(ref context, ref renderer, settings);
        }

        protected virtual void OnRasterizeChildren (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
        }

        protected virtual DecorationSettings MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state, bool compositing) {
            return new DecorationSettings {
                Box = box,
                ContentBox = contentBox,
                State = state,
                BackgroundColor = GetBackgroundColor(Context.NowL),
                TextColor = GetTextColor(Context.NowL),
                BackgroundImage = Appearance.BackgroundImage,
                Traits = Appearance.DecorationTraits,
                IsCompositing = compositing
            };
        }

        private void UpdateVisibleRegion (ref UIOperationContext context, ref RectF box) {
            var vr = context.VisibleRegion;
            vr.Left = Math.Max(context.VisibleRegion.Left, box.Left - UIContext.VisibilityPadding);
            vr.Top = Math.Max(context.VisibleRegion.Top, box.Top - UIContext.VisibilityPadding);
            var right = Math.Min(context.VisibleRegion.Extent.X, box.Extent.X + UIContext.VisibilityPadding);
            var bottom = Math.Min(context.VisibleRegion.Extent.Y, box.Extent.Y + UIContext.VisibilityPadding);
            vr.Width = right - vr.Left;
            vr.Height = bottom - vr.Top;
            context.VisibleRegion = vr;
        }

        private void RasterizePass (
            ref UIOperationContext context, 
            ref DecorationSettings settings, IDecorator decorations,
            bool compositing, ref RasterizePassSet passSet, 
            ref ImperativeRenderer renderer, RasterizePasses pass
        ) {
            UIOperationContext passContext;
            context.Clone(out passContext);
            passContext.Pass = pass;
            var hasNestedContext = (pass == RasterizePasses.Content) && 
                (ShouldClipContent || (HasChildren && CreateNestedContextForChildren));
            if (hasNestedContext)
                UpdateVisibleRegion(ref passContext, ref settings.Box);

            // FIXME: The memset for these actually burns a measurable amount of time
            ImperativeRenderer contentRenderer;
            RasterizePassSet childrenPassSet;
            UIOperationContext contentContext;

            int previousStackDepth = passSet.StackDepth, newStackDepth = previousStackDepth;

            // For clipping we need to create a separate batch group that contains all the rasterization work
            //  for our children. At the start of it we'll generate the stencil mask that will be used for our
            //  rendering operation(s).
            if (hasNestedContext) {
                NestedContextPassSetup(
                    ref context, ref passSet, ref renderer, ref passContext, 
                    out contentRenderer, out childrenPassSet, out contentContext, 
                    previousStackDepth, ref newStackDepth
                );
            } else {
                contentContext = passContext;
                childrenPassSet = default;
                contentRenderer = default;
            }

            // TODO: all the copying of settings here burns CPU time

            if (HasPreRasterizeHandler && (pass == RasterizePasses.Content))
                OnPreRasterize(ref contentContext, settings, decorations);

            if (hasNestedContext)
                OnRasterize(ref contentContext, ref contentRenderer, settings, decorations);
            else
                OnRasterize(ref contentContext, ref renderer, settings, decorations);

            if ((pass == RasterizePasses.Content) && HasChildren) {
                if (hasNestedContext)
                    OnRasterizeChildren(ref contentContext, ref childrenPassSet, settings);
                else
                    // FIXME: Save/restore layers?
                    OnRasterizeChildren(ref contentContext, ref passSet, settings);
            }

            if (hasNestedContext) {
                // GROSS OPTIMIZATION HACK: Detect that any rendering operation(s) occurred inside the
                //  group and if so, set up the stencil mask so that they will be clipped.
                if (ShouldClipContent && !contentRenderer.Container.IsEmpty)
                    NestedContextPassTeardown(
                        ref context, ref settings, decorations, ref passSet, 
                        ref contentRenderer, ref contentContext, previousStackDepth
                    );

                renderer.Layer += 1;
            }
        }

        private void NestedContextPassSetup (
            ref UIOperationContext context, ref RasterizePassSet passSet, ref ImperativeRenderer renderer, ref UIOperationContext passContext, 
            out ImperativeRenderer contentRenderer, out RasterizePassSet childrenPassSet, out UIOperationContext contentContext, 
            int previousStackDepth, ref int newStackDepth
        ) {
            renderer.Layer += 1;
            passContext.Clone(out contentContext);
            contentRenderer = renderer.MakeSubgroup();
            if (ShouldClipContent) {
                newStackDepth = previousStackDepth + 1;
                contentRenderer.DepthStencilState = context.UIContext.GetStencilTest(newStackDepth);
                childrenPassSet = new RasterizePassSet(ref contentRenderer, newStackDepth, passSet.OverlayQueue);
            } else {
                contentRenderer.DepthStencilState =
                    (previousStackDepth <= 0)
                    ? DepthStencilState.None
                    : context.UIContext.GetStencilTest(previousStackDepth);
                childrenPassSet = new RasterizePassSet(ref contentRenderer, newStackDepth, passSet.OverlayQueue);
            }
            renderer.Layer += 1;
        }

        private void NestedContextPassTeardown (
            ref UIOperationContext context, ref DecorationSettings settings, IDecorator decorations, 
            ref RasterizePassSet passSet, ref ImperativeRenderer contentRenderer, ref UIOperationContext contentContext, 
            int previousStackDepth
        ) {
            // If this is the first stencil pass instead of a nested one, clear the stencil buffer
            if (passSet.StackDepth < 1) {
                contentRenderer.Clear(stencil: 0, layer: -9999);
            } else {
                // Erase any siblings' clip regions
                contentRenderer.DepthStencilState = context.UIContext.GetStencilRestore(previousStackDepth);
                contentRenderer.FillRectangle(new Rectangle(-1, -1, 9999, 9999), Color.Transparent, blendState: RenderStates.DrawNone, layer: -1000);
            }

            contentRenderer.DepthStencilState = context.UIContext.GetStencilWrite(previousStackDepth);

            // FIXME: Separate context?
            contentContext.Pass = RasterizePasses.ContentClip;

            // FIXME
            var temp = settings;
            ApplyClipMargins(ref contentContext, ref temp.Box);

            var crLayer = contentRenderer.Layer;
            contentRenderer.Layer = -999;
            settings.State = default(ControlStates);
            decorations?.Rasterize(ref contentContext, ref contentRenderer, temp);

            contentRenderer.Layer = crLayer;

            // passSet.NextReferenceStencil = childrenPassSet.NextReferenceStencil;
        }

        private void RasterizeAllPassesTransformed (ref UIOperationContext context, ref RectF box, ref RasterizePassSet passSet) {
            if (MostRecentCompositeData == null)
                MostRecentCompositeData = new CompositionData();
            MostRecentCompositeData.Box = box;
            var subPassSet = new RasterizePassSet(ref passSet, this, ApplyGlobalTransformMatrix);
            RasterizeAllPasses(ref context, ref box, ref subPassSet, false);
        }

        private void RasterizeAllPasses (ref UIOperationContext context, ref RectF box, ref RasterizePassSet passSet, bool compositing) {
            try {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PushDecorationProvider(ref context, Appearance.DecorationProvider);

                var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
                var contentBox = GetRect(contentRect: true);
                var state = GetCurrentState(ref context);
                var settings = MakeDecorationSettings(ref box, ref contentBox, state, compositing);
                if (!IsPassDisabled(RasterizePasses.Below, decorations))
                    RasterizePass(ref context, ref settings, decorations, compositing, ref passSet, ref passSet.Below, RasterizePasses.Below);
                if (!IsPassDisabled(RasterizePasses.Content, decorations))
                    RasterizePass(ref context, ref settings, decorations, compositing, ref passSet, ref passSet.Content, RasterizePasses.Content);
                if (!IsPassDisabled(RasterizePasses.Above, decorations))
                    RasterizePass(ref context, ref settings, decorations, compositing, ref passSet, ref passSet.Above, RasterizePasses.Above);
            } finally {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PopDecorationProvider(ref context);
            }
        }

        protected virtual bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            // Best not to default this optimization on
            // return decorations.IsPassDisabled(pass);
            return false;
        }

        protected virtual pSRGBColor GetDebugBoxColor (int depth) {
            return Color.Lerp(Color.Red, Color.Yellow, depth / 24f);
        }

        private void RasterizeDebugMargins (ref UIOperationContext context, ref RasterizePassSet passSet, ref RectF rect, Margins margins, float direction, Color color, int? layer) {
            float lineWidth = 1.33f, extentLength = 16f, extentThickness = 0.75f;
            var exteriorRect = rect;
            exteriorRect.Left -= margins.Left * direction;
            exteriorRect.Top -= margins.Top * direction;
            exteriorRect.Width += margins.X * direction;
            exteriorRect.Height += margins.Y * direction;
            var center = rect.Center;

            if (margins.Left > 0) {
                passSet.Above.RasterizeRectangle(
                    new Vector2(exteriorRect.Left, center.Y - lineWidth),
                    new Vector2(rect.Left, center.Y + lineWidth),
                    0, color, layer: layer
                );
                passSet.Above.RasterizeRectangle(
                    new Vector2(exteriorRect.Left, center.Y - extentLength),
                    new Vector2(exteriorRect.Left + extentThickness, center.Y + extentLength),
                    0, color, layer: layer
                );
            }

            if (margins.Top > 0) {
                passSet.Above.RasterizeRectangle(
                    new Vector2(center.X - lineWidth, exteriorRect.Top),
                    new Vector2(center.X + lineWidth, rect.Top),
                    0, color, layer: layer
                );
                passSet.Above.RasterizeRectangle(
                    new Vector2(center.X - extentLength, exteriorRect.Top),
                    new Vector2(center.X + extentLength, exteriorRect.Top + extentThickness),
                    0, color, layer: layer
                );
            }

            if (margins.Right > 0) {
                passSet.Above.RasterizeRectangle(
                    new Vector2(exteriorRect.Extent.X, center.Y - lineWidth),
                    new Vector2(rect.Extent.X, center.Y + lineWidth),
                    0, color, layer: layer
                );
                passSet.Above.RasterizeRectangle(
                    new Vector2(exteriorRect.Extent.X, center.Y - extentLength),
                    new Vector2(exteriorRect.Extent.X - extentThickness, center.Y + extentLength),
                    0, color, layer: layer
                );
            }

            if (margins.Bottom > 0) {
                passSet.Above.RasterizeRectangle(
                    new Vector2(center.X - lineWidth, exteriorRect.Extent.Y),
                    new Vector2(center.X + lineWidth, rect.Extent.Y),
                    0, color, layer: layer
                );
                passSet.Above.RasterizeRectangle(
                    new Vector2(center.X - extentLength, exteriorRect.Extent.Y),
                    new Vector2(center.X + extentLength, exteriorRect.Extent.Y + extentThickness),
                    0, color, layer: layer
                );
            }
        }

        private void RasterizeDebugOverlays (ref UIOperationContext context, ref RasterizePassSet passSet, RectF rect) {
            if (!ShowDebugBoxes && !ShowDebugBreakMarkers && !ShowDebugMargins && !ShowDebugPadding && !ShowDebugBoxesForLeavesOnly)
                return;

            var mouseIsOver = rect.Contains(context.MousePosition);
            var alpha = mouseIsOver ? 1.0f : 0.5f;
            // HACK: Show outlines for controls that don't have any children or contain the mouse position
            var isLeaf = (((this as IControlContainer)?.Children?.Count ?? 0) == 0) || mouseIsOver;

            int? layer = null;

            if (ShowDebugBoxes || (ShowDebugBoxesForLeavesOnly && isLeaf))
                passSet.Above.RasterizeRectangle(
                    rect.Position, rect.Extent, 0f, 1f, Color.Transparent, Color.Transparent, 
                    GetDebugBoxColor(context.Depth) * alpha, layer: layer
                );

            if (!context.Layout.TryGetFlags(LayoutKey, out ControlFlags flags))
                return;

            if (ShowDebugMargins)
                RasterizeDebugMargins(ref context, ref passSet, ref rect, context.Layout.GetMargins(LayoutKey), 1f, Color.Green, layer);

            if (ShowDebugPadding)
                RasterizeDebugMargins(ref context, ref passSet, ref rect, context.Layout.GetPadding(LayoutKey), -1f, Color.Yellow, layer);

            if (ShowDebugBreakMarkers && mouseIsOver && flags.IsBreak()) {
                rect = new RectF(
                    new Vector2(rect.Left - 1.5f, rect.Center.Y - 7.5f),
                    new Vector2(6.5f, 15)
                );
                
                var facingRight = false;
                Vector2 a = !facingRight ? rect.Extent : rect.Position,
                    b = !facingRight 
                        ? new Vector2(rect.Position.X, rect.Center.Y)
                        : new Vector2(rect.Extent.X, rect.Center.Y),
                    c = !facingRight
                        ? new Vector2(rect.Extent.X, rect.Position.Y)
                        : new Vector2(rect.Position.X, rect.Extent.Y);

                var arrowColor =
                    flags.IsFlagged(ControlFlags.Layout_ForceBreak)
                        ? Color.White
                        : Color.Yellow;

                passSet.Above.RasterizeTriangle(
                    a, b, c, radius: 0f, outlineRadius: 1f,
                    innerColor: arrowColor * alpha, outerColor: arrowColor * alpha, 
                    outlineColor: Color.Black * (alpha * 0.8f)
                );
            }
        }

        public bool Rasterize (ref UIOperationContext context, ref RasterizePassSet passSet, float opacity = 1) {
            // HACK: Do this first since it fires opacity change events
            var hidden = false;

            var tweenOpacity = GetOpacity(context.NowL);
            opacity *= tweenOpacity;

            if (opacity <= 0)
                hidden = true;
            if (!Visible)
                hidden = true;

            var box = default(RectF);
            bool isZeroSized = false, isOutOfView = false;
            if (IsLayoutInvalid) {
                hidden = true;
            } else {
                box = GetRect();
                Vector2 ext = box.Extent,
                    vext = context.VisibleRegion.Extent;
                // HACK: There might be corner cases where you want to rasterize a zero-sized control...
                isZeroSized = (box.Width <= 0) || (box.Height <= 0);
                isOutOfView = (ext.X < context.VisibleRegion.Left) ||
                    (ext.Y < context.VisibleRegion.Top) ||
                    (box.Left > vext.X) ||
                    (box.Top > vext.Y);

                RasterizeDebugOverlays(ref context, ref passSet, box);
            }

#if DETECT_DOUBLE_RASTERIZE
            if (!RasterizeIsPending)
                throw new Exception("Double rasterize detected");
            RasterizeIsPending = false;
#endif

            if (isZeroSized)
                hidden = true;

            // Only visibility cull controls that have a parent and aren't overlaid.
            if (isOutOfView && (WeakParent != null) && !Appearance.Overlay && !Appearance.HasTransformMatrix)
                hidden = true;

            if (hidden) {
                // HACK: Ensure pre-rasterize handlers run for hidden controls, because the handler
                //  may be doing something important like updating animations or repainting a buffer
                RunPreRasterizeHandlerForHiddenControl(ref context, ref box);
                return false;
            }

            Appearance.AutoClearTransform(context.NowL);

            var enableCompositor = Appearance.Compositor?.WillComposite(this, opacity) == true;
            var hasTransformMatrix = Appearance.HasTransformMatrix &&
                // HACK: If the current transform matrix is the identity matrix, suppress composition
                //  this allows simple transform animations that end at the identity matrix to work
                //  without explicitly clearing the transform after the animation is over.
                Appearance.GetTransform(out Matrix transform, context.NowL) &&
                (transform != Matrix.Identity);
            var needsComposition = NeedsComposition(opacity < 1, hasTransformMatrix) || enableCompositor;
            var oldOpacity = context.Opacity;
            if (!needsComposition) {
                context.Opacity *= opacity;
                if (hasTransformMatrix)
                    RasterizeAllPassesTransformed(ref context, ref box, ref passSet);
                else
                    RasterizeAllPasses(ref context, ref box, ref passSet, false);
            } else {
                RasterizeComposited(ref context, ref box, ref passSet, opacity, enableCompositor);
            }
            context.Opacity = oldOpacity;

            return true;
        }

        private void RunPreRasterizeHandlerForHiddenControl (ref UIOperationContext context, ref RectF box) {
            if (!HasPreRasterizeHandler)
                return;

            var decorations = GetDecorator(context.DecorationProvider, context.DefaultDecorator);
            var state = GetCurrentState(ref context) | ControlStates.Invisible;
            var settings = MakeDecorationSettings(ref box, ref box, state, false);
            OnPreRasterize(ref context, settings, decorations);
        }

        private void RasterizeComposited (ref UIOperationContext context, ref RectF box, ref RasterizePassSet passSet, float opacity, bool enableCompositor) {
            // HACK: Create padding around the element for drop shadows
            var padding = Appearance.Compositor?.Padding ?? Context.CompositorPaddingPx;
            box.SnapAndInset(out Vector2 tl, out Vector2 br, -padding);
            // Don't overflow the edges of the canvas with padding, it'd produce garbage pixels
            var canvasRect = context.UIContext.CanvasRect;
            canvasRect.Clamp(ref tl);
            canvasRect.Clamp(ref br);

            var compositeBox = new RectF(tl, br - tl);
            var srt = context.UIContext.GetScratchRenderTarget(context.Prepass, in compositeBox);
            if (context.RenderTargetStack.Count > 0)
                context.RenderTargetStack[context.RenderTargetStack.Count - 1].Dependencies.Add(srt);
            context.RenderTargetStack.Add(srt);
            try {
                // passSet.Above.RasterizeRectangle(box.Position, box.Extent, 1f, Color.Red * 0.1f);
                RasterizeIntoPrepass(ref context, passSet, opacity, ref box, ref compositeBox, srt, enableCompositor);
                // passSet.Above.RasterizeEllipse(box.Center, Vector2.One * 3f, Color.White);
            } finally {
                context.RenderTargetStack.RemoveTail(1);
                context.UIContext.ReleaseScratchRenderTarget(srt.Instance);
            }
        }

        private static void _BeforeIssueComposite (DeviceManager dm, object _control) {
            var control = (Control)_control;
            control.Appearance.Compositor?.BeforeIssueComposite(control, dm, in control.MostRecentCompositeData.DrawCall);
        }

        private static void _AfterIssueComposite (DeviceManager dm, object _control) {
            var control = (Control)_control;
            control.Appearance.Compositor?.AfterIssueComposite(control, dm, in control.MostRecentCompositeData.DrawCall);
        }

        private static void _ApplyLocalTransformMatrix (ref ViewTransform vt, object _control) {
            var control = (Control)_control;
            control.Appearance.GetFinalTransformMatrix(
                control.MostRecentCompositeData.Box, control.Context.NowL, out Matrix transform
            );
            vt.ModelView *= transform;
        }

        private static void _ApplyGlobalTransformMatrix (ref ViewTransform vt, object _control) {
            var control = (Control)_control;
            if (!control.Appearance.GetTransform(out Matrix matrix, control.Context.NowL))
                return;

            var rect = control.MostRecentCompositeData.Box;
            var offset = (rect.Size * control.Appearance.TransformOrigin) + rect.Position;
            Matrix.CreateTranslation(-offset.X, -offset.Y, 0f, out Matrix before);
            Matrix.CreateTranslation(offset.X, offset.Y, 0f, out Matrix after);
            var result = before * matrix * after;
            vt.ModelView *= result;
        }

        private void RasterizeIntoPrepass (
            ref UIOperationContext context, RasterizePassSet passSet, float opacity, 
            ref RectF box, ref RectF compositeBox, 
            UIContext.ScratchRenderTarget rt, bool enableCompositor
        ) {
            UIOperationContext compositionContext;
            context.Clone(out compositionContext);
            compositionContext.Opacity = 1.0f;
            UpdateVisibleRegion(ref compositionContext, ref box);

            var newPassSet = new RasterizePassSet(ref rt.Renderer, 0, passSet.OverlayQueue);
            // newPassSet.Above.RasterizeEllipse(box.Center, Vector2.One * 6f, Color.White * 0.7f);
            RasterizeAllPasses(ref compositionContext, ref box, ref newPassSet, true);
            rt.Renderer.Layer += 1;
            var pos = Appearance.HasTransformMatrix ? Vector2.Zero : compositeBox.Position.Floor();
            // FIXME: Is this the right layer?
            var sourceRect = new Rectangle(
                (int)compositeBox.Left, (int)compositeBox.Top,
                (int)compositeBox.Width, (int)compositeBox.Height
            );
            var effectiveOpacity = context.Opacity * opacity;
            var dc = new BitmapDrawCall(
                // FIXME
                rt.Instance.Get(), pos,
                GameExtensionMethods.BoundsFromRectangle((int)Context.CanvasSize.X, (int)Context.CanvasSize.Y, in sourceRect),
                new Color(effectiveOpacity, effectiveOpacity, effectiveOpacity, effectiveOpacity), scale: 1.0f / Context.ScratchScaleFactor
            );

            if (Appearance.HasTransformMatrix || enableCompositor) {
                RasterizeIntoPrepassComposited(ref passSet, ref compositeBox, dc, enableCompositor, effectiveOpacity);
            } else if (Appearance.Overlay) {
                passSet.OverlayQueue.Add(in dc);
            } else {
                GetMaterialAndBlendStateForCompositing(out Material compositeMaterial, out BlendState compositeBlendState);
                passSet.Above.Draw(
                    in dc, material: compositeMaterial,
                    blendState: compositeBlendState ?? RenderStates.PorterDuffOver
                );
                passSet.Above.Layer += 1;
            }
        }

        private void RasterizeIntoPrepassComposited (
            ref RasterizePassSet passSet, ref RectF compositeBox, in BitmapDrawCall dc,
            bool enableCompositor, float effectiveOpacity
        ) {
            if (MostRecentCompositeData == null)
                MostRecentCompositeData = new CompositionData();
            MostRecentCompositeData.DrawCall = dc;
            MostRecentCompositeData.Box = compositeBox;
            var subgroup = passSet.Above.MakeSubgroup(
                before: BeforeComposite,
                after: AfterComposite,
                userData: this
            );
            ((BatchGroup)subgroup.Container).SetViewTransform(Appearance.HasTransformMatrix ? ApplyLocalTransformMatrix : null);
            subgroup.BlendState = RenderStates.PorterDuffOver;
            if (enableCompositor)
                Appearance.Compositor.Composite(this, ref subgroup, in dc, effectiveOpacity);
            else
                subgroup.Draw(in dc, material: Appearance.CompositeMaterial);
        }
    }
}

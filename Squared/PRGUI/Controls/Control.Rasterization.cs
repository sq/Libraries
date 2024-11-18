﻿using System;
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
        private static int RasterizeOrderingWarningCount = 0;

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

        protected virtual void OnPreRasterize (ref UIOperationContext context, ref DecorationSettings settings, IDecorator decorations) {
            UpdateAnimation(context.NowL);
        }

        protected virtual void OnRasterize (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings, IDecorator decorations) {
            decorations?.Rasterize(ref context, ref passSet, ref settings);
        }

        protected virtual void OnRasterizeChildren (ref UIOperationContext context, ref RasterizePassSet passSet, DecorationSettings settings) {
        }

        protected virtual void MakeDecorationSettingsFast (ControlStates state, ref DecorationSettings result) {
            result.State = state;
            result.Traits.OverwriteWith(ref Appearance.DecorationTraits, true);
            result.UniqueId = ControlIndex;
        }

        // fast=true omits boxes, images and colors
        protected virtual void MakeDecorationSettings (ref RectF box, ref RectF contentBox, ControlStates state, bool compositing, out DecorationSettings result) {
            result = new DecorationSettings {
                Box = box,
                ContentBox = contentBox,
                State = state,
                BackgroundColor = GetBackgroundColor(Context.NowL),
                TextColor = GetTextColor(Context.NowL),
                BackgroundImage = Appearance.BackgroundImage,
                IsCompositing = compositing,
                UniqueId = ControlIndex
            };
            Appearance.DecorationTraits.Clone(ref result.Traits, true);
        }

        private void UpdateVisibleRegion (ref UIOperationContext context, ref RectF box) {
            var vr = context.VisibleRegion;
            vr.Left = Math.Max(vr.Left, box.Left - UIContext.VisibilityPadding);
            vr.Top = Math.Max(vr.Top, box.Top - UIContext.VisibilityPadding);
            var right = Math.Min(vr.Extent.X, box.Extent.X + UIContext.VisibilityPadding);
            var bottom = Math.Min(vr.Extent.Y, box.Extent.Y + UIContext.VisibilityPadding);
            vr.Width = right - vr.Left;
            vr.Height = bottom - vr.Top;
            context.VisibleRegion = vr;
        }

        private void RasterizePassesCommonBody (
            ref UIOperationContext context, ref DecorationSettings settings, ref RasterizePassSet passSet, IDecorator decorations
        ) {
            if (HasPreRasterizeHandler)
                OnPreRasterize(ref context, ref settings, decorations);

            OnRasterize(ref context, ref passSet, settings, decorations);

            // HACK: Without this our background color can cover our children's below pass, because
            //  they both rasterize on the same layer
            // It may be best to just always advance the below layer
            if (
                (!Appearance.BackgroundColor.IsTransparent || Appearance.BackgroundImage != null) &&
                HasChildren
            ) {
                passSet.Below.Layer += 1;
            }
        }

        private void NestedContextPassSetup (
            ref UIOperationContext context, ref RasterizePassSet passSet, ref UIOperationContext passContext, 
            out ImperativeRenderer contentRenderer, out RasterizePassSet childrenPassSet, out UIOperationContext contentContext, 
            int previousStackDepth, ref int newStackDepth
        ) {
            passSet.Content.Layer += 1;
            passContext.Clone(out contentContext);
            passSet.Content.MakeSubgroup(out contentRenderer);
            passSet.Content.Layer += 1;

            if (ShouldClipContent) {
                newStackDepth = previousStackDepth + 1;
                contentRenderer.DepthStencilState = context.UIContext.GetStencilTest(newStackDepth);
                childrenPassSet = new RasterizePassSet(ref contentRenderer, this, newStackDepth);
            } else {
                contentRenderer.DepthStencilState =
                    (previousStackDepth <= 0)
                    ? DepthStencilState.None
                    : context.UIContext.GetStencilTest(previousStackDepth);
                childrenPassSet = new RasterizePassSet(ref contentRenderer, this, newStackDepth);
            }
        }

        private void NestedContextPassTeardown (
            ref UIOperationContext context, ref DecorationSettings settings, IDecorator decorations, 
            ref RasterizePassSet passSet, ref ImperativeRenderer contentRenderer, ref UIOperationContext contentContext, 
            int previousStackDepth
        ) {
            // If this is the first stencil pass instead of a nested one, clear the stencil buffer
            if (passSet.StackDepth < 1) {
                // FIXME: We're currently doing this too often, but maybe that's fine since it's a hw clear op and not a draw?
                contentRenderer.Clear(stencil: 0, layer: -9999, name: "Clear stencil for StackDepth <= 0");
            } else {
                // Erase any siblings' clip regions
                contentRenderer.DepthStencilState = context.UIContext.GetStencilRestore(previousStackDepth);
                var gb = contentRenderer.GetGeometryBatch(-1000, null, RenderStates.DrawNone);
                gb.Name = "Erase sibling clip regions";
                // Unbalanced vertices to draw one triangle instead of two
                gb.AddFilledQuad(Vector2.One * -1, new Vector2(9999999, 9999), Color.Transparent);
            }

            // HACK: If content clipping is disabled, painting our content region into the stencil buffer is unnecessary
            // FIXME: Suppress the clears too? That might be wrong, and the clears are cheaper anyway since they don't use a decorator
            if (ShouldClipContent) {
                contentRenderer.DepthStencilState = context.UIContext.GetStencilWrite(previousStackDepth);

                // FIXME
                var temp = settings;
                ApplyClipMargins(ref contentContext, ref temp.Box);

                var crLayer = contentRenderer.Layer;
                contentRenderer.Layer = -999;
                settings.State = default;
                decorations?.RasterizeClip(ref contentContext, ref contentRenderer, ref temp);
                contentRenderer.Layer = crLayer + 1;
            }

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

                var decorations = GetDecorator(context.DecorationProvider);
                var contentBox = GetRect(contentRect: true);
                var state = GetCurrentState(ref context);
                MakeDecorationSettings(ref box, ref contentBox, state, compositing, out var settings);

                context.Clone(out var passContext);
                var icrc = this as IClippedRasterizationControl;
                var hasNestedContext = ShouldClipContent || (HasChildren && CreateNestedContextForChildren);

                int previousStackDepth = passSet.StackDepth, newStackDepth = previousStackDepth;

                if (hasNestedContext) {
                    UpdateVisibleRegion(ref passContext, ref settings.Box);

                    // For clipping we need to create a separate batch group that contains all the rasterization work
                    //  for our children. At the start of it we'll generate the stencil mask that will be used for our
                    //  rendering operation(s).
                    NestedContextPassSetup(
                        ref context, ref passSet, ref passContext,
                        out var contentRenderer, out var contentPassSet, out var contentContext, 
                        previousStackDepth, ref newStackDepth
                    );

                    RasterizePassesCommonBody(
                        ref passContext, ref settings, ref passSet, decorations
                    );

                    icrc?.RasterizeClipped(ref contentContext, ref contentPassSet, settings, decorations); 

                    if (HasChildren)
                        // FIXME: Save/restore layers?
                        OnRasterizeChildren(ref contentContext, ref contentPassSet, settings);

                    NestedContextPassTeardown(
                        ref context, ref settings, decorations, ref passSet, 
                        ref contentRenderer, ref contentContext, previousStackDepth
                    );
                } else {
                    RasterizePassesCommonBody(
                        ref passContext, ref settings, ref passSet, decorations
                    );

                    icrc?.RasterizeClipped(ref passContext, ref passSet, settings, decorations); 

                    if (HasChildren)
                        // FIXME: Save/restore layers?
                        OnRasterizeChildren(ref context, ref passSet, settings);
                }
            } finally {
                if (Appearance.DecorationProvider != null)
                    UIOperationContext.PopDecorationProvider(ref context);
            }
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

            var config = Record(ref context).Config;

            Margins margins = default, padding = default;
            if (ShowDebugMargins || ShowDebugPadding) {
                ComputeAppearanceSpacing(this, ref context, out margins, out var padding1, out var padding2);
                padding = padding1 + padding2;
            }

            if (ShowDebugMargins)
                RasterizeDebugMargins(ref context, ref passSet, ref rect, margins, 1f, Color.Green, layer);

            if (ShowDebugPadding)
                RasterizeDebugMargins(ref context, ref passSet, ref rect, padding, -1f, Color.Yellow, layer);

            if (ShowDebugBreakMarkers && mouseIsOver && config.ForceBreak) {
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

                pSRGBColor arrowColor = Color.White;

                passSet.Above.RasterizeTriangle(
                    a, b, c, radius: 0f, outlineRadius: 1f,
                    innerColor: arrowColor * alpha, outerColor: arrowColor * alpha, 
                    outlineColor: pSRGBColor.Black(alpha * 0.8f)
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
            bool isZeroSized = false, isOutOfView = false,
                transformActive = (context.TransformsActive > 0) || Appearance.HasTransformMatrix;
            if (IsLayoutInvalid) {
                if ((_LayoutKey == ControlKey.Corrupt) && !hidden && (RasterizeOrderingWarningCount++ < 10))
                    System.Diagnostics.Debug.WriteLine($"WARNING: Control {this} failed to rasterize because it had no valid layout. This likely means it was created after the most recent UIContext.Update.");
                hidden = true;
            } else {
                box = context.UIContext.Engine.Result(_LayoutKey).Rect;
                box.Left += _AbsoluteDisplayOffset.X;
                box.Top += _AbsoluteDisplayOffset.Y;

                Vector2 vext = context.VisibleRegion.Extent;
                // HACK: There might be corner cases where you want to rasterize a zero-sized control...
                isZeroSized = (box.Width <= 0) || (box.Height <= 0);
                isOutOfView = ((box.Left + box.Width) < context.VisibleRegion.Left) ||
                    ((box.Top + box.Height) < context.VisibleRegion.Top) ||
                    (box.Left > vext.X) ||
                    (box.Top > vext.Y);

                // FIXME: Transform not applied in non-composited mode. But maybe that's good?
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
            if (isOutOfView && (WeakParent != null) && !transformActive)
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
                Appearance.GetTransform(out Matrix transform, out _, context.NowL) &&
                (transform != ControlMatrixInfo.IdentityMatrix);

            var needsComposition = NeedsComposition(opacity < 1, hasTransformMatrix) || enableCompositor;
            var oldOpacity = context.Opacity;
            if (hasTransformMatrix)
                context.TransformsActive++;
            try {
                if (!needsComposition) {
                    context.Opacity *= opacity;
                    if (hasTransformMatrix)
                        RasterizeAllPassesTransformed(ref context, ref box, ref passSet);
                    else
                        RasterizeAllPasses(ref context, ref box, ref passSet, false);
                } else {
                    RasterizeComposited(ref context, ref box, ref passSet, opacity, enableCompositor);
                }
            } finally {
                context.Opacity = oldOpacity;
                if (hasTransformMatrix)
                    context.TransformsActive--;
            }

            return true;
        }

        private void RunPreRasterizeHandlerForHiddenControl (ref UIOperationContext context, ref RectF box) {
            if (!HasPreRasterizeHandler)
                return;

            var decorations = GetDecorator(context.DecorationProvider);
            var state = GetCurrentState(ref context) | ControlStates.Invisible;
            MakeDecorationSettings(ref box, ref box, state, false, out var settings);
            OnPreRasterize(ref context, ref settings, decorations);
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
            AutoRenderTarget rt = GetCustomCompositingSurface(ref compositeBox);
            UIContext.ScratchRenderTarget srt = null;
            if (rt == null) {
                srt = context.UIContext.GetScratchRenderTarget(context.Prepass, ref compositeBox);
                if (context.RenderTargetStack.Count > 0)
                    context.RenderTargetStack[context.RenderTargetStack.Count - 1].Dependencies.Add(srt);
                context.RenderTargetStack.Add(srt);
                rt = srt.Instance;
            }

            var oldTarget = context.CompositingTarget;
            try {
                context.CompositingTarget = rt;
                // passSet.Above.RasterizeRectangle(box.Position, box.Extent, 1f, Color.Red * 0.1f);
                if (srt != null)
                    RasterizeIntoPrepass(
                        ref context, passSet, opacity, ref box, ref compositeBox,
                        srt.Instance.Get(), ref srt.Renderer, enableCompositor
                    );
                else {
                    var vt = ViewTransform.CreateOrthographic((int)Context.CanvasSize.X, (int)Context.CanvasSize.Y);
                    var scratchRenderer =
                        new ImperativeRenderer(context.Prepass, context.Materials).ForRenderTarget(
                            rt, viewTransform: vt,
                            // HACK: Scratch render target layers start at -9999 post-sort, ensure that custom
                            //  composition surfaces come before automatic ones since they don't participate
                            //  in sorting
                            layer: -10000
                        );
                    RasterizeIntoPrepass(
                        ref context, passSet, opacity, ref box, ref compositeBox,
                        rt.Get(), ref scratchRenderer, enableCompositor
                    );
                }
                // passSet.Above.RasterizeEllipse(box.Center, Vector2.One * 3f, Color.White);
            } finally {
                context.CompositingTarget = oldTarget;
                if (srt != null) {
                    context.RenderTargetStack.RemoveTail(1);
                    context.UIContext.ReleaseScratchRenderTarget(srt.Instance);
                }
            }
        }

        protected virtual AutoRenderTarget GetCustomCompositingSurface (ref RectF compositeBox) => null;

        private static void _BeforeIssueComposite (DeviceManager dm, object _control) {
            var control = (Control)_control;
            control.Appearance.Compositor?.BeforeIssueComposite(control, dm, ref control.MostRecentCompositeData.DrawCall);
        }

        private static void _AfterIssueComposite (DeviceManager dm, object _control) {
            var control = (Control)_control;
            control.Appearance.Compositor?.AfterIssueComposite(control, dm, ref control.MostRecentCompositeData.DrawCall);
        }

        private static void _ApplyLocalTransformMatrix (ref ViewTransform vt, object _control) {
            var control = (Control)_control;
            control.Appearance.GetPlacementTransformMatrix(
                control.MostRecentCompositeData.Box, control.Context.NowL, out Matrix transform
            );
            vt.ModelView *= transform;
        }

        private static void _ApplyGlobalTransformMatrix (ref ViewTransform vt, object _control) {
            var control = (Control)_control;
            if (!control.Appearance.GetTransform(out Matrix matrix, out Vector2 origin, control.Context.NowL))
                return;

            var rect = control.MostRecentCompositeData.Box;
            var offset = (rect.Size * origin) + rect.Position;
            Matrix.CreateTranslation(-offset.X, -offset.Y, 0f, out Matrix before);
            Matrix.CreateTranslation(offset.X, offset.Y, 0f, out Matrix after);

            // For nested non-composited transforms to work, we need to apply our transform in an unusual order
            Matrix.Multiply(ref before, ref matrix, out var temp);
            Matrix.Multiply(ref temp, ref after, out matrix);
            Matrix.Multiply(ref matrix, ref vt.ModelView, out temp);
            vt.ModelView = temp;
        }

        private void RasterizeIntoPrepass (
            ref UIOperationContext context, RasterizePassSet passSet, float opacity, 
            ref RectF box, ref RectF compositeBox, 
            Texture2D compositingSurface, ref ImperativeRenderer compositingRenderer, 
            bool enableCompositor
        ) {
            context.Clone(out var compositionContext);
            compositionContext.Opacity = 1.0f;
            UpdateVisibleRegion(ref compositionContext, ref box);

            var newPassSet = new RasterizePassSet(ref compositingRenderer, this, 0);
            // newPassSet.Above.RasterizeEllipse(box.Center, Vector2.One * 6f, Color.White * 0.7f);
            RasterizeAllPasses(ref compositionContext, ref box, ref newPassSet, true);
            compositingRenderer.Layer += 1;
            var pos = Appearance.HasTransformMatrix ? Vector2.Zero : compositeBox.Position.Floor();
            // FIXME: Is this the right layer?
            var sourceRect = new Rectangle(
                (int)compositeBox.Left, (int)compositeBox.Top,
                (int)compositeBox.Width, (int)compositeBox.Height
            );
            var effectiveOpacity = context.Opacity * opacity;
            var dc = new BitmapDrawCall(
                // FIXME
                compositingSurface, pos,
                GameExtensionMethods.BoundsFromRectangle((int)Context.CanvasSize.X, (int)Context.CanvasSize.Y, in sourceRect),
                new Color(effectiveOpacity, effectiveOpacity, effectiveOpacity, effectiveOpacity), scale: 1.0f / Context.ScratchScaleFactor
            );

            if (Appearance.HasTransformMatrix || enableCompositor) {
                GetMaterialAndBlendStateForCompositing(out _, out BlendState compositeBlendState);
                RasterizeIntoPrepassComposited(ref passSet, ref compositeBox, ref dc, enableCompositor, effectiveOpacity, compositeBlendState);
            } else {
                GetMaterialAndBlendStateForCompositing(out Material compositeMaterial, out BlendState compositeBlendState);
                passSet.Above.Draw(
                    ref dc, material: compositeMaterial,
                    blendState: compositeBlendState ?? RenderStates.PorterDuffOver
                );
                passSet.Above.Layer += 1;
            }
        }

        private void RasterizeIntoPrepassComposited (
            ref RasterizePassSet passSet, ref RectF compositeBox, ref BitmapDrawCall dc,
            bool enableCompositor, float effectiveOpacity, BlendState blendState
        ) {
            if (MostRecentCompositeData == null)
                MostRecentCompositeData = new CompositionData();
            MostRecentCompositeData.DrawCall = dc;
            MostRecentCompositeData.Box = compositeBox;
            passSet.Above.MakeSubgroup(
                out var subgroup,
                before: BeforeComposite,
                after: AfterComposite,
                userData: this
            );
			// FIXME: Is this right?
            ((BatchGroup)subgroup.Container).SetViewTransform(Appearance.HasTransformMatrix ? ApplyLocalTransformMatrix : null);
            subgroup.BlendState = RenderStates.PorterDuffOver;
            if (enableCompositor)
                Appearance.Compositor.Composite(this, ref subgroup, ref dc, effectiveOpacity, blendState);
            else
                subgroup.Draw(ref dc, material: Appearance.CompositeMaterial, blendState: blendState);
        }
    }
}

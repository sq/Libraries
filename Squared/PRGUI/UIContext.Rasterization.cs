﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Input;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.PRGUI {
    public sealed partial class UIContext : IDisposable {
        /// <summary>
        /// Performance stats
        /// </summary>
        public int LastPassCount;

        internal int FrameIndex;

        private UnorderedList<ScratchRenderTarget> ScratchRenderTargets = new UnorderedList<ScratchRenderTarget>();
        private readonly static Dictionary<int, DepthStencilState> StencilEraseStates = new Dictionary<int, DepthStencilState>();
        private readonly static Dictionary<int, DepthStencilState> StencilWriteStates = new Dictionary<int, DepthStencilState>();
        private readonly static Dictionary<int, DepthStencilState> StencilTestStates = new Dictionary<int, DepthStencilState>();

        /// <summary>
        /// The surface format used for scratch compositor textures. Update this if you want to use sRGB.
        /// </summary>
        public SurfaceFormat ScratchSurfaceFormat = SurfaceFormat.Color;

        private bool WasBackgroundFaded = false;
        private Tween<float> BackgroundFadeTween = new Tween<float>(0f);
        private UnorderedList<BitmapDrawCall> OverlayQueue = new UnorderedList<BitmapDrawCall>();

        private List<ScratchRenderTarget> TopoSortTable = new List<ScratchRenderTarget>();

        public ImperativeRenderer OverlayRenderer;

        internal sealed class ScratchRenderTarget : IDisposable {
            public readonly UIContext Context;
            public readonly AutoRenderTarget Instance;
            public readonly UnorderedList<RectF> UsedRectangles = new UnorderedList<RectF>();
            public ImperativeRenderer Renderer;
            public List<ScratchRenderTarget> Dependencies = new List<ScratchRenderTarget>();
            internal bool VisitedByTopoSort;

            public ScratchRenderTarget (RenderCoordinator coordinator, UIContext context) {
                Context = context;
                int width = (int)(context.CanvasSize.X * context.ScratchScaleFactor),
                    height = (int)(context.CanvasSize.Y * context.ScratchScaleFactor);
                Instance = new AutoRenderTarget(
                    coordinator, width, height,
                    false, Context.ScratchSurfaceFormat, DepthFormat.Depth24Stencil8
                );
            }

            public void Update () {
                int width = (int)(Context.CanvasSize.X * Context.ScratchScaleFactor),
                    height = (int)(Context.CanvasSize.Y * Context.ScratchScaleFactor);
                Instance.Resize(width, height);
            }

            public void Reset () {
                UsedRectangles.Clear();
                Dependencies.Clear();
                VisitedByTopoSort = false;
            }

            public void Dispose () {
                Instance?.Dispose();
            }

            internal bool IsSpaceAvailable (in RectF rectangle) {
                foreach (var used in UsedRectangles) {
                    if (used.Intersects(in rectangle))
                        return false;
                }

                return true;
            }
        }

        public T ForEachScratchRenderTarget<T> (Func<T, Texture2D, T> f, T initialValue = default(T)) {
            var result = initialValue;
            foreach (var srt in ScratchRenderTargets) {
                var tex = srt?.Instance?.Get();
                result = f(result, tex);
            }
            return result;
        }

        private void FlushOverlayQueue (ref ImperativeRenderer renderer) {
            foreach (var dc in OverlayQueue) {
                renderer.Draw(dc);
                renderer.Layer += 1;
            }

            OverlayQueue.Clear();
        }

        internal ScratchRenderTarget GetScratchRenderTarget (BatchGroup prepass, ref RectF rectangle) {
            ScratchRenderTarget result = null;

            foreach (var rt in ScratchRenderTargets) {
                if (rt.IsSpaceAvailable(in rectangle)) {
                    result = rt;
                    break;
                }
            }

            if (result == null) {
                result = new ScratchRenderTarget(prepass.Coordinator, this);
                ScratchRenderTargets.Add(result);
            }

            if (result.UsedRectangles.Count == 0) {
                using (var group = BatchGroup.ForRenderTarget(prepass, 0, result.Instance, name: "Scratch Prepass")) {
                    result.Renderer = new ImperativeRenderer(group, Materials);
                    result.Renderer.BlendState = BlendState.AlphaBlend;
                    result.Renderer.Clear(-9999, color: Color.Transparent, z: 0f, /* FrameColors[FrameIndex % FrameColors.Length] * 0.5f */ stencil: 0);
                }
            }

            result.UsedRectangles.Add(ref rectangle);
            return result;
        }

        internal void ReleaseScratchRenderTarget (AutoRenderTarget rt) {
            // FIXME: Do we need to do anything here?
        }

        private readonly static Color[] DebugColors = new[] {
            new Color(240, 0, 0),
            new Color(210, 100, 0),
            new Color(192, 192, 0),
            new Color(0, 240, 0),
            new Color(0, 0, 240),
            new Color(192, 0, 192),
            new Color(0, 192, 192),
            Color.Gray,
            new Color(96, 96, 0),
            new Color(0, 96, 96),
        };

        private StringBuilder LayoutTreeBuilder = new StringBuilder();

        private void RasterizeLayoutTree (
            ref ImperativeRenderer renderer, Render.Text.IGlyphSource font, ref NewEngine.BoxRecord record, 
            HashSet<Layout.ControlKey> focusChain
        ) {
#if DEBUG
            // HACK to allow easily hiding subtrees
            if ((record.Width.Fixed == 0) && (record.Height.Fixed == 0))
                return;

            ref var result = ref Engine.Result(record.Key);
            var obscuredByFocus = (focusChain != null) && !focusChain.Contains(record.Key);

            if (record.Key.ID > 0) {
                var alpha = obscuredByFocus ? 0.45f : 1f;
                pSRGBColor fillColor = DebugColors[result.Depth % DebugColors.Length],
                    lineColor = fillColor.AdjustBrightness(0.33f, true) * (obscuredByFocus ? 0.2f : 1f),
                    textColor = fillColor.AdjustBrightness(2f, true) * (obscuredByFocus ? 0.6f : 1f);
                var outlineSize = 1f;
                var offset = new Vector2(outlineSize);
                var layer = result.Depth * 2;
                renderer.RasterizeRectangle(
                    result.Rect.Position + offset, result.Rect.Extent - offset, 
                    0.75f, outlineSize, fillColor * alpha, fillColor * alpha, lineColor,
                    layer: layer
                );
                var obscureText = obscuredByFocus && !focusChain.Contains(record.Parent);
                if (!obscureText && (font != null)) {
                    LayoutTreeBuilder.Clear();
#if DEBUG
                    var tagText = record.DebugLabel ?? record.Tag.ToString();
#else
                    var tagText = record.Tag;
#endif
                    LayoutTreeBuilder.AppendFormat("{0} {1} {2},{3}", record.Key.ID, tagText, Math.Floor(result.Rect.Width), Math.Floor(result.Rect.Height));
                    var layout = font.LayoutString(LayoutTreeBuilder, color: textColor.ToColor());
                    var textScale = (obscuredByFocus ? 0.6f : 1f);
                    var scale = Arithmetic.Clamp(
                        Math.Min(
                            (result.Rect.Size.Y - 2) / layout.Size.Y,
                            (result.Rect.Size.X - 4) / layout.Size.X
                        ) * textScale,
                        0.33f, textScale
                    );
                    result.Rect.Intersection(CanvasRect, out RectF textRect);
                    var textOffset = textRect.Position + (textRect.Size - (layout.Size * scale)) * 0.5f;
                    renderer.DrawMultiple(layout.DrawCalls, textOffset, scale: new Vector2(scale), layer: layer + 1);
                }
            }

            if (obscuredByFocus)
                return;
            foreach (var ckey in Engine.Children(record.Key)) {
                ref var child = ref Engine[ckey];
                RasterizeLayoutTree(ref renderer, font, ref child, focusChain);
            }
#endif
                }

        public void Rasterize (BatchGroup container, int layer, BatchGroup prepassContainer, int prepassLayer, Color? clearColor = null) {
            FrameIndex++;

            Now = (float)TimeProvider.Seconds;
            NowL = TimeProvider.Ticks;

            MakeOperationContext(ref _RasterizeFree, ref _RasterizeInUse, out var context);

            try {
                ScratchRenderTargetsUsedThisFrame = 0;
                foreach (var srt in ScratchRenderTargets) {
                    if (_ScratchRenderTargetsInvalid)
                        container.Coordinator.DisposeResource(srt);
                    else {
                        srt.Update();
                        srt.Reset();
                    }
                }
                if (_ScratchRenderTargetsInvalid) {
                    ScratchRenderTargets.Clear();
                    _ScratchRenderTargetsInvalid = false;
                }

                var seq = Controls.InDisplayOrder(FrameIndex);

                var activeModal = ActiveModal;
                var maxFadeLevel = 0f;
                int fadeBackgroundAtIndex = -1;
                for (int i = 0; i < ModalStack.Count; i++) {
                    var modal = ModalStack[i];
                    if (modal.BackgroundFadeLevel > 0f) {
                        maxFadeLevel = Math.Max(maxFadeLevel, modal.BackgroundFadeLevel);
                        if (!WasBackgroundFaded) {
                            BackgroundFadeTween = Tween.StartNow(
                                BackgroundFadeTween.Get(NowL), 1f,
                                seconds: BackgroundFadeDuration * (Animations?.AnimationDurationMultiplier ?? 1), now: NowL
                            );
                        }

                        fadeBackgroundAtIndex = seq.IndexOf((Control)modal);
                        WasBackgroundFaded = true;
                    }
                }

                if (fadeBackgroundAtIndex < 0 && WasBackgroundFaded) {
                    BackgroundFadeTween = new Tween<float>(0f);
                    WasBackgroundFaded = false;
                }

                var topLevelHovering = FindTopLevelAncestor(Hovering);

                OverlayQueue.Clear();

                context.Prepass = prepassContainer;
                var renderer = new ImperativeRenderer(container, Materials) {
                    BlendState = BlendState.AlphaBlend,
                    RasterizerState = RenderStates.ScissorOnly,
                };
                renderer.Clear(color: clearColor, stencil: 0, z: 0f, layer: -999);
                // FIXME: Modals that don't have background fade will be overlapped by these overlays
                // The ideal is for those to instead float over the current overlay plane on a new one
                renderer.MakeSubgroup(out OverlayRenderer, layer: 999);

                var topLevelFocusIndex = seq.IndexOf(TopLevelFocused);
                for (int i = 0; i < seq.Count; i++) {
                    var control = seq[i];
                    if (i == fadeBackgroundAtIndex) {
                        var opacity = BackgroundFadeTween.Get(NowL) * BackgroundFadeOpacity * maxFadeLevel;
                        // HACK: Push the post-fade controls and their its overlay plane above the previous one
                        renderer.Layer = 1000;
                        renderer.FillRectangle(
                            Game.Bounds.FromPositionAndSize(Vector2.One * -9999, Vector2.One * 99999), 
                            new Color(opacity, opacity, opacity, 0), blendState: RenderStates.SubtractiveBlend
                        );
                        renderer.Layer += 1;
                        renderer.MakeSubgroup(out OverlayRenderer, layer: 9999);
                    }

                    var m = control as IModal;
                    if ((m != null) && ModalStack.Contains(m))
                        FlushOverlayQueue(ref renderer);

                    // When the accelerator overlay is visible, fade out any top-level controls
                    //  that cover the currently focused top-level control so that the user can see
                    //  any controls that might be active
                    var fadeForKeyboardFocusVisibility = AcceleratorOverlayVisible ||
                    // HACK: Also do this if gamepad input is active so that it's easier to tell what's going on
                    //  when the dpad is used to move focus around
                        ((InputSources[0] is GamepadVirtualKeyboardAndCursor) && (KeyboardSelection != null));

                    var opacityModifier = (fadeForKeyboardFocusVisibility && (topLevelFocusIndex >= 0))
                        ? (
                            (i == topLevelFocusIndex) || (i < topLevelFocusIndex)
                                ? 1.0f
                                // Mousing over an inactive control that's being faded will make it more opaque
                                //  so that you can see what it is
                                : (
                                    (topLevelHovering == control)
                                        // FIXME: oh my god
                                        // HACK: When the accelerator overlay is visible we want to make any top-level control
                                        //  that the mouse is currently over more opaque, so you can see what you're about to
                                        //  focus by clicking on it
                                        // If it's not visible and we're using a virtual cursor, we want to make top-level controls
                                        //  that are currently covering the keyboard selection *less visible* since the user is
                                        //  currently interacting with something underneath it
                                        // he;lp
                                        ? (AcceleratorOverlayVisible ? 0.9f : 0.33f)
                                        : (AcceleratorOverlayVisible ? 0.65f : 0.95f)
                                )
                        )
                        : 1.0f;
                    // HACK: Each top-level control is its own group of passes. This ensures that they cleanly
                    //  overlap each other, at the cost of more draw calls.
                    var passSet = new RasterizePassSet(ref renderer, control, 0, OverlayQueue);
                    passSet.Below.DepthStencilState =
                        passSet.Content.DepthStencilState =
                        passSet.Above.DepthStencilState = null;
                    control.Rasterize(ref context, ref passSet, opacityModifier);
                }

                FlushOverlayQueue(ref renderer);

                LastPassCount = prepassContainer.Count + 1;

                if (AcceleratorOverlayVisible) {
                    renderer.Layer += 1;
                    RasterizeAcceleratorOverlay(ref context, ref renderer);
                }

                {
                    renderer.MakeSubgroup(out var subRenderer);
                    subRenderer.BlendState = BlendState.NonPremultiplied;
                    // HACK
                    context.Pass = RasterizePasses.Below;
                    foreach (var isrc in InputSources) {
                        isrc.SetContext(this);
                        isrc.Rasterize(ref context, ref subRenderer);
                    }
                    subRenderer.Layer += 1;
                    context.Pass = RasterizePasses.Content;
                    foreach (var isrc in InputSources)
                        isrc.Rasterize(ref context, ref subRenderer);
                    subRenderer.Layer += 1;
                    context.Pass = RasterizePasses.Above;
                    foreach (var isrc in InputSources)
                        isrc.Rasterize(ref context, ref subRenderer);
                }

                // Now that we have a dependency graph for the scratch targets, use it to
                //  reorder their batches so that the dependencies come first
                {
                    TopoSortTable.Clear();

                    foreach (var srt in ScratchRenderTargets) {
                        if (srt.UsedRectangles.Count > 0)
                            ScratchRenderTargetsUsedThisFrame++;
                        PushRecursive(srt);
                    }

                    int i = -9999;
                    foreach (var item in TopoSortTable) {
                        ((Batch)item.Renderer.Container).Layer = i++;
                    }

                    var retainCount = Math.Max(1, ScratchRenderTargetsUsedThisFrame);
                    for (int j = ScratchRenderTargets.Count - 1; j >= retainCount; j--) {
                        var srt = ScratchRenderTargets.DangerousGetItem(j);
                        container.Coordinator.DisposeResource(srt);
                        ScratchRenderTargets.RemoveAtOrdered(j);
                    }
                }
            } finally {
                context.Shared.InUse = false;
                OverlayRenderer = default;
            }
        }

        public void RasterizeLayoutTree (
            Frame frame, AutoRenderTarget renderTarget, int layer, 
            Render.Text.IGlyphSource font = null, Layout.ControlKey? focusedKey = null
        ) {
            if (Engine == null)
                return;

            var focusChain = focusedKey.HasValue
                ? new HashSet<Layout.ControlKey>()
                : null;
            if (focusedKey.HasValue) {
                var id = focusedKey.Value;
                while (!id.IsInvalid) {
                    focusChain.Add(id);
                    id = Engine[id].Parent;
                }
            }
            using (var outerGroup = BatchGroup.New(frame, layer, name: "Rasterize UI"))
            using (var rtBatch = BatchGroup.ForRenderTarget(outerGroup, 1, renderTarget, name: "Final Pass")) {
                var renderer = new ImperativeRenderer(rtBatch, Materials) {
                    BlendState = BlendState.AlphaBlend,
                };
                renderer.Clear(color: Color.Black);
                renderer.Layer += 1;
                RasterizeLayoutTree(ref renderer, font, ref Engine.Root(), focusChain);
            }
        }

        public void Rasterize (Frame frame, AutoRenderTarget renderTarget, int layer) {
            using (var outerGroup = BatchGroup.New(frame, layer, name: "Rasterize UI"))
            using (var prepassGroup = BatchGroup.New(outerGroup, -999, name: "Prepass"))
            using (var rtBatch = BatchGroup.ForRenderTarget(outerGroup, 1, renderTarget, name: "Final Pass")) {
                Rasterize(rtBatch, 0, prepassGroup, 0, clearColor: Color.Transparent);
            }
        }

        private void PushRecursive (ScratchRenderTarget srt) {
            if (srt.VisitedByTopoSort)
                return;

            srt.VisitedByTopoSort = true;
            foreach (var dep in srt.Dependencies)
                PushRecursive(dep);

            TopoSortTable.Add(srt);
        }

        internal DepthStencilState GetStencilRestore (int targetReferenceStencil) {
            DepthStencilState result;
            if (StencilEraseStates.TryGetValue(targetReferenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Less,
                StencilPass = StencilOperation.Replace,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = targetReferenceStencil,
                DepthBufferEnable = false,
                DepthBufferWriteEnable = true,
                DepthBufferFunction = CompareFunction.Always
            };

            StencilEraseStates[targetReferenceStencil] = result;
            return result;
        }

        internal DepthStencilState GetStencilWrite (int previousReferenceStencil) {
            DepthStencilState result;
            if (StencilWriteStates.TryGetValue(previousReferenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Equal,
                StencilPass = StencilOperation.IncrementSaturation,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = previousReferenceStencil,
                DepthBufferEnable = false,
                DepthBufferWriteEnable = true,
                DepthBufferFunction = CompareFunction.Always
            };

            StencilWriteStates[previousReferenceStencil] = result;
            return result;
        }

        internal DepthStencilState GetStencilTest (int referenceStencil) {
            DepthStencilState result;
            if (StencilTestStates.TryGetValue(referenceStencil, out result))
                return result;

            result = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.LessEqual,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = referenceStencil,
                StencilWriteMask = 0,
                DepthBufferEnable = false,
                DepthBufferWriteEnable = true,
                DepthBufferFunction = CompareFunction.Always
            };

            StencilTestStates[referenceStencil] = result;
            return result;
        }

        // HACK
        internal bool IsSRGB => 
            (ScratchSurfaceFormat == Squared.Render.Evil.TextureUtils.ColorSrgbEXT) &&
                (ScratchSurfaceFormat != SurfaceFormat.Color);

        public Color ConvertColor (Color color) {
            var result = ColorSpace.ConvertColor(color, IsSRGB ? ColorConversionMode.SRGBToLinear : ColorConversionMode.None);
            return result;
        }
    }

    public struct RasterizePassSet {
        public ImperativeRenderer Below, Content, Above;
        public UnorderedList<BitmapDrawCall> OverlayQueue;
        public int StackDepth;

        public RasterizePassSet (ref RasterizePassSet parent, Control control, ViewTransformModifier viewTransformModifier) {
            parent.Below.MakeSubgroup(out Below, name: "Below {userData} (Nested)", userData: control);
            parent.Content.MakeSubgroup(out Content, name: "Content {userData} (Nested)", userData: control);
            parent.Above.MakeSubgroup(out Above, name: "Above {userData} (Nested)", userData: control);
            StackDepth = parent.StackDepth + 1;
            OverlayQueue = parent.OverlayQueue;
            ((BatchGroup)Below.Container).SetViewTransform(viewTransformModifier);
            ((BatchGroup)Content.Container).SetViewTransform(viewTransformModifier);
            ((BatchGroup)Above.Container).SetViewTransform(viewTransformModifier);
        }

        public RasterizePassSet (ref ImperativeRenderer container, Control control, int stackDepth, UnorderedList<BitmapDrawCall> overlayQueue) {
            // FIXME: Order them?
            container.MakeSubgroup(out Below, name: "Below {userData}", userData: control);
            container.MakeSubgroup(out Content, name: "Content {userData}", userData: control);
            container.MakeSubgroup(out Above, name: "Above {userData}", userData: control);
            StackDepth = stackDepth;
            OverlayQueue = overlayQueue;
        }

        public RasterizePassSet (ref ImperativeRenderer container, Control control, int stackDepth, UnorderedList<BitmapDrawCall> overlayQueue, ref int layer) {
            container.MakeSubgroup(out Below, name: "Below {userData}", layer: layer, userData: control);
            container.MakeSubgroup(out Content, name: "Content {userData}", layer: layer + 1, userData: control);
            container.MakeSubgroup(out Above, name: "Above {userData}", layer: layer + 2, userData: control);
            StackDepth = stackDepth;
            OverlayQueue = overlayQueue;
            layer = layer + 3;
        }
    }
}

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
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.PRGUI.Controls {
    // Copied so the callee can pass it by-ref elsewhere
    public delegate void CanvasPaintHandler (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings);

    public class Canvas : Control {
        public event CanvasPaintHandler OnPaint;
        public bool HasPaintHandler => (OnPaint != null);

        private bool _Buffered;
        public bool Buffered {
            get => _Buffered;
            set {
                if (_Buffered == value)
                    return;
                _Buffered = value;
                Invalidate();
            }
        }

        /// <summary>
        /// If buffering is enabled, the size of the internal buffer will be increased by this amount
        /// </summary>
        public float InternalResolution = 1.0f;

        private bool _ContentIsValid;
        private bool _CacheContent;
        public bool CacheContent {
            get => _CacheContent && _Buffered;
            set => _CacheContent = value;
        }

        new public bool AcceptsFocus {
            get => base.AcceptsFocus;
            set => base.AcceptsFocus = value;
        }

        public bool DisabledDueToException { get; private set; }

        private bool _ShouldDisposeBuffer;
        public bool MipMap = false;
        public AutoRenderTarget Buffer { get; private set; }

        public BlendState CompositingBlendState = BlendState.Opaque;

        private SurfaceFormat _SurfaceFormat = SurfaceFormat.Color;
        public SurfaceFormat SurfaceFormat {
            get => _SurfaceFormat;
            set {
                if (_SurfaceFormat == value)
                    return;
                _SurfaceFormat = value;
                _ShouldDisposeBuffer = true;
            }
        }

        private DepthFormat _DepthFormat = DepthFormat.None;
        public DepthFormat DepthFormat {
            get => _DepthFormat;
            set {
                if (_DepthFormat == value)
                    return;
                _DepthFormat = value;
                _ShouldDisposeBuffer = true;
            }
        }

        protected override bool ShouldClipContent => !_Buffered;
        protected override bool HasPreRasterizeHandler => (_Buffered && (!_CacheContent || !_ContentIsValid || (Buffer == null))) || base.HasPreRasterizeHandler;

        public Canvas () 
            : base () {
            // HACK: Set Intangible if you don't want this
            AcceptsFocus = true;
            AcceptsMouseInput = true;
        }

        public void Invalidate () {
            _ContentIsValid = false;
        }

        protected override void OnIntangibleChange (bool newValue) {
            AcceptsFocus = !newValue;
            if (newValue)
                Context?.NotifyControlBecomingInvalidFocusTarget(this, false);
        }

        public override void InvalidateLayout () {
            base.InvalidateLayout();
            _ContentIsValid = false;
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider.Canvas ?? base.GetDefaultDecorator(provider) ?? provider.None;
        }

        protected virtual void Paint (ref UIOperationContext context, ref ImperativeRenderer renderer, in DecorationSettings settings) {
            if (DisabledDueToException)
                return;

            try {
                if (OnPaint != null)
                    OnPaint(ref context, ref renderer, settings);
            } catch (Exception exc) {
                Context.Log($"Unhandled exception in canvas {this}: {exc}");
                DisabledDueToException = true;
            }
        }

        private void AutoDisposeBuffer (RenderCoordinator coordinator) {
            _ShouldDisposeBuffer = false;

            if (Buffer == null)
                return;

            if (!_Buffered || _ShouldDisposeBuffer) {
                coordinator.DisposeResource(Buffer);
                Buffer = null;
            }
        }

        protected bool EnsureValidBuffer (RenderCoordinator coordinator, ref RectF contentBox) {
            AutoDisposeBuffer(coordinator);
            var bufferSize = (contentBox.Size * InternalResolution).Ceiling();
            int w = (int)bufferSize.X, h = (int)bufferSize.Y;
            if ((w <= 0) || (h <= 0)) {
                _ContentIsValid = false;
                return false;
            }

            if (Buffer == null) {
                _ContentIsValid = false;
                Buffer = new AutoRenderTarget(coordinator, w, h, MipMap, SurfaceFormat, DepthFormat, name: "Canvas.Buffer");
            } else {
                _ContentIsValid = !Buffer.Resize(w, h);
            }

            return true;
        }

        protected override void OnPreRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings _settings, IDecorator decorations) {
            var settings = _settings;
            base.OnPreRasterize(ref context, ref renderer, ref settings, decorations);
            if (!EnsureValidBuffer(context.RenderCoordinator, ref _settings.ContentBox))
                return;
            settings.ContentBox.Position = settings.Box.Position = Vector2.Zero;
            settings.IsCompositing = true;
            // FIXME
            var layer = 0;
            using (var container = BatchGroup.ForRenderTarget(
                context.Prepass, layer, Buffer, materialSet: context.Materials,
                viewTransform: ViewTransform.CreateOrthographic(
                    // Maintain a fixed width/height even if internal resolution is not 1.0
                    (int)Math.Ceiling(settings.ContentBox.Width), 
                    (int)Math.Ceiling(settings.ContentBox.Height)
                )
            )) {
                var contentRenderer = new ImperativeRenderer(container, context.Materials);
                Paint(ref context, ref contentRenderer, in settings);
            }
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            ImperativeRenderer contentRenderer;

            settings.ContentBox.SnapAndInset(out Vector2 a, out Vector2 b);

            if (!_Buffered) {
                AutoDisposeBuffer(renderer.Container.Coordinator);
                renderer.MakeSubgroup(out contentRenderer);
                Paint(ref context, ref contentRenderer, in settings);
            } else {
                var buffer = Buffer.Get();
                if (buffer == null)
                    return;
                renderer.Draw(
                    Buffer.Get(), (int)a.X, (int)a.Y, blendState: CompositingBlendState, 
                    scaleX: 1.0f / InternalResolution, scaleY: 1.0f / InternalResolution
                );
            }
        }
    }
}
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
    public delegate void CanvasPaintHandler (ref ImperativeRenderer renderer, ref RectF contentRect);

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

        private bool _ContentIsValid;
        private bool _CacheContent;
        public bool CacheContent {
            get => _CacheContent && _Buffered;
            set => _CacheContent = value;
        }

        private bool _ShouldDisposeBuffer;
        public AutoRenderTarget Buffer { get; private set; }

        public BlendState BlendState = BlendState.NonPremultiplied;

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
        protected override bool HasPreRasterizeHandler => _Buffered && (!_CacheContent || !_ContentIsValid || (Buffer == null));

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

        protected virtual void Paint (ref ImperativeRenderer renderer, ref RectF contentRect) {
            if (OnPaint != null)
                OnPaint(ref renderer, ref contentRect);
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

        protected override void OnPreRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnPreRasterize(context, ref renderer, settings, decorations);
            AutoDisposeBuffer(renderer.Container.Coordinator);
            int w = (int)Math.Ceiling(settings.ContentBox.Width),
                h = (int)Math.Ceiling(settings.ContentBox.Height);
            var box = settings.ContentBox;
            box.Position = Vector2.Zero;
            if (Buffer == null) {
                _ContentIsValid = false;
                Buffer = new AutoRenderTarget(renderer.Container.Coordinator, w, h, false, SurfaceFormat, DepthFormat);
            } else {
                _ContentIsValid = !Buffer.Resize(w, h);
            }

            var contentRenderer = renderer.ForRenderTarget(Buffer, viewTransform: ViewTransform.CreateOrthographic(w, h));
            contentRenderer.BlendState = BlendState.NonPremultiplied;
            Paint(ref contentRenderer, ref box);
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            if (context.Pass != RasterizePasses.Content)
                return;

            ImperativeRenderer contentRenderer;

            settings.ContentBox.SnapAndInset(out Vector2 a, out Vector2 b);

            if (!_Buffered) {
                AutoDisposeBuffer(renderer.Container.Coordinator);
                contentRenderer = renderer.MakeSubgroup();
                contentRenderer.BlendState = BlendState;
                Paint(ref contentRenderer, ref settings.ContentBox);
            } else {
                renderer.Draw(
                    Buffer.Get(),
                    new Rectangle((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y)),
                    blendState: BlendState
                );
            }
        }
    }
}
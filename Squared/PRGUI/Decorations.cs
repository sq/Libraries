using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI.Decorations {
    public class BackgroundImageSettings {
        public AbstractTextureReference Texture;
        public Bounds TextureBounds;
        public RasterTextureSettings Settings;

        public BackgroundImageSettings (AbstractTextureReference texture = default(AbstractTextureReference)) {
            Texture = texture;
            Settings = new RasterTextureSettings {
                SamplerState = SamplerState.LinearClamp,
                Mode = RasterTextureCompositeMode.Over,
                Scale = Vector2.One,
                PreserveAspectRatio = true,
                Origin = Vector2.One * 0.5f,
                Position = Vector2.One * 0.5f
            };
            TextureBounds = Bounds.Unit;
        }

        public static implicit operator BackgroundImageSettings (Texture2D texture) {
            return new BackgroundImageSettings(texture);
        }
    }

    public struct DecorationSettings {
        public RectF Box, ContentBox;
        public ControlStates State;
        public DenseList<string> Traits;
        public pSRGBColor? BackgroundColor, TextColor;
        public BackgroundImageSettings BackgroundImage;
        public bool IsCompositing;

        public bool HasTrait (string trait) {
            for (int i = 0, c = Traits.Count; i < c; i++)
                if (Traits[i].Equals(trait, StringComparison.Ordinal))
                    return true;

            return false;
        }

        public Texture2D GetTexture () {
            return BackgroundImage?.Texture.Instance;
        }

        public Bounds GetTextureRegion () {
            return BackgroundImage?.TextureBounds ?? Bounds.Unit;
        }

        public RasterTextureSettings GetTextureSettings () {
            return BackgroundImage?.Settings ?? default(RasterTextureSettings);
        }
    }

    public interface IControlAnimation {
        float DefaultDuration { get; }
        void Start (Control control, long now, float duration);
        void End (Control control, bool cancelled);
    }

    public interface IMetricsProvider {
        Margins Margins { get; }
        Margins Padding { get; }
        IGlyphSource GlyphSource { get; }
        void GetContentAdjustment (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale);
        bool GetTextSettings (UIOperationContext context, ControlStates state, out Material material, ref Color? color);
    }

    public interface IWidgetDecorator<TData> : IMetricsProvider {
        Vector2 MinimumSize { get; }
        bool OnMouseEvent (DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args);
        bool HitTest (DecorationSettings settings, ref TData data, Vector2 position);
        void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, ref TData data);
    }

    public interface IDecorator : IMetricsProvider {
        bool IsPassDisabled (RasterizePasses pass);
        void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings);
    }

    public interface IAnimationProvider {
        float AnimationDurationMultiplier { get; }

        IControlAnimation ShowModalDialog { get; }
        IControlAnimation HideModalDialog { get; }
        IControlAnimation ShowMenu { get; }
        IControlAnimation HideMenu { get; }
    }

    public interface IDecorationProvider {
        Vector2 SizeScaleRatio { get; }
        Vector2 SpacingScaleRatio { get; }
        Vector2 PaddingScaleRatio { get; }
        Vector2 MarginScaleRatio { get; }
        float OutlineScaleRatio { get; }

        IDecorator None { get; }
        IDecorator Container { get; }
        IDecorator TitledContainer { get; }
        IDecorator ContainerTitle { get; }
        IDecorator FloatingContainer { get; }
        IDecorator Window { get; }
        IDecorator WindowTitle { get; }
        IDecorator StaticText { get; }
        IDecorator StaticImage { get; }
        IDecorator EditableText { get; }
        IDecorator Selection { get; }
        IDecorator Button { get; }
        IDecorator Tooltip { get; }
        IDecorator Menu { get; }
        IDecorator MenuSelection { get; }
        IDecorator ListBox { get; }
        IDecorator ListSelection { get; }
        IDecorator CompositionPreview { get; }
        IDecorator Checkbox { get; }
        IDecorator RadioButton { get; }
        IMetricsProvider Description { get; }
        IDecorator Slider { get; }
        IDecorator SliderThumb { get; }
        IDecorator Dropdown { get; }
        IDecorator DropdownArrow { get; }
        IDecorator AcceleratorTarget { get; }
        IDecorator AcceleratorLabel { get; }
        IDecorator ParameterGauge { get; }
        IDecorator Gauge { get; }
        IDecorator VirtualCursor { get; }
        IDecorator VirtualCursorAnchor { get; }
        IDecorator Tab { get; }
        IDecorator TabPage { get; }
        IDecorator Canvas { get; }
        IWidgetDecorator<ScrollbarState> Scrollbar { get; }
    }

    public delegate bool TextSettingsGetter (UIOperationContext context, ControlStates state, out Material material, ref Color? color);
    public delegate void DecoratorDelegate (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings);
    public delegate void ContentAdjustmentGetter (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale);

    public abstract class DelegateBaseDecorator : IMetricsProvider {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }

        public IGlyphSource Font;
        public Func<IGlyphSource> GetFont;
        public TextSettingsGetter GetTextSettings;
        public ContentAdjustmentGetter GetContentAdjustment;

        IGlyphSource IMetricsProvider.GlyphSource => 
            (GetFont != null) ? GetFont() : Font;

        bool IMetricsProvider.GetTextSettings (UIOperationContext context, ControlStates state, out Material material, ref Color? color) {
            if (GetTextSettings != null) {
                return GetTextSettings(context, state, out material, ref color);
            } else {
                material = default(Material);
                return false;
            }
        }

        void IMetricsProvider.GetContentAdjustment (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale) {
            if (GetContentAdjustment != null)
                GetContentAdjustment(context, state, out offset, out scale);
            else {
                offset = Vector2.Zero;
                scale = Vector2.One;
            }
        }
    }

    public sealed class DelegateDecorator : DelegateBaseDecorator, IDecorator {
        public DecoratorDelegate Below, Content, Above, ContentClip;

        public DelegateDecorator Clone () {
            return new DelegateDecorator {
                Below = Below,
                Content = Content,
                Above = Above,
                ContentClip = ContentClip,
                Margins = Margins,
                Padding = Padding,
                GetTextSettings = GetTextSettings,
                GetContentAdjustment = GetContentAdjustment
            };
        }

        bool IDecorator.IsPassDisabled (RasterizePasses pass) {
            switch (pass) {
                case RasterizePasses.Below:
                    return (Below == null);
                case RasterizePasses.Content:
                    return (Content == null);
                case RasterizePasses.Above:
                    return (Above == null);
                case RasterizePasses.ContentClip:
                    return (ContentClip == null);
                default:
                    return true;
            }
        }

        void IDecorator.Rasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, ref renderer, settings);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, ref renderer, settings);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, ref renderer, settings);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(context, ref renderer, settings);
                    return;
            }
        }
    }

    public delegate void WidgetDecoratorRasterizer<TData> (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, ref TData data);
    public delegate bool WidgetDecoratorMouseEventHandler<TData> (DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args);
    public delegate bool WidgetDecoratorHitTestHandler<TData> (DecorationSettings settings, ref TData data, Vector2 position);

    public sealed class DelegateWidgetDecorator<TData> : DelegateBaseDecorator, IWidgetDecorator<TData> {
        public Vector2 MinimumSize { get; set; }

        public WidgetDecoratorRasterizer<TData> Below, Content, Above, ContentClip;
        public WidgetDecoratorHitTestHandler<TData> OnHitTest;
        public WidgetDecoratorMouseEventHandler<TData> OnMouseEvent;

        public void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, ref TData data) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(context, ref renderer, settings, ref data);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(context, ref renderer, settings, ref data);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(context, ref renderer, settings, ref data);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(context, ref renderer, settings, ref data);
                    return;
            }
        }

        bool IWidgetDecorator<TData>.OnMouseEvent (DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args) {
            if (OnMouseEvent != null)
                return OnMouseEvent(settings, ref data, eventName, args);
            else
                return false;
        }

        public bool HitTest (DecorationSettings settings, ref TData data, Vector2 position) {
            if (OnHitTest != null)
                return OnHitTest(settings, ref data, position);
            else
                return false;
        }
    }

    public struct ScrollbarState {
        public float Position;
        public float ContentSize, ViewportSize;
        public Vector2? DragInitialMousePosition;
        public bool HasCounterpart, Horizontal;
        internal float DragSizePx, DragInitialPosition;
    }
}

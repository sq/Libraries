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
        public pSRGBColor? BackgroundColor;
        public BackgroundImageSettings BackgroundImage;

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

    public interface IBaseDecorator {
        Margins Margins { get; }
        Margins Padding { get; }
        void GetContentAdjustment (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale);
        bool GetTextSettings (UIOperationContext context, ControlStates state, out Material material, out IGlyphSource font, ref Color? color);
    }

    public interface IWidgetDecorator<TData> : IBaseDecorator {
        Vector2 MinimumSize { get; }
        bool OnMouseEvent (DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args);
        void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, ref TData data);
    }

    public interface IDecorator : IBaseDecorator {
        void Rasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings);
    }

    public interface IDecorationProvider {
        Vector2 SizeScaleRatio { get; }
        Vector2 SpacingScaleRatio { get; }

        IDecorator None { get; }
        IDecorator Container { get; }
        IDecorator TitledContainer { get; }
        IDecorator ContainerTitle { get; }
        IDecorator FloatingContainer { get; }
        IDecorator Window { get; }
        IDecorator WindowTitle { get; }
        IDecorator StaticText { get; }
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
        IDecorator Description { get; }
        IDecorator Slider { get; }
        IDecorator SliderThumb { get; }
        IDecorator Dropdown { get; }
        IDecorator AcceleratorTarget { get; }
        IDecorator AcceleratorLabel { get; }
        IDecorator ParameterGauge { get; }
        IDecorator Gauge { get; }
        IWidgetDecorator<ScrollbarState> Scrollbar { get; }
    }

    public delegate bool TextSettingsGetter (UIOperationContext context, ControlStates state, out Material material, out IGlyphSource font, ref Color? color);
    public delegate void DecoratorDelegate (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings);
    public delegate void ContentAdjustmentGetter (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale);

    public abstract class DelegateBaseDecorator : IBaseDecorator {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }

        public IGlyphSource Font;
        public TextSettingsGetter GetTextSettings;
        public ContentAdjustmentGetter GetContentAdjustment;

        bool IBaseDecorator.GetTextSettings (UIOperationContext context, ControlStates state, out Material material, out IGlyphSource font, ref Color? color) {
            if (GetTextSettings != null)
                return GetTextSettings(context, state, out material, out font, ref color);
            else {
                material = default(Material);
                font = Font;
                return false;
            }
        }

        void IBaseDecorator.GetContentAdjustment (UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale) {
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

    public sealed class DelegateWidgetDecorator<TData> : DelegateBaseDecorator, IWidgetDecorator<TData> {
        public Vector2 MinimumSize { get; set; }
        public WidgetDecoratorRasterizer<TData> Below, Content, Above, ContentClip;
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
    }

    public struct ScrollbarState {
        public float Position;
        public float ContentSize, ViewportSize;
        public Vector2? DragInitialMousePosition;
        public bool HasCounterpart, Horizontal;
        internal float DragSizePx, DragInitialPosition;
    }
}

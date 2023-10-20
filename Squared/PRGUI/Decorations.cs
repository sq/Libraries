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
    public sealed class BackgroundImageSettings {
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

    public enum TextStyle {
        Normal,
        Selected,
        Shaded
    }

    public struct DecorationSettings {
        public RectF Box, ContentBox;
        public ControlStates State;
        public DenseList<string> Traits;
        public pSRGBColor? BackgroundColor, TextColor;
        public BackgroundImageSettings BackgroundImage;
        public Vector4 UserData;
        public int UniqueId;
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

        public bool HasStateFlag (ControlStates flag) => State.IsFlagged(flag);
    }

    public interface IControlAnimation {
        float DefaultDuration { get; }
        void Start (Control control, long now, float duration);
        void End (Control control, bool cancelled);
    }

    public interface IMetricsProvider {
        Margins Margins { get; }
        Margins Padding { get; }
        Margins UnscaledPadding { get; }
        IGlyphSource GetGlyphSource (ref DecorationSettings settings);
        void GetContentAdjustment (ref UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale);
        bool GetTextSettings (ref UIOperationContext context, ControlStates state, pSRGBColor backgroundColor, out Material material, ref Color? color, out Vector4 userData);
    }

    public interface IWidgetDecorator<TData> : IMetricsProvider {
        Vector2 MinimumSize { get; }
        bool OnMouseEvent (ref DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args);
        bool HitTest (ref DecorationSettings settings, ref TData data, Vector2 position);
        void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings, ref TData data);
    }

    public interface IDecorator : IMetricsProvider {
        bool IsPassDisabled (RasterizePasses pass);
        void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings);
        T Clone<T> () where T : class, IDecorator;
    }

    public interface IAnimationProvider {
        float AnimationDurationMultiplier { get; }

        IControlAnimation ShowModalDialog { get; }
        IControlAnimation HideModalDialog { get; }
        IControlAnimation ShowMenu { get; }
        IControlAnimation HideMenu { get; }
    }

    public interface IDecorationProvider {
        /// <summary>
        /// Scale factor for the overall size of controls (minimum/fixed/maximum)
        /// </summary>
        Vector2 SizeScaleRatio { get; }
        /// <summary>
        /// Scale factor for decoration outlines
        /// </summary>
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
        IDecorator HyperTextHotspot { get; }
        IDecorator LoadingSpinner { get; }
        IWidgetDecorator<ScrollbarState> Scrollbar { get; }

        /// <summary>
        /// Allows reacting to control events to play feedback animations or sound effects
        /// </summary>
        void OnEvent<T> (Control control, string name, T args);

        /// <summary>
        /// Computes the effective margin and padding scale ratios
        /// </summary>
        void ComputeScaleRatios (out Vector2 margins, out Vector2 padding);
    }

    public delegate bool TextSettingsGetter (ref UIOperationContext context, ControlStates state, ref pSRGBColor backgroundColor, out Material material, ref Color? color, out Vector4 userData);
    public delegate void DecoratorDelegate (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings);
    public delegate void ContentAdjustmentGetter (ref UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale);
    public delegate IGlyphSource DecorationFontGetter (ref DecorationSettings settings);

    public abstract class DelegateBaseDecorator : IMetricsProvider {
        public Margins Margins { get; set; }
        public Margins Padding { get; set; }
        public Margins UnscaledPadding { get; set; }

        private object _Font;

        public IGlyphSource Font {
            get => _Font as IGlyphSource;
            set => _Font = value;
        }
        public Func<IGlyphSource> GetFont {
            get => _Font as Func<IGlyphSource>;
            set => _Font = value;
        }
        public DecorationFontGetter GetFont2 {
            get => _Font as DecorationFontGetter;
            set => _Font = value;
        }
        public Material TextMaterial;
        public TextSettingsGetter GetTextSettings;
        public ContentAdjustmentGetter GetContentAdjustment;

        public IGlyphSource GetGlyphSource (ref DecorationSettings settings) {
            if (_Font is DecorationFontGetter dfg)
                return dfg(ref settings);
            else if (_Font is Func<IGlyphSource> fg)
                return fg();
            else
                return _Font as IGlyphSource;
        }

        bool IMetricsProvider.GetTextSettings (ref UIOperationContext context, ControlStates state, pSRGBColor backgroundColor, out Material material, ref Color? color, out Vector4 userData) {
            var ok = false;
            if (GetTextSettings != null) {
                ok = GetTextSettings(ref context, state, ref backgroundColor, out material, ref color, out userData);
                material = TextMaterial ?? material;
            } else {
                material = TextMaterial;
                userData = default;
            }
            return ok;
        }

        void IMetricsProvider.GetContentAdjustment (ref UIOperationContext context, ControlStates state, out Vector2 offset, out Vector2 scale) {
            if (GetContentAdjustment != null)
                GetContentAdjustment(ref context, state, out offset, out scale);
            else {
                offset = Vector2.Zero;
                scale = Vector2.One;
            }
        }
    }

    public sealed class DelegateDecorator : DelegateBaseDecorator, IDecorator {
        public DecoratorDelegate Below, Content, Above, ContentClip;

        T IDecorator.Clone<T> () => Clone() as T;

        public DelegateDecorator Clone () {
            return new DelegateDecorator {
                Below = Below,
                Content = Content,
                Above = Above,
                ContentClip = ContentClip,
                Margins = Margins,
                Padding = Padding,
                UnscaledPadding = UnscaledPadding,
                Font = Font,
                GetFont = GetFont,
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

        void IDecorator.Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(ref context, ref renderer, ref settings);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(ref context, ref renderer, ref settings);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(ref context, ref renderer, ref settings);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(ref context, ref renderer, ref settings);
                    return;
            }
        }
    }

    public delegate void WidgetDecoratorRasterizer<TData> (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings, ref TData data);
    public delegate bool WidgetDecoratorMouseEventHandler<TData> (ref DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args);
    public delegate bool WidgetDecoratorHitTestHandler<TData> (ref DecorationSettings settings, ref TData data, Vector2 position);

    public sealed class DelegateWidgetDecorator<TData> : DelegateBaseDecorator, IWidgetDecorator<TData> {
        public Vector2 MinimumSize { get; set; }

        public WidgetDecoratorRasterizer<TData> Below, Content, Above, ContentClip;
        public WidgetDecoratorHitTestHandler<TData> OnHitTest;
        public WidgetDecoratorMouseEventHandler<TData> OnMouseEvent;

        public void Rasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, ref DecorationSettings settings, ref TData data) {
            switch (context.Pass) {
                case RasterizePasses.Below:
                    if (Below != null)
                        Below(ref context, ref renderer, ref settings, ref data);
                    return;
                case RasterizePasses.Content:
                    if (Content != null)
                        Content(ref context, ref renderer, ref settings, ref data);
                    return;
                case RasterizePasses.Above:
                    if (Above != null)
                        Above(ref context, ref renderer, ref settings, ref data);
                    return;
                case RasterizePasses.ContentClip:
                    if (ContentClip != null)
                        ContentClip(ref context, ref renderer, ref settings, ref data);
                    return;
            }
        }

        bool IWidgetDecorator<TData>.OnMouseEvent (ref DecorationSettings settings, ref TData data, string eventName, MouseEventArgs args) {
            if (OnMouseEvent != null)
                return OnMouseEvent(ref settings, ref data, eventName, args);
            else
                return false;
        }

        public bool HitTest (ref DecorationSettings settings, ref TData data, Vector2 position) {
            if (OnHitTest != null)
                return OnHitTest(ref settings, ref data, position);
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Tooltip : StaticTextBase, IPostLayoutListener {
        public static Vector2 DefaultAnchorPoint = new Vector2(0.5f, 1f),
            DefaultControlAlignmentPoint = new Vector2(0.5f, 0f);

        public readonly ControlAlignmentHelper<Tooltip> Aligner;
        protected int FramesWaitingForAlignmentUpdate = 0;

        public Tooltip ()
            : base() {
            // FIXME: Centered?
            Aligner = new ControlAlignmentHelper<Tooltip>(this) {
                AllowOverlap = false,
                AnchorPoint = DefaultAnchorPoint,
                ControlAlignmentPoint = DefaultControlAlignmentPoint,
                ConstrainToParentInsteadOfScreen = true,
                HideIfNotInsideParent = true,
                UseTransformedAnchor = true
            };
            ConfigureDefaultLayout(Content);
            AcceptsMouseInput = false;
            AcceptsFocus = false;
            AutoSize = true;
            Appearance.AutoScaleMetrics = false;
            Intangible = true;
            LayoutFlags = ControlFlags.Layout_Floating;
            Wrap = true;
            Multiline = true;
            // FIXME: This can cause a weird corner case where all tooltips are tiny
            // ScaleToFitY = true;
        }

        new public void Invalidate () {
            base.Invalidate();
            Aligner.AlignmentPending = true;
            // FIXME: Do something else here? Invalidate the alignment?
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var decorator = GetDefaultDecorator(context.DecorationProvider);
            Aligner.ExtraMargins = decorator.Margins;
            var result = base.OnGenerateLayoutTree(ref context, parent, existingKey);
            Record(ref context).Tag = LayoutTags.Tooltip;
            return result;
        }

        RichTextConfiguration _RichTextConfiguration;

        protected override RichTextConfiguration GetRichTextConfiguration() =>
            _RichTextConfiguration ?? base.GetRichTextConfiguration();

        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }

        /// <param name="anchor">If set, alignment will be relative to this control. Otherwise, the screen will be used.</param>
        /// <param name="anchorPoint">Configures what point on the anchor [0 - 1] is used as the center for alignment</param>
        /// <param name="controlAlignmentPoint">Configures what point on the control [0 - 1] is aligned onto the anchor point</param>
        public void Move (Control anchor, Vector2? anchorPoint, Vector2? controlAlignmentPoint) {
            // HACK: Prevent a single-frame glitch when the tooltip moves to a new control
            if (Aligner.Anchor != anchor)
                FramesWaitingForAlignmentUpdate = 1;

            Aligner.Enabled = true;
            Aligner.Anchor = anchor;
            Aligner.AnchorPoint = anchorPoint ?? DefaultAnchorPoint;
            Aligner.ControlAlignmentPoint = controlAlignmentPoint ?? DefaultControlAlignmentPoint;
            // FIXME
            Aligner.ComputeNewAlignment = true;
            Aligner.AlignmentPending = true;
        }

        protected override bool IsPassDisabled (RasterizePasses pass, IDecorator decorations) {
            // HACK
            if (pass == RasterizePasses.Above)
                return false;
            else
                return base.IsPassDisabled(pass, decorations);
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            // HACK
            if (FramesWaitingForAlignmentUpdate > 0) {
                if (context.Pass == RasterizePasses.Above)
                    FramesWaitingForAlignmentUpdate--;
                return;
            }

            base.OnRasterize(ref context, ref renderer, settings, decorations);
        }

        void IPostLayoutListener.OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            Aligner.EnsureAligned(ref context, ref relayoutRequested);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Tooltip;
        }

        // HACK: If a tooltip's settings messes with ours we need to reset them
        private void ConfigureDefaultLayout (DynamicStringLayout content) {
            _NeedsLayoutReset = false;
            content.Reset();
            content.DisableMarkers = true;
            content.AlignToPixels = StaticTextBase.DefaultGlyphPixelAlignment;
            content.WordWrap = true;
            content.CharacterWrap = false;
            content.Alignment = HorizontalAlignment.Left;
        }

        private bool _NeedsLayoutReset = true;

        public void ApplySettings (TooltipSettings settings) {
            RichText = settings.RichText;
            _RichTextConfiguration = settings.RichTextConfiguration;
            if (settings.ConfigureLayout != null) {
                settings.ConfigureLayout(Content);
                _NeedsLayoutReset = true;
            } else if (_NeedsLayoutReset)
                ConfigureDefaultLayout(Content);
            LayoutFilter = settings.LayoutFilter;
            Appearance.GlyphSourceProvider = settings.DefaultGlyphSource;
            TextAlignment = settings.TextAlignment;
        }
    }
}

namespace Squared.PRGUI {
    public interface ICustomTooltipTarget {
        AbstractTooltipContent GetContent ();
        Control Anchor { get; }
        TooltipTargetSettings TooltipSettings { get; }
    }

    public sealed class TooltipTargetSettings {
        public static TooltipTargetSettings Default = new TooltipTargetSettings();

        /// <summary>
        /// Configures what point on the anchor [0 - 1] is used as the center for alignment
        /// </summary>
        public Vector2? AnchorPoint;
        /// <summary>
        /// Configures what point on the control [0 - 1] is aligned onto the anchor point
        /// </summary>
        public Vector2? ControlAlignmentPoint;
        /// <summary>
        /// The maximum size factor of the tooltip (1.0 is the full size of the canvas)
        /// </summary>
        public Vector2? MaxSize;
        /// <summary>
        /// If set, the tooltip can appear while the mouse is pressed. You may also need to change HideOnMousePress
        /// </summary>
        public bool ShowWhileMouseIsHeld = false;
        /// <summary>
        /// If set, the tooltip can appear while the mouse is not pressed
        /// </summary>
        public bool ShowWhileMouseIsNotHeld = true;
        /// <summary>
        /// If set, the tooltip will always be visible while this control has focus
        /// </summary>
        public bool ShowWhileFocused = false;
        /// <summary>
        /// If set, the tooltip will always be visible while this control has keyboard focus
        /// </summary>
        public bool ShowWhileKeyboardFocused = true;
        /// <summary>
        /// If set, the tooltip will automatically be hidden when the mouse is pressed
        /// </summary>
        public bool HideOnMousePress = true;
        /// <summary>
        /// If set, tooltips for child controls will be hosted by this control when possible
        /// </summary>
        public bool HostsChildTooltips = false;
        public float? AppearDelay;
        public float? DisappearDelay;
    }

    public struct TooltipSettings {
        public bool RichText;
        public RichTextConfiguration RichTextConfiguration;
        public Func<IGlyphSource> DefaultGlyphSource;
        /// <summary>
        /// Configures what point on the control [0 - 1] is aligned onto the anchor point
        /// </summary>
        public Vector2? ControlAlignmentPoint;
        /// <summary>
        /// Configures what point on the anchor [0 - 1] is used as the center for alignment
        /// </summary>
        public Vector2? AnchorPoint;
        public Vector2? MaxSize;
        public Action<DynamicStringLayout> ConfigureLayout;
        public StringLayoutFilter LayoutFilter;
        public HorizontalAlignment TextAlignment;

        public TooltipSettings Clone (bool deep) {
            return new TooltipSettings {
                RichText = RichText,
                RichTextConfiguration = deep ? RichTextConfiguration.Clone(true) : RichTextConfiguration,
                DefaultGlyphSource = DefaultGlyphSource,
                AnchorPoint = AnchorPoint,
                ControlAlignmentPoint = ControlAlignmentPoint,
                MaxSize = MaxSize,
                ConfigureLayout = ConfigureLayout,
                LayoutFilter = LayoutFilter,
                TextAlignment = TextAlignment,
            };
        }

        public bool Equals (TooltipSettings rhs) {
            return (RichText == rhs.RichText) &&
                object.Equals(RichTextConfiguration, rhs.RichTextConfiguration) &&
                (MaxSize == rhs.MaxSize) &&
                (AnchorPoint == rhs.AnchorPoint) &&
                (ControlAlignmentPoint == rhs.ControlAlignmentPoint) &&
                (ConfigureLayout == rhs.ConfigureLayout) &&
                (LayoutFilter == rhs.LayoutFilter) &&
                (DefaultGlyphSource == rhs.DefaultGlyphSource) &&
                (TextAlignment == rhs.TextAlignment);
        }

        public override int GetHashCode () {
            return 0;
        }

        public override bool Equals (object obj) {
            if (obj is TooltipSettings tts)
                return Equals(tts);
            else
                return false;
        }

        public static bool operator == (TooltipSettings lhs, TooltipSettings rhs) => lhs.Equals(rhs);
        public static bool operator != (TooltipSettings lhs, TooltipSettings rhs) => !lhs.Equals(rhs);
    }

    public struct AbstractTooltipContent {
        public Func<Control, AbstractString> GetText;
        public AbstractString Text;
        public int Version;
        public TooltipSettings Settings;

        public AbstractTooltipContent (Func<Control, AbstractString> getText, int version = 0, TooltipSettings settings = default(TooltipSettings)) {
            Text = default(AbstractString);
            GetText = getText;
            Version = version;
            Settings = settings;
            /*
            if (settings.DefaultGlyphSource?.IsDisposed == true)
                throw new ObjectDisposedException("settings.DefaultGlyphSource");
            */
        }

        public AbstractTooltipContent (AbstractString text, int version = 0, TooltipSettings settings = default(TooltipSettings)) {
            Text = text;
            GetText = null;
            Version = version;
            Settings = settings;
            /*
            if (settings.DefaultGlyphSource?.IsDisposed == true)
                throw new ObjectDisposedException("settings.DefaultGlyphSource");
            */
        }

        public AbstractString Get (Control target) {
            if (GetText != null)
                return GetText(target);
            else
                return Text;
        }

        public AbstractString GetPlainText (Control target) {
            var result = Get(target);
            if (Settings.RichText)
                result = Squared.Render.Text.RichText.ToPlainText(result.ToString());
            return result;
        }

        public bool Equals (AbstractTooltipContent rhs) {
            var result = (GetText == rhs.GetText) && Text.Equals(rhs.Text) && Settings.Equals(rhs.Settings);
            if (result) {
                if ((GetText == null) && Text.Equals(default(AbstractString)))
                    return true;
                else
                    result = (Version == rhs.Version);
            }
            return result;
        }

        public override int GetHashCode () {
            return 0;
        }

        public override bool Equals (object obj) {
            if (obj is AbstractTooltipContent atc)
                return Equals(atc);
            else
                return false;
        }

        public static bool operator == (AbstractTooltipContent lhs, AbstractTooltipContent rhs) => lhs.Equals(rhs);
        public static bool operator != (AbstractTooltipContent lhs, AbstractTooltipContent rhs) => !lhs.Equals(rhs);

        public static implicit operator AbstractTooltipContent (Func<Control, AbstractString> func) {
            return new AbstractTooltipContent { GetText = func };
        }

        public static implicit operator AbstractTooltipContent (AbstractString text) {
            return new AbstractTooltipContent { Text = text };
        }

        public static implicit operator AbstractTooltipContent (string text) {
            return new AbstractTooltipContent { Text = text };
        }

        public static implicit operator bool (AbstractTooltipContent content) {
            return (content.GetText != null) || (content.Text != default(AbstractString));
        }
    }
}
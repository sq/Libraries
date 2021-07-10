using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Text;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Tooltip : StaticTextBase, IPostLayoutListener {
        public static Vector2 DefaultAnchorPoint = new Vector2(0.5f, 1f),
            DefaultControlAlignmentPoint = new Vector2(0.5f, 0f);

        protected ControlAlignmentHelper Aligner;

        public Tooltip ()
            : base() {
            // FIXME: Centered?
            Aligner = new ControlAlignmentHelper(this) {
                AllowOverlap = false,
                AnchorPoint = DefaultAnchorPoint,
                ControlAlignmentPoint = DefaultControlAlignmentPoint,
                ConstrainToParentInsteadOfScreen = true,
                HideIfNotInsideParent = true
            };
            Content.Alignment = HorizontalAlignment.Left;
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
            // FIXME: Do something else here? Invalidate the alignment?
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            var decorator = GetDefaultDecorator(context.DecorationProvider);
            Aligner.ExtraMargins = decorator.Margins;
            return base.OnGenerateLayoutTree(ref context, parent, existingKey);
        }

        RichTextConfiguration _RichTextConfiguration;

        protected override RichTextConfiguration GetRichTextConfiguration() =>
            _RichTextConfiguration ?? base.GetRichTextConfiguration();

        new public AbstractString Text {
            get => base.Text;
            set => base.Text = value;
        }

        public void Move (Control anchor, Vector2? anchorPoint, Vector2? controlAlignmentPoint) {
            Aligner.Enabled = true;
            Aligner.Anchor = anchor;
            Aligner.AnchorPoint = anchorPoint ?? DefaultAnchorPoint;
            Aligner.ControlAlignmentPoint = controlAlignmentPoint ?? DefaultControlAlignmentPoint;
            // FIXME
            Aligner.ComputeNewAlignment = true;
            Aligner.AlignmentPending = true;
        }

        void IPostLayoutListener.OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            Aligner.EnsureAligned(ref context, ref relayoutRequested);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Tooltip;
        }

        public void ApplySettings (TooltipSettings settings) {
            RichText = settings.RichText;
            _RichTextConfiguration = settings.RichTextConfiguration;
            if (settings.ConfigureLayout != null)
                settings.ConfigureLayout(Content);
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

    public class TooltipTargetSettings {
        public static TooltipTargetSettings Default = new TooltipTargetSettings();

        public Vector2? AnchorPoint;
        public Vector2? ControlAlignmentPoint;
        public Vector2? MaxSize;
        public bool ShowWhileMouseIsHeld = false;
        public bool ShowWhileMouseIsNotHeld = true;
        public bool ShowWhileFocused = false;
        public bool ShowWhileKeyboardFocused = true;
        public bool HideOnMousePress = true;
        public float? AppearDelay;
        public float? DisappearDelay;
    }

    public struct TooltipSettings {
        public bool RichText;
        public RichTextConfiguration RichTextConfiguration;
        public Func<IGlyphSource> DefaultGlyphSource;
        public Vector2? AnchorPoint, ControlAlignmentPoint, MaxSize;
        public Action<DynamicStringLayout> ConfigureLayout;
        public StringLayoutFilter LayoutFilter;
        public HorizontalAlignment TextAlignment;

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

        public override bool Equals (object obj) {
            if (obj is TooltipSettings)
                return Equals((TooltipSettings)obj);
            else
                return false;
        }
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

        public override bool Equals (object obj) {
            if (obj is AbstractTooltipContent)
                return Equals((AbstractTooltipContent)obj);
            else
                return false;
        }

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
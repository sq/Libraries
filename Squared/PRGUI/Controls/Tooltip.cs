using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
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
            ScaleToFitY = true;
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

        void IPostLayoutListener.OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            Aligner.EnsureAligned(context, ref relayoutRequested);
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.Tooltip;
        }
    }
}

namespace Squared.PRGUI {
    public interface ICustomTooltipTarget {
        AbstractTooltipContent GetContent ();
        float? TooltipAppearanceDelay { get; }
        float? TooltipDisappearDelay { get; }
        bool ShowTooltipWhileMouseIsHeld { get; }
        bool ShowTooltipWhileMouseIsNotHeld { get; }
        bool ShowTooltipWhileFocus { get; }
        bool ShowTooltipWhileKeyboardFocus { get; }
        bool HideTooltipOnMousePress { get; }
    }

    public struct TooltipSettings {
        public bool RichText;
        public Vector2? AnchorPoint, ControlAlignmentPoint;

        public bool Equals (TooltipSettings rhs) {
            return (RichText == rhs.RichText) &&
                (AnchorPoint == rhs.AnchorPoint) &&
                (ControlAlignmentPoint == rhs.ControlAlignmentPoint);
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
        }

        public AbstractTooltipContent (AbstractString text, int version = 0, TooltipSettings settings = default(TooltipSettings)) {
            Text = text;
            GetText = null;
            Version = version;
            Settings = settings;
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
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
        protected ControlAlignmentHelper Aligner;

        public Tooltip ()
            : base() {
            // FIXME: Centered?
            Aligner = new ControlAlignmentHelper(this) {
                AnchorPoint = new Vector2(0.5f, 1f),
                ControlAlignmentPoint = new Vector2(0.5f, 0f),
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

        public void Move (Control anchor) {
            Aligner.Enabled = true;
            Aligner.Anchor = anchor;
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

    public struct AbstractTooltipContent {
        public Func<Control, AbstractString> GetText;
        public AbstractString Text;
        public int Version;

        public AbstractTooltipContent (Func<Control, AbstractString> getText, int version = 0) {
            Text = default(AbstractString);
            GetText = getText;
            Version = version;
        }

        public AbstractTooltipContent (AbstractString text, int version = 0) {
            Text = text;
            GetText = null;
            Version = version;
        }

        public AbstractString Get (Control target) {
            if (GetText != null)
                return GetText(target);
            else
                return Text;
        }

        public bool Equals (AbstractTooltipContent rhs) {
            var result = (GetText == rhs.GetText) && Text.Equals(rhs.Text);
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
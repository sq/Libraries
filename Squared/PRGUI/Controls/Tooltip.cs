using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render.Text;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class Tooltip : StaticText {
        public Tooltip ()
            : base () {
            Content.Alignment = HorizontalAlignment.Left;
            AcceptsMouseInput = false;
            AcceptsFocus = false;
            AutoSize = true;
            Intangible = true;
            LayoutFlags = ControlFlags.Layout_Floating;
            PaintOrder = 9999;
            Wrap = true;
            Multiline = true;
        }

        protected override IDecorator GetDefaultDecorations (IDecorationProvider provider) {
            return provider?.Tooltip;
        }
    }
}

namespace Squared.PRGUI {
    public interface ICustomTooltipTarget {
        float? TooltipAppearanceDelay { get; }
        float? TooltipDisappearDelay { get; }
        bool ShowTooltipWhileMouseIsHeld { get; }
        bool ShowTooltipWhileMouseIsNotHeld { get; }
        bool ShowTooltipWhileKeyboardFocus { get; }
        bool HideTooltipOnMousePress { get; }
    }

    public struct AbstractTooltipContent {
        public Func<Control, AbstractString> GetText;
        public AbstractString Text;

        public AbstractTooltipContent (Func<Control, AbstractString> getText) {
            Text = default(AbstractString);
            GetText = getText;
        }

        public AbstractTooltipContent (AbstractString text) {
            Text = text;
            GetText = null;
        }

        public AbstractString Get (Control target) {
            if (GetText != null)
                return GetText(target);
            else
                return Text;
        }

        public bool Equals (AbstractTooltipContent rhs) {
            return (GetText == rhs.GetText) && Text.Equals(rhs.Text);
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
    }
}
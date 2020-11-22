using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public struct RichStyle {
        public IGlyphSource GlyphSource;
        public Color? Color;
        public float? Scale;
        // public float? Kerning;
    }

    public struct RichImage {
        public AbstractTextureReference Texture;
        public Bounds Bounds;
    }

    public struct RichTextConfiguration : IEquatable<RichTextConfiguration> {
        public Dictionary<string, RichStyle> Styles;
        public Dictionary<string, RichImage> Images;
        public Dictionary<char, KerningAdjustment> KerningAdjustments;

        public void Append (
            ref StringLayoutEngine layoutEngine, IGlyphSource defaultGlyphSource, AbstractString text
        ) {
            var initialColor = layoutEngine.overrideColor;
            var initialScale = layoutEngine.scale;
            RichStyle style;
            RichImage image;
            var count = text.Length;
            var currentRangeStart = 0;
            IGlyphSource glyphSource = null;
            for (int i = 0; i < count; i++) {
                var ch = text[i];
                var next = (i < count - 2) ? text[i + 1] : '\0';
                if ((ch == '$') && (next == '[')) {
                    AppendRange(ref layoutEngine, glyphSource ?? defaultGlyphSource, text, currentRangeStart, i);
                    var command = ParseCommand(text, ref i, ref currentRangeStart);
                    if (command == null) {
                        continue;
                    } else if (string.IsNullOrWhiteSpace(command)) {
                        glyphSource = null;
                        layoutEngine.overrideColor = initialColor;
                        layoutEngine.scale = initialScale;
                    } else if ((Styles != null) && Styles.TryGetValue(command, out style)) {
                        glyphSource = style.GlyphSource ?? glyphSource;
                        layoutEngine.overrideColor = style.Color ?? initialColor;
                        layoutEngine.scale = style.Scale * initialScale ?? initialScale;
                    } else if ((Images != null) && Images.TryGetValue(command, out image)) {
                        // FIXME
                    } else {
                        ;
                    }
                }
            }
            AppendRange(ref layoutEngine, glyphSource ?? defaultGlyphSource, text, currentRangeStart, count);
        }

        private void AppendRange (
            ref StringLayoutEngine layoutEngine, IGlyphSource glyphSource, AbstractString text,
            int rangeStart, int rangeEnd
        ) {
            if (rangeEnd <= rangeStart)
                return;
            layoutEngine.AppendText(glyphSource, text, KerningAdjustments, start: rangeStart, end: rangeEnd);
        }

        private string ParseCommand (AbstractString text, ref int i, ref int currentRangeStart) {
            var count = text.Length;
            var start = i + 2;
            while (i < count) {
                var ch = text[i];
                if (ch == ']') {
                    i++;
                    currentRangeStart = i;
                    return text.Substring(start, i - start - 1);
                }
                if (ch < ' ')
                    return null;
                i++;
            }
            return null;
        }

        public bool Equals (RichTextConfiguration other) {
            return (Styles == other.Styles) &&
                (Images == other.Images);
        }
    }
}

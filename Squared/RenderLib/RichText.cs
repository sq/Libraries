using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public struct RichStyle {
        public IGlyphSource GlyphSource;
        public Color? Color;
        public float? Scale;
        public float? Spacing;
    }

    public struct RichImage {
        public AbstractTextureReference Texture;
        public Bounds? Bounds;
        public Vector2 Margin;
        private float VerticalAlignmentMinusOne;
        private float ScaleMinusOne;

        public float VerticalAlignment {
            get {
                return VerticalAlignmentMinusOne + 1;
            }
            set {
                VerticalAlignmentMinusOne = value - 1;
            }
        }

        public float Scale {
            get {
                return ScaleMinusOne + 1;
            }
            set {
                ScaleMinusOne = value - 1;
            }
        }

        public static implicit operator RichImage (AbstractTextureReference texture) {
            return new RichImage {
                Texture = texture
            };
        }

        public static implicit operator RichImage (Texture2D texture) {
            return new RichImage {
                Texture = texture
            };
        }
    }

    public struct RichTextConfiguration : IEquatable<RichTextConfiguration> {
        private static readonly Regex RuleRegex = new Regex(@"([\w\-_]+)(?:\s*):(?:\s*)([^;\]]*)(?:;|)", RegexOptions.Compiled);
        private static readonly Dictionary<string, Color?> SystemNamedColorCache = new Dictionary<string, Color?>();

        private int Version;
        public Dictionary<string, Color> NamedColors;
        public Dictionary<string, IGlyphSource> GlyphSources;
        public Dictionary<string, RichStyle> Styles;
        public Dictionary<string, RichImage> Images;
        public Dictionary<char, KerningAdjustment> KerningAdjustments;

        private Color? ParseColor (string text) {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (
                text.StartsWith("#") && 
                uint.TryParse(text.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out uint decoded)
            ) {
                var result = new Color { PackedValue = decoded };
                if (text.Length <= 7)
                    result.A = 255;
                return result;
            } else if ((NamedColors != null) && NamedColors.TryGetValue(text, out Color namedColor)) {
                return namedColor;
            } else if (SystemNamedColorCache.TryGetValue(text, out Color? systemNamedColor)) {
                return systemNamedColor;
            } else {
                var tColor = typeof(Color);
                var prop = tColor.GetProperty(text, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                Color? result = null;
                if (prop != null)
                    result = (Color)prop.GetValue(null);
                SystemNamedColorCache[text] = result;
                return result;
            }
        }

        public void Append (
            ref StringLayoutEngine layoutEngine, IGlyphSource defaultGlyphSource, AbstractString text
        ) {
            var initialColor = layoutEngine.overrideColor;
            var initialScale = layoutEngine.scale;
            var initialSpacing = layoutEngine.spacing;
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
                        // FIXME: Can this cause an infinite loop?
                        continue;
                    } else if (string.IsNullOrWhiteSpace(command)) {
                        glyphSource = null;
                        layoutEngine.overrideColor = initialColor;
                        layoutEngine.scale = initialScale;
                        layoutEngine.spacing = initialSpacing;
                    } else if ((Styles != null) && command.StartsWith(".") && Styles.TryGetValue(command.Substring(1), out style)) {
                        glyphSource = style.GlyphSource ?? glyphSource;
                        layoutEngine.overrideColor = style.Color ?? initialColor;
                        layoutEngine.scale = style.Scale * initialScale ?? initialScale;
                        layoutEngine.spacing = style.Spacing ?? initialSpacing;
                    } else if ((Images != null) && Images.TryGetValue(command, out image)) {
                        AppendImage(ref layoutEngine, image);
                    } else if (command.Contains(":")) {
                        foreach (var _match in RuleRegex.Matches(command)) {
                            var match = (Match)_match;
                            if (!match.Success)
                                continue;

                            var key = match.Groups[1].Value;
                            var value = match.Groups[2].Value;
                            switch (key) {
                                case "color":
                                case "c":
                                    layoutEngine.overrideColor = ParseColor(value) ?? initialColor;
                                    break;
                                case "scale":
                                case "sc":
                                    if (!float.TryParse(value, out float newScale))
                                        layoutEngine.scale = initialScale;
                                    else
                                        layoutEngine.scale = initialScale * newScale;
                                    break;
                                case "spacing":
                                case "sp":
                                    if (!float.TryParse(value, out float newSpacing))
                                        layoutEngine.spacing = initialSpacing;
                                    else
                                        layoutEngine.spacing = initialSpacing * newSpacing;
                                    break;
                                case "font":
                                case "glyph-source":
                                case "glyphSource":
                                case "gs":
                                case "f":
                                    if (GlyphSources != null)
                                        GlyphSources.TryGetValue(value, out glyphSource);
                                    else
                                        glyphSource = null;
                                    break;
                            }
                        }
                    } else {
                        ;
                    }
                }
            }
            AppendRange(ref layoutEngine, glyphSource ?? defaultGlyphSource, text, currentRangeStart, count);
        }

        private void AppendImage (ref StringLayoutEngine layoutEngine, RichImage image) {
            layoutEngine.AppendImage(
                image.Texture.Instance, scale: image.Scale, 
                verticalAlignment: image.VerticalAlignment,
                margin: image.Margin,
                textureRegion: image.Bounds ?? Bounds.Unit
            );
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
            i = start;
            while (i < count) {
                var ch = text[i];
                switch (ch) {
                    case ']':
                        i++;
                        currentRangeStart = i;
                        return text.Substring(start, i - start - 1);
                    case '\"':
                    case '\'':
                    case '$':
                    case '[': {
                        i = start;
                        return null;
                    }
                }

                if (ch < ' ') {
                    i = start;
                    return null;
                }
                i++;
            }
            return null;
        }

        public void Invalidate () {
            Version++;
        }

        public bool Equals (RichTextConfiguration other) {
            return (NamedColors == other.NamedColors) &&
                (GlyphSources == other.GlyphSources) &&
                (Styles == other.Styles) &&
                (Images == other.Images) &&
                (KerningAdjustments == other.KerningAdjustments) &&
                (Version == other.Version);
        }
    }
}

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
using Squared.Util;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public static class RichText {
        private static readonly Regex RichCommandRegex = new Regex(@"\$\[[^\]]*\]", RegexOptions.Compiled);

        public static string ToPlainText (AbstractString richText) {
            if (richText.IsNull)
                return null;
            return RichCommandRegex.Replace(richText.ToString(), "");
        }
    }

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

    public struct RichTextLayoutState {
        public IGlyphSource GlyphSource;
        public readonly Color? InitialColor;
        public readonly float InitialScale;
        public readonly float InitialSpacing;
        public DenseList<string> MarkedStrings;

        public RichTextLayoutState (ref StringLayoutEngine engine) {
            InitialColor = engine.overrideColor;
            InitialScale = engine.scale;
            InitialSpacing = engine.spacing;
            GlyphSource = null;
            MarkedStrings = default(DenseList<string>);
        }

        public void Reset (ref StringLayoutEngine engine) {
            GlyphSource = null;
            engine.overrideColor = InitialColor;
            engine.scale = InitialScale;
            engine.spacing = InitialSpacing;
        }
    }

    public delegate bool MarkedStringProcessor (ref AbstractString text, ref RichTextLayoutState state, ref StringLayoutEngine layoutEngine);

    public struct RichTextConfiguration : IEquatable<RichTextConfiguration> {
        private static readonly Regex RuleRegex = new Regex(@"([\w\-_]+)(?:\s*):(?:\s*)([^;\]]*)(?:;|)", RegexOptions.Compiled);
        private static readonly Dictionary<string, Color?> SystemNamedColorCache = new Dictionary<string, Color?>();

        private int Version;
        public Dictionary<string, Color> NamedColors;
        public Dictionary<string, IGlyphSource> GlyphSources;
        public Dictionary<string, RichStyle> Styles;
        public Dictionary<string, RichImage> Images;
        public Dictionary<char, KerningAdjustment> KerningAdjustments;
        public MarkedStringProcessor MarkedStringProcessor;

        public string DefaultStyle;

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
            ref StringLayoutEngine layoutEngine, IGlyphSource defaultGlyphSource, AbstractString text, string styleName
        ) {
            var state = new RichTextLayoutState(ref layoutEngine);
            Append(ref layoutEngine, ref state, defaultGlyphSource, text, styleName);
        }

        public void Append (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, IGlyphSource defaultGlyphSource, AbstractString text, string styleName
        ) {
            var count = text.Length;
            var currentRangeStart = 0;
            RichStyle style;
            RichImage image;

            try {
                styleName = styleName ?? DefaultStyle;
                if (!string.IsNullOrWhiteSpace(styleName) && Styles.TryGetValue(styleName, out RichStyle defaultStyle))
                    ApplyStyle(ref layoutEngine, ref state, ref defaultStyle);

                for (int i = 0; i < count; i++) {
                    var ch = text[i];
                    var next = (i < count - 2) ? text[i + 1] : '\0';
                    if ((ch == '$') && ((next == '[') || (next == '('))) {
                        AppendRange(ref layoutEngine, state.GlyphSource ?? defaultGlyphSource, text, currentRangeStart, i);
                        var bracketed = ParseBracketedText(text, ref i, ref currentRangeStart);
                        var commandMode = next == '[';
                        if (bracketed == null) {
                            // FIXME: Can this cause an infinite loop?
                            continue;
                        } else if (commandMode && string.IsNullOrWhiteSpace(bracketed)) {
                            state.Reset(ref layoutEngine);
                        } else if (commandMode && (Styles != null) && bracketed.StartsWith(".") && Styles.TryGetValue(bracketed.Substring(1), out style)) {
                            ApplyStyle(ref layoutEngine, ref state, ref style);
                        } else if (commandMode && (Images != null) && Images.TryGetValue(bracketed, out image)) {
                            AppendImage(ref layoutEngine, image);
                        } else if (commandMode && bracketed.Contains(":")) {
                            foreach (var _match in RuleRegex.Matches(bracketed)) {
                                var match = (Match)_match;
                                if (!match.Success)
                                    continue;

                                var key = match.Groups[1].Value;
                                var value = match.Groups[2].Value;
                                switch (key) {
                                    case "color":
                                    case "c":
                                        layoutEngine.overrideColor = ParseColor(value) ?? state.InitialColor;
                                        break;
                                    case "scale":
                                    case "sc":
                                        if (!float.TryParse(value, out float newScale))
                                            layoutEngine.scale = state.InitialScale;
                                        else
                                            layoutEngine.scale = state.InitialScale * newScale;
                                        break;
                                    case "spacing":
                                    case "sp":
                                        if (!float.TryParse(value, out float newSpacing))
                                            layoutEngine.spacing = state.InitialSpacing;
                                        else
                                            layoutEngine.spacing = state.InitialSpacing * newSpacing;
                                        break;
                                    case "font":
                                    case "glyph-source":
                                    case "glyphSource":
                                    case "gs":
                                    case "f":
                                        if (GlyphSources != null)
                                            GlyphSources.TryGetValue(value, out state.GlyphSource);
                                        else
                                            state.GlyphSource = null;
                                        break;
                                }
                            }
                        } else if (!commandMode) {
                            AbstractString astr = bracketed;
                            var ok = true;
                            // HACK: The string processor may mess with layout state, so we want to restore it after
                            var markedState = new RichTextLayoutState(ref layoutEngine) {
                                GlyphSource = state.GlyphSource
                            };
                            if (MarkedStringProcessor != null)
                                ok = MarkedStringProcessor(ref astr, ref markedState, ref layoutEngine);
                            if (ok) {
                                var m = new LayoutMarker(layoutEngine.currentCharacterIndex, layoutEngine.currentCharacterIndex + astr.Length, bracketed);
                                layoutEngine.Markers.Add(m);
                                state.MarkedStrings.Add(bracketed);
                                AppendRange(ref layoutEngine, markedState.GlyphSource ?? defaultGlyphSource, astr, 0, astr.Length);
                            }
                            if (MarkedStringProcessor != null)
                                markedState.Reset(ref layoutEngine);
                        } else {
                            var close = (next == '[') ? ']' : ')';
                            layoutEngine.AppendText(state.GlyphSource ?? defaultGlyphSource, "<invalid: $" + next + bracketed + close + ">");
                        }
                    }
                }
                AppendRange(ref layoutEngine, state.GlyphSource ?? defaultGlyphSource, text, currentRangeStart, count);
            } finally {
                state.Reset(ref layoutEngine);
            }
        }

        private static void ApplyStyle (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, ref RichStyle style
        ) {
            state.GlyphSource = style.GlyphSource ?? state.GlyphSource;
            layoutEngine.overrideColor = style.Color ?? state.InitialColor;
            layoutEngine.scale = style.Scale * state.InitialScale ?? state.InitialScale;
            layoutEngine.spacing = style.Spacing ?? state.InitialSpacing;
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

        private string ParseBracketedText (AbstractString text, ref int i, ref int currentRangeStart) {
            var count = text.Length;
            var start = i + 2;
            i = start;
            while (i < count) {
                var ch = text[i];
                switch (ch) {
                    case ')':
                    case ']':
                        currentRangeStart = i + 1;
                        return text.Substring(start, i - start);
                    case '\"':
                    case '\'':
                    case '$':
                    case '(':
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

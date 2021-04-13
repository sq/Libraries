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
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public static class RichText {
        private static string ToPlainText_Slow (AbstractString richText, int firstDollarOffset) {
            var result = new StringBuilder();
            for (int i = 0; i < firstDollarOffset; i++)
                result.Append(richText[i]);

            int rangeStarted = 0;
            char? closer = null;
            for (int i = firstDollarOffset, l = richText.Length; i < l; i++) {
                var ch = richText[i];
                var next = (i < richText.Length - 1) ? richText[i + 1] : '\0';
                if (closer != null) {
                    if (ch == closer) {
                        if (closer == ')') {
                            var markedText = richText.Substring(rangeStarted, i - rangeStarted);
                            var pipeOffset = markedText.IndexOf('|');
                            if (pipeOffset >= 0)
                                result.Append(markedText.Substring(pipeOffset + 1));
                            else
                                result.Append(markedText);
                        }
                        closer = null;
                    }
                    continue;
                } else if (ch == '$') {
                    if ((next == '(') || (next == '[')) {
                        closer = (next == '(') ? ')' : ']';
                        i++;
                        rangeStarted = i + 1;
                        continue;
                    }
                } else {
                    result.Append(ch);
                }
            }

            return result.ToString();
        }

        public static string ToPlainText (AbstractString richText) {
            if (richText.IsNull)
                return null;

            for (int i = 0, l = richText.Length; i < l - 1; i++) {
                var c = richText[i];
                if ((c == '$') && (richText[i + 1] == '[') || (richText[i + 1] == '('))
                    return ToPlainText_Slow(richText, i);
            }

            return richText.ToString();
        }
    }

    public delegate void RichStyleApplier (ref RichStyle style, ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state);

    public struct RichStyle {
        public IGlyphSource GlyphSource;
        public Color? Color;
        public float? Scale;
        public float? Spacing;
        public RichStyleApplier Apply;
    }

    public struct AsyncRichImage {
        public Future<RichImage> Future;
        public RichImage? Value;
        public int? Width, Height;
        public Vector2? Margin;
        public bool DoNotAdjustLineSpacing, CreateBox;

        public AsyncRichImage (ref RichImage img) {
            var tex = img.Texture.Instance;
            if (tex == null)
                throw new ArgumentNullException("img.Texture");
            Width = tex.Width;
            Height = tex.Height;
            Margin = img.Margin;
            Value = img;
            Future = null;
            DoNotAdjustLineSpacing = img.DoNotAdjustLineSpacing;
            CreateBox = img.CreateBox;
        }

        public AsyncRichImage (Future<RichImage> f, int? width = null, int? height = null, Vector2? margin = null, bool doNotAdjustLineSpacing = false, bool createBox = false) {
            if (f == null)
                throw new ArgumentNullException("f");
            Future = f;
            Width = width;
            Height = height;
            Margin = margin;
            Value = null;
            DoNotAdjustLineSpacing = doNotAdjustLineSpacing;
            CreateBox = createBox;
        }

        public bool IsInitialized {
            get {
                return (Future != null) || Value.HasValue;
            }
        }
    }

    public struct RichImage {
        public AbstractTextureReference Texture;
        public Bounds? Bounds;
        public Vector2 Margin;
        public bool DoNotAdjustLineSpacing;
        public bool CreateBox;
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
        public IGlyphSource DefaultGlyphSource, GlyphSource;
        public readonly Color? InitialColor;
        public readonly float InitialScale;
        public readonly float InitialSpacing;
        public DenseList<string> MarkedStrings;

        public RichTextLayoutState (ref StringLayoutEngine engine, IGlyphSource defaultGlyphSource) {
            InitialColor = engine.overrideColor;
            InitialScale = engine.scale;
            InitialSpacing = engine.spacing;
            DefaultGlyphSource = defaultGlyphSource;
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

    /// <summary>
    /// Prepares a layout engine to display a marked string (i.e. '$(text)' or '$(id|text)'), and potentially changes its display text.
    /// </summary>
    /// <param name="text">The display text for the marked string. You can modify this to change the text.</param>
    /// <param name="id">The identifier for the string, if any. i.e. $(identifier|text)</param>
    /// <param name="state">A state snapshot that can be used to examine or restore the current state of the layout engine.</param>
    /// <param name="layoutEngine">The layout engine that will be used to append the text (after this method returns).</param>
    /// <returns>true if the string should be laid out, false if it should be omitted from the output entirely.</returns>
    public delegate bool MarkedStringProcessor (ref AbstractString text, string id, ref RichTextLayoutState state, ref StringLayoutEngine layoutEngine);

    public struct RichTextConfiguration : IEquatable<RichTextConfiguration> {
        private static readonly Regex RuleRegex = new Regex(@"([\w\-_]+)(?:\s*):(?:\s*)([^;\]]*)(?:;|)", RegexOptions.Compiled);
        private static readonly Dictionary<string, Color?> SystemNamedColorCache = new Dictionary<string, Color?>();

        private int Version;
        public Dictionary<string, Color> NamedColors;
        public Dictionary<string, IGlyphSource> GlyphSources;
        public Dictionary<string, RichStyle> Styles;
        public Dictionary<string, RichImage> Images;
        public Func<string, AsyncRichImage> ImageProvider;
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

        /// <returns>a list of rich images that were referenced</returns>
        public DenseList<AsyncRichImage> Append (
            ref StringLayoutEngine layoutEngine, IGlyphSource defaultGlyphSource, AbstractString text, 
            string styleName, bool? overrideSuppress = null
        ) {
            var state = new RichTextLayoutState(ref layoutEngine, defaultGlyphSource);
            return Append(ref layoutEngine, ref state, text, styleName, overrideSuppress);
        }

        private static readonly HashSet<char>
            CommandTerminators = new HashSet<char> { '\"', '\'', '$', '[' },
            StringTerminators = new HashSet<char> { '$', '(' };

        /// <returns>a list of rich images that were referenced</returns>
        public DenseList<AsyncRichImage> Append (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, AbstractString text, 
            string styleName, bool? overrideSuppress = null
        ) {
            var result = new DenseList<AsyncRichImage>();
            var count = text.Length;
            var currentRangeStart = 0;
            RichStyle style;
            RichImage image;
            AsyncRichImage ai;

            try {
                styleName = styleName ?? DefaultStyle;
                if (!string.IsNullOrWhiteSpace(styleName) && Styles.TryGetValue(styleName, out RichStyle defaultStyle))
                    ApplyStyle(ref layoutEngine, ref state, ref defaultStyle);

                for (int i = 0; i < count; i++) {
                    var ch = text[i];
                    var next = (i < count - 2) ? text[i + 1] : '\0';
                    if ((ch == '$') && ((next == '[') || (next == '('))) {
                        AppendRange(ref layoutEngine, state.GlyphSource ?? state.DefaultGlyphSource, text, currentRangeStart, i, overrideSuppress);
                        var commandMode = next == '[';
                        var bracketed = ParseBracketedText(
                            text, ref i, ref currentRangeStart, 
                            commandMode ? CommandTerminators : StringTerminators, 
                            commandMode ? ']' : ')'
                        );
                        if (bracketed == null) {
                            // FIXME: Can this cause an infinite loop?
                            continue;
                        } else if (commandMode && string.IsNullOrWhiteSpace(bracketed)) {
                            state.Reset(ref layoutEngine);
                        } else if (commandMode && (Styles != null) && bracketed.StartsWith(".") && Styles.TryGetValue(bracketed.Substring(1), out style)) {
                            ApplyStyle(ref layoutEngine, ref state, ref style);
                        } else if (commandMode && (Images != null) && Images.TryGetValue(bracketed, out image)) {
                            AppendImage(ref layoutEngine, image);
                            ai = new AsyncRichImage(ref image);
                            result.Add(ref ai);
                        } else if (
                            commandMode && (ImageProvider != null) && 
                            (ai = ImageProvider(bracketed)).IsInitialized
                        ) {
                            if (ai.Value.HasValue) {
                                AppendImage(ref layoutEngine, ai.Value.Value);
                            } else if (ai.Width.HasValue) {
                                var m = ai.Margin ?? Vector2.Zero;
                                var w = ai.Width.Value + m.X;
                                var h = (ai.Height ?? 0) + m.Y;
                                if (ai.CreateBox) {
                                    Bounds box;
                                    layoutEngine.CreateBox(w, h, out box);
                                }
                                layoutEngine.Advance(w, h, ai.DoNotAdjustLineSpacing, false);
                            }
                            result.Add(ref ai);
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
                            string id = null;
                            int pipeIndex = bracketed.IndexOf('|');
                            if (pipeIndex >= 0) {
                                id = bracketed.Substring(0, pipeIndex);
                                bracketed = bracketed.Substring(pipeIndex + 1);
                                astr = bracketed;
                            }

                            var ok = true;
                            // HACK: The string processor may mess with layout state, so we want to restore it after
                            var markedState = new RichTextLayoutState(ref layoutEngine, state.DefaultGlyphSource) {
                                GlyphSource = state.GlyphSource
                            };
                            if (MarkedStringProcessor != null)
                                ok = MarkedStringProcessor(ref astr, id, ref markedState, ref layoutEngine);
                            if (ok) {
                                var m = new LayoutMarker(layoutEngine.currentCharacterIndex, layoutEngine.currentCharacterIndex + astr.Length - 1, bracketed, id);
                                layoutEngine.Markers.Add(m);
                                state.MarkedStrings.Add(bracketed);
                                AppendRange(ref layoutEngine, markedState.GlyphSource ?? state.DefaultGlyphSource, astr, 0, astr.Length, overrideSuppress);
                            }
                            if (MarkedStringProcessor != null)
                                markedState.Reset(ref layoutEngine);
                        } else {
                            var close = (next == '[') ? ']' : ')';
                            layoutEngine.AppendText(state.GlyphSource ?? state.DefaultGlyphSource, "<invalid: $" + next + bracketed + close + ">");
                        }
                    }
                }
                AppendRange(ref layoutEngine, state.GlyphSource ?? state.DefaultGlyphSource, text, currentRangeStart, count, overrideSuppress);
            } finally {
                state.Reset(ref layoutEngine);
            }

            return result;
        }

        private static void ApplyStyle (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, ref RichStyle style
        ) {
            state.GlyphSource = style.GlyphSource ?? state.GlyphSource;
            layoutEngine.overrideColor = style.Color ?? state.InitialColor;
            layoutEngine.scale = style.Scale * state.InitialScale ?? state.InitialScale;
            layoutEngine.spacing = style.Spacing ?? state.InitialSpacing;
            if (style.Apply != null)
                style.Apply(ref style, ref layoutEngine, ref state);
        }

        private void AppendImage (ref StringLayoutEngine layoutEngine, RichImage image) {
            layoutEngine.AppendImage(
                image.Texture.Instance, scale: image.Scale, 
                verticalAlignment: image.VerticalAlignment,
                margin: image.Margin,
                textureRegion: image.Bounds ?? Bounds.Unit,
                doNotAdjustLineSpacing: image.DoNotAdjustLineSpacing,
                createBox: image.CreateBox
            );
        }

        private void AppendRange (
            ref StringLayoutEngine layoutEngine, IGlyphSource glyphSource, AbstractString text,
            int rangeStart, int rangeEnd, bool? overrideSuppress
        ) {
            if (rangeEnd <= rangeStart)
                return;
            layoutEngine.AppendText(glyphSource, text, KerningAdjustments, start: rangeStart, end: rangeEnd, overrideSuppress: overrideSuppress);
        }

        private string ParseBracketedText (AbstractString text, ref int i, ref int currentRangeStart, HashSet<char> terminators, char close) {
            var count = text.Length;
            var start = i + 2;
            i = start;
            while (i < count) {
                var ch = text[i];
                if (ch == close) {
                    currentRangeStart = i + 1;
                    return text.Substring(start, i - start);
                } else if (terminators.Contains(ch) || (ch < ' ')) {
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

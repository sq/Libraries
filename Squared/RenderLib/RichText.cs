using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
                var next = (i < l - 1) ? richText[i + 1] : '\0';
                if (closer != null) {
                    if (ch == closer) {
                        if (closer == ')') {
                            var markedText = richText.Substring(rangeStarted, i - rangeStarted);
                            var pipeOffset = markedText.IndexOf('|');
                            if (pipeOffset >= 0)
                                markedText = markedText.Substring(pipeOffset);
                            markedText.CopyTo(result);
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

        private static int PlainTextLength_Slow (AbstractString richText, int firstDollarOffset, bool includeWhitespace = true) {
            var result = firstDollarOffset;

            int rangeStarted = 0;
            char? closer = null;
            for (int i = firstDollarOffset, l = richText.Length; i < l; i++) {
                var ch = richText[i];
                var next = (i < l - 1) ? richText[i + 1] : '\0';
                if (closer != null) {
                    if (ch == closer) {
                        if (closer == ')') {
                            var markedText = richText.Substring(rangeStarted, i - rangeStarted);
                            var pipeOffset = markedText.IndexOf('|');
                            if (pipeOffset >= 0)
                                markedText = markedText.Substring(pipeOffset);

                            if (!includeWhitespace)
                                result += PlainTextLength_Slow(markedText, 0, false);
                            else
                                result += markedText.Length;
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
                } else if (includeWhitespace || !char.IsWhiteSpace(ch)) {
                    result++;
                }
            }

            return result;
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

        public static int PlainTextLength (AbstractString richText, bool includeWhitespace = true) {
            if (richText.IsNull)
                return 0;

            if (!includeWhitespace)
                return PlainTextLength_Slow(richText, 0, false);

            for (int i = 0, l = richText.Length; i < l - 1; i++) {
                var c = richText[i];
                if ((c == '$') && (richText[i + 1] == '[') || (richText[i + 1] == '('))
                    return PlainTextLength_Slow(richText, i, true);
            }

            return richText.Length;
        }

        public static DenseList<RichRule> ParseRules (AbstractString text, ref DenseList<RichParseError> parseErrors) {
            var result = new DenseList<RichRule>();
            int keyStart = 0;
            int? keyEnd = null, valueStart = null;
            for (int i = 0, l = text.Length; i <= l; i++) {
                var ch = (i == l) ? '\0' : text[i];
                switch (ch) {
                    case ':':
                        if (valueStart.HasValue)
                            parseErrors.Add(new RichParseError {
                                Text = text,
                                Offset = i,
                                Message = "Unexpected :"
                            });
                        else {
                            keyEnd = i;
                            valueStart = i + 1;
                        }
                        break;

                    case '\0':
                    case ';':
                        if (!valueStart.HasValue) {
                            if (ch != '\0')
                                parseErrors.Add(new RichParseError {
                                    Text = text,
                                    Offset = i,
                                    Message = "Unexpected ;"
                                });
                        } else {
                            result.Add(new RichRule {
                                Key = text.Substring(keyStart, keyEnd.Value - keyStart),
                                Value = text.Substring(valueStart.Value, i - valueStart.Value),
                            });
                            keyStart = i + 1;
                            keyEnd = valueStart = null;
                        }
                        break;
                }
            }
            return result;
        }
    }

    public delegate void RichStyleApplier (in RichStyle style, ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state);

    public struct RichParseError {
        public AbstractString Text;
        public int Offset;
        public string Message;

        public override string ToString () {
            return $"'{Message}' at {Offset}";
        }
    }

    public struct RichRule {
        public AbstractString Key;
        public AbstractString Value;

        public bool Equals (RichRule rhs) {
            return Key.TextEquals(rhs.Key, StringComparison.Ordinal) &&
                Value.TextEquals(rhs.Value, StringComparison.Ordinal);
        }

        public override int GetHashCode () {
            return Key.GetHashCode() ^ Value.GetHashCode();
        }

        public override bool Equals (object obj) {
            if (obj is RichRule rr)
                return Equals(rr);
            else
                return false;
        }

        public override string ToString () {
            return $"{Key}: {Value}";
        }
    }

    public struct RichStyle {
        public IGlyphSource GlyphSource;
        public Color? Color;
        public float? Scale;
        public float? Spacing;
        public float? AdditionalLineSpacing;
        public RichStyleApplier Apply;
    }

    public struct AsyncRichImage {
        public Future<Texture2D> Future;
        public RichImage? Value;
        public float? Width, Height, MaxWidthPercent;
        public Vector2? Margin;
        public float? HardHorizontalAlignment, HardVerticalAlignment;
        public float Scale;
        public float VerticalAlignment;
        public bool DoNotAdjustLineSpacing, CreateBox, Clear;
        public readonly bool Dead;

        public AsyncRichImage (bool dead) {
            if (!dead)
                throw new ArgumentException();
            this = default;
            Dead = true;
        }

        public AsyncRichImage (ref RichImage img) {
            var tex = img.Texture.Instance;
            if (tex == null)
                throw new ArgumentNullException("img.Texture");
            Width = tex.Width;
            Height = tex.Height;
            Margin = img.Margin;
            Scale = img.Scale;
            Value = img;
            Future = null;
            DoNotAdjustLineSpacing = img.DoNotAdjustLineSpacing;
            CreateBox = img.CreateBox;
            Clear = img.Clear;
            HardHorizontalAlignment = img.HardHorizontalAlignment;
            HardVerticalAlignment = img.HardVerticalAlignment;
            VerticalAlignment = img.VerticalAlignment;
            Dead = false;
            MaxWidthPercent = null;
        }

        public AsyncRichImage (RichImage img)
            : this(ref img) {
        }

        public AsyncRichImage (
            Future<Texture2D> f, float? width = null, float? height = null, 
            Vector2? margin = null, float? hardHorizontalAlignment = null, float? hardVerticalAlignment = null, 
            float scale = 1f, float verticalAlignment = 1f, bool doNotAdjustLineSpacing = false, 
            bool createBox = false, float? maxWidthPercent = null, bool clear = false
        ) {
            if (f == null)
                throw new ArgumentNullException("f");
            Future = f;
            Width = width;
            Scale = scale;
            Height = height;
            Margin = margin;
            HardHorizontalAlignment = hardHorizontalAlignment;
            HardVerticalAlignment = hardVerticalAlignment;
            VerticalAlignment = verticalAlignment;
            Value = null;
            DoNotAdjustLineSpacing = doNotAdjustLineSpacing;
            CreateBox = createBox;
            Clear = clear;
            Dead = false;
            MaxWidthPercent = maxWidthPercent;
        }

        public bool TryGetValue (out RichImage result) {
            if (Value.HasValue) {
                result = Value.Value;
                return true;
            } else if ((Future == null) || !Future.Completed) {
                result = default(RichImage);
                return false;
            } else if (Future.Failed) {
                result = default;
                return false;
            } else {
                var tex = Future.Result;
                if (tex == null) {
                    result = default;
                    return false;
                }

                float? autoScaleX = Width / tex.Width,
                    autoScaleY = Height / tex.Height;
                float scale = Scale;
                if (autoScaleX.HasValue && autoScaleY.HasValue)
                    scale *= Math.Min(autoScaleX.Value, autoScaleY.Value);
                else if (autoScaleX.HasValue)
                    scale *= autoScaleX.Value;
                else if (autoScaleY.HasValue)
                    scale *= autoScaleY.Value;

                result = new RichImage {
                    Texture = tex,
                    CreateBox = CreateBox,
                    Clear = Clear,
                    DoNotAdjustLineSpacing = DoNotAdjustLineSpacing,
                    Scale = scale,
                    HardHorizontalAlignment = HardHorizontalAlignment,
                    HardVerticalAlignment = HardVerticalAlignment,
                    Margin = Margin ?? Vector2.Zero,
                    OverrideHeight = Height * Scale,
                    OverrideWidth = Width * Scale,
                    VerticalAlignment = VerticalAlignment,
                    MaxWidthPercent = MaxWidthPercent
                };
                return true;
            }
        }

        public bool HasValue => Value.HasValue || (Future?.Completed ?? false);
        public bool IsInitialized => (Future != null) || Value.HasValue || Dead;
    }

    public struct RichImage {
        public AbstractTextureReference Texture;
        public Bounds? Bounds;
        public Vector2 Margin;
        public float? OverrideWidth, OverrideHeight, MaxWidthPercent;
        public float? HardHorizontalAlignment, HardVerticalAlignment;
        public bool DoNotAdjustLineSpacing;
        public bool CreateBox, Clear;
        private float VerticalAlignmentMinusOne;
        private float ScaleMinusOne;

        public float VerticalAlignment {
            get => VerticalAlignmentMinusOne + 1;
            set => VerticalAlignmentMinusOne = value - 1;
        }

        public float Scale {
            get => ScaleMinusOne + 1;
            set => ScaleMinusOne = value - 1;
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

    public struct RichTextLayoutState : IDisposable {
        private static readonly ThreadLocal<UnorderedList<StringBuilder>> ScratchStringBuilders =
            new ThreadLocal<UnorderedList<StringBuilder>>(() => new UnorderedList<StringBuilder>());
        private static readonly ThreadLocal<List<AbstractString>> MarkedStringLists =
            new ThreadLocal<List<AbstractString>>();

        public IGlyphSource DefaultGlyphSource, GlyphSource;
        public readonly Color? InitialColor;
        public readonly float InitialScale;
        public readonly float InitialSpacing;
        public readonly float InitialLineSpacing;
        public DenseList<string> Tags;
        public List<AbstractString> MarkedStrings;
        private DenseList<StringBuilder> StringBuildersToReturn;

        public RichTextLayoutState (ref StringLayoutEngine engine, IGlyphSource defaultGlyphSource) {
            InitialColor = engine.overrideColor;
            InitialScale = engine.scale;
            InitialSpacing = engine.spacing;
            InitialLineSpacing = engine.additionalLineSpacing;
            DefaultGlyphSource = defaultGlyphSource;
            GlyphSource = null;
            MarkedStrings = MarkedStringLists.Value;
            MarkedStringLists.Value = null;
            Tags = default;
        }

        /// <summary>
        /// Allocates a temporary empty stringbuilder from local storage, that will be released after layout is done.
        /// </summary>
        public StringBuilder GetStringBuilder () {
            StringBuilder result;
            var sbs = ScratchStringBuilders.Value;
            lock (sbs)
                sbs.TryPopBack(out result);
            if (result == null)
                result = new StringBuilder();
            else
                result.Clear();

            // HACK: Avoid allocating a backing store, at the cost of leaking some string builders
            if (StringBuildersToReturn.Count < 4)
                StringBuildersToReturn.Add(result);

            return result;
        }

        /// <summary>
        /// Allocates a temporary empty stringbuilder from local storage, that will be released after layout is done,
        ///  then concatenates the provided strings into it and returns the result.
        /// </summary>
        public AbstractString ConcatStrings (string s1, string s2, string s3 = null, string s4 = null, string s5 = null) {
            bool e1 = string.IsNullOrEmpty(s1),
                e2 = string.IsNullOrEmpty(s2),
                e3 = string.IsNullOrEmpty(s3),
                e4 = string.IsNullOrEmpty(s4),
                e5 = string.IsNullOrEmpty(s5);

            // Fast paths for "hello" + "" and "" + "hello"
            if (!e1 && e2 && e3 && e4 && e5)
                return s1;
            else if (e1 && !e2 && e3 && e4 && e5)
                return s2;

            var result = GetStringBuilder();
            if (!e1)
                result.Append(s1);
            if (!e2)
                result.Append(s2);
            if (!e3)
                result.Append(s3);
            if (!e4)
                result.Append(s4);
            if (!e5)
                result.Append(s5);
            return result;
        }

        public void Reset (ref StringLayoutEngine engine) {
            GlyphSource = null;
            engine.overrideColor = InitialColor;
            engine.scale = InitialScale;
            engine.spacing = InitialSpacing;
            engine.additionalLineSpacing = InitialLineSpacing;
        }

        public void Dispose () {
            MarkedStrings?.Clear();
            if (MarkedStrings != null) {
                MarkedStringLists.Value = MarkedStrings;
                MarkedStrings = null;
            }

            var sbs = ScratchStringBuilders.Value;
            lock (sbs)
                foreach (var sb in StringBuildersToReturn)
                    sbs.Add(sb);
            StringBuildersToReturn.Clear();
        }

        public void AppendPlainText (ref StringLayoutEngine layoutEngine, AbstractString text) {
            layoutEngine.AppendText(GlyphSource ?? DefaultGlyphSource, text);
        }
    }

    public enum MarkedStringAction {
        /// <summary>
        /// Lay out the text as normal and generate a marker
        /// </summary>
        Default = 0,
        /// <summary>
        /// Lay out the text as plain text and do not generate a marker
        /// </summary>
        PlainText = 1,
        /// <summary>
        /// Do not lay out the text
        /// </summary>
        Omit = 2,
        /// <summary>
        /// The processor generated new rich text, so process that (gross)
        /// </summary>
        RichText = 3,
        /// <summary>
        /// The processor failed to process the text
        /// </summary>
        Error = 4
    }

    /// <summary>
    /// Prepares a layout engine to display a marked string (i.e. '$(text)' or '$(id|text)'), and potentially changes its display text.
    /// </summary>
    /// <param name="text">The display text for the marked string. You can modify this to change the text.</param>
    /// <param name="id">The identifier for the string, if any. i.e. $(identifier|text)</param>
    /// <param name="state">A state snapshot that can be used to examine or restore the current state of the layout engine.</param>
    /// <param name="layoutEngine">The layout engine that will be used to append the text (after this method returns).</param>
    /// <returns>true if the string should be laid out, false if it should be omitted from the output entirely.</returns>
    public delegate MarkedStringAction MarkedStringProcessor (ref AbstractString text, ref AbstractString id, ref RichTextLayoutState state, ref StringLayoutEngine layoutEngine);

    public sealed class RichTextConfiguration : IEquatable<RichTextConfiguration> {
        public class GlyphSourceCollection : ImmutableAbstractStringLookup<GlyphSourceEntry> {
            public GlyphSourceCollection (bool ignoreCase = false)
                : base (ignoreCase) {
            }

            public GlyphSourceCollection (int capacity, bool ignoreCase = false)
                : base (capacity, ignoreCase) {
            }

            public void Add (ImmutableAbstractString key, IGlyphSource value) => Add(key, new GlyphSourceEntry(value));
            public void Add (ImmutableAbstractString key, Func<IGlyphSource> value) => Add(key, new GlyphSourceEntry(value));
        }

        public struct GlyphSourceEntry {
            private object _Value;

            public GlyphSourceEntry (IGlyphSource instance) {
                _Value = instance;
            }

            public GlyphSourceEntry (Func<IGlyphSource> getter) {
                _Value = getter;
            }

            public IGlyphSource Value {
                get {
                    if (_Value is IGlyphSource instance)
                        return instance;
                    else if (_Value is Func<IGlyphSource> getter)
                        return getter();
                    else if (_Value == null)
                        return null;
                    else
                        throw new Exception("Corrupt GlyphSourceEntry");
                }
            }
        }

        public event Action<RichTextConfiguration, RichParseError> OnParseError;

        private int Version;
        public GlyphSourceCollection GlyphSources;
        public ImmutableAbstractStringLookup<Color> NamedColors;
        public ImmutableAbstractStringLookup<RichStyle> Styles;
        public ImmutableAbstractStringLookup<RichImage> Images;
        public Func<AbstractString, RichTextConfiguration, AsyncRichImage> ImageProvider;
        public MarkedStringProcessor MarkedStringProcessor;
        public ColorConversionMode ColorMode;
        /// <summary>
        /// If set, any images referenced by rich text will not be inserted.
        /// </summary>
        public bool DisableImages;
        /// <summary>
        /// Contains user-provided data that can be used by your ImageProvider or MarkedStringProcessor.
        /// </summary>
        public DenseList<string> Tags;

        public string DefaultStyle;

        private Color AutoConvert (Color color) {
            return ColorSpace.ConvertColor(color, ColorMode);
        }

        private Color? AutoConvert (Color? color) {
            return ColorSpace.ConvertColor(color, ColorMode);
        }

        private Color? ParseColor (AbstractString text) {
            if (text.IsNullOrEmpty)
                return null;

            if (
                text.StartsWith("#") && 
                uint.TryParse(text.SubstringCopy(1), NumberStyles.HexNumber, null, out uint decoded)
            ) {
                var result = new Color { PackedValue = decoded };
                var temp = result.R;
                result.R = result.B;
                result.B = temp;
                if (text.Length <= 7)
                    result.A = 255;
                return AutoConvert(result);
            } else if ((NamedColors != null) && NamedColors.TryGetValue(text, out Color namedColor)) {
                return AutoConvert(namedColor);
            } else {
                Color? result;
                if (NamedColor.TryParse(text, out Color named))
                    result = named;
                else
                    result = null;
                return AutoConvert(result);
            }
        }

        public void DefineColor (ImmutableAbstractString key, Color color) {
            key.GetHashCode();
            if (NamedColors == null)
                NamedColors = new ImmutableAbstractStringLookup<Color>(true);
            NamedColors[key] = color;
        }

        public void DefineGlyphSource (ImmutableAbstractString key, IGlyphSource gs) {
            key.GetHashCode();
            if (GlyphSources == null)
                GlyphSources = new GlyphSourceCollection(true);
            GlyphSources[key] = new GlyphSourceEntry(gs);
        }

        public void DefineGlyphSource (ImmutableAbstractString key, Func<IGlyphSource> getter) {
            key.GetHashCode();
            if (GlyphSources == null)
                GlyphSources = new GlyphSourceCollection(true);
            GlyphSources[key] = new GlyphSourceEntry(getter);
        }

        public void DefineStyle (ImmutableAbstractString key, RichStyle style) {
            key.GetHashCode();
            if (Styles == null)
                Styles = new ImmutableAbstractStringLookup<RichStyle>(true);
            Styles[key] = style;
        }

        public void DefineImage (ImmutableAbstractString key, RichImage image) {
            key.GetHashCode();
            if (Images == null)
                Images = new ImmutableAbstractStringLookup<RichImage>(true);
            Images[key] = image;
        }

        /// <returns>a list of rich images that were referenced</returns>
        public DenseList<AsyncRichImage> Append (
            ref StringLayoutEngine layoutEngine, IGlyphSource defaultGlyphSource, AbstractString text, 
            string styleName, bool? overrideSuppress = null
        ) {
            var state = new RichTextLayoutState(ref layoutEngine, defaultGlyphSource);
            state.Tags.AddRange(ref Tags);
            try {
                return Append(ref layoutEngine, ref state, text, styleName, overrideSuppress);
            } finally {
                state.Dispose();
            }
        }

        private static readonly HashSet<char>
            CommandTerminators = new HashSet<char> { '\"', '\'', '$', '[' },
            StringTerminators = new HashSet<char> { '$', '(' };

        private enum RichRuleId : int {
            Unknown,
            Color,
            Scale,
            Spacing,
            Font,
            LineSpacing
        }

        private static readonly ImmutableAbstractStringLookup<RichRuleId> RuleNameTable =
            new ImmutableAbstractStringLookup<RichRuleId>(true) {
                { "color", RichRuleId.Color },
                { "c", RichRuleId.Color },
                { "scale", RichRuleId.Scale },
                { "sc", RichRuleId.Scale },
                { "spacing", RichRuleId.Spacing },
                { "sp", RichRuleId.Spacing },
                { "linespacing", RichRuleId.LineSpacing },
                { "ls", RichRuleId.LineSpacing },
                { "line-spacing", RichRuleId.LineSpacing },
                { "font", RichRuleId.Font },
                { "f", RichRuleId.Font },
                { "glyphsource", RichRuleId.Font },
                { "gs", RichRuleId.Font },
                { "glyph-source", RichRuleId.Font },
            };

        private void AppendRichRange (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, AbstractString text, 
            bool? overrideSuppress, ref DenseList<AsyncRichImage> referencedImages, ref DenseList<RichParseError> parseErrors
        ) {
            int currentRangeStart = 0;
            RichStyle style;
            RichImage image;
            AsyncRichImage ai;

            var count = text.Length;
            for (int i = 0; i < count; i++) {
                var ch = text[i];
                var next = (i < count - 2) ? text[i + 1] : '\0';
                if ((ch == '$') && ((next == '[') || (next == '('))) {
                    AppendPlainRange(ref layoutEngine, state.GlyphSource ?? state.DefaultGlyphSource, text, currentRangeStart, i, overrideSuppress);
                    var commandMode = next == '[';
                    var bracketed = ParseBracketedText(
                        text, ref i, ref currentRangeStart, 
                        commandMode ? CommandTerminators : StringTerminators, 
                        commandMode ? ']' : ')'
                    );
                    if (commandMode && bracketed.Value.IsNullOrEmpty) {
                        state.Reset(ref layoutEngine);
                    } else if (
                        commandMode && (Styles != null) && 
                        bracketed.Value.StartsWith(".") && 
                        Styles.TryGetValue(bracketed.Value.Substring(1).AsImmutable(true), out style)
                    ) {
                        ApplyStyle(ref layoutEngine, ref state, in style);
                    } else if (commandMode && (Images != null) && Images.TryGetValue(bracketed, out image)) {
                        if (!DisableImages)
                            AppendImage(ref layoutEngine, image);
                        else
                            parseErrors.Add(new RichParseError {
                                Offset = bracketed.Offset,
                                Message = "Images are disabled",
                                Text = bracketed.Value
                            });
                        ai = new AsyncRichImage(ref image);
                        referencedImages.Add(ref ai);
                    } else if (
                        commandMode && (ImageProvider != null) && 
                        (ai = ImageProvider(bracketed.Value, this)).IsInitialized
                    ) {
                        var currentX1 = 0f;
                        var currentX2 = Math.Max(layoutEngine.currentLineBreakAtX ?? 0, layoutEngine.currentLineMaxX);
                        if (ai.Dead) {
                            referencedImages.Add(ai);
                        } else if (DisableImages) {
                            referencedImages.Add(ai);
                            parseErrors.Add(new RichParseError {
                                Offset = bracketed.Offset,
                                Message = "Images are disabled",
                                Text = bracketed.Value
                            });
                        } else if (ai.TryGetValue(out RichImage ri)) {
                            AppendImage(ref layoutEngine, ri);
                            referencedImages.Add(ref ai);
                        } else if (ai.Width.HasValue) {
                            var m = ai.Margin ?? Vector2.Zero;
                            var halfM = m / 2f;
                            var w = ai.Width.Value;
                            var h = (ai.Height ?? 0);
                            if (ai.CreateBox) {
                                Bounds box;
                                float boxX = layoutEngine.characterOffset.X,
                                    boxY = layoutEngine.characterOffset.Y;
                                if (ai.HardHorizontalAlignment.HasValue)
                                    // FIXME
                                    boxX = Arithmetic.Lerp(layoutEngine.actualPosition.X, layoutEngine.actualPosition.X + layoutEngine.currentLineBreakAtX - w ?? 0f, ai.HardHorizontalAlignment.Value);
                                if (ai.HardVerticalAlignment.HasValue)
                                    // FIXME
                                    boxY = Arithmetic.Lerp(layoutEngine.actualPosition.Y, layoutEngine.actualPosition.Y + layoutEngine.stopAtY - h ?? 0f, ai.HardVerticalAlignment.Value);
                                box = Bounds.FromPositionAndSize(boxX - halfM.X, boxY - halfM.Y, w + m.X, h + m.Y);
                                layoutEngine.CreateBox(ref box);
                                if (ai.HardHorizontalAlignment.HasValue || ai.HardVerticalAlignment.HasValue)
                                    ;
                                else
                                    layoutEngine.Advance(w, h, ai.DoNotAdjustLineSpacing, false);
                            } else {
                                layoutEngine.Advance(w, h, ai.DoNotAdjustLineSpacing, false);
                            }
                            referencedImages.Add(ref ai);
                        } else {
                            referencedImages.Add(ref ai);
                        }
                    } else if (commandMode && bracketed.Value.Contains(":")) {
                        foreach (var rule in RichText.ParseRules(bracketed.Value, ref parseErrors)) {
                            var value = rule.Value;
                            RuleNameTable.TryGetValue(rule.Key, out var ruleId);
                            switch (ruleId) {
                                case RichRuleId.Color:
                                    layoutEngine.overrideColor = ParseColor(value) ?? state.InitialColor;
                                    break;
                                case RichRuleId.Scale:
                                    if (!value.TryParse(out float newScale))
                                        layoutEngine.scale = state.InitialScale;
                                    else
                                        layoutEngine.scale = state.InitialScale * newScale;
                                    break;
                                case RichRuleId.Spacing:
                                    if (!value.TryParse(out float newSpacing))
                                        layoutEngine.spacing = state.InitialSpacing;
                                    else
                                        layoutEngine.spacing = state.InitialSpacing * newSpacing;
                                    break;
                                case RichRuleId.LineSpacing:
                                    if (!value.TryParse(out float newLineSpacing))
                                        layoutEngine.additionalLineSpacing = state.InitialLineSpacing;
                                    else
                                        layoutEngine.additionalLineSpacing = newLineSpacing;
                                    break;
                                case RichRuleId.Font:
                                    if ((GlyphSources != null) && GlyphSources.TryGetValue(value, out var gse))
                                        state.GlyphSource = gse.Value;
                                    else
                                        state.GlyphSource = null;
                                    break;
                                default:
                                    parseErrors.Add(new RichParseError {
                                        Offset = rule.Key.Offset,
                                        Message = "Unrecognized rule",
                                        Text = rule.Key
                                    });
                                    break;
                            }
                        }
                    } else if (!commandMode) {
                        AbstractString astr = bracketed.Value;
                        AbstractString id = default;
                        int pipeIndex = astr.IndexOf('|');
                        if (pipeIndex >= 0) {
                            id = astr.Substring(0, pipeIndex);
                            bracketed = astr.Substring(pipeIndex + 1).AsImmutable(true);
                            astr = bracketed.Value;
                        }

                        var action = MarkedStringAction.Default;
                        // HACK: The string processor may mess with layout state, so we want to restore it after
                        var markedState = new RichTextLayoutState(ref layoutEngine, state.DefaultGlyphSource) {
                            GlyphSource = state.GlyphSource
                        };
                        markedState.Tags.AddRange(ref Tags);
                        try {
                            if (MarkedStringProcessor != null)
                                action = MarkedStringProcessor(ref astr, ref id, ref markedState, ref layoutEngine);

                            if (action == MarkedStringAction.Error)
                                parseErrors.Add(new RichParseError {
                                    Message = "Processing failed",
                                    Offset = bracketed.Offset,
                                    Text = bracketed.Value,
                                });

                            if (action != MarkedStringAction.Omit) {
                                var l = astr.Length;
                                // FIXME: Omit this too?
                                if (state.MarkedStrings == null)
                                    state.MarkedStrings = new List<AbstractString>();
                                state.MarkedStrings.Add(bracketed.Value);
                                if (action == MarkedStringAction.RichText)
                                    AppendRichRange(ref layoutEngine, ref state, astr, overrideSuppress, ref referencedImages, ref parseErrors);
                                else if (action != MarkedStringAction.PlainText) {
                                    var initialIndex = layoutEngine.currentCharacterIndex;
                                    var m = new LayoutMarker(initialIndex, initialIndex + l - 1) {
                                        MarkedString = bracketed.Value,
                                        MarkedID = id,
                                        MarkedStringActualText = astr
                                    };
                                    layoutEngine.Markers.Add(m);

                                    AppendPlainRange(ref layoutEngine, markedState.GlyphSource ?? state.DefaultGlyphSource, astr, 0, l, overrideSuppress);
                                } else {
                                    AppendPlainRange(ref layoutEngine, markedState.GlyphSource ?? state.DefaultGlyphSource, astr, 0, l, overrideSuppress);
                                }
                            }

                            if (MarkedStringProcessor != null)
                                markedState.Reset(ref layoutEngine);
                        } finally {
                            markedState.Dispose();
                        }
                    } else {
                        var close = (next == '[') ? ']' : ')';
                        layoutEngine.AppendText(state.GlyphSource ?? state.DefaultGlyphSource, "<invalid: $" + next + bracketed + close + ">");
                    }
                }
            }

            AppendPlainRange(ref layoutEngine, state.GlyphSource ?? state.DefaultGlyphSource, text, currentRangeStart, count, overrideSuppress);
        }

        /// <returns>a list of rich images that were referenced</returns>
        public DenseList<AsyncRichImage> Append (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, AbstractString text, 
            string styleName, bool? overrideSuppress = null
        ) {
            var referencedImages = new DenseList<AsyncRichImage>();
            var parseErrors = new DenseList<RichParseError>();

            try {
                styleName = styleName ?? DefaultStyle;
                if (!string.IsNullOrWhiteSpace(styleName) && Styles.TryGetValue(styleName, out RichStyle defaultStyle))
                    ApplyStyle(ref layoutEngine, ref state, in defaultStyle);

                AppendRichRange(ref layoutEngine, ref state, text, overrideSuppress, ref referencedImages, ref parseErrors);

                if (OnParseError != null)
                    foreach (var pe in parseErrors)
                        OnParseError(this, pe);
            } finally {
                state.Reset(ref layoutEngine);
            }

            return referencedImages;
        }

        private static void ApplyStyle (
            ref StringLayoutEngine layoutEngine, ref RichTextLayoutState state, in RichStyle style
        ) {
            state.GlyphSource = style.GlyphSource ?? state.GlyphSource;
            layoutEngine.overrideColor = style.Color ?? layoutEngine.overrideColor;
            layoutEngine.scale = style.Scale * state.InitialScale ?? state.InitialScale;
            layoutEngine.spacing = style.Spacing ?? state.InitialSpacing;
            layoutEngine.additionalLineSpacing = style.AdditionalLineSpacing ?? state.InitialLineSpacing;
            if (style.Apply != null)
                style.Apply(in style, ref layoutEngine, ref state);
        }

        private void AppendImage (ref StringLayoutEngine layoutEngine, RichImage image) {
            layoutEngine.AppendImage(
                image.Texture.Instance, scale: image.Scale, 
                verticalAlignment: image.VerticalAlignment,
                margin: image.Margin,
                textureRegion: image.Bounds ?? Bounds.Unit,
                doNotAdjustLineSpacing: image.DoNotAdjustLineSpacing, createBox: image.CreateBox, 
                hardXAlignment: image.HardHorizontalAlignment, hardYAlignment: image.HardVerticalAlignment,
                overrideWidth: image.OverrideWidth, overrideHeight: image.OverrideHeight,
                maxWidthPercent: image.MaxWidthPercent, clear: image.Clear
            );
        }

        private void AppendPlainRange (
            ref StringLayoutEngine layoutEngine, IGlyphSource glyphSource, AbstractString text,
            int rangeStart, int rangeEnd, bool? overrideSuppress
        ) {
            if (rangeEnd <= rangeStart)
                return;
            layoutEngine.AppendText(glyphSource, text, start: rangeStart, end: rangeEnd, overrideSuppress: overrideSuppress);
        }

        private ImmutableAbstractString ParseBracketedText (AbstractString text, ref int i, ref int currentRangeStart, HashSet<char> terminators, char close) {
            var count = text.Length;
            var start = i + 2;
            i = start;
            while (i < count) {
                var ch = text[i];
                if (ch == close) {
                    currentRangeStart = i + 1;
                    // HACK: Say the value is immutable since we're only using this temporarily.
                    // This avoids an allocation.
                    return text.Substring(start, i - start).AsImmutable(true);
                } else if (terminators.Contains(ch) || (ch < ' ')) {
                    i = start;
                    return default;
                }

                i++;
            }
            return default;
        }

        private static Dictionary<K, V> CloneDictionary<K, V> (bool deep, Dictionary<K, V> value) {
            if (deep == false)
                return value;
            else if (value == null)
                return value;

            var result = new Dictionary<K, V>(value.Count, value.Comparer);
            foreach (var kvp in value)
                result.Add(kvp.Key, kvp.Value);
            return result;
        }

        private static GlyphSourceCollection CloneDictionary (bool deep, GlyphSourceCollection value) {
            if (deep == false)
                return value;
            else if (value == null)
                return value;

            var result = new GlyphSourceCollection(value.Count, value.IgnoreCase);
            foreach (var kvp in value)
                result.Add(kvp.Key, kvp.Value);
            return result;
        }

        private static ImmutableAbstractStringLookup<V> CloneDictionary<V> (bool deep, ImmutableAbstractStringLookup<V> value) {
            if (deep == false)
                return value;
            else if (value == null)
                return value;

            var result = new ImmutableAbstractStringLookup<V>(value.Count, value.IgnoreCase);
            foreach (var kvp in value)
                result.Add(kvp.Key, kvp.Value);
            return result;
        }

        public RichTextConfiguration Clone (bool deep) {
            var result = new RichTextConfiguration {
                NamedColors = CloneDictionary(deep, NamedColors),
                GlyphSources = CloneDictionary(deep, GlyphSources),
                Styles = CloneDictionary(deep, Styles),
                Images = CloneDictionary(deep, Images),
                ImageProvider = ImageProvider,
                MarkedStringProcessor = MarkedStringProcessor,
                DefaultStyle = DefaultStyle,
                ColorMode = ColorMode,
                Version = Version + 1
            };
            if (OnParseError != null)
                result.OnParseError += OnParseError;
            return result;
        }

        public void Invalidate () {
            Version++;
        }

        public override int GetHashCode () {
            return Version;
        }

        public bool Equals (RichTextConfiguration other) {
            if (ReferenceEquals(this, other))
                return true;

            return (NamedColors == other.NamedColors) &&
                (GlyphSources == other.GlyphSources) &&
                (Styles == other.Styles) &&
                (Images == other.Images) &&
                (MarkedStringProcessor == other.MarkedStringProcessor) &&
                Tags.SequenceEqual(ref other.Tags) &&
                (DefaultStyle == other.DefaultStyle) &&
                (ColorMode == other.ColorMode) &&
                (DisableImages == other.DisableImages) &&
                (ImageProvider == other.ImageProvider);
        }

        public override bool Equals (object obj) {
            if (obj is RichTextConfiguration rtc)
                return Equals(rtc);
            else
                return false;
        }
    }
}

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
using TLayoutEngine = Squared.Render.TextLayout2.StringLayoutEngine2;

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
                    if ((next == '(') || (next == '[') || (next == '<')) {
                        closer = (next == '(') 
                            ? ')' 
                            : (
                                (next == '<')
                                    ? '>'
                                    : ']'
                            );
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
                    if ((next == '(') || (next == '[') || (next == '<')) {
                        closer = (next == '(') 
                            ? ')' 
                            : (
                                (next == '<')
                                    ? '>'
                                    : ']'
                            );
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
                if (
                    (c == '$') && (
                        (richText[i + 1] == '[') || 
                        (richText[i + 1] == '(') ||
                        (richText[i + 1] == '<')
                    )
                )
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
                if (
                    (c == '$') && (
                        (richText[i + 1] == '[') || 
                        (richText[i + 1] == '(') ||
                        (richText[i + 1] == '<')
                    )
                )
                    return PlainTextLength_Slow(richText, i, true);
            }

            return richText.Length;
        }

        public static DenseList<RichProperty> ParseProperties (AbstractString text, ref DenseList<RichParseError> parseErrors) {
            var result = new DenseList<RichProperty>();
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
                            var property = new RichProperty {
                                Offset = i,
                                Key = text.Substring(keyStart, keyEnd.Value - keyStart),
                                Value = text.Substring(valueStart.Value, i - valueStart.Value),
                            };
                            result.Add(property);
                            keyStart = i + 1;
                            keyEnd = valueStart = null;
                        }
                        break;
                }
            }
            return result;
        }
    }

    public delegate void RichStyleApplier (in RichStyle style, ref TLayoutEngine layoutEngine, ref RichTextLayoutState state);

    public struct RichParseError {
        public AbstractString Text;
        public int Offset;
        public string Message;

        public override string ToString () {
            return $"'{Message}' at {Offset}";
        }
    }

    public struct RichProperty {
        // NOTE: Not considered by Equals or GetHashCode.
        public int Offset;
        public AbstractString Key;
        public AbstractString Value;

        public bool Equals (RichProperty rhs) {
            return Key.TextEquals(rhs.Key, StringComparison.Ordinal) &&
                Value.TextEquals(rhs.Value, StringComparison.Ordinal);
        }

        public override int GetHashCode () {
            return Key.GetHashCode() ^ Value.GetHashCode();
        }

        public override bool Equals (object obj) {
            if (obj is RichProperty rr)
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
        public float? Width, Height, MaxWidthPercent, BaselineAlignment;
        public Vector2 Margin;
        public ImageHorizontalAlignment HorizontalAlignment;
        public float Scale;
        public bool DoNotAdjustLineSpacing;
        public readonly bool Dead;

        public AsyncRichImage (bool dead) : this() {
            if (!dead)
                throw new ArgumentException();
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
            HorizontalAlignment = img.HorizontalAlignment;
            BaselineAlignment = img.BaselineAlignment;
            Dead = false;
            MaxWidthPercent = null;
        }

        public AsyncRichImage (RichImage img)
            : this(ref img) {
        }

        public AsyncRichImage (
            Future<Texture2D> f
        ) : this() {
            if (f == null)
                throw new ArgumentNullException("f");
            Future = f;
            Dead = false;
            Value = null;
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
                    HorizontalAlignment = HorizontalAlignment,
                    BaselineAlignment = BaselineAlignment,
                    DoNotAdjustLineSpacing = DoNotAdjustLineSpacing,
                    Scale = scale,
                    Margin = Margin,
                    OverrideHeight = Height * Scale,
                    OverrideWidth = Width * Scale,
                    MaxWidthPercent = MaxWidthPercent
                };
                return true;
            }
        }

        public bool HasValue => Value.HasValue || (Future?.Completed ?? false);
        public bool IsInitialized => (Future != null) || Value.HasValue || Dead;
    }

    public enum ImageHorizontalAlignment : byte {
        Inline = 0,
        Left,
        Right,
    }

    public struct RichImage {
        public AbstractTextureReference Texture;
        public Bounds? Bounds;
        public Vector2 Margin;
        public ImageHorizontalAlignment HorizontalAlignment;
        public float? OverrideWidth, OverrideHeight, MaxWidthPercent, BaselineAlignment;
        public bool DoNotAdjustLineSpacing;

        private float ScaleMinusOne;

        public readonly bool Dead;

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

        public readonly RichTextConfiguration Configuration;
        public IGlyphSource DefaultGlyphSource, GlyphSource;
        public readonly Color InitialColor;
        public readonly Vector2 InitialScale;
        public readonly Vector2 InitialSpacing;
        public readonly float InitialLineSpacing;
        public readonly bool InitialOverrideColor, InitialCharacterWrap, InitialWordWrap;
        public DenseList<string> Tags;
        public IRichTextStateTracker Tracker;
        public object UserData;
        private DenseList<StringBuilder> StringBuildersToReturn;

        public RichTextLayoutState (
            RichTextConfiguration configuration, ref TLayoutEngine engine, 
            IGlyphSource defaultGlyphSource, IRichTextStateTracker stateTracker = null
        ) {
            Configuration = configuration;
            InitialOverrideColor = engine.OverrideColor;
            InitialColor = engine.MultiplyColor;
            InitialScale = engine.Scale;
            InitialSpacing = engine.Spacing;
            InitialLineSpacing = engine.AdditionalLineSpacing;
            InitialWordWrap = engine.WordWrap;
            InitialCharacterWrap = engine.CharacterWrap;
            DefaultGlyphSource = defaultGlyphSource;
            GlyphSource = null;
            Tracker = stateTracker;
            Tags = default;
            UserData = null;
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

        public void Reset (ref TLayoutEngine engine) {
            GlyphSource = null;
            engine.OverrideColor = InitialOverrideColor;
            engine.MultiplyColor = InitialColor;
            engine.Scale = InitialScale;
            engine.Spacing = InitialSpacing;
            engine.AdditionalLineSpacing = InitialLineSpacing;
            engine.WordWrap = InitialWordWrap;
            engine.CharacterWrap = InitialCharacterWrap;
            engine.Alignment = engine.DefaultAlignment;
            // FIXME: Are there cases where we shouldn't do this?
            Tracker?.ResetStyle(ref this, ref engine);
        }

        public void Dispose () {
            var sbs = ScratchStringBuilders.Value;
            lock (sbs)
                foreach (var sb in StringBuildersToReturn)
                    sbs.Add(sb);
            StringBuildersToReturn.Clear();
        }

        public void AppendPlainText (ref TLayoutEngine layoutEngine, AbstractString text) {
            layoutEngine.AppendText(GlyphSource ?? DefaultGlyphSource, text);
        }
    }

    public enum MarkedStringAction {
        /// <summary>
        /// Lay out the text as normal and generate a marker
        /// </summary>
        Default = 0b0,
        /// <summary>
        /// Lay out the text as plain text and do not generate a marker
        /// </summary>
        PlainText = 0b1,
        /// <summary>
        /// Do not lay out the text
        /// </summary>
        Omit = 0b10,
        /// <summary>
        /// The processor generated new rich text, so process that (gross)
        /// </summary>
        RichText = 0b100,
        /// <summary>
        /// The processor failed to process the text
        /// </summary>
        Error = 0b1000,
    }

    public enum RichCommandResult {
        /// <summary>
        /// This command was not handled, perform default handling
        /// </summary>
        NotHandled = 0b0,
        /// <summary>
        /// This command was handled, don't perform default handling
        /// </summary>
        Handled = 0b1,
        /// <summary>
        /// Halt the current append operation
        /// </summary>
        Halt = 0b10,
        /// <summary>
        /// This command was not handled successfully, record an error
        /// </summary>
        Error = 0b100,
    }

    public enum RichStyleResult {
        /// <summary>
        /// This style attribute was not handled, perform default handling
        /// </summary>
        NotHandled = 0b0,
        /// <summary>
        /// This style attribute was handled, don't perform default handling
        /// </summary>
        Handled = 0b1,
        /// <summary>
        /// This style attribute was not handled successfully, record an error
        /// </summary>
        Error = 0b10,
    }

    public struct MarkedStringConfiguration {
        public MarkedStringAction Action;
        public AbstractString RubyText;
        public bool Unmarked;

        public MarkedStringConfiguration (MarkedStringAction action, bool unmarked = false) 
            : this () {
            Action = action;
            Unmarked = unmarked;
        }

        public static MarkedStringConfiguration Ruby (string text) =>
            new MarkedStringConfiguration {
                Action = MarkedStringAction.PlainText,
                RubyText = text,
                Unmarked = true,
            };

        public static implicit operator MarkedStringConfiguration (MarkedStringAction action) =>
            new MarkedStringConfiguration { Action = action };
    }

    /// <summary>
    /// Prepares a layout engine to display a marked string (i.e. '$(text)' or '$(id|text)'), and potentially changes its display text.
    /// </summary>
    /// <param name="text">The display text for the marked string. You can modify this to change the text.</param>
    /// <param name="id">The identifier for the string, if any. i.e. $(identifier|text)</param>
    /// <param name="state">A state snapshot that can be used to examine or restore the current state of the layout engine.</param>
    /// <param name="layoutEngine">The layout engine that will be used to append the text (after this method returns).</param>
    /// <returns>true if the string should be laid out, false if it should be omitted from the output entirely.</returns>
    public delegate MarkedStringConfiguration MarkedStringProcessor (
        ref AbstractString text, ref AbstractString id, 
        ref RichTextLayoutState state, ref TLayoutEngine layoutEngine
    );

    /// <summary>
    /// Processes a command, i.e. '$<x:y,z:w>'
    /// </summary>
    /// <param name="command">the command text</param>
    public delegate RichCommandResult RichCommandProcessor (
        AbstractString command, ref RichTextLayoutState layoutState, ref TLayoutEngine layoutEngine
    );

    public interface IRichTextStateTracker {
        RichCommandResult TryProcessCommand (AbstractString value, ref RichTextLayoutState state, ref TLayoutEngine layoutEngine);
        void MarkString (RichTextConfiguration config, AbstractString originalText, AbstractString text, AbstractString id, uint spanIndex);
        void ReferencedImage (RichTextConfiguration config, ref AsyncRichImage image);
        void ResetStyle (ref RichTextLayoutState state, ref TLayoutEngine layoutEngine);
        RichStyleResult TryApplyStyleProperty (ref RichTextLayoutState state, ref TLayoutEngine layoutEngine, RichProperty rule);
    }

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
        public IGlyphSource DefaultRubyGlyphSource;
        public float DefaultRubyScale = 0.66f;
        public ImmutableAbstractStringLookup<Color> NamedColors;
        public ImmutableAbstractStringLookup<RichStyle> Styles;
        public ImmutableAbstractStringLookup<RichImage> Images;
        public RichCommandProcessor CommandProcessor;
        public Func<AbstractString, RichTextConfiguration, AsyncRichImage> ImageProvider;
        public MarkedStringProcessor MarkedStringProcessor;
        public ColorConversionMode ColorMode;
        /// <summary>
        /// If set, any images referenced by rich text will not be inserted.
        /// </summary>
        public bool DisableImages;
        /// <summary>
        /// If set, unhandled commands will not generate a parse error.
        /// </summary>
        public bool IgnoreUnhandledCommands;
        /// <summary>
        /// Contains user-provided data that can be used by your ImageProvider or MarkedStringProcessor.
        /// </summary>
        public DenseList<string> Tags;
        public Vector4? ImageUserData;
        public string DefaultStyle;
        public object UserData;

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
                else if (pSRGBColor.TryParse(text, out pSRGBColor pSRGB, null)) {
                    result = ColorMode == ColorConversionMode.LinearToSRGB ? pSRGB.ToColor() : pSRGB.ToLinearColor();
                } else 
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
        public void Append (
            ref TLayoutEngine layoutEngine, IGlyphSource defaultGlyphSource, AbstractString text, 
            string styleName, IRichTextStateTracker stateTracker = null
        ) {
            var state = new RichTextLayoutState(this, ref layoutEngine, defaultGlyphSource, stateTracker);
            state.Tags.AddRange(ref Tags);
            try {
                Append(ref layoutEngine, ref state, text, styleName);
            } finally {
                state.Dispose();
            }
        }

        // FIXME: These all seem wrong
        private static readonly HashSet<char>
            StyleTerminators = new HashSet<char> { '\"', '\'', '$', '[' },
            InsertionTerminators = new HashSet<char> { '\"', '\'', '$', '<' },
            StringTerminators = new HashSet<char> { '$', '(' };

        private enum RichPropertyId : int {
            Unknown,
            Color,
            Scale,
            Spacing,
            Font,
            LineSpacing,
            WordWrap,
            CharacterWrap,
            Alignment,
        }

        private static readonly ImmutableAbstractStringLookup<RichPropertyId> PropertyNameTable =
            new ImmutableAbstractStringLookup<RichPropertyId>(true) {
                { "color", RichPropertyId.Color },
                { "c", RichPropertyId.Color },
                { "scale", RichPropertyId.Scale },
                { "sc", RichPropertyId.Scale },
                { "spacing", RichPropertyId.Spacing },
                { "sp", RichPropertyId.Spacing },
                { "linespacing", RichPropertyId.LineSpacing },
                { "ls", RichPropertyId.LineSpacing },
                { "line-spacing", RichPropertyId.LineSpacing },
                { "font", RichPropertyId.Font },
                { "f", RichPropertyId.Font },
                { "glyphsource", RichPropertyId.Font },
                { "gs", RichPropertyId.Font },
                { "glyph-source", RichPropertyId.Font },
                { "word-wrap", RichPropertyId.WordWrap },
                { "wordwrap", RichPropertyId.WordWrap },
                { "character-wrap", RichPropertyId.CharacterWrap },
                { "characterwrap", RichPropertyId.CharacterWrap },
                { "align", RichPropertyId.Alignment },
                { "alignment", RichPropertyId.Alignment },
            };

        private void AppendRichRange (
            ref TLayoutEngine layoutEngine, ref RichTextLayoutState state, AbstractString text, 
            ref DenseList<RichParseError> parseErrors, bool rubyAnchor
        ) {
            int currentRangeStart = 0;
            RichStyle style;
            RichImage image;
            AsyncRichImage ai;

            var count = text.Length;
            for (int i = 0; i < count; i++) {
                var ch = text[i];
                var next = (i < count - 2) ? text[i + 1] : '\0';
                if ((ch == '$') && ((next == '[') || (next == '(') || (next == '<'))) {
                    AppendPlainRange(ref layoutEngine, state.GlyphSource ?? state.DefaultGlyphSource, text, currentRangeStart, i, rubyAnchor);
                    bool styleMode = next == '[',
                        insertionMode = next == '<';
                    var closer = (next == '(') 
                        ? ')' 
                        : (
                            (next == '<')
                                ? '>'
                                : ']'
                        );
                    var bracketed = ParseBracketedText(
                        text, ref i, ref currentRangeStart, 
                        styleMode 
                            ? StyleTerminators 
                            : (insertionMode
                                ? InsertionTerminators
                                : StringTerminators
                            ), 
                        closer
                    );
                    if (styleMode && bracketed.Value.IsNullOrEmpty) {
                        state.Reset(ref layoutEngine);
                    } else if (
                        styleMode && (Styles != null) && 
                        bracketed.Value.StartsWith(".") && 
                        Styles.TryGetValue(bracketed.Value.Substring(1).AsImmutable(true), out style)
                    ) {
                        ApplyStyle(ref layoutEngine, ref state, in style);
                    } else if (
                        insertionMode && 
                        TryProcessCommand(bracketed.Value, ref state, ref layoutEngine, out var commandResult)
                    ) {
                        switch (commandResult) {
                            case RichCommandResult.NotHandled:
                                if (!IgnoreUnhandledCommands)
                                    parseErrors.Add(new RichParseError {
                                        Offset = bracketed.Offset,
                                        Message = "Failed to process command",
                                        Text = bracketed.Value
                                    });
                                break;
                            case RichCommandResult.Handled:
                                break;
                            case RichCommandResult.Error:
                                parseErrors.Add(new RichParseError {
                                    Offset = bracketed.Offset,
                                    Message = "Failed to process command",
                                    Text = bracketed.Value
                                });
                                break;
                            case RichCommandResult.Halt:
                                return;
                        }
                    } else if (insertionMode && (Images != null) && Images.TryGetValue(bracketed, out image)) {
                        if (!DisableImages)
                            AppendImage(ref layoutEngine, image);
                        else
                            parseErrors.Add(new RichParseError {
                                Offset = bracketed.Offset,
                                Message = "Images are disabled",
                                Text = bracketed.Value
                            });

                        ai = new AsyncRichImage(ref image);
                        state.Tracker?.ReferencedImage(this, ref ai);
                    } else if (
                        insertionMode && (ImageProvider != null) && 
                        (ai = ImageProvider(bracketed.Value, this)).IsInitialized
                    ) {
                        // layoutEngine.ComputeConstrainedSize(out var constrainedSize);
                        if (ai.Dead) {
                            state.Tracker?.ReferencedImage(this, ref ai);
                        } else if (DisableImages) {
                            state.Tracker?.ReferencedImage(this, ref ai);
                            parseErrors.Add(new RichParseError {
                                Offset = bracketed.Offset,
                                Message = "Images are disabled",
                                Text = bracketed.Value
                            });
                        } else if (ai.TryGetValue(out RichImage ri)) {
                            AppendImage(ref layoutEngine, ri);
                            state.Tracker?.ReferencedImage(this, ref ai);
                        } else if (ai.Width.HasValue) {
                            // Missing image of explicit size
                            var w = ai.Width.Value;
                            var h = (ai.Height ?? 0);
                            if (ai.MaxWidthPercent.HasValue && (w > 0)) {
                                var mw = ai.MaxWidthPercent.Value * Math.Max(layoutEngine.MaximumWidth, layoutEngine.DesiredWidth) / 100f;
                                float ratio = Math.Min(w, mw) / w;
                                w *= ratio;
                                h *= ratio;
                            }
                            layoutEngine.CreateEmptyBox(w, h, ai.Margin, ai.HorizontalAlignment, ai.DoNotAdjustLineSpacing);
                            state.Tracker?.ReferencedImage(this, ref ai);
                        } else {
                            state.Tracker?.ReferencedImage(this, ref ai);
                        }
                    } else if (insertionMode) {
                        if (!IgnoreUnhandledCommands)
                            parseErrors.Add(new RichParseError {
                                Offset = bracketed.Offset,
                                Message = "Failed to process command",
                                Text = bracketed.Value
                            });
                        // HACK: Eat unhandled commands, don't inject them as text
                    } else if (styleMode && bracketed.Value.Contains(":")) {
                        foreach (var rule in RichText.ParseProperties(bracketed.Value, ref parseErrors))
                            ApplyStyleProperty(ref layoutEngine, ref state, ref parseErrors, rule);
                    } else if (!styleMode) {
                        AbstractString astr = bracketed.Value;
                        AbstractString id = default;
                        int pipeIndex = astr.IndexOf('|');
                        if (pipeIndex >= 0) {
                            id = astr.Substring(0, pipeIndex);
                            bracketed = astr.Substring(pipeIndex + 1).AsImmutable(true);
                            astr = bracketed.Value;
                        }

                        var config = default(MarkedStringConfiguration);
                        // HACK: The string processor may mess with layout state, so we want to restore it after
                        var markedState = new RichTextLayoutState(this, ref layoutEngine, state.DefaultGlyphSource, state.Tracker) {
                            GlyphSource = state.GlyphSource,
                            UserData = state.UserData
                        };
                        markedState.Tags.AddRange(ref Tags);
                        try {
                            ref var span = ref layoutEngine.BeginSpan(true);
                            if (MarkedStringProcessor != null)
                                config = MarkedStringProcessor(ref astr, ref id, ref markedState, ref layoutEngine);

                            if (!config.RubyText.IsNullOrWhiteSpace) {
                                if (rubyAnchor)
                                    throw new InvalidOperationException("Ruby text cannot have its own ruby text");
                                else
                                    layoutEngine.PushRubyText(
                                        DefaultRubyGlyphSource ?? state.GlyphSource ?? state.DefaultGlyphSource, 
                                        config.RubyText, DefaultRubyScale
                                    );
                            }

                            if (config.Action == MarkedStringAction.Error)
                                parseErrors.Add(new RichParseError {
                                    Message = "Processing failed",
                                    Offset = bracketed.Offset,
                                    Text = bracketed.Value,
                                });
                            else if (config.Action != MarkedStringAction.Omit) {
                                var l = astr.Length;
                                if (!config.Unmarked)
                                    state.Tracker?.MarkString(this, bracketed.Value, astr, id, span.Index);

                                if (config.Action == MarkedStringAction.RichText)
                                    AppendRichRange(ref layoutEngine, ref state, astr, ref parseErrors, rubyAnchor || !config.RubyText.IsNullOrWhiteSpace);
                                else if (config.Action != MarkedStringAction.PlainText) {
                                    // FIXME
                                    /*
                                    var initialIndex = layoutEngine.currentCharacterIndex;
                                    var m = new LayoutMarker(initialIndex, initialIndex + l - 1) {
                                        MarkedString = bracketed.Value,
                                        MarkedID = id,
                                        MarkedStringActualText = astr
                                    };
                                    layoutEngine.Markers.Add(m);
                                    */

                                    AppendPlainRange(ref layoutEngine, markedState.GlyphSource ?? state.DefaultGlyphSource, astr, 0, l, rubyAnchor || !config.RubyText.IsNullOrWhiteSpace);
                                } else {
                                    AppendPlainRange(ref layoutEngine, markedState.GlyphSource ?? state.DefaultGlyphSource, astr, 0, l, rubyAnchor || !config.RubyText.IsNullOrWhiteSpace);
                                }
                            }

                            if (MarkedStringProcessor != null)
                                markedState.Reset(ref layoutEngine);

                            layoutEngine.EndSpanByIndex(span.Index);
                        } finally {
                            markedState.Dispose();
                        }
                    } else {
                        layoutEngine.AppendText(state.GlyphSource ?? state.DefaultGlyphSource, "<invalid: $" + next + bracketed + closer + ">");
                    }
                }
            }

            AppendPlainRange(ref layoutEngine, state.GlyphSource ?? state.DefaultGlyphSource, text, currentRangeStart, count, false);
        }

        public void ApplyStyleProperty (ref TLayoutEngine layoutEngine, ref RichTextLayoutState state, ref DenseList<RichParseError> parseErrors, RichProperty rule) {
            var value = rule.Value;
            PropertyNameTable.TryGetValue(rule.Key, out var ruleId);

            var trackerResult = state.Tracker?.TryApplyStyleProperty(ref state, ref layoutEngine, rule);
            if (trackerResult == RichStyleResult.Handled)
                return;
            else if (trackerResult == RichStyleResult.Error) {
                parseErrors.Add(new RichParseError {
                    Offset = rule.Key.Offset,
                    Message = "Error in property",
                    Text = rule.Key
                });
                return;
            }

            switch (ruleId) {
                case RichPropertyId.Color:
                    if (value.IsNullOrEmpty || value.TextEquals("default", StringComparison.OrdinalIgnoreCase)) {
                        layoutEngine.OverrideColor = state.InitialOverrideColor;
                        layoutEngine.MultiplyColor = state.InitialColor;
                    } else {
                        layoutEngine.OverrideColor = true;
                        layoutEngine.MultiplyColor = ParseColor(value) ?? state.InitialColor;
                    }
                    break;
                case RichPropertyId.Scale:
                    if (!value.TryParse(out float newScale))
                        layoutEngine.Scale = state.InitialScale;
                    else
                        layoutEngine.Scale = state.InitialScale * newScale;
                    break;
                case RichPropertyId.Spacing:
                    if (!value.TryParse(out float newSpacing))
                        layoutEngine.Spacing = state.InitialSpacing;
                    else
                        layoutEngine.Spacing = state.InitialSpacing * newSpacing;
                    break;
                case RichPropertyId.LineSpacing:
                    if (!value.TryParse(out float newLineSpacing))
                        layoutEngine.AdditionalLineSpacing = state.InitialLineSpacing;
                    else
                        layoutEngine.AdditionalLineSpacing = newLineSpacing;
                    break;
                case RichPropertyId.Font:
                    if ((GlyphSources != null) && GlyphSources.TryGetValue(value, out var gse))
                        state.GlyphSource = gse.Value;
                    else
                        state.GlyphSource = null;
                    break;
                case RichPropertyId.WordWrap:
                case RichPropertyId.CharacterWrap:
                    if (value.TryParse(out bool b)) {
                        if (ruleId == RichPropertyId.WordWrap)
                            layoutEngine.WordWrap = b;
                        else
                            layoutEngine.CharacterWrap = b;
                    } else {
                        if (ruleId == RichPropertyId.WordWrap)
                            layoutEngine.WordWrap = state.InitialWordWrap;
                        else
                            layoutEngine.CharacterWrap = state.InitialCharacterWrap;
                    }
                    break;
                case RichPropertyId.Alignment:
                    if (value.TryParseEnum(out HorizontalAlignment alignment)) {
                        layoutEngine.Alignment = alignment;
                    } else {
                        layoutEngine.Alignment = layoutEngine.DefaultAlignment;
                    }
                    break;
                default:
                    parseErrors.Add(new RichParseError {
                        Offset = rule.Key.Offset,
                        Message = "Unrecognized property",
                        Text = rule.Key
                    });
                    break;
            }
        }

        private bool TryProcessCommand (AbstractString value, ref RichTextLayoutState state, ref TLayoutEngine layoutEngine, out RichCommandResult commandResult) {
            commandResult = RichCommandResult.NotHandled;

            var trackerResult = state.Tracker?.TryProcessCommand(value, ref state, ref layoutEngine);
            if (trackerResult.HasValue && (trackerResult != RichCommandResult.NotHandled)) {
                commandResult = trackerResult.Value;
            } else if (CommandProcessor != null) {
                commandResult = CommandProcessor(value, ref state, ref layoutEngine);
            } else {
                ;
            }

            return commandResult != RichCommandResult.NotHandled;
        }

        /// <returns>a list of rich images that were referenced</returns>
        public void Append (
            ref TLayoutEngine layoutEngine, ref RichTextLayoutState state, AbstractString text, string styleName
        ) {
            var parseErrors = new DenseList<RichParseError>();

            try {
                styleName = styleName ?? DefaultStyle;
                if (!string.IsNullOrWhiteSpace(styleName) && Styles.TryGetValue(styleName, out RichStyle defaultStyle))
                    ApplyStyle(ref layoutEngine, ref state, in defaultStyle);

                AppendRichRange(ref layoutEngine, ref state, text, ref parseErrors, false);

                if (OnParseError != null)
                    foreach (var pe in parseErrors)
                        OnParseError(this, pe);
            } finally {
                state.Reset(ref layoutEngine);
            }
        }

        private static void ApplyStyle (
            ref TLayoutEngine layoutEngine, ref RichTextLayoutState state, in RichStyle style
        ) {
            state.GlyphSource = style.GlyphSource ?? state.GlyphSource;
            layoutEngine.OverrideColor = style.Color.HasValue;
            layoutEngine.MultiplyColor = style.Color ?? layoutEngine.MultiplyColor;
            layoutEngine.Scale = style.Scale * state.InitialScale ?? state.InitialScale;
            layoutEngine.Spacing = style.Spacing.HasValue
                ? new Vector2(style.Spacing.Value) 
                : state.InitialSpacing;
            layoutEngine.AdditionalLineSpacing = style.AdditionalLineSpacing ?? state.InitialLineSpacing;
            if (style.Apply != null)
                style.Apply(in style, ref layoutEngine, ref state);
        }

        private void AppendImage (ref TLayoutEngine layoutEngine, RichImage image) {
            layoutEngine.AppendImage(ref image);
        }

        private void AppendPlainRange (
            ref TLayoutEngine layoutEngine, IGlyphSource glyphSource, AbstractString text,
            int rangeStart, int rangeEnd, bool rubyAnchor
        ) {
            if (rangeEnd <= rangeStart)
                return;
            var range = text.Substring(rangeStart, rangeEnd - rangeStart);
            // FIXME: overrideSuppress
            layoutEngine.AppendText(glyphSource, range, rubyAnchor ? TextLayout2.FragmentCategory.Regular : null);
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
                CommandProcessor = CommandProcessor,
                DefaultStyle = DefaultStyle,
                ColorMode = ColorMode,
                Version = Version + 1,
                Tags = Tags.Clone(),
                DefaultRubyGlyphSource = DefaultRubyGlyphSource,
                DefaultRubyScale = DefaultRubyScale,
                DisableImages = DisableImages,
                IgnoreUnhandledCommands = IgnoreUnhandledCommands,
                UserData = UserData,
                ImageUserData = ImageUserData
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Squared.Render.Evil;
using Squared.Render.Text;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace Squared.Render.Text {
    public struct AbstractString : IEquatable<AbstractString> {
        private readonly string String;
        private readonly StringBuilder StringBuilder;
        private readonly ArraySegment<char> ArraySegment;

        public AbstractString (string text) {
            String = text;
            StringBuilder = null;
            ArraySegment = default(ArraySegment<char>);
        }

        public AbstractString (StringBuilder stringBuilder) {
            String = null;
            StringBuilder = stringBuilder;
            ArraySegment = default(ArraySegment<char>);
        }

        public AbstractString (char[] array) {
            String = null;
            StringBuilder = null;
            ArraySegment = new ArraySegment<char>(array);
        }

        public AbstractString (ArraySegment<char> array) {
            String = null;
            StringBuilder = null;
            ArraySegment = array;
        }

        public static implicit operator AbstractString (string text) {
            return new AbstractString(text);
        }

        public static implicit operator AbstractString (StringBuilder stringBuilder) {
            return new AbstractString(stringBuilder);
        }

        public static implicit operator AbstractString (char[] array) {
            return new AbstractString(array);
        }

        public static implicit operator AbstractString (ArraySegment<char> array) {
            return new AbstractString(array);
        }

        public bool Equals (AbstractString other) {
            return (String == other.String) &&
                (StringBuilder == other.StringBuilder) &&
                (ArraySegment == other.ArraySegment);
        }

        public char this[int index] {
            get {
                if (String != null)
                    return String[index];
                else if (StringBuilder != null)
                    return StringBuilder[index];
                else if (ArraySegment.Array != null) {
                    if ((index <= 0) || (index >= ArraySegment.Count))
                        throw new ArgumentOutOfRangeException("index");

                    return ArraySegment.Array[index + ArraySegment.Offset];
                } else
                    throw new NullReferenceException("This string contains no text");
            }
        }

        public int Length {
            get {
                if (String != null)
                    return String.Length;
                else if (StringBuilder != null)
                    return StringBuilder.Length;
                else // Default fallback to 0 characters
                    return ArraySegment.Count;
            }
        }

        public bool IsNull {
            get {
                return
                    (String == null) &&
                    (StringBuilder == null) &&
                    (ArraySegment.Array == null);
            }
        }

        public override string ToString () {
            if (String != null)
                return String;
            else if (StringBuilder != null)
                return StringBuilder.ToString();
            else if (ArraySegment.Array != null)
                return new string(ArraySegment.Array, ArraySegment.Offset, ArraySegment.Count);
            else
                throw new NullReferenceException("This string contains no text");
        }
    }

    public class DynamicStringLayout {
        private ArraySegment<BitmapDrawCall> _Buffer; 
        private StringLayout? _CachedStringLayout;

        private Dictionary<char, KerningAdjustment> _KerningAdjustments; 
        private SpriteFont _Font;
        private AbstractString _Text;
        private Vector2 _Position = Vector2.Zero;
        private Color _Color = Color.White;
        private float _Scale = 1;
        private float _SortKey = 0;
        private int _CharacterSkipCount = 0;
        private int _CharacterLimit = int.MaxValue;
        private float _XOffsetOfFirstLine = 0;
        private float? _LineBreakAtX = null;
        private bool _WordWrap = false;
        private bool _CharacterWrap = true;
        private float _WrapIndentation = 0f;
        private bool _AlignToPixels = false;
        private char _WrapCharacter = '\0';

        public DynamicStringLayout (SpriteFont font, string text = "") {
            _Font = font;
            _Text = text;
        }

        private void InvalidatingNullableAssignment<T> (ref Nullable<T> destination, Nullable<T> newValue)
            where T : struct, IEquatable<T> {
            if (!destination.Equals(newValue)) {
                destination = newValue;
                _CachedStringLayout = null;
            }
        }

        private void InvalidatingValueAssignment<T> (ref T destination, T newValue) 
            where T : struct, IEquatable<T>
        {
            if (!destination.Equals(newValue)) {
                destination = newValue;
                _CachedStringLayout = null;
            }
        }

        private void InvalidatingReferenceAssignment<T> (ref T destination, T newValue)
            where T : class
        {
            if (destination != newValue) {
                destination = newValue;
                _CachedStringLayout = null;
            }
        }

        public ArraySegment<BitmapDrawCall> Buffer {
            get {
                return _Buffer;
            }
            set {
                _Buffer = value;
            }
        }

        public AbstractString Text {
            get {
                return _Text;
            }
            set {
                InvalidatingValueAssignment(ref _Text, value);
            }
        }

        public SpriteFont Font {
            get {
                return _Font;
            }
            set {
                InvalidatingReferenceAssignment(ref _Font, value);
            }
        }

        public Vector2 Position {
            get {
                return _Position;
            }
            set {
                InvalidatingValueAssignment(ref _Position, value);
            }
        }

        public Color Color {
            get {
                return _Color;
            }
            set {
                InvalidatingValueAssignment(ref _Color, value);
            }
        }

        public float Scale {
            get {
                return _Scale;
            }
            set {
                InvalidatingValueAssignment(ref _Scale, value);
            }
        }

        public float SortKey {
            get {
                return _SortKey;
            }
            set {
                InvalidatingValueAssignment(ref _SortKey, value);
            }
        }

        public int CharacterSkipCount {
            get {
                return _CharacterSkipCount;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterSkipCount, value);
            }
        }

        public int CharacterLimit {
            get {
                return _CharacterLimit;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterLimit, value);
            }
        }

        public float XOffsetOfFirstLine {
            get {
                return _XOffsetOfFirstLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfFirstLine, value);
            }
        }

        public float? LineBreakAtX {
            get {
                return _LineBreakAtX;
            }
            set {
                InvalidatingNullableAssignment(ref _LineBreakAtX, value);
            }
        }

        public bool WordWrap {
            get {
                return _WordWrap;
            }
            set {
                InvalidatingValueAssignment(ref _WordWrap, value);
            }
        }

        /// <summary>
        /// NOTE: Only valid if WordWrap is also true
        /// </summary>
        public bool CharacterWrap {
            get {
                return _CharacterWrap || _WordWrap;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterWrap, value);
            }
        }

        public char WrapCharacter {
            get {
                return _WrapCharacter;
            }
            set {
                InvalidatingValueAssignment(ref _WrapCharacter, value);
            }
        }

        /// <summary>
        /// NOTE: Only valid if WordWrap is also true
        /// </summary>
        public float WrapIndentation {
            get {
                return _WrapIndentation;
            }
            set {
                InvalidatingValueAssignment(ref _WrapIndentation, value);
            }
        }

        public bool AlignToPixels {
            get {
                return _AlignToPixels;
            }
            set {
                InvalidatingValueAssignment(ref _AlignToPixels, value);
            }
        }

        public Dictionary<char, KerningAdjustment> KerningAdjustments {
            get {
                return _KerningAdjustments;
            }
            set {
                InvalidatingReferenceAssignment(ref _KerningAdjustments, value);
            }
        }

        public StringLayout Get () {
            if (_Text.IsNull)
                return new StringLayout();

            if (!_CachedStringLayout.HasValue) {
                int length = _Text.Length;

                int capacity = length;
                if (_WordWrap)
                    capacity *= 2;

                ArraySegment<BitmapDrawCall> seg1;
                ArraySegment<BitmapDrawCall>? seg2 = null;

                if ((_Buffer.Array != null) && (_Buffer.Count < capacity))
                    _Buffer = default(ArraySegment<BitmapDrawCall>);

                if (_Buffer.Array == null) {
                    var newCapacity = 1 << (int)Math.Ceiling(Math.Log(capacity, 2));
                    var array = new BitmapDrawCall[newCapacity];
                    _Buffer = new ArraySegment<BitmapDrawCall>(array);
                }

                if (_Buffer.Count < capacity)
                    throw new InvalidOperationException("Buffer too small");

                seg1 = _Buffer;
                // FIXME: Insufficient space?
                if (_WordWrap)
                    seg2 = new ArraySegment<BitmapDrawCall>(
                        _Buffer.Array, _Buffer.Offset + length, length
                    );

                _CachedStringLayout = Font.LayoutString(
                    _Text, seg1, 
                    _Position, _Color, 
                    _Scale, _SortKey, 
                    _CharacterSkipCount, _CharacterLimit, 
                    _XOffsetOfFirstLine, (_CharacterWrap && !_WordWrap) ? null : _LineBreakAtX,
                    _AlignToPixels, _KerningAdjustments,
                    _WordWrap, _WrapCharacter
                );
            }

            return _CachedStringLayout.Value;
        }
    }

    public struct StringLayout {
        private static readonly ConditionalWeakTable<SpriteFont, Dictionary<char, KerningAdjustment>> _DefaultKerningAdjustments =
            new ConditionalWeakTable<SpriteFont, Dictionary<char, KerningAdjustment>>(); 

        public readonly Vector2 Position;
        public readonly Vector2 Size;
        public readonly float LineHeight;
        public readonly Bounds FirstCharacterBounds;
        public readonly Bounds LastCharacterBounds;
        public readonly ArraySegment<BitmapDrawCall> DrawCalls;

        public StringLayout (Vector2 position, Vector2 size, float lineHeight, Bounds firstCharacter, Bounds lastCharacter, ArraySegment<BitmapDrawCall> drawCalls) {
            Position = position;
            Size = size;
            LineHeight = lineHeight;
            FirstCharacterBounds = firstCharacter;
            LastCharacterBounds = lastCharacter;
            DrawCalls = drawCalls;
        }

        public int Count {
            get {
                return DrawCalls.Count;
            }
        }

        public BitmapDrawCall this[int index] {
            get {
                if ((index < 0) || (index >= Count))
                    throw new ArgumentOutOfRangeException("index");

                return DrawCalls.Array[DrawCalls.Offset + index];
            }
            set {
                if ((index < 0) || (index >= Count))
                    throw new ArgumentOutOfRangeException("index");

                DrawCalls.Array[DrawCalls.Offset + index] = value;
            }
        }

        public ArraySegment<BitmapDrawCall> Slice (int skip, int count) {
            return new ArraySegment<BitmapDrawCall>(
                DrawCalls.Array, DrawCalls.Offset + skip, Math.Max(Math.Min(count, DrawCalls.Count - skip), 0)
            );
        }

        public static implicit operator ArraySegment<BitmapDrawCall> (StringLayout layout) {
            return layout.DrawCalls;
        }

        public static Dictionary<char, KerningAdjustment> GetDefaultKerningAdjustments (SpriteFont font) {
            Dictionary<char, KerningAdjustment> result;
            _DefaultKerningAdjustments.TryGetValue(font, out result);
            return result;
        }

        public static void SetDefaultKerningAdjustments (SpriteFont font, Dictionary<char, KerningAdjustment> adjustments) {
            _DefaultKerningAdjustments.Remove(font);
            _DefaultKerningAdjustments.Add(font, adjustments);
        }
    }

    public struct KerningAdjustment {
        public float LeftSideBearing, RightSideBearing, Width;

        public KerningAdjustment (float leftSide = 0f, float rightSide = 0f, float width = 0f) {
            LeftSideBearing = leftSide;
            RightSideBearing = rightSide;
            Width = width;
        }
    }

    public struct StringLayoutEngine {
        // Parameters
        public Vector2? position;
        public Color? color;
        public float scale;
        public DrawCallSortKey sortKey;
        public int characterSkipCount;
        public int characterLimit;
        public float xOffsetOfFirstLine;
        public float? lineBreakAtX;
        public bool alignToPixels;
        public bool wordWrap;
        public char wrapCharacter;

        // State
        public Vector2 actualPosition, totalSize, characterOffset;
        public Bounds  firstCharacterBounds, lastCharacterBounds;
        public float   spacing, lineSpacing;
        public ArraySegment<BitmapDrawCall> remainingBuffer;
        int 
            // Beginning of the most recent whitespace/non-whitespace range
            firstNonWhitespace, firstWhitespace,
            // End of the most recent whitespace/non-whitespace range
            lastNonWhitespace, lastWhitespace,
            rowIndex, colIndex;

        private bool IsInitialized;

        public void Initialize () {
            actualPosition = position.GetValueOrDefault(Vector2.Zero);
            characterOffset = new Vector2(xOffsetOfFirstLine, 0);

            firstNonWhitespace = firstWhitespace = lastWhitespace = lastNonWhitespace = -999;
            rowIndex = colIndex = 0;

            IsInitialized = true;
        }

        public ArraySegment<BitmapDrawCall> AppendText (
            SpriteFont font, AbstractString text, 
            ArraySegment<BitmapDrawCall>? buffer = null,
            Dictionary<char, KerningAdjustment> kerningAdjustments = null
        ) {
            if (!IsInitialized)
                throw new InvalidOperationException("Call Initialize first");

            if (font == null)
                throw new ArgumentNullException("font");
            if (text.IsNull)
                throw new ArgumentNullException("text");

            ArraySegment<BitmapDrawCall> _buffer;
            if (buffer.HasValue)
                _buffer = buffer.Value;
            else if ((remainingBuffer.Array != null) && (remainingBuffer.Count > 0))
                _buffer = remainingBuffer;
            else
                _buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[text.Length]);

            if (_buffer.Count < text.Length)
                throw new ArgumentException("buffer too small", "buffer");

            if (kerningAdjustments == null)
                kerningAdjustments = StringLayout.GetDefaultKerningAdjustments(font);

            spacing = font.Spacing;
            lineSpacing = Math.Max(lineSpacing, font.LineSpacing);

            var glyphSource = font.GetGlyphSource();

            var drawCall = new BitmapDrawCall(
                glyphSource.Texture, default(Vector2), default(Bounds), color.GetValueOrDefault(Color.White), scale
            );
            drawCall.SortKey = sortKey;

            float rectScaleX = 1f / glyphSource.Texture.Width;
            float rectScaleY = 1f / glyphSource.Texture.Height;

            int bufferWritePosition = _buffer.Offset;
            int drawCallsWritten = 0;

            for (int i = 0, l = text.Length; i < l; i++) {
                var ch = text[i];
                var isWhiteSpace = char.IsWhiteSpace(ch);

                var lineBreak = false;
                if (ch == '\r') {
                    if (((i + 1) < l) && (text[i + 1] == '\n'))
                        i += 1;

                    lineBreak = true;
                } else if (ch == '\n') {
                    lineBreak = true;
                }

                bool deadGlyph;

                Glyph glyph;
                deadGlyph = !glyphSource.GetGlyph(ch, out glyph);

                KerningAdjustment kerningAdjustment;
                if ((kerningAdjustments != null) && kerningAdjustments.TryGetValue(ch, out kerningAdjustment)) {
                    glyph.LeftSideBearing += kerningAdjustment.LeftSideBearing;
                    glyph.Width += kerningAdjustment.Width;
                    glyph.RightSideBearing += kerningAdjustment.RightSideBearing;
                }

                if (!deadGlyph) {
                    var x = characterOffset.X + 
                        glyph.LeftSideBearing + 
                        glyph.RightSideBearing + 
                        glyph.Width + spacing;

                    if ((x >= lineBreakAtX) && (colIndex > 0) && !isWhiteSpace)
                        lineBreak = true;
                }

                if (lineBreak) {
                    characterOffset.X = 0;
                    characterOffset.Y += lineSpacing;
                    rowIndex += 1;
                    colIndex = 0;
                }

                if (deadGlyph) {
                    characterSkipCount--;
                    characterLimit--;
                    continue;
                }

                characterOffset.X += spacing;

                lastCharacterBounds = Bounds.FromPositionAndSize(
                    characterOffset, new Vector2(
                        glyph.LeftSideBearing + glyph.Width + glyph.RightSideBearing,
                        font.LineSpacing
                    )
                );

                if ((rowIndex == 0) && (colIndex == 0))
                    firstCharacterBounds = lastCharacterBounds;

                characterOffset.X += glyph.LeftSideBearing;

                if (colIndex == 0)
                    characterOffset.X = Math.Max(characterOffset.X, 0);

                if (characterSkipCount <= 0) {
                    if (characterLimit <= 0)
                        break;

                    var glyphPosition = new Vector2(
                        actualPosition.X + (glyph.Cropping.X + characterOffset.X) * scale,
                        actualPosition.Y + (glyph.Cropping.Y + characterOffset.Y) * scale
                    );

                    bool characterInvisible = char.IsWhiteSpace(ch);

                    if (!characterInvisible) {
                        drawCall.TextureRegion = glyphSource.Texture.BoundsFromRectangle(ref glyph.BoundsInTexture);
                        if (alignToPixels)
                            drawCall.Position = glyphPosition.Floor();
                        else
                            drawCall.Position = glyphPosition;

                        _buffer.Array[bufferWritePosition] = drawCall;

                        bufferWritePosition += 1;
                        drawCallsWritten += 1;
                    }

                    characterLimit--;
                } else {
                    characterSkipCount--;
                }

                characterOffset.X += (glyph.Width + glyph.RightSideBearing);

                totalSize.X = Math.Max(totalSize.X, characterOffset.X);
                totalSize.Y = Math.Max(totalSize.Y, characterOffset.Y + font.LineSpacing);

                if (isWhiteSpace) {
                    if (lastWhitespace != i - 1)
                        firstWhitespace = i;
                    lastWhitespace = i;
                } else {
                    if (lastNonWhitespace != i - 1)
                        firstNonWhitespace = i;
                    lastNonWhitespace = i;
                }

                colIndex += 1;
            }

            var segment = new ArraySegment<BitmapDrawCall>(
                _buffer.Array, _buffer.Offset, drawCallsWritten
            );
            remainingBuffer = new ArraySegment<BitmapDrawCall>(
                _buffer.Array, _buffer.Offset + drawCallsWritten,
                _buffer.Count - drawCallsWritten
            );

            if (segment.Count > text.Length)
                throw new InvalidDataException();

            return segment;
        }
    }
}

namespace Squared.Render {
    public static class SpriteFontExtensions {
        public static StringLayout LayoutString (
            this SpriteFont font, AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            Vector2? position = null, Color? color = null, float scale = 1, DrawCallSortKey sortKey = default(DrawCallSortKey),
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            bool alignToPixels = false,
            Dictionary<char, KerningAdjustment> kerningAdjustments = null,
            bool wordWrap = false, char wrapCharacter = '\0'
        ) {
            var state = new StringLayoutEngine {
                position = position,
                color = color,
                scale = scale,
                sortKey = sortKey,
                characterSkipCount = characterSkipCount,
                characterLimit = characterLimit,
                xOffsetOfFirstLine = xOffsetOfFirstLine,
                lineBreakAtX = lineBreakAtX,
                alignToPixels = alignToPixels,
                wordWrap = wordWrap,
                wrapCharacter = wrapCharacter
            };

            state.Initialize();

            var segment = state.AppendText(
                font, text, buffer, kerningAdjustments
            );

            return new StringLayout(
                position.GetValueOrDefault(), state.totalSize, font.LineSpacing,
                state.firstCharacterBounds, state.lastCharacterBounds,
                segment
            );
        }
    }
}
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
        private float _XOffsetOfNewLine = 0;
        private float? _LineBreakAtX = null;
        private bool _WordWrap = false;
        private bool _CharacterWrap = true;
        private float _WrapIndentation = 0f;
        private bool _AlignToPixels = false;
        private char _WrapCharacter = '\0';
        private int _Alignment = (int)HorizontalAlignment.Left;

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

        public float XOffsetOfNewLine {
            get {
                return _XOffsetOfNewLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfNewLine, value);
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

        public bool CharacterWrap {
            get {
                // FIXME: Is this right?
                return _CharacterWrap || _WordWrap;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterWrap, value);
            }
        }

        public HorizontalAlignment Alignment {
            get {
                return (HorizontalAlignment)_Alignment;
            }
            set {
                InvalidatingValueAssignment(ref _Alignment, (int)value);
            }
        }

        public char? WrapCharacter {
            get {
                return (_WrapCharacter == '\0') ? null : (char?)_WrapCharacter;
            }
            set {
                if (value.HasValue)
                    InvalidatingValueAssignment(ref _WrapCharacter, value.Value);
                else
                    InvalidatingValueAssignment(ref _WrapCharacter, '\0');
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
                    capacity += StringLayoutEngine.DefaultBufferPadding;

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

                using (
                    var le = new StringLayoutEngine {
                        buffer = _Buffer,
                        position = _Position,
                        color = _Color,
                        scale = _Scale,
                        sortKey = _SortKey,
                        characterSkipCount = _CharacterSkipCount,
                        characterLimit = _CharacterLimit,
                        xOffsetOfFirstLine = _XOffsetOfFirstLine,
                        xOffsetOfWrappedLine = _XOffsetOfNewLine + _WrapIndentation,
                        xOffsetOfNewLine = _XOffsetOfNewLine,
                        lineBreakAtX = (_CharacterWrap || _WordWrap) ? _LineBreakAtX : null,
                        alignToPixels = _AlignToPixels,
                        wordWrap = _WordWrap,
                        wrapCharacter = _WrapCharacter,
                        alignment = (HorizontalAlignment)_Alignment
                    }
                ) {
                    le.Initialize();
                    le.AppendText(_Font, _Text, _KerningAdjustments);

                    _CachedStringLayout = le.Finish();
                }
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

    public enum HorizontalAlignment : int {
        Left,
        Center,
        Right,
        // Justify
    }

    public struct StringLayoutEngine : IDisposable {
        public const int DefaultBufferPadding = 64;

        // Parameters
        public ArraySegment<BitmapDrawCall>? buffer;
        public Vector2? position;
        public Color?   color;
        public float    scale;
        public DrawCallSortKey sortKey;
        public int      characterSkipCount;
        public int      characterLimit;
        public float    xOffsetOfFirstLine;
        public float    xOffsetOfWrappedLine;
        public float    xOffsetOfNewLine;
        public float?   lineBreakAtX;
        public bool     alignToPixels;
        public bool     wordWrap;
        public char     wrapCharacter;
        public HorizontalAlignment alignment;
        public Func<ArraySegment<BitmapDrawCall>, ArraySegment<BitmapDrawCall>> growBuffer;

        // State
        public float    maxLineHeight;
        public Vector2  actualPosition, totalSize, characterOffset;
        public Bounds   firstCharacterBounds, lastCharacterBounds;
        public float    spacing, lineSpacing;
        public int      drawCallsWritten;
        public ArraySegment<BitmapDrawCall> remainingBuffer;
        float   initialLineXOffset, currentLineMaxX, lastWordEndX;
        int     bufferWritePosition, wordStartWritePosition;
        int     rowIndex, colIndex;
        bool    wordWrapSuppressed;
        Vector2 wordStartOffset;

        private bool IsInitialized;

        public void Initialize () {
            actualPosition = position.GetValueOrDefault(Vector2.Zero);
            characterOffset = new Vector2(xOffsetOfFirstLine, 0);
            initialLineXOffset = characterOffset.X;

            bufferWritePosition = 0;
            wordStartWritePosition = -1;
            wordStartOffset = Vector2.Zero;
            rowIndex = colIndex = 0;
            wordWrapSuppressed = false;

            IsInitialized = true;
        }

        private void WrapWord (
            ArraySegment<BitmapDrawCall> buffer,
            Vector2 firstOffset, int firstIndex, int lastIndex, float newX
        ) {
            for (var i = firstIndex; i <= lastIndex; i++) {
                var dc = buffer.Array[buffer.Offset + i];
                var newCharacterX = xOffsetOfWrappedLine + (dc.Position.X - firstOffset.X);

                dc.Position = new Vector2(newCharacterX, dc.Position.Y + lineSpacing);
                if (alignment != HorizontalAlignment.Left)
                    dc.SortKey.Order += 1;

                buffer.Array[buffer.Offset + i] = dc;
            }

            newX -= characterOffset.X;
            characterOffset.X = xOffsetOfWrappedLine + (characterOffset.X - firstOffset.X);

            // HACK: firstOffset may include whitespace so we want to pull the right edge in.
            //  Without doing this, the size rect for the string is too large.
            var actualRightEdge = firstOffset.X;
            if (firstIndex > 0)
                actualRightEdge = Math.Min(
                    actualRightEdge, 
                    buffer.Array[buffer.Offset + firstIndex - 1].EstimateDrawBounds().BottomRight.X
                );
            totalSize.X = Math.Max(totalSize.X, actualRightEdge);

            lastWordEndX = newX + characterOffset.X;
        }

        private void AlignLine (
            ArraySegment<BitmapDrawCall> buffer, HorizontalAlignment alignment,
            int firstIndex, int lastIndex
        ) {
            var firstDc = buffer.Array[buffer.Offset + firstIndex].EstimateDrawBounds();
            var endDc = buffer.Array[buffer.Offset + lastIndex].EstimateDrawBounds();
            var lineWidth = (endDc.BottomRight.X - firstDc.TopLeft.X);

            float whitespace;
            if (lineBreakAtX.HasValue)
                whitespace = lineBreakAtX.Value - lineWidth;
            else
                whitespace = totalSize.X - lineWidth;

            if (alignment == HorizontalAlignment.Center)
                whitespace /= 2;

            for (var j = firstIndex; j <= lastIndex; j++) {
                buffer.Array[buffer.Offset + j].Position.X += whitespace;
                // We used the sortkey to store line numbers, now we put the right data there
                buffer.Array[buffer.Offset + j].SortKey = sortKey;
            }
        }

        private void AlignLines (
            ArraySegment<BitmapDrawCall> buffer, HorizontalAlignment alignment
        ) {
            if (alignment == HorizontalAlignment.Left)
                return;
            if (buffer.Count == 0)
                return;

            int lineStartIndex = 0;
            float currentLine = buffer.Array[buffer.Offset].SortKey.Order;

            for (var i = 1; i < buffer.Count; i++) {
                var line = buffer.Array[buffer.Offset + i].SortKey.Order;

                if (line > currentLine) {
                    AlignLine(buffer, alignment, lineStartIndex, i - 1);

                    lineStartIndex = i;
                    currentLine = line;
                }
            }

            AlignLine(buffer, alignment, lineStartIndex, buffer.Count - 1);
        }

        public ArraySegment<BitmapDrawCall> AppendText (
            SpriteFont font, AbstractString text,
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
                buffer = _buffer = new ArraySegment<BitmapDrawCall>(
                    new BitmapDrawCall[text.Length + DefaultBufferPadding]
                );

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
            float x = 0;

            for (int i = 0, l = text.Length; i < l; i++) {
                var ch = text[i];
                bool isWhiteSpace = char.IsWhiteSpace(ch),
                     forcedWrap = false, lineBreak = false,
                     deadGlyph = false;

                if (ch == '\r') {
                    if (((i + 1) < l) && (text[i + 1] == '\n'))
                        i += 1;

                    lineBreak = true;
                } else if (ch == '\n') {
                    lineBreak = true;
                }

                if (!isWhiteSpace) {
                    if (wordStartWritePosition < 0) {
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                    }
                } else {
                    if (wordStartWritePosition >= 0)
                        lastWordEndX = x;

                    wordStartWritePosition = -1;
                    wordWrapSuppressed = false;
                }

                Glyph glyph;
                deadGlyph = !glyphSource.GetGlyph(ch, out glyph);

                KerningAdjustment kerningAdjustment;
                if ((kerningAdjustments != null) && kerningAdjustments.TryGetValue(ch, out kerningAdjustment)) {
                    glyph.LeftSideBearing += kerningAdjustment.LeftSideBearing;
                    glyph.Width += kerningAdjustment.Width;
                    glyph.RightSideBearing += kerningAdjustment.RightSideBearing;
                }

                x = characterOffset.X + 
                    glyph.LeftSideBearing + 
                    glyph.RightSideBearing + 
                    glyph.Width + spacing;

                if (x >= lineBreakAtX) {
                    if (
                        !deadGlyph &&
                        (colIndex > 0) &&
                        !isWhiteSpace
                    )
                        forcedWrap = true;
                }

                if (forcedWrap) {
                    var currentWordSize = x - wordStartOffset.X;

                    if (wordWrap && !wordWrapSuppressed && (currentWordSize <= lineBreakAtX)) {
                        WrapWord(_buffer, wordStartOffset, wordStartWritePosition, bufferWritePosition - 1, x);
                        wordWrapSuppressed = true;
                    } else {
                        characterOffset.X = xOffsetOfWrappedLine;
                        totalSize.X = Math.Max(totalSize.X, currentLineMaxX);
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                    }

                    lineBreak = true;
                }

                if (lineBreak) {
                    if (!forcedWrap) {
                        characterOffset.X = xOffsetOfNewLine;
                        totalSize.X = Math.Max(totalSize.X, currentLineMaxX);
                    }

                    initialLineXOffset = characterOffset.X;
                    currentLineMaxX = 0;
                    lastWordEndX = 0;
                    characterOffset.Y += lineSpacing;
                    rowIndex += 1;
                    colIndex = 0;
                }

                if (deadGlyph) {
                    characterSkipCount--;
                    characterLimit--;
                    continue;
                }

                // HACK: Recompute after wrapping
                x = characterOffset.X + 
                    glyph.LeftSideBearing + 
                    glyph.RightSideBearing + 
                    glyph.Width + spacing;

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

                    if (!isWhiteSpace) {                    
                        if (bufferWritePosition >= _buffer.Count) {
                            if (growBuffer != null)
                                buffer = _buffer = growBuffer(_buffer);
                            else
                                throw new ArgumentException("buffer too small", "buffer");
                        }

                        drawCall.TextureRegion = glyphSource.Texture.BoundsFromRectangle(ref glyph.BoundsInTexture);
                        if (alignToPixels)
                            drawCall.Position = glyphPosition.Floor();
                        else
                            drawCall.Position = glyphPosition;

                        // HACK so that the alignment pass can detect rows. We strip this later.
                        if (alignment != HorizontalAlignment.Left)
                            drawCall.SortKey.Order = rowIndex;

                        _buffer.Array[_buffer.Offset + bufferWritePosition] = drawCall;

                        bufferWritePosition += 1;
                        drawCallsWritten += 1;
                    }

                    characterLimit--;
                } else {
                    characterSkipCount--;
                }

                characterOffset.X += (glyph.Width + glyph.RightSideBearing);

                if (!isWhiteSpace) {
                    currentLineMaxX = Math.Max(currentLineMaxX, characterOffset.X);
                    totalSize.Y = Math.Max(totalSize.Y, characterOffset.Y + font.LineSpacing);
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

        public StringLayout Finish () {
            totalSize.X = Math.Max(totalSize.X, currentLineMaxX);

            var resultSegment = new ArraySegment<BitmapDrawCall>(
                buffer.Value.Array, buffer.Value.Offset, drawCallsWritten
            );
            if (alignment != HorizontalAlignment.Left)
                AlignLines(resultSegment, alignment);

            return new StringLayout(
                position.GetValueOrDefault(), totalSize, lineSpacing,
                firstCharacterBounds, lastCharacterBounds,
                resultSegment
            );
        }

        public void Dispose () {
        }
    }
}

namespace Squared.Render {
    public static class SpriteFontExtensions {
        public static StringLayout LayoutString (
            this SpriteFont font, AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            Vector2? position = null, Color? color = null, float scale = 1, 
            DrawCallSortKey sortKey = default(DrawCallSortKey),
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
                wrapCharacter = wrapCharacter,
                buffer = buffer
            };

            state.Initialize();

            using (state) {
                var segment = state.AppendText(
                    font, text, kerningAdjustments
                );

                return state.Finish();
            }
        }
    }
}
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
using Squared.Util.Text;

namespace Squared.Render.Text {
    public struct StringLayout {
        private static readonly ConditionalWeakTable<SpriteFont, Dictionary<char, KerningAdjustment>> _DefaultKerningAdjustments =
            new ConditionalWeakTable<SpriteFont, Dictionary<char, KerningAdjustment>>(); 

        public readonly Vector2 Position;
        /// <summary>
        /// The size of the layout's visible characters in their wrapped positions.
        /// </summary>
        public readonly Vector2 Size;
        /// <summary>
        /// The size that the layout would have had if it was unconstrained by wrapping and character/line limits.
        /// </summary>
        public readonly Vector2 UnconstrainedSize;
        public readonly float LineHeight;
        public readonly Bounds FirstCharacterBounds;
        public readonly Bounds LastCharacterBounds;
        public readonly ArraySegment<BitmapDrawCall> DrawCalls;

        public StringLayout (
            Vector2 position, Vector2 size, Vector2 unconstrainedSize, 
            float lineHeight, Bounds firstCharacter, Bounds lastCharacter, 
            ArraySegment<BitmapDrawCall> drawCalls
        ) {
            Position = position;
            Size = size;
            UnconstrainedSize = unconstrainedSize;
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

        public static Dictionary<char, KerningAdjustment> GetDefaultKerningAdjustments (IGlyphSource font) {
            // FIXME
            if (font is SpriteFontGlyphSource) {
                Dictionary<char, KerningAdjustment> result;
                _DefaultKerningAdjustments.TryGetValue(((SpriteFontGlyphSource)font).Font, out result);
                return result;
            } else {
                return null;
            }
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

    public struct LayoutMarker {
        public class Comparer : IRefComparer<LayoutMarker> {
            public static readonly Comparer Instance = new Comparer();

            public int Compare (ref LayoutMarker lhs, ref LayoutMarker rhs) {
                var result = lhs.FirstCharacterIndex.CompareTo(rhs.FirstCharacterIndex);
                if (result == 0)
                    result = lhs.LastCharacterIndex.CompareTo(rhs.LastCharacterIndex);
                return result;
            }
        }

        public int FirstCharacterIndex, LastCharacterIndex;
        public int? FirstDrawCallIndex, LastDrawCallIndex;
        public int GlyphCount;
        public Bounds? Bounds;
    }

    public struct LayoutHitTest {
        public class Comparer : IRefComparer<LayoutHitTest> {
            public static readonly Comparer Instance = new Comparer();

            public int Compare (ref LayoutHitTest lhs, ref LayoutHitTest rhs) {
                var result = lhs.Position.X.CompareTo(rhs.Position.X);
                if (result == 0)
                    result = lhs.Position.Y.CompareTo(rhs.Position.Y);
                return result;
            }
        }

        public Vector2 Position;
        public int? FirstCharacterIndex, LastCharacterIndex;
        public bool LeaningRight;

        public override string ToString () {
            return $"hitTest {Position} -> {FirstCharacterIndex} leaning {(LeaningRight ? "right" : "left")}";
        }
    }

    public struct StringLayoutEngine : IDisposable {
        public DenseList<LayoutMarker> Markers;
        public DenseList<LayoutHitTest> HitTests;

        public const int DefaultBufferPadding = 64;

        // Parameters
        public ArraySegment<BitmapDrawCall> buffer;
        public Vector2? position;
        public Color?   color;
        public float    scale;
        public DrawCallSortKey sortKey;
        public int      characterSkipCount;
        public int?     characterLimit;
        public float    xOffsetOfFirstLine;
        public float    xOffsetOfWrappedLine;
        public float    xOffsetOfNewLine;
        public float?   lineBreakAtX;
        public bool     characterWrap;
        public bool     wordWrap;
        public char     wrapCharacter;
        public bool     reverseOrder;
        public int?     lineLimit;
        public GlyphPixelAlignment alignToPixels;
        public HorizontalAlignment alignment;
        public Func<ArraySegment<BitmapDrawCall>, ArraySegment<BitmapDrawCall>> growBuffer;

        // State
        public float    maxLineHeight;
        public Vector2  actualPosition, characterOffset, characterOffsetUnconstrained;
        public Bounds   firstCharacterBounds, lastCharacterBounds;
        public int      drawCallsWritten, drawCallsSuppressed;
        float   initialLineXOffset;
        int     bufferWritePosition, wordStartWritePosition;
        int     rowIndex, colIndex;
        bool    wordWrapSuppressed;
        float   currentLineMaxX, currentLineMaxXUnconstrained;
        float   currentLineWhitespaceMaxXLeft, currentLineWhitespaceMaxX;
        float   maxX, maxY, maxXUnconstrained, maxYUnconstrained;
        float?  currentLineSpacing;
        float   maxLineSpacing;
        Vector2 wordStartOffset;
        private bool ownsBuffer;

        int currentCharacterIndex;

        private bool IsInitialized;

        public void Initialize () {
            actualPosition = position.GetValueOrDefault(Vector2.Zero);
            characterOffsetUnconstrained = characterOffset = new Vector2(xOffsetOfFirstLine, 0);
            initialLineXOffset = characterOffset.X;

            bufferWritePosition = 0;
            drawCallsWritten = 0;
            drawCallsSuppressed = 0;
            wordStartWritePosition = -1;
            wordStartOffset = Vector2.Zero;
            rowIndex = colIndex = 0;
            wordWrapSuppressed = false;
            currentLineSpacing = null;
            maxLineSpacing = 0;

            HitTests.Sort(LayoutHitTest.Comparer.Instance);
            for (int i = 0; i < HitTests.Count; i++) {
                var ht = HitTests[i];
                ht.FirstCharacterIndex = null;
                ht.LastCharacterIndex = null;
                HitTests[i] = ht;
            }

            Markers.Sort(LayoutMarker.Comparer.Instance);
            for (int i = 0; i < Markers.Count; i++) {
                var m = Markers[i];
                m.Bounds = null;
                Markers[i] = m;
            }

            currentCharacterIndex = 0;

            IsInitialized = true;
        }

        private void ProcessHitTests (ref Bounds bounds, float centerX) {
            var characterIndex = currentCharacterIndex;
            for (int i = 0; i < HitTests.Count; i++) {
                var ht = HitTests[i];
                if (bounds.Contains(ht.Position)) {
                    if (!ht.FirstCharacterIndex.HasValue) {
                        ht.FirstCharacterIndex = characterIndex;
                        // FIXME: Why is this literally always wrong?
                        ht.LeaningRight = (ht.Position.X >= centerX);
                    }
                    ht.LastCharacterIndex = characterIndex;
                    HitTests[i] = ht;
                }
            }
        }

        private void ProcessMarkers (ref Bounds bounds, int currentCodepointSize, int? drawCallIndex) {
            var characterIndex1 = currentCharacterIndex - currentCodepointSize + 1;
            var characterIndex2 = currentCharacterIndex;
            for (int i = 0; i < Markers.Count; i++) {
                var m = Markers[i];
                if (m.FirstCharacterIndex > characterIndex2)
                    continue;
                if (m.LastCharacterIndex < characterIndex1)
                    continue;
                if (m.Bounds.HasValue)
                    m.Bounds = Bounds.FromUnion(bounds, m.Bounds.Value);
                else
                    m.Bounds = bounds;
                if (drawCallIndex != null)
                    m.GlyphCount++;
                m.FirstDrawCallIndex = m.FirstDrawCallIndex ?? drawCallIndex;
                m.LastDrawCallIndex = drawCallIndex ?? m.LastDrawCallIndex;
                Markers[i] = m;
            }
        }

        private void WrapWord (
            ArraySegment<BitmapDrawCall> buffer,
            Vector2 firstOffset, int firstIndex, int lastIndex, float effectiveScale, float effectiveLineSpacing
        ) {
            // FIXME: Can this ever happen?
            if (currentLineWhitespaceMaxX <= 0)
                maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);
            else
                maxX = Math.Max(maxX, currentLineWhitespaceMaxXLeft * effectiveScale);

            var scaledFirstOffset = firstOffset * effectiveScale;
            var scaledLineSpacing = currentLineSpacing.GetValueOrDefault(maxLineSpacing) * effectiveScale;

            for (var i = firstIndex; i <= lastIndex; i++) {
                var dc = buffer.Array[buffer.Offset + i];
                var newCharacterX = (xOffsetOfWrappedLine * effectiveScale) + (dc.Position.X - scaledFirstOffset.X);

                dc.Position = new Vector2(newCharacterX, dc.Position.Y + scaledLineSpacing);
                if (alignment != HorizontalAlignment.Left)
                    dc.SortKey.Order += 1;

                buffer.Array[buffer.Offset + i] = dc;
            }

            characterOffset.X = xOffsetOfWrappedLine + (characterOffset.X - firstOffset.X);
            characterOffset.Y += effectiveLineSpacing;

            // HACK: firstOffset may include whitespace so we want to pull the right edge in.
            //  Without doing this, the size rect for the string is too large.
            var actualRightEdge = firstOffset.X;
            if (firstIndex > 0)
                actualRightEdge = Math.Min(
                    actualRightEdge, 
                    buffer.Array[buffer.Offset + firstIndex - 1].EstimateDrawBounds().BottomRight.X
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Snap (Vector2 pos, out Vector2 result) {
            switch (alignToPixels.Horizontal) {
                case PixelAlignmentMode.Floor:
                    result.X = (float)Math.Floor(pos.X);
                    break;
                case PixelAlignmentMode.FloorHalf:
                    result.X = (float)Math.Floor(pos.X * 2) / 2;
                    break;
                default:
                    result.X = pos.X;
                    break;
            }

            switch (alignToPixels.Vertical) {
                case PixelAlignmentMode.Floor:
                    result.Y = (float)Math.Floor(pos.Y);
                    break;
                case PixelAlignmentMode.FloorHalf:
                    result.Y = (float)Math.Floor(pos.Y * 2) / 2;
                    break;
                default:
                    result.Y = pos.Y;
                    break;
            }
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
                whitespace = maxX - lineWidth;

            // HACK: Don't do anything if the line is too big, just overflow to the right.
            //  Otherwise, the sizing info will be wrong and bad things happen.
            if (whitespace <= 0)
                whitespace = 0;

            // HACK: We compute this before halving the whitespace, so that the size of 
            //  the layout is enough to ensure manually centering the whole layout will
            //  still preserve per-line centering.
            maxX = Math.Max(maxX, whitespace + lineWidth);

            if (alignment == HorizontalAlignment.Center)
                whitespace /= 2;

            for (var j = firstIndex; j <= lastIndex; j++) {
                var newPosition = buffer.Array[buffer.Offset + j].Position;
                newPosition.X += whitespace;
                Snap(newPosition, out buffer.Array[buffer.Offset + j].Position);
                // We used the sortkey to store line numbers, now we put the right data there
                var key = sortKey;
                if (reverseOrder)
                    key.Order += j;
                buffer.Array[buffer.Offset + j].SortKey = key;
            }
        }

        private void AlignLines (
            ArraySegment<BitmapDrawCall> buffer, HorizontalAlignment alignment
        ) {
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

        private void SnapPositions (ArraySegment<BitmapDrawCall> buffer) {
            for (var i = 0; i < buffer.Count; i++)
                Snap(buffer.Array[buffer.Offset + i].Position, out buffer.Array[buffer.Offset + i].Position);
        }

        private void EnsureBufferCapacity (int count) {
            int paddedCount = count + DefaultBufferPadding;

            if (buffer.Array == null) {
                ownsBuffer = true;
                buffer = new ArraySegment<BitmapDrawCall>(
                    new BitmapDrawCall[paddedCount]
                );
            } else if (buffer.Count < paddedCount) {
                if (ownsBuffer) {
                    var oldBuffer = buffer;
                    var newSize = Math.Min(paddedCount + 256, oldBuffer.Count * 2);
                    buffer = new ArraySegment<BitmapDrawCall>(
                        new BitmapDrawCall[newSize]
                    );
                    Array.Copy(oldBuffer.Array, buffer.Array, oldBuffer.Count);
                } else if (buffer.Count >= count) {
                    // This is OK, there should be enough room...
                } else {
                    throw new InvalidOperationException("Buffer too small");
                }
            }
        }

        public ArraySegment<BitmapDrawCall> AppendText (
            IGlyphSource font, AbstractString text,
            Dictionary<char, KerningAdjustment> kerningAdjustments = null
        ) {
            if (!IsInitialized)
                throw new InvalidOperationException("Call Initialize first");

            if (font == null)
                throw new ArgumentNullException("font");
            if (text.IsNull)
                throw new ArgumentNullException("text");

            EnsureBufferCapacity(bufferWritePosition + text.Length);

            if (kerningAdjustments == null)
                kerningAdjustments = StringLayout.GetDefaultKerningAdjustments(font);

            var effectiveScale = scale / font.DPIScaleFactor;

            var drawCall = default(BitmapDrawCall);
            drawCall.MultiplyColor = color.GetValueOrDefault(Color.White);
            drawCall.ScaleF = effectiveScale;
            drawCall.SortKey = sortKey;

            float x = 0;
            float? defaultLineSpacing = null;
            var suppress = false;

            for (int i = 0, l = text.Length; i < l; i++) {
                if (lineLimit.HasValue && lineLimit.Value <= 0)
                    suppress = true;

                var stringOffset = i;
                char ch1 = text[i], 
                    ch2 = i < (text.Length - 1)
                        ? text[i + 1]
                        : '\0';

                int currentCodepointSize = 1;
                uint codepoint;
                if (Unicode.DecodeSurrogatePair(ch1, ch2, out codepoint)) {
                    currentCodepointSize = 2;
                    currentCharacterIndex++;
                    i++;
                } else if (ch1 == '\r') {
                    if (ch2 == '\n') {
                        currentCodepointSize = 2;
                        ch1 = ch2;
                        i++;
                        currentCharacterIndex++;
                        stringOffset++;
                    }
                }

                bool isWhiteSpace = char.IsWhiteSpace(ch1),
                     forcedWrap = false, lineBreak = false,
                     deadGlyph = false;
                Glyph glyph;
                KerningAdjustment kerningAdjustment;

                if (ch1 == '\n')
                    lineBreak = true;

                if (lineBreak) {
                    if (lineLimit.HasValue)
                        lineLimit--;
                    if (lineLimit.HasValue && lineLimit.Value <= 0)
                        suppress = true;
                }

                if (isWhiteSpace) {
                    wordStartWritePosition = -1;
                    wordWrapSuppressed = false;
                } else {
                    if (wordStartWritePosition < 0) {
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                    }
                }

                deadGlyph = !font.GetGlyph(codepoint, out glyph);

                float effectiveLineSpacing = glyph.LineSpacing;
                if (deadGlyph) {
                    if (currentLineSpacing.HasValue)
                        effectiveLineSpacing = currentLineSpacing.Value;
                    else if (defaultLineSpacing.HasValue)
                        effectiveLineSpacing = defaultLineSpacing.Value;
                    else {
                        Glyph space;
                        if (font.GetGlyph(' ', out space))
                            defaultLineSpacing = effectiveLineSpacing = space.LineSpacing;
                    }
                }

                // FIXME: Don't key kerning adjustments off 'char'
                if ((kerningAdjustments != null) && kerningAdjustments.TryGetValue(ch1, out kerningAdjustment)) {
                    glyph.LeftSideBearing += kerningAdjustment.LeftSideBearing;
                    glyph.Width += kerningAdjustment.Width;
                    glyph.RightSideBearing += kerningAdjustment.RightSideBearing;
                }

                x = 
                    characterOffset.X + 
                    glyph.LeftSideBearing + 
                    glyph.RightSideBearing + 
                    glyph.Width + glyph.CharacterSpacing;

                if ((x * effectiveScale) >= lineBreakAtX) {
                    if (
                        !deadGlyph &&
                        (colIndex > 0) &&
                        !isWhiteSpace
                    )
                        forcedWrap = true;
                }

                if (forcedWrap) {
                    var currentWordSize = x - wordStartOffset.X;

                    if (wordWrap && !wordWrapSuppressed && (currentWordSize * effectiveScale <= lineBreakAtX)) {
                        if (lineLimit.HasValue)
                            lineLimit--;
                        WrapWord(buffer, wordStartOffset, wordStartWritePosition, bufferWritePosition - 1, effectiveScale, effectiveLineSpacing);
                        wordWrapSuppressed = true;
                        lineBreak = true;

                        // FIXME: While this will abort when the line limit is reached, we need to erase the word we wrapped to the next line
                        if (lineLimit.HasValue && lineLimit.Value <= 0)
                            suppress = true;
                    } else if (characterWrap) {
                        if (lineLimit.HasValue)
                            lineLimit--;
                        characterOffset.X = xOffsetOfWrappedLine;
                        characterOffset.Y += effectiveLineSpacing;

                        maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                        lineBreak = true;

                        if (lineLimit.HasValue && lineLimit.Value <= 0)
                            suppress = true;
                    }
                }

                if (lineBreak) {
                    if (!forcedWrap) {
                        characterOffset.X = xOffsetOfNewLine;
                        characterOffset.Y += effectiveLineSpacing;
                        characterOffsetUnconstrained.X = xOffsetOfNewLine;
                        characterOffsetUnconstrained.Y += effectiveLineSpacing;

                        maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);
                        maxXUnconstrained = Math.Max(maxXUnconstrained, currentLineMaxXUnconstrained * effectiveScale);
                        currentLineMaxXUnconstrained = 0;
                    }

                    initialLineXOffset = characterOffset.X;
                    currentLineMaxX = 0;
                    currentLineWhitespaceMaxX = 0;
                    currentLineWhitespaceMaxXLeft = 0;
                    rowIndex += 1;
                    colIndex = 0;
                }

                // HACK: Recompute after wrapping
                x = 
                    characterOffset.X + 
                    glyph.LeftSideBearing + 
                    glyph.RightSideBearing + 
                    glyph.Width + glyph.CharacterSpacing;
                var xUnconstrained = x - characterOffset.X + characterOffsetUnconstrained.X;

                if (deadGlyph || isWhiteSpace) {
                    var whitespaceBounds = Bounds.FromPositionAndSize(
                        characterOffset * effectiveScale,
                        new Vector2(x - characterOffset.X, glyph.LineSpacing) * effectiveScale
                    );

                    // FIXME: is the center X right?
                    ProcessHitTests(ref whitespaceBounds, whitespaceBounds.Center.X);
                    ProcessMarkers(ref whitespaceBounds, currentCodepointSize, null);
                }

                if (deadGlyph) {
                    currentCharacterIndex++;
                    characterSkipCount--;
                    if (characterLimit.HasValue)
                        characterLimit--;
                    continue;
                }

                if (!suppress)
                    characterOffset.X += glyph.CharacterSpacing;
                characterOffsetUnconstrained.X += glyph.CharacterSpacing;

                var scaledGlyphSize = new Vector2(
                    glyph.LeftSideBearing + glyph.Width + glyph.RightSideBearing,
                    glyph.LineSpacing
                ) * effectiveScale;

                if (!suppress)
                    lastCharacterBounds = Bounds.FromPositionAndSize(
                        characterOffset * effectiveScale, scaledGlyphSize
                    );

                var testBounds = lastCharacterBounds;
                var centerX = ((characterOffset.X * effectiveScale) + scaledGlyphSize.X) * 0.5f;

                ProcessHitTests(ref testBounds, testBounds.Center.X);

                if ((rowIndex == 0) && (colIndex == 0))
                    firstCharacterBounds = lastCharacterBounds;

                if (!suppress)
                    characterOffset.X += glyph.LeftSideBearing;
                characterOffsetUnconstrained.X += glyph.LeftSideBearing;

                if (colIndex == 0) {
                    characterOffset.X = Math.Max(characterOffset.X, 0);
                    characterOffsetUnconstrained.X = Math.Max(characterOffsetUnconstrained.X, 0);
                }

                if (characterSkipCount <= 0) {
                    if (characterLimit.HasValue && characterLimit.Value <= 0)
                        suppress = true;

                    var glyphPosition = new Vector2(
                        actualPosition.X + (glyph.XOffset + characterOffset.X) * effectiveScale,
                        actualPosition.Y + (glyph.YOffset + characterOffset.Y) * effectiveScale
                    );

                    if (!isWhiteSpace) {
                        if (bufferWritePosition >= buffer.Count)
                            EnsureBufferCapacity(bufferWritePosition);

                        drawCall.Textures = new TextureSet(glyph.Texture);
                        drawCall.TextureRegion = glyph.BoundsInTexture;
                        drawCall.Position = glyphPosition;

                        // HACK so that the alignment pass can detect rows. We strip this later.
                        if (alignment != HorizontalAlignment.Left)
                            drawCall.SortKey.Order = rowIndex;
                        else if (reverseOrder)
                            drawCall.SortKey.Order += 1;

                        if (!suppress) {
                            buffer.Array[buffer.Offset + bufferWritePosition] = drawCall;
                            ProcessMarkers(ref testBounds, currentCodepointSize, bufferWritePosition);
                            bufferWritePosition += 1;
                            drawCallsWritten += 1;
                            currentLineMaxX = Math.Max(currentLineMaxX, x);
                            maxY = Math.Max(maxY, (characterOffset.Y + effectiveLineSpacing) * effectiveScale);
                        } else {
                            drawCallsSuppressed++;
                        }

                        currentLineMaxXUnconstrained = Math.Max(currentLineMaxXUnconstrained, xUnconstrained);
                        maxYUnconstrained = Math.Max(maxYUnconstrained, (characterOffsetUnconstrained.Y + effectiveLineSpacing) * effectiveScale);
                    } else {
                        currentLineWhitespaceMaxXLeft = Math.Max(currentLineWhitespaceMaxXLeft, characterOffset.X);
                        currentLineWhitespaceMaxX = Math.Max(currentLineWhitespaceMaxX, x);

                        ProcessMarkers(ref testBounds, currentCodepointSize, null);
                    }

                    characterLimit--;
                } else {
                    characterSkipCount--;
                }

                if (!suppress)
                    characterOffset.X += (glyph.Width + glyph.RightSideBearing);
                characterOffsetUnconstrained.X += (glyph.Width + glyph.RightSideBearing);
                currentLineSpacing = glyph.LineSpacing;
                maxLineSpacing = Math.Max(maxLineSpacing, effectiveLineSpacing);

                currentCharacterIndex++;
                colIndex += 1;
            }

            var segment = new ArraySegment<BitmapDrawCall>(
                buffer.Array, buffer.Offset, drawCallsWritten
            );

            maxXUnconstrained = Math.Max(maxXUnconstrained, currentLineMaxXUnconstrained * effectiveScale);
            maxX = Math.Max(maxX, currentLineMaxX * effectiveScale);

            return segment;
        }

        private void Scramble (ArraySegment<BitmapDrawCall> result) {
            var rng = new Random();
            FisherYates.Shuffle(rng, result);
        }

        public StringLayout Finish () {
            ArraySegment<BitmapDrawCall> result;
            if (buffer.Array == null)
                result = default(ArraySegment<BitmapDrawCall>);
            else
                result = new ArraySegment<BitmapDrawCall>(
                    buffer.Array, buffer.Offset, drawCallsWritten
                );

            if (alignment != HorizontalAlignment.Left)
                AlignLines(result, alignment);
            else
                SnapPositions(result);

            if (reverseOrder) {
                int i = result.Offset;
                int j = result.Offset + result.Count - 1;
                while (i < j) {
                    var temp = result.Array[i];
                    result.Array[i] = result.Array[j];
                    result.Array[j] = temp;
                    i++;
                    j--;
                }
            }

            // HACK: For troubleshooting sort issues
            if (false)
                Scramble(result);

            var endpointBounds = lastCharacterBounds;
            // FIXME: Index of last draw call?
            // FIXME: Codepoint size?
            ProcessMarkers(ref endpointBounds, 1, null);

            return new StringLayout(
                position.GetValueOrDefault(), 
                new Vector2(maxX, maxY), new Vector2(maxXUnconstrained, maxYUnconstrained),
                maxLineSpacing,
                firstCharacterBounds, lastCharacterBounds,
                result
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
            int characterSkipCount = 0, int? characterLimit = null,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            GlyphPixelAlignment alignToPixels = default(GlyphPixelAlignment),
            Dictionary<char, KerningAdjustment> kerningAdjustments = null,
            bool wordWrap = false, char wrapCharacter = '\0',
            bool reverseOrder = false, HorizontalAlignment? horizontalAlignment = null
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
                characterWrap = lineBreakAtX.HasValue,
                wordWrap = wordWrap,
                wrapCharacter = wrapCharacter,
                buffer = buffer.GetValueOrDefault(default(ArraySegment<BitmapDrawCall>)),
                reverseOrder = reverseOrder
            };
            var gs = new SpriteFontGlyphSource(font);

            if (horizontalAlignment.HasValue)
                state.alignment = horizontalAlignment.Value;

            state.Initialize();

            using (state) {
                var segment = state.AppendText(
                    gs, text, kerningAdjustments
                );

                return state.Finish();
            }
        }

        // Yuck :(
        public static StringLayout LayoutString (
            this IGlyphSource glyphSource, AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            Vector2? position = null, Color? color = null, float scale = 1, 
            DrawCallSortKey sortKey = default(DrawCallSortKey),
            int characterSkipCount = 0, int? characterLimit = null,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            bool alignToPixels = false,
            Dictionary<char, KerningAdjustment> kerningAdjustments = null,
            bool wordWrap = false, char wrapCharacter = '\0',
            bool reverseOrder = false, HorizontalAlignment? horizontalAlignment = null
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
                characterWrap = lineBreakAtX.HasValue,
                wordWrap = wordWrap,
                wrapCharacter = wrapCharacter,
                buffer = buffer.GetValueOrDefault(default(ArraySegment<BitmapDrawCall>)),
                reverseOrder = reverseOrder
            };

            if (horizontalAlignment.HasValue)
                state.alignment = horizontalAlignment.Value;

            state.Initialize();

            using (state) {
                var segment = state.AppendText(
                    glyphSource, text, kerningAdjustments
                );

                return state.Finish();
            }
        }
    }

    namespace Text {
        public enum PixelAlignmentMode {
            None,
            Floor,
            // Like Floor but allows half-pixel values (x.5 in addition to x.0)
            FloorHalf
        }

        public struct GlyphPixelAlignment : IEquatable<GlyphPixelAlignment> {
            public PixelAlignmentMode Horizontal, Vertical;

            public GlyphPixelAlignment (bool alignToPixels) {
                Horizontal = Vertical = alignToPixels ? PixelAlignmentMode.Floor : PixelAlignmentMode.None;
            }

            public GlyphPixelAlignment (PixelAlignmentMode mode) {
                Horizontal = Vertical = mode;
            }

            public GlyphPixelAlignment (PixelAlignmentMode horizontal, PixelAlignmentMode vertical) {
                Horizontal = horizontal;
                Vertical = vertical;
            }

            public static implicit operator GlyphPixelAlignment (bool alignToPixels) {
                return new GlyphPixelAlignment(alignToPixels);
            }

            public static readonly GlyphPixelAlignment Default = new GlyphPixelAlignment(PixelAlignmentMode.None);
            public static readonly GlyphPixelAlignment FloorXY = new GlyphPixelAlignment(PixelAlignmentMode.Floor);
            public static readonly GlyphPixelAlignment FloorY = new GlyphPixelAlignment(PixelAlignmentMode.None, PixelAlignmentMode.Floor);

            public bool Equals (GlyphPixelAlignment other) {
                return (other.Horizontal == Horizontal) && (other.Vertical == Vertical);
            }

            public override bool Equals (object obj) {
                if (obj is GlyphPixelAlignment)
                    return Equals((GlyphPixelAlignment)obj);

                return false;
            }

            public override string ToString () {
                if (Horizontal == Vertical)
                    return Horizontal.ToString();
                else
                    return string.Format("{0}, {1}", Horizontal, Vertical);
            }
        }
    }
}
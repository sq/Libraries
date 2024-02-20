using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public struct StringLayoutEngine : IDisposable {
        internal sealed class UintComparer : IComparer<uint> {
            public static readonly UintComparer Instance = new UintComparer();

            public int Compare (uint x, uint y) {
                unchecked {
                    return (int)x - (int)y;
                }
            }
        }

        public DenseList<LayoutMarker> Markers;
        public DenseList<LayoutHitTest> HitTests;
        public DenseList<uint> WordWrapCharacters;

        public const int DefaultBufferPadding = 4;

        // Parameters
        public bool                characterWrap;
        public bool                wordWrap;
        public bool                hideOverflow;
        public bool                reverseOrder;
        public bool                measureOnly;
        public bool                disableMarkers;
        public bool                recordUsedTextures;
        public bool                expandHorizontallyWhenAligning;
        public bool                splitAtWrapCharactersOnly;
        public bool                includeTrailingWhitespace;
        public bool                clearUserData;
        private ArraySegment<BitmapDrawCall> buffer;
        public Vector2?            position;
        public Color?              overrideColor;
        public Color               defaultColor;
        public Color               addColor;
        public DrawCallSortKey     sortKey;
        public int                 characterSkipCount;
        public int?                characterLimit;
        public int?                lineLimit;
        public int?                lineBreakLimit;
        public float               scale;
        private float              _spacingMinusOne;
        public float               additionalLineSpacing;
        public float               xOffsetOfFirstLine;
        public float               xOffsetOfWrappedLine;
        public float               xOffsetOfNewLine;
        public float               desiredWidth;
        public float               extraLineBreakSpacing;
        public float?              maxExpansionPerSpace;
        public float?              lineBreakAtX;
        public float?              stopAtY;
        public GlyphPixelAlignment alignToPixels;
        public HorizontalAlignment alignment;
        public uint?               replacementCodepoint;
        public Func<ArraySegment<BitmapDrawCall>, ArraySegment<BitmapDrawCall>> growBuffer;
        public Vector4             userData;
        public Vector4             imageUserData;
        public DenseList<AbstractTextureReference> usedTextures;

        public float spacing {
            get {
                return _spacingMinusOne + 1;
            }
            set {
                _spacingMinusOne = value - 1;
            }
        }

        // State
        public  float  maxLineHeight;
        public  float  currentLineMaxX, currentLineMaxXUnconstrained;
        private float  initialLineXOffset;
        private float  currentLineWrapPointLeft, currentLineWhitespaceMaxX;
        private float  maxX, maxY, maxXUnconstrained, maxYUnconstrained;
        private float  initialLineSpacing, currentLineSpacing;
        private float  currentXOverhang;
        private float  currentBaseline;
        private float  maxLineSpacing;
        public  float? currentLineBreakAtX;
        public Vector2 actualPosition, characterOffset, characterOffsetUnconstrained;
        public Bounds  firstCharacterBounds, lastCharacterBounds;
        public  int    drawCallsWritten, drawCallsSuppressed;
        private int    bufferWritePosition, wordStartWritePosition, baselineAdjustmentStart;
        private int _rowIndex, _colIndex, _wordIndex;
        private int    wordStartColumn;
        Vector2        wordStartOffset;
        private bool   allowBufferGrowth, suppress, suppressForHorizontalOverflow, previousGlyphWasDead, 
            newLinePending, wordWrapSuppressed;
        private AbstractTextureReference lastUsedTexture;
        private DenseList<Bounds> boxes;

        public int     rowIndex => _rowIndex;
        public int     colIndex => _colIndex;
        public int     wordIndex => _wordIndex;

        public int     currentCharacterIndex { get; private set; }

        private bool IsInitialized;

        public void Initialize () {
            actualPosition = position.GetValueOrDefault(Vector2.Zero);
            characterOffsetUnconstrained = characterOffset = new Vector2(xOffsetOfFirstLine, 0);
            initialLineXOffset = characterOffset.X;

            previousGlyphWasDead = suppress = suppressForHorizontalOverflow = false;

            bufferWritePosition = 0;
            drawCallsWritten = 0;
            drawCallsSuppressed = 0;
            wordStartWritePosition = -1;
            wordStartOffset = Vector2.Zero;
            wordStartColumn = 0;
            _rowIndex = _colIndex = _wordIndex = 0;
            wordWrapSuppressed = false;
            initialLineSpacing = 0;
            currentBaseline = 0;
            currentLineSpacing = 0;
            maxLineSpacing = 0;
            currentXOverhang = 0;

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
                m.Bounds.UnsafeFastClear();
                Markers[i] = m;
            }

            currentCharacterIndex = 0;
            lastUsedTexture = null;
            boxes = default(DenseList<Bounds>);
            ComputeLineBreakAtX();

            IsInitialized = true;
        }

        public void GetBuffer (out ArraySegment<BitmapDrawCall> result) {
            result = this.buffer;
        }

        public void SetBuffer (ArraySegment<BitmapDrawCall> buffer, bool allowResize) {
            this.buffer = buffer;
            allowBufferGrowth = allowResize;
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

        private void ProcessMarkers (ref Bounds bounds, int currentCodepointSize, int? drawCallIndex, bool splitMarker, bool didWrapWord) {
            if (measureOnly || disableMarkers)
                return;
            if (suppress || suppressForHorizontalOverflow)
                return;

            var characterIndex1 = currentCharacterIndex - currentCodepointSize + 1;
            var characterIndex2 = currentCharacterIndex;
            for (int i = 0; i < Markers.Count; i++) {
                var m = Markers[i];
                if (m.FirstCharacterIndex > characterIndex2)
                    continue;
                if (m.LastCharacterIndex < characterIndex1)
                    continue;
                var curr = m.Bounds.LastOrDefault();
                if (curr != default(Bounds)) {
                    if (splitMarker && !didWrapWord) {
                        var newBounds = bounds;
                        if (m.CurrentSplitGlyphCount > 0) {
                            newBounds.TopLeft.X = Math.Min(curr.BottomRight.X, bounds.TopLeft.X);
                            newBounds.TopLeft.Y = Math.Min(curr.TopLeft.Y, bounds.TopLeft.Y);
                        }
                        m.CurrentSplitGlyphCount = 0;
                        m.Bounds.Add(newBounds);
                    } else if (didWrapWord && splitMarker && (m.CurrentSplitGlyphCount == 0)) {
                        m.Bounds[m.Bounds.Count - 1] = bounds;
                    } else {
                        var newBounds = Bounds.FromUnion(bounds, curr);
                        m.Bounds[m.Bounds.Count - 1] = newBounds;
                    }
                } else if (bounds != default(Bounds))
                    m.Bounds.Add(bounds);

                if (drawCallIndex != null) {
                    m.GlyphCount++;
                    m.CurrentSplitGlyphCount++;
                }

                m.FirstWordIndex = m.FirstWordIndex ?? wordIndex;
                m.FirstLineIndex = m.FirstLineIndex ?? (ushort)_rowIndex;
                m.LineCount = (ushort)(_rowIndex - m.FirstLineIndex.Value);
                m.FirstDrawCallIndex = m.FirstDrawCallIndex ?? drawCallIndex;
                m.LastDrawCallIndex = (drawCallIndex ?? m.LastDrawCallIndex);
                Markers[i] = m;
            }
        }

        private void ProcessLineSpacingChange_Slow (in ArraySegment<BitmapDrawCall> buffer, float newLineSpacing, float newBaseline) {
            if (bufferWritePosition > baselineAdjustmentStart) {
                var yOffset = newBaseline - currentBaseline;
                for (int i = baselineAdjustmentStart; i < bufferWritePosition; i++) {
                    buffer.Array[buffer.Offset + i].Position.Y += yOffset * (1 - buffer.Array[buffer.Offset + i].UserData.W);
                }

                if (!measureOnly) {
                    for (int i = 0; i < Markers.Count; i++) {
                        var m = Markers[i];
                        if (m.Bounds.Count <= 0)
                            continue;
                        if (m.FirstCharacterIndex > bufferWritePosition)
                            continue;
                        if (m.LastCharacterIndex < baselineAdjustmentStart)
                            continue;
                        // FIXME
                        var b = m.Bounds.LastOrDefault();
                        b.TopLeft.Y += yOffset;
                        b.BottomRight.Y += yOffset;
                        m.Bounds[m.Bounds.Count - 1] = b;
                        Markers[i] = m;
                    }
                }
            }
            currentBaseline = newBaseline;
            baselineAdjustmentStart = bufferWritePosition;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ProcessLineSpacingChange (in ArraySegment<BitmapDrawCall> buffer, float newLineSpacing, float newBaseline) {
            if (newBaseline > currentBaseline)
                ProcessLineSpacingChange_Slow(buffer, newLineSpacing, newBaseline);

            if (newLineSpacing > currentLineSpacing)
                currentLineSpacing = newLineSpacing;

            ComputeLineBreakAtX();
        }

        private void WrapWord (
            ArraySegment<BitmapDrawCall> buffer,
            Vector2 firstOffset, int firstGlyphIndex, int lastGlyphIndex, 
            float glyphLineSpacing, float glyphBaseline, float currentWordSize
        ) {
            // FIXME: Can this ever happen?
            if (currentLineWhitespaceMaxX <= 0)
                maxX = Math.Max(maxX, currentLineMaxX);
            else
                maxX = Math.Max(maxX, currentLineWrapPointLeft);

            var previousLineSpacing = currentLineSpacing;
            var previousBaseline = currentBaseline;

            currentBaseline = glyphBaseline;
            initialLineSpacing = currentLineSpacing = glyphLineSpacing;

            // Remove the effect of the previous baseline adjustment then realign to our new baseline
            var yOffset = -previousBaseline + previousLineSpacing + currentBaseline;

            var suppressedByLineLimit = lineLimit.HasValue && (lineLimit.Value <= 0);
            var adjustment = Vector2.Zero;

            var xOffset = xOffsetOfWrappedLine;
            AdjustCharacterOffsetForBoxes(ref xOffset, characterOffset.Y + yOffset, currentLineSpacing, leftPad: 0f);
            var oldFirstGlyphBounds = (firstGlyphIndex > 0)
                ? buffer.Array[buffer.Offset + firstGlyphIndex - 1].EstimateDrawBounds()
                : default(Bounds);

            float wordX1 = 0, wordX2 = 0;

            for (var i = firstGlyphIndex; i <= lastGlyphIndex; i++) {
                var dc = buffer.Array[buffer.Offset + i];
                if ((dc.UserData.Y > 0) || (dc.UserData.Z != 0))
                    continue;
                var newCharacterX = (xOffset) + (dc.Position.X - firstOffset.X);
                if (i == firstGlyphIndex)
                    wordX1 = dc.Position.X;

                // FIXME: Baseline?
                var newPosition = new Vector2(newCharacterX, dc.Position.Y + yOffset);
                if (i == firstGlyphIndex)
                    adjustment = newPosition - dc.Position;
                dc.Position = newPosition;

                unchecked {
                    dc.LocalData2 = (byte)(dc.LocalData2 + 1);
                }

                if (i == lastGlyphIndex) {
                    var db = dc.EstimateDrawBounds();
                    wordX2 = db.BottomRight.X;
                }

                if (suppressedByLineLimit && hideOverflow)
                    // HACK: Just setting multiplycolor or scale etc isn't enough since a layout filter may modify it
                    buffer.Array[buffer.Offset + i] = default(BitmapDrawCall);
                else
                    buffer.Array[buffer.Offset + i] = dc;
            }

            // FIXME: If we hit a box on the right edge, this is broken
            characterOffset.X = xOffset + (characterOffset.X - firstOffset.X);
            characterOffset.Y += previousLineSpacing;

            // HACK: firstOffset may include whitespace so we want to pull the right edge in.
            //  Without doing this, the size rect for the string is too large.
            var actualRightEdge = firstOffset.X;
            var newFirstGlyphBounds = (firstGlyphIndex > 0)
                ? buffer.Array[buffer.Offset + firstGlyphIndex - 1].EstimateDrawBounds()
                : default(Bounds);
            if (firstGlyphIndex > 0)
                actualRightEdge = Math.Min(
                    actualRightEdge, newFirstGlyphBounds.BottomRight.X
                );

            // FIXME: This will break if the word mixes styles
            baselineAdjustmentStart = firstGlyphIndex;

            if (Markers.Count <= 0)
                return;

            // HACK: If a marker is inside of the wrapped word or around it, we need to adjust the marker to account
            //  for the fact that its anchoring characters have just moved
            for (int i = 0; i < Markers.Count; i++) {
                ref var m = ref Markers.Item(i);

                if ((m.FirstDrawCallIndex == null) || (m.FirstDrawCallIndex > lastGlyphIndex))
                    continue;
                if (m.LastDrawCallIndex < firstGlyphIndex)
                    continue;

                for (int j = 0; j < m.Bounds.Count; j++) {
                    var oldBounds = m.Bounds[j];

                    if (oldBounds.TopLeft.X < firstOffset.X)
                        continue;

                    var newBounds = oldBounds.Translate(adjustment);

                    newBounds.TopLeft.X = (position?.X ?? 0) + xOffset;
                    newBounds.TopLeft.Y = Math.Max(newBounds.TopLeft.Y, newBounds.BottomRight.Y - currentLineSpacing);

                    m.Bounds[j] = newBounds;
                }
            }
        }

        private float AdjustCharacterOffsetForBoxes (ref float x, float y1, float h, float? leftPad = null) {
            if (boxes.Count < 1)
                return 0;

            Bounds b;
            float result = 0;
            var tempBounds = Bounds.FromPositionAndSize(x, y1, 1f, Math.Max(h, 1));
            if ((_rowIndex == 0) && (leftPad == null))
                leftPad = xOffsetOfFirstLine;
            for (int i = 0, c = boxes.Count; i < c; i++) {
                boxes.GetItem(i, out b);
                b.BottomRight.X += (leftPad ?? 0f);
                if (!Bounds.Intersect(b, tempBounds))
                    continue;
                var oldX = x;
                var newX = Math.Max(x, b.BottomRight.X);
                if (!currentLineBreakAtX.HasValue || (newX < currentLineBreakAtX.Value)) {
                    x = newX;
                    result += (oldX - x);
                }
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Snap (ref float value, PixelAlignmentMode mode) {
            switch (mode) {
                case PixelAlignmentMode.Floor:
                    value = (float)Math.Floor(value);
                    break;
                case PixelAlignmentMode.FloorHalf:
                    value = (float)Math.Floor(value * 2) / 2;
                    break;
                case PixelAlignmentMode.FloorQuarter:
                    value = (float)Math.Floor(value * 4) / 4;
                    break;
                case PixelAlignmentMode.Round:
                    value = (float)Math.Round(value, 0, MidpointRounding.AwayFromZero);
                    break;
                case PixelAlignmentMode.RoundHalf:
                    value = (float)Math.Round(value * 2, 0, MidpointRounding.AwayFromZero) / 2;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Snap (ref float x) {
            Snap(ref x, alignToPixels.Horizontal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Snap (Vector2 pos, out Vector2 result) {
            result = pos;
            Snap(ref result.X, alignToPixels.Horizontal);
            Snap(ref result.Y, alignToPixels.Vertical);
        }

        private void AlignLine (
            ArraySegment<BitmapDrawCall> buffer, int line, HorizontalAlignment globalAlignment,
            int firstIndex, int lastIndex, float originalMaxX
        ) {
            Bounds firstDc = default(Bounds), endDc = default(Bounds);
            int firstWord = 999999, lastWord = 0, firstValidIndex = -1, lastValidIndex = -1;
            for (int i = firstIndex; i <= lastIndex; i++) {
                var dc = buffer.Array[buffer.Offset + i];
                firstWord = Math.Min(firstWord, dc.LocalData1);
                lastWord = Math.Max(lastWord, dc.LocalData1);
                if (dc.UserData.X > 0)
                    continue;

                if (firstValidIndex < 0)
                    firstValidIndex = i;
                lastValidIndex = i;
            }

            if (firstValidIndex >= 0)
                firstDc = buffer.Array[buffer.Offset + firstValidIndex].EstimateDrawBounds();
            if (lastValidIndex >= 0)
                endDc = buffer.Array[buffer.Offset + lastValidIndex].EstimateDrawBounds();

            int wordCountMinusOne = (firstWord < lastWord)
                ? lastWord - firstWord
                : 0; // FIXME: detect and handle wrap-around. Will only happen with very large word count

            // In justify mode if there is only one word or one character on the line, fall back to centering, otherwise
            //  the math will have some nasty divides by zero or one
            var localAlignment = globalAlignment;
            if (localAlignment >= HorizontalAlignment.JustifyWords) {
                if (wordCountMinusOne < 1) {
                    if (localAlignment == HorizontalAlignment.JustifyWordsCentered)
                        localAlignment = HorizontalAlignment.Center;
                    else
                        return;
                }
            } else if (localAlignment >= HorizontalAlignment.JustifyCharacters) {
                if (lastIndex <= (firstIndex + 1)) {
                    if (localAlignment == HorizontalAlignment.JustifyCharactersCentered)
                        localAlignment = HorizontalAlignment.Center;
                    else
                        return;
                }
            }

            float lineWidth = (endDc.BottomRight.X - firstDc.TopLeft.X),
                // FIXME: Why is this padding here?
                localMinX = firstDc.TopLeft.X - actualPosition.X, localMaxX = originalMaxX - 0.1f,
                actualDesiredWidth = Math.Max(desiredWidth, localMaxX);

            if (
                (actualDesiredWidth > 1) && expandHorizontallyWhenAligning
            )
                localMaxX = Math.Max(localMaxX, actualDesiredWidth);

            // HACK: Attempt to ensure that alignment doesn't penetrate boxes
            // FIXME: This doesn't work and I can't figure out why
            /*
            AdjustCharacterOffsetForBoxes(ref localMinX, firstDc.TopLeft.Y, Math.Max(firstDc.Size.Y, endDc.Size.Y));
            AdjustCharacterOffsetForBoxes(ref localMaxX, firstDc.TopLeft.Y, Math.Max(firstDc.Size.Y, endDc.Size.Y));
            */

            float whitespace;
            // Factor in text starting offset from the left side, if we don't the text
            //  will overhang to the right after alignment. This is usually caused by boxes
            // FIXME: This doesn't seem to work anymore
            whitespace = localMaxX - lineWidth - localMinX;

            // HACK: Don't do anything if the line is too big, just overflow to the right.
            //  Otherwise, the sizing info will be wrong and bad things happen.
            if (whitespace <= 0)
                whitespace = 0;

            // HACK: We compute this before halving the whitespace, so that the size of 
            //  the layout is enough to ensure manually centering the whole layout will
            //  still preserve per-line centering.
            maxX = Math.Max(maxX, whitespace + lineWidth);

            if (localAlignment == HorizontalAlignment.Center)
                whitespace /= 2;

            // In JustifyCharacters mode we spread all the characters out to fill the line.
            // In JustifyWords mode we spread all the extra whitespace into the gaps between words.
            // In both cases the goal is for the last character of each line to end up flush
            //  against the right side of the layout box.
            float characterSpacing = 0, wordSpacing = 0, accumulatedSpacing = 0;
            if (localAlignment >= HorizontalAlignment.JustifyWords) {
                wordSpacing = whitespace / wordCountMinusOne;

                if (maxExpansionPerSpace.HasValue && wordSpacing > maxExpansionPerSpace.Value) {
                    wordSpacing = 0;
                    whitespace = (localAlignment == HorizontalAlignment.JustifyWordsCentered)
                        ? whitespace / 2
                        : 0;
                } else {
                    whitespace = 0;
                }
            } else if (localAlignment >= HorizontalAlignment.JustifyCharacters) {
                characterSpacing = whitespace / (lastIndex - firstIndex);

                if (maxExpansionPerSpace.HasValue && characterSpacing > maxExpansionPerSpace.Value) {
                    characterSpacing = 0;
                    whitespace = (localAlignment == HorizontalAlignment.JustifyCharactersCentered)
                        ? whitespace / 2
                        : 0;
                } else {
                    whitespace = 0;
                }
            }

            whitespace = (float)Math.Round(whitespace, alignToPixels.Horizontal != PixelAlignmentMode.Floor ? 2 : 0, MidpointRounding.AwayFromZero);

            // FIXME: This double-applies whitespace for some reason, or doesn't work at all?
            /*
            for (int j = 0, n = Markers.Count; j < n; j++) {
                Markers.TryGetItem(j, out LayoutMarker m);
                // FIXME: Multiline markers
                if ((m.FirstLineIndex >= line) && (m.LastLineIndex <= line)) {
                    for (int k = 0, kn = m.Bounds.Count; k < kn; k++) {
                        m.Bounds.TryGetItem(k, out Bounds b);
                        b.TopLeft.X += whitespace;
                        b.BottomRight.X += whitespace;
                        m.Bounds[k] = b;
                    }
                }
                Markers[j] = m;
            }
            */

            var previousWordIndex = firstWord;

            for (int j = firstIndex; j <= lastIndex; j++) {
                if (buffer.Array[buffer.Offset + j].UserData.X > 0)
                    continue;

                // If a word transition has happened, we want to shift the following characters
                //  over to the right to consume the extra whitespace in word justify mode.
                var currentWordIndex = (int)buffer.Array[buffer.Offset + j].LocalData1;
                if (currentWordIndex != previousWordIndex) {
                    previousWordIndex = currentWordIndex;
                    accumulatedSpacing += wordSpacing;
                }

                var computedOffset = whitespace + accumulatedSpacing;
                buffer.Array[buffer.Offset + j].Position.X += computedOffset;

                // In character justify mode we just spread all the characters out.
                accumulatedSpacing += characterSpacing;
            }

            for (int j = 0, c = Markers.Count; j < c; j++) {
                ref var marker = ref Markers.Item(j);
                // HACK: If word justification is enabled, do approximate justification of the boxes too
                var wordSpace = marker.FirstWordIndex.HasValue 
                    ? (marker.FirstWordIndex.Value - firstWord) * wordSpacing
                    : 0f;
                // FIXME: Multiline boxes
                if ((marker.FirstLineIndex != line) || (marker.LastLineIndex != line))
                    continue;

                for (int k = 0, ck = marker.Bounds.Count; k < ck; k++) {
                    ref var b = ref marker.Bounds.Item(k);
                    b.TopLeft.X += whitespace + wordSpace;
                    b.BottomRight.X += whitespace + wordSpace;
                }
            }
        }

        private void AlignLines (
            ArraySegment<BitmapDrawCall> buffer, HorizontalAlignment alignment
        ) {
            if (buffer.Count == 0)
                return;

            int lineStartIndex = 0;
            int currentLine = buffer.Array[buffer.Offset].LocalData2;

            var originalMaxX = maxX;

            for (var i = 1; i < buffer.Count; i++) {
                var line = buffer.Array[buffer.Offset + i].LocalData2;

                if (line != currentLine) {
                    AlignLine(buffer, (int)currentLine, alignment, lineStartIndex, i - 1, originalMaxX);

                    lineStartIndex = i;
                    currentLine = line;
                }
            }

            AlignLine(buffer, _rowIndex, alignment, lineStartIndex, buffer.Count - 1, originalMaxX);
        }

        private void SnapPositions (ArraySegment<BitmapDrawCall> buffer) {
            for (var i = 0; i < buffer.Count; i++)
                Snap(buffer.Array[buffer.Offset + i].Position, out buffer.Array[buffer.Offset + i].Position);
        }

        private void EnsureBufferCapacity (int count) {
            int paddedCount = count + DefaultBufferPadding;

            if (buffer.Array == null) {
                allowBufferGrowth = true;
                buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[paddedCount]);
            } else if (buffer.Count < count) {
                if (allowBufferGrowth) {
                    var newSize = UnorderedList<BitmapDrawCall>.PickGrowthSize(buffer.Count, paddedCount);
                    buffer = UnorderedList<BitmapDrawCall>.Allocator.Resize(buffer, newSize);
                } else if (buffer.Count >= count) {
                    // This is OK, there should be enough room...
                    ;
                } else {
                    throw new InvalidOperationException("Buffer too small");
                }
            }
        }

        public bool IsTruncated =>
            // FIXME: < 0 instead of <= 0?
            ((lineLimit ?? int.MaxValue) <= 0) ||
            ((lineBreakLimit ?? int.MaxValue) <= 0) ||
            ((characterLimit ?? int.MaxValue) <= 0);

        public void CreateBox (
            float width, float height, out Bounds box
        ) {
            box = Bounds.FromPositionAndSize(characterOffset.X, characterOffset.Y, width, height);
            CreateBox(ref box);
        }

        public void CreateBox (ref Bounds box) {
            boxes.Add(ref box);
        }

        /// <summary>
        /// Move the character offset forward as if an image of this size had been appended,
        ///  without actually appending anything
        /// </summary>
        public void Advance (
            float width, float height, bool doNotAdjustLineSpacing = false, bool considerBoxes = true
        ) {
            var lineSpacing = height + additionalLineSpacing;
            float x = characterOffset.X;
            if (!doNotAdjustLineSpacing)
                ProcessLineSpacingChange(buffer, lineSpacing, lineSpacing);
            var position = new Vector2(characterOffset.X, characterOffset.Y + currentBaseline);
            characterOffset.X += width;
            characterOffsetUnconstrained.X += width;
            if (_colIndex == 0) {
                characterOffset.X = Math.Max(characterOffset.X, 0);
                characterOffsetUnconstrained.X = Math.Max(characterOffsetUnconstrained.X, 0);
            }
            if (considerBoxes) {
                AdjustCharacterOffsetForBoxes(ref characterOffset.X, characterOffset.Y, Math.Max(lineSpacing, height));
                AdjustCharacterOffsetForBoxes(ref characterOffsetUnconstrained.X, characterOffsetUnconstrained.Y, Math.Max(lineSpacing, height));
            }
            currentLineMaxX = Math.Max(currentLineMaxX, x);
            currentLineMaxXUnconstrained = Math.Max(currentLineMaxXUnconstrained, x);
            if (characterSkipCount <= 0) {
                characterLimit--;
            } else {
                characterSkipCount--;
            }
        }

        /// <summary>
        /// Append an image as if it were a character
        /// </summary>
        /// <param name="verticalAlignment">Specifies the image's Y origin relative to the baseline</param>
        public void AppendImage (
            Texture2D texture, Bounds? textureRegion = null,
            Vector2? margin = null, 
            float scale = 1, float verticalAlignment = 1,
            Color? multiplyColor = null, bool doNotAdjustLineSpacing = false,
            bool createBox = false, float? hardXAlignment = null, float? hardYAlignment = null,
            float? overrideWidth = null, float? overrideHeight = null, float? maxWidthPercent = null,
            bool clear = false
        ) {
            float x = characterOffset.X, y = characterOffset.Y;

            var dc = new BitmapDrawCall {
                Position = Vector2.Zero,
                Texture = texture,
                SortKey = sortKey,
                TextureRegion = textureRegion ?? Bounds.Unit,
                ScaleF = scale * this.scale,
                MultiplyColor = multiplyColor ?? overrideColor ?? Color.White,
                AddColor = addColor,
                Origin = new Vector2(0, 0),
                // HACK
                UserData = new Vector4(
                    hardXAlignment.HasValue ? 1 : 0, 
                    hardYAlignment.HasValue ? 1 : 0, 
                    1, // This is an image
                    (hardYAlignment.HasValue ? 1 : 1 - verticalAlignment)
                )
            };
            clearUserData = true;
            var estimatedBounds = dc.EstimateDrawBounds();

            float widthConstraint = maxWidthPercent.HasValue
                ? Math.Max(desiredWidth, currentLineBreakAtX ?? lineBreakAtX ?? desiredWidth) * maxWidthPercent.Value / 100f
                : float.MaxValue;
            if (widthConstraint <= 0)
                widthConstraint = float.MaxValue;

            var constrainedWidth = Math.Min(widthConstraint, overrideWidth ?? estimatedBounds.Size.X);
            if (constrainedWidth < estimatedBounds.Size.X) {
                dc.ScaleF = constrainedWidth / texture.Width;
                estimatedBounds = dc.EstimateDrawBounds();
            }

            // FIXME: Rewrite all this stuff, it makes no sense

            estimatedBounds.BottomRight.X = estimatedBounds.TopLeft.X + constrainedWidth;
            if (maxWidthPercent >= 100)
                estimatedBounds.BottomRight.Y = estimatedBounds.TopLeft.Y + (overrideHeight ?? estimatedBounds.Size.Y);
            var lineSpacing = (float)Math.Ceiling(estimatedBounds.Size.Y + additionalLineSpacing + ((margin?.Y ?? 0) * 0.5f));
            if (!doNotAdjustLineSpacing)
                ProcessLineSpacingChange(buffer, lineSpacing, lineSpacing);

            float y1 = y,
                y2 = y + currentBaseline - estimatedBounds.Size.Y - (margin?.Y * 0.5f ?? 0);
            float? overrideX = null, overrideY = null;
            if (hardXAlignment.HasValue)
                overrideX = Arithmetic.Lerp(0, (lineBreakAtX ?? 0f) - estimatedBounds.Size.X, hardXAlignment.Value);
            if (hardYAlignment.HasValue)
                overrideY = Arithmetic.Lerp(0, (stopAtY ?? 0f) - estimatedBounds.Size.Y, hardYAlignment.Value);

            if (clear) {
                var testBox = estimatedBounds.Translate(new Vector2(overrideX ?? x, y1));
                for (int i = 0, c = boxes.Count; i < c; i++) {
                    ref var box = ref boxes.Item(i);
                    if (!box.Intersects(testBox))
                        continue;
                    var adjustment = Math.Max(0, box.BottomRight.Y - y1);
                    characterOffset.Y += adjustment;
                    // FIXME: Unconstrained?
                    y1 += adjustment;
                    y2 += adjustment;
                    testBox.TopLeft.Y += adjustment;
                    testBox.BottomRight.Y += adjustment;
                }
            }

            if (createBox)
                y2 = Math.Max(y1, y2);

            dc.Position = new Vector2(
                overrideX ?? x, 
                overrideY ?? Arithmetic.Lerp(y1, y2, verticalAlignment)
            );
            estimatedBounds = dc.EstimateDrawBounds();
            estimatedBounds.BottomRight.X = estimatedBounds.TopLeft.X + Math.Min(constrainedWidth, overrideWidth ?? estimatedBounds.Size.X);
            if (maxWidthPercent >= 100)
                estimatedBounds.BottomRight.Y = estimatedBounds.TopLeft.Y + (overrideHeight ?? estimatedBounds.Size.Y);
            var sizeX = Math.Min(constrainedWidth, overrideWidth ?? estimatedBounds.Size.X) + (margin?.X ?? 0);
            if (!overrideX.HasValue) {
                characterOffset.X += sizeX;
                characterOffsetUnconstrained.X += sizeX;
                AdjustCharacterOffsetForBoxes(ref characterOffset.X, characterOffset.Y, currentLineSpacing);
                AdjustCharacterOffsetForBoxes(ref characterOffsetUnconstrained.X, characterOffsetUnconstrained.Y, currentLineSpacing);
            }
            dc.Position += actualPosition;
            // FIXME: Margins and stuff
            AppendDrawCall(ref dc, overrideX ?? x, 1, false, currentLineSpacing, 0f, x, ref estimatedBounds, false, false);
            maxY = Math.Max(maxY, characterOffset.Y + estimatedBounds.Size.Y);
            maxYUnconstrained = Math.Max(maxYUnconstrained, characterOffsetUnconstrained.Y + estimatedBounds.Size.Y);

            if (createBox) {
                var mx = (margin?.X ?? 0) / 2f;
                var my = (margin?.Y ?? 0) / 2f;
                estimatedBounds.TopLeft.X -= mx;
                estimatedBounds.TopLeft.Y -= my;
                estimatedBounds.BottomRight.X += mx;
                estimatedBounds.BottomRight.Y += my;
                CreateBox(ref estimatedBounds);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ComputeSuppress (bool? overrideSuppress) {
            if (suppressForHorizontalOverflow)
                return true;
            return overrideSuppress ?? suppress;
        }

        public void AppendText<TGlyphSource> (
            TGlyphSource font, in AbstractString text,
            int? start = null, int? end = null, bool? overrideSuppress = null
        ) where TGlyphSource : IGlyphSource {
            if (!IsInitialized)
                throw new InvalidOperationException("Call Initialize first");

            if (!typeof(TGlyphSource).IsValueType) {
                if (font == null)
                    throw new ArgumentNullException("font");
            }
            if (text.IsNull)
                throw new ArgumentNullException("text");

            if (!measureOnly)
                EnsureBufferCapacity(bufferWritePosition + text.Length);

            var effectiveScale = scale / Math.Max(0.0001f, font.DPIScaleFactor);
            var effectiveSpacing = spacing;
            KerningData thisKerning = default, nextKerning = default;

            var drawCall = new BitmapDrawCall {
                MultiplyColor = defaultColor,
                ScaleF = effectiveScale,
                SortKey = sortKey,
                AddColor = addColor
            };

            bool hasBoxes = boxes.Count > 0, hasKerningNow = false, hasKerningNext = false;

            for (int i = start ?? 0, l = Math.Min(end ?? text.Length, text.Length); i < l; i++) {
                if (lineLimit.HasValue && lineLimit.Value <= 0)
                    suppress = true;

                DecodeCodepoint(text, ref i, l, out char ch1, out int currentCodepointSize, out uint codepoint);

                AnalyzeWhitespace(
                    ch1, codepoint, out bool isWhiteSpace, out bool forcedWrap, out bool lineBreak, 
                    out bool deadGlyph, out bool isWordWrapPoint, out bool didWrapWord
                );

                if (isWordWrapPoint) {
                    _wordIndex++;
                    currentLineWrapPointLeft = Math.Max(currentLineWrapPointLeft, characterOffset.X);
                    if (isWhiteSpace)
                        wordStartWritePosition = -1;
                    else
                        wordStartWritePosition = bufferWritePosition;
                    wordStartOffset = characterOffset;
                    wordStartColumn = _colIndex;
                    wordWrapSuppressed = false;
                } else {
                    if (wordStartWritePosition < 0) {
                        wordStartWritePosition = bufferWritePosition;
                        wordStartOffset = characterOffset;
                        wordStartColumn = _colIndex;
                    }
                }

                BuildGlyphInformation(
                    font, effectiveScale, effectiveSpacing, ch1, codepoint,
                    out deadGlyph, out Glyph glyph, out float glyphLineSpacing, out float glyphBaseline
                );

                // FIXME: Kerning across multiple AppendText calls
                if ((glyph.KerningProvider != null) && (i < l - 2)) {
                    var temp = i + 1;
                    DecodeCodepoint(text, ref temp, l, out _, out _, out var codepoint2);
                    // FIXME: Also do adjustment for next glyph!
                    // FIXME: Cache the result of this GetGlyph call and use it next iteration to reduce CPU usage
                    if (
                        font.GetGlyph(codepoint2, out var glyph2) &&
                        (glyph2.KerningProvider == glyph.KerningProvider)
                    ) {
                        hasKerningNow = hasKerningNext = glyph.KerningProvider.TryGetKerning(glyph.GlyphId, glyph2.GlyphId, ref thisKerning, ref nextKerning);
                    }
                }

                if (hasKerningNow) {
                    glyph.XOffset += thisKerning.XOffset;
                    glyph.YOffset += thisKerning.YOffset;
                    glyph.RightSideBearing += thisKerning.RightSideBearing;
                }

                if (hasKerningNext)
                    thisKerning = nextKerning;
                hasKerningNow = hasKerningNext;
                hasKerningNext = false;

                float x =
                    characterOffset.X +
                    ((
                        glyph.WidthIncludingBearing + glyph.CharacterSpacing
                    ) * effectiveScale);

                if ((x >= currentLineBreakAtX) && (_colIndex > 0)) {
                    if (deadGlyph || isWhiteSpace) {
                        // HACK: We're wrapping a dead glyph or whitespace.
                        // We want to wrap and then suppress it because wrapping a space or tab
                        //  shouldn't actually indent the next line (it just looks gross if it does).
                        // The alternative (and old behavior) is to not wrap whitespace,
                        //  but that produces layouts wider than the break point (which is awful).
                        // The suppression is done further below.
                        forcedWrap = true;
                    } else
                        forcedWrap = true;
                } else if (suppressForHorizontalOverflow && isWordWrapPoint && !lineBreak)
                    forcedWrap = true;

                if (forcedWrap)
                    PerformForcedWrap(x, isWordWrapPoint, ref lineBreak, ref didWrapWord, glyphLineSpacing, glyphBaseline);

                if (lineBreak)
                    PerformLineBreak(forcedWrap);

                // We performed a wrap for a whitespace character. Don't advance x or do anything else.
                // We want to bail out here *even* if the wrap operation did not produce a line break
                //  (which will happen if the line break position is almost exactly the same as x)
                if (forcedWrap && isWhiteSpace)
                    continue;

                // HACK: Recompute after wrapping
                x =
                    characterOffset.X +
                    (glyph.WidthIncludingBearing + glyph.CharacterSpacing) * effectiveScale;
                var yOffset = currentBaseline - glyphBaseline;
                var xUnconstrained = x - characterOffset.X + characterOffsetUnconstrained.X;

                if (deadGlyph || isWhiteSpace)
                    ProcessDeadGlyph(effectiveScale, x, isWhiteSpace, deadGlyph, glyph, yOffset);

                if (deadGlyph)
                    continue;

                if (!ComputeSuppress(overrideSuppress))
                    characterOffset.X += (glyph.CharacterSpacing * effectiveScale);
                characterOffsetUnconstrained.X += (glyph.CharacterSpacing * effectiveScale);
                // FIXME: Is this y/h right
                if (hasBoxes) {
                    AdjustCharacterOffsetForBoxes(ref characterOffset.X, characterOffset.Y, glyph.LineSpacing * effectiveScale);
                    AdjustCharacterOffsetForBoxes(ref characterOffsetUnconstrained.X, characterOffsetUnconstrained.Y, glyph.LineSpacing * effectiveScale);
                }

                // FIXME: Shift this stuff below into the append function
                var scaledGlyphSize = new Vector2(
                    glyph.WidthIncludingBearing,
                    glyph.LineSpacing
                ) * effectiveScale;

                if (!ComputeSuppress(overrideSuppress))
                    lastCharacterBounds = Bounds.FromPositionAndSize(
                        actualPosition + characterOffset + new Vector2(0, yOffset), scaledGlyphSize
                    );

                var testBounds = lastCharacterBounds;
                // FIXME: boxes

                ProcessHitTests(ref testBounds, testBounds.Center.X);

                if ((_rowIndex == 0) && (_colIndex == 0))
                    firstCharacterBounds = lastCharacterBounds;

                if (!ComputeSuppress(overrideSuppress))
                    characterOffset.X += glyph.LeftSideBearing * effectiveScale;
                characterOffsetUnconstrained.X += glyph.LeftSideBearing * effectiveScale;

                // If a glyph has negative overhang on the right side we want to make a note of that,
                //  so that if a line ends with negative overhang we can expand the layout to include it.
                currentXOverhang = (glyph.RightSideBearing < 0) ? -glyph.RightSideBearing : 0;

                if (!measureOnly && !isWhiteSpace) {
                    var glyphPosition = new Vector2(
                        actualPosition.X + (glyph.XOffset * effectiveScale) + characterOffset.X,
                        actualPosition.Y + (glyph.YOffset * effectiveScale) + characterOffset.Y + yOffset
                    );
                    drawCall.ScaleF = effectiveScale * glyph.RenderScale;
                    drawCall.Textures = new TextureSet(glyph.Texture);
                    drawCall.TextureRegion = glyph.BoundsInTexture;
                    drawCall.Position = glyphPosition;
                    drawCall.MultiplyColor = overrideColor ?? glyph.DefaultColor ?? defaultColor;
                }

                AppendDrawCall(
                    ref drawCall,
                    x, currentCodepointSize,
                    isWhiteSpace, glyphLineSpacing,
                    yOffset, xUnconstrained, ref testBounds,
                    lineBreak, didWrapWord, overrideSuppress
                );

                if (!ComputeSuppress(overrideSuppress))
                    characterOffset.X += (glyph.Width + glyph.RightSideBearing) * effectiveScale;
                characterOffsetUnconstrained.X += (glyph.Width + glyph.RightSideBearing) * effectiveScale;
                if (hasBoxes) {
                    AdjustCharacterOffsetForBoxes(ref characterOffset.X, characterOffset.Y, currentLineSpacing);
                    AdjustCharacterOffsetForBoxes(ref characterOffsetUnconstrained.X, characterOffsetUnconstrained.Y, currentLineSpacing);
                }
                ProcessLineSpacingChange(buffer, glyphLineSpacing, glyphBaseline);
                maxLineSpacing = Math.Max(maxLineSpacing, currentLineSpacing);

                currentCharacterIndex++;
                _colIndex += 1;
            }

            maxXUnconstrained = Math.Max(maxXUnconstrained, currentLineMaxXUnconstrained);
            maxX = Math.Max(maxX, currentLineMaxX);

            if (newLinePending) {
                var trailingSpace = currentLineSpacing;
                if (trailingSpace <= 0)
                    trailingSpace = font.LineSpacing;
                maxY += trailingSpace;
                maxYUnconstrained += trailingSpace;
                newLinePending = false;
            }
        }

        private void AnalyzeWhitespace (char ch1, uint codepoint, out bool isWhiteSpace, out bool forcedWrap, out bool lineBreak, out bool deadGlyph, out bool isWordWrapPoint, out bool didWrapWord) {
            isWhiteSpace = Unicode.IsWhiteSpace(ch1) && !replacementCodepoint.HasValue;
            forcedWrap = false;
            lineBreak = false;
            deadGlyph = false;
            didWrapWord = false;

            if (splitAtWrapCharactersOnly)
                isWordWrapPoint = WordWrapCharacters.BinarySearchNonRef(codepoint, UintComparer.Instance) >= 0;
            else
                isWordWrapPoint = isWhiteSpace || char.IsSeparator(ch1) ||
                    replacementCodepoint.HasValue || WordWrapCharacters.BinarySearchNonRef(codepoint, UintComparer.Instance) >= 0;

            if (codepoint > 255) {
                // HACK: Attempt to word-wrap at "other" punctuation in non-western character sets, which will include things like commas
                // This is less than ideal but .NET does not appear to expose the classification tables needed to do this correctly
                var category = CharUnicodeInfo.GetUnicodeCategory(ch1);
                if (category == UnicodeCategory.OtherPunctuation)
                    isWordWrapPoint = true;
            }
            // Wrapping and justify expansion should never occur for a non-breaking space
            if (codepoint == 0x00A0)
                isWordWrapPoint = false;

            if (ch1 == '\n')
                lineBreak = true;

            if (lineBreak) {
                if (lineLimit.HasValue) {
                    lineLimit--;
                    if (lineLimit.Value <= 0)
                        suppress = true;
                }
                if (lineBreakLimit.HasValue) {
                    lineBreakLimit--;
                    if (lineBreakLimit.Value <= 0)
                        suppress = true;
                }
                if (!suppress && includeTrailingWhitespace)
                    newLinePending = true;
            } else if (lineLimit.HasValue && lineLimit.Value <= 0) {
                suppress = true;
            }
        }

        private void DecodeCodepoint (in AbstractString text, ref int i, int l, out char ch1, out int currentCodepointSize, out uint codepoint) {
            char ch2 = i < (l - 1)
                    ? text[i + 1]
                    : '\0';
            ch1 = text[i];
            currentCodepointSize = 1;
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
                }
            }

            codepoint = replacementCodepoint ?? codepoint;
        }

        private void BuildGlyphInformation<TGlyphSource> (
            in TGlyphSource font, float effectiveScale, float effectiveSpacing, 
            char ch1, uint codepoint, out bool deadGlyph, out Glyph glyph, out float glyphLineSpacing, out float glyphBaseline
        ) where TGlyphSource : IGlyphSource {
            deadGlyph = !font.GetGlyph(codepoint, out glyph);

            glyphLineSpacing = glyph.LineSpacing * effectiveScale;
            glyphLineSpacing += additionalLineSpacing;
            glyphBaseline = glyph.Baseline * effectiveScale;
            if (deadGlyph) {
                if (currentLineSpacing > 0) {
                    glyphLineSpacing = currentLineSpacing;
                    glyphBaseline = currentBaseline;
                } else {
                    Glyph space;
                    if (font.GetGlyph(' ', out space)) {
                        glyphLineSpacing = space.LineSpacing * effectiveScale;
                        glyphLineSpacing += additionalLineSpacing;
                        glyphBaseline = space.Baseline * effectiveScale;
                    }
                }
            }

            // glyph.LeftSideBearing *= effectiveSpacing;
            float leftSideDelta = 0;
            if (effectiveSpacing >= 0)
                glyph.LeftSideBearing *= effectiveSpacing;
            else
                leftSideDelta = Math.Abs(glyph.LeftSideBearing * effectiveSpacing);
            glyph.RightSideBearing *= effectiveSpacing;
            glyph.RightSideBearing -= leftSideDelta;

            if (initialLineSpacing <= 0)
                initialLineSpacing = glyphLineSpacing;
            ProcessLineSpacingChange(buffer, glyphLineSpacing, glyphBaseline);

            // MonoGame#1355 rears its ugly head: If a character with negative left-side bearing is at the start of a line,
            //  we need to compensate for the bearing to prevent the character from extending outside of the layout bounds
            if (_colIndex == 0) {
                if (glyph.LeftSideBearing < 0)
                    glyph.LeftSideBearing = 0;
            }
        }

        private void ProcessDeadGlyph (float effectiveScale, float x, bool isWhiteSpace, bool deadGlyph, Glyph glyph, float yOffset) {
            if (deadGlyph || isWhiteSpace) {
                var whitespaceBounds = Bounds.FromPositionAndSize(
                    new Vector2(characterOffset.X, characterOffset.Y + yOffset),
                    new Vector2(x - characterOffset.X, glyph.LineSpacing * effectiveScale)
                );
                // HACK: Why is this necessary?
                whitespaceBounds.TopLeft.Y = Math.Max(whitespaceBounds.TopLeft.Y, whitespaceBounds.BottomRight.Y - currentLineSpacing);

                // FIXME: is the center X right?
                // ProcessHitTests(ref whitespaceBounds, whitespaceBounds.Center.X);
                // HACK: AppendCharacter will invoke ProcessMarkers anyway
                // ProcessMarkers(ref whitespaceBounds, currentCodepointSize, null, false, didWrapWord);

                // Ensure that trailing spaces are factored into total size
                if (isWhiteSpace)
                    maxX = Math.Max(maxX, whitespaceBounds.BottomRight.X);
            }

            if (deadGlyph) {
                previousGlyphWasDead = true;
                currentCharacterIndex++;
                characterSkipCount--;
                if (characterLimit.HasValue)
                    characterLimit--;
            }

            if (isWhiteSpace) {
                previousGlyphWasDead = true;
                currentLineWrapPointLeft = Math.Max(currentLineWrapPointLeft, characterOffset.X);
                currentLineWhitespaceMaxX = Math.Max(currentLineWhitespaceMaxX, x);
            } else
                previousGlyphWasDead = false;
        }

        private void PerformLineBreak (bool forcedWrap) {
            // FIXME: We also want to expand markers to enclose the overhang
            currentLineMaxX += currentXOverhang;
            currentLineMaxXUnconstrained += currentXOverhang;

            if (!forcedWrap) {
                var spacingForThisLineBreak = currentLineSpacing + extraLineBreakSpacing;
                if (!suppress) {
                    // We just wrapped to a new line so disable horizontal suppression. Without this, in 
                    //  word wrap only (no character wrap) mode, as soon as we hit a word that can't fit
                    //  onto a line, we suppress the rest of the string.
                    suppressForHorizontalOverflow = false;

                    characterOffset.X = xOffsetOfNewLine;
                    // FIXME: didn't we already do this?
                    characterOffset.Y += spacingForThisLineBreak;
                    maxX = Math.Max(maxX, currentLineMaxX);
                }
                characterOffsetUnconstrained.X = xOffsetOfNewLine;
                AdjustCharacterOffsetForBoxes(ref characterOffset.X, characterOffset.Y, spacingForThisLineBreak, leftPad: xOffsetOfNewLine);
                AdjustCharacterOffsetForBoxes(ref characterOffsetUnconstrained.X, characterOffsetUnconstrained.Y, spacingForThisLineBreak, leftPad: xOffsetOfNewLine);
                characterOffsetUnconstrained.Y += spacingForThisLineBreak;

                maxXUnconstrained = Math.Max(maxXUnconstrained, currentLineMaxXUnconstrained);
                currentLineMaxXUnconstrained = 0;
                initialLineSpacing = currentLineSpacing = 0;
                currentBaseline = 0;
                baselineAdjustmentStart = bufferWritePosition;
            }

            ComputeLineBreakAtX();
            initialLineXOffset = characterOffset.X;
            if (!suppress) {
                currentLineMaxX = 0;
                currentLineWhitespaceMaxX = 0;
                currentLineWrapPointLeft = 0;
            }
            _rowIndex += 1;
            _colIndex = 0;
        }

        private void PerformForcedWrap (float x, bool isWordWrapPoint, ref bool lineBreak, ref bool didWrapWord, float glyphLineSpacing, float glyphBaseline) {
            var currentWordSize = x - wordStartOffset.X;

            if (
                wordWrap && !wordWrapSuppressed &&
                // FIXME: If boxes shrink the current line too far, we want to just keep wrapping until we have enough room
                //  instead of giving up
                (currentWordSize <= currentLineBreakAtX) &&
                (wordStartColumn > 0) &&
                // The first word could be big enough to trigger a wrap, in which case we need to make sure that
                //  it's actually possible to word wrap it (pro tip: it's not)
                (wordStartWritePosition >= 0)
            ) {
                if (lineLimit.HasValue)
                    lineLimit--;
                WrapWord(buffer, wordStartOffset, wordStartWritePosition, bufferWritePosition - 1, glyphLineSpacing, glyphBaseline, currentWordSize);
                wordWrapSuppressed = true;
                lineBreak = true;
                didWrapWord = true;

                // FIXME: While this will abort when the line limit is reached, we need to erase the word we wrapped to the next line
                if (lineLimit.HasValue && lineLimit.Value <= 0)
                    suppress = true;
            } else if (
                characterWrap || 
                // If we previously started suppressing characters for horizontal overflow, the next time
                //  we hit a word wrap point we need to perform a character wrap
                (isWordWrapPoint && suppressForHorizontalOverflow && wordWrap)
            ) {
                if (lineLimit.HasValue)
                    lineLimit--;
                characterOffset.X = xOffsetOfWrappedLine;
                AdjustCharacterOffsetForBoxes(ref characterOffset.X, characterOffset.Y, currentLineSpacing, leftPad: xOffsetOfWrappedLine);
                characterOffset.Y += currentLineSpacing;
                initialLineSpacing = currentLineSpacing = glyphLineSpacing;
                currentBaseline = glyphBaseline;
                baselineAdjustmentStart = bufferWritePosition;

                maxX = Math.Max(maxX, currentLineMaxX);
                wordStartWritePosition = bufferWritePosition;
                wordStartOffset = characterOffset;
                wordStartColumn = _colIndex;
                lineBreak = true;

                if (lineLimit.HasValue && lineLimit.Value <= 0)
                    suppress = true;

                suppressForHorizontalOverflow = false;
            } else if (wordStartWritePosition < 0) {
                // This means we haven't actually rendered any glyphs yet, so there's no reason to start suppressing
                //  overflow (there is no overflow yet). Without this elseif branch, text would get truncated when
                //  character wrapping is disabled and the desired width exactly aligns with a whitespace wrap point.
                ;
            } else if (hideOverflow) {
                // If wrapping is disabled but we've hit the line break boundary, we want to suppress glyphs from appearing
                //  until the beginning of the next line (i.e. hard line break), but continue performing layout
                suppressForHorizontalOverflow = true;
            } else {
                // Just overflow. Hooray!
                ;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ComputeLineBreakAtX () {
            if (!lineBreakAtX.HasValue)
                currentLineBreakAtX = null;
            else if (boxes.Count > 0)
                ComputeLineBreakAtX_Slow();
            else
                currentLineBreakAtX = lineBreakAtX.Value;
        }

        private void ComputeLineBreakAtX_Slow () {
            float rightEdge = lineBreakAtX.Value;
            var row = Bounds.FromPositionAndSize(0f, characterOffset.Y, lineBreakAtX.Value, currentLineSpacing);
            for (int i = 0, c = boxes.Count; i < c; i++) {
                ref var b = ref boxes.Item(i);
                // HACK
                if (b.BottomRight.X <= (rightEdge - 2f))
                    continue;
                if (!Bounds.Intersect(row, b))
                    continue;
                // HACK: Don't force wrap for boxes that extend all the way to the left, since they are probably full-width images
                //  from a previous line.
                // FIXME: We should just force entire lines down so this never happens
                if (b.TopLeft.X <= 0.5f)
                    continue;
                rightEdge = Math.Min(b.TopLeft.X, rightEdge);
            }
            currentLineBreakAtX = rightEdge;
        }

        private void AppendDrawCall (
            ref BitmapDrawCall drawCall, 
            float x, int currentCodepointSize, 
            bool isWhiteSpace, float glyphLineSpacing, float yOffset, 
            float xUnconstrained, ref Bounds testBounds, bool splitMarker, bool didWrapWord,
            bool? overrideSuppress = null
        ) {
            if (recordUsedTextures && 
                (drawCall.Textures.Texture1 != lastUsedTexture) && 
                (drawCall.Textures.Texture1 != null)
            ) {
                lastUsedTexture = drawCall.Textures.Texture1;
                int existingIndex = -1;
                for (int i = 0; i < usedTextures.Count; i++) {
                    if (usedTextures[i].Id == drawCall.Textures.Texture1.Id) {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex < 0)
                    usedTextures.Add(lastUsedTexture);
            }

            if (_colIndex == 0) {
                characterOffset.X = Math.Max(characterOffset.X, 0);
                characterOffsetUnconstrained.X = Math.Max(characterOffsetUnconstrained.X, 0);
            }

            if (stopAtY.HasValue && (characterOffset.Y >= stopAtY))
                suppress = true;

            if (characterSkipCount <= 0) {
                if (characterLimit.HasValue && characterLimit.Value <= 0)
                    suppress = true;

                if (!isWhiteSpace) {
                    unchecked {
                        drawCall.LocalData1 = (short)(_wordIndex % 32767);
                    }

                    if (!measureOnly) {
                        if (bufferWritePosition >= buffer.Count)
                            EnsureBufferCapacity(bufferWritePosition);

                        // So the alignment pass can detect rows
                        unchecked {
                            drawCall.LocalData2 = (byte)(_rowIndex % 256);
                        }

                        if (reverseOrder)
                            drawCall.SortOrder += 1;
                    }

                    newLinePending = false;

                    if (!ComputeSuppress(overrideSuppress)) {
                        if (!measureOnly) {
                            buffer.Array[buffer.Offset + bufferWritePosition] = drawCall;
                            ProcessMarkers(ref testBounds, currentCodepointSize, bufferWritePosition, splitMarker || previousGlyphWasDead, didWrapWord);
                            bufferWritePosition += 1;
                            drawCallsWritten += 1;
                        }
                        currentLineMaxX = Math.Max(currentLineMaxX, x);
                        maxY = Math.Max(maxY, characterOffset.Y + glyphLineSpacing);
                    } else {
                        drawCallsSuppressed++;
                    }

                    currentLineMaxXUnconstrained = Math.Max(currentLineMaxXUnconstrained, xUnconstrained);
                    maxYUnconstrained = Math.Max(maxYUnconstrained, (characterOffsetUnconstrained.Y + glyphLineSpacing));
                } else {
                    currentLineWrapPointLeft = Math.Max(currentLineWrapPointLeft, characterOffset.X);
                    currentLineWhitespaceMaxX = Math.Max(currentLineWhitespaceMaxX, x);

                    ProcessMarkers(ref testBounds, currentCodepointSize, null, splitMarker || previousGlyphWasDead, didWrapWord);
                }

                characterLimit--;
            } else {
                characterSkipCount--;
            }
        }

        private void FinishProcessingMarkers (ArraySegment<BitmapDrawCall> result) {
            if (measureOnly)
                return;

            // HACK: During initial layout we split each word of a marked region into
            //  separate bounds so that wrapping would work correctly. Now that we're
            //  done, we want to find words that weren't wrapped and weld their bounds
            //  together so the entire marked string will be one bounds (if possible).
            for (int i = 0; i < Markers.Count; i++) {
                var m = Markers[i];
                if (m.Bounds.Count <= 1)
                    continue;

                for (int j = m.Bounds.Count - 1; j >= 1; j--) {
                    var b1 = m.Bounds[j - 1];
                    var b2 = m.Bounds[j];
                    // HACK: Detect a wrap/line break
                    if (b2.TopLeft.Y >= b1.Center.Y)
                        continue;
                    var xDelta = b2.TopLeft.X - b1.BottomRight.X;
                    if (xDelta > 0.5f)
                        continue;
                    m.Bounds[j - 1] = Bounds.FromUnion(b1, b2);
                    m.Bounds.RemoveAt(j);
                }

                Markers[i] = m;
            }
        }

        public StringLayout Finish () {
            if (currentXOverhang > 0) {
                currentLineMaxX += currentXOverhang;
                currentLineMaxXUnconstrained += currentXOverhang;
                maxX = Math.Max(currentLineMaxX, maxX);
                maxXUnconstrained = Math.Max(currentLineMaxXUnconstrained, maxXUnconstrained);
            }

            var result = default(ArraySegment<BitmapDrawCall>);
            if (!measureOnly) {
                if (buffer.Array != null)
                    result = new ArraySegment<BitmapDrawCall>(
                        buffer.Array, buffer.Offset, drawCallsWritten
                    );

                if (alignment != HorizontalAlignment.Left)
                    AlignLines(result, alignment);
                else
                    SnapPositions(result);

                if (reverseOrder) {
                    for (int k = 0; k < Markers.Count; k++) {
                        var m = Markers[k];
                        var a = result.Count - m.FirstDrawCallIndex - 1;
                        var b = result.Count - m.LastDrawCallIndex - 1;
                        m.FirstDrawCallIndex = b;
                        m.LastDrawCallIndex = a;
                        Markers[k] = m;
                    }

                    int i = result.Offset;
                    int j = result.Offset + result.Count - 1;
                    while (i < j) {
                        var temp = result.Array[i];
                        temp.UserData = (temp.UserData.Z != 0) ? imageUserData : userData;
                        result.Array[i] = result.Array[j];
                        result.Array[j] = temp;
                        i++;
                        j--;
                    }
                } else if (clearUserData) {
                    for (int i = 0, l = result.Count; i < l; i++)
                        result.Array[i + result.Offset].UserData = 
                            (result.Array[i + result.Offset].UserData.Z != 0) ? imageUserData : userData;
                }
            }

            var endpointBounds = lastCharacterBounds;
            // FIXME: Index of last draw call?
            // FIXME: Codepoint size?
            ProcessMarkers(ref endpointBounds, 1, null, false, false);

            FinishProcessingMarkers(result);

            // HACK: Boxes are in local space so we have to offset them at the end
            for (int i = 0, c = boxes.Count; i < c; i++) {
                ref var box = ref boxes.Item(i);
                box.TopLeft += actualPosition;
                box.BottomRight += actualPosition;
            }

            maxX = Math.Max(maxX, desiredWidth);
            maxXUnconstrained = Math.Max(maxXUnconstrained, desiredWidth);

            return new StringLayout(
                position.GetValueOrDefault(), 
                new Vector2(maxX, maxY), new Vector2(maxXUnconstrained, maxYUnconstrained),
                maxLineSpacing,
                firstCharacterBounds, lastCharacterBounds,
                result, (lineLimit.HasValue && (lineLimit.Value <= 0)) || 
                    (lineBreakLimit.HasValue && (lineBreakLimit.Value <= 0)),
                wordIndex + 1, rowIndex + 1
            ) {
                Boxes = boxes,
                UsedTextures = usedTextures
            };
        }

        public void Dispose () {
        }
    }
}

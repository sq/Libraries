using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Util.Text;
using Squared.Render.Text;
using Squared.Util;
using Squared.Game;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Globalization;
using Squared.Util.DeclarativeSort;
using Microsoft.Xna.Framework.Graphics;
using Squared.Threading;

namespace Squared.Render.TextLayout2 {
    public struct Line {
        public uint Index, VisibleWordCount;
        public uint FirstDrawCall, DrawCallCount;
        public Vector2 Location;
        public float Width, Height, Baseline;
    }

    public struct Span {
        public uint Index;
        public uint FirstDrawCall, DrawCallCount;
        public uint FirstLineIndex, LineCount;
    }

    public struct Word {
        public uint FirstDrawCall, DrawCallCount;
        public float LeadingWhitespace, Width, Height, Baseline;
        public float TotalWidth {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => LeadingWhitespace + Width;
        }
    }

    public struct Box {
        public Vector2 Size, Margin;
        public float? HardHorizontalAlignment;
        public float BaselineAlignment;
        // Optional
        public uint DrawCallIndex;
    }

    public interface IStringLayoutListener {
        void Initializing (ref StringLayoutEngine2 engine);
        void RecordTexture (ref StringLayoutEngine2 engine, TextureSet textures);
        void Finishing (ref StringLayoutEngine2 engine);
        void Finished (ref StringLayoutEngine2 engine, uint spanCount, uint lineCount, ref StringLayout result);
    }

    public struct StringLayoutEngine2 : IDisposable {
        private unsafe struct StagingBuffers : IDisposable {
            public BitmapDrawCall* DrawCalls;
            public Line* Lines;
            public Span* Spans;
            public Box* Boxes;
            public uint DrawCallCapacity, LineCapacity, SpanCapacity, BoxCapacity;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref BitmapDrawCall DrawCall (uint index) {
                if (index >= DrawCallCapacity)
                    Reallocate(ref DrawCalls, ref DrawCallCapacity, index + 1);
                return ref DrawCalls[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Line Line (uint index) {
                if (index >= LineCapacity)
                    Reallocate(ref Lines, ref LineCapacity, index + 1);
                return ref Lines[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Span Span (uint index) {
                if (index >= SpanCapacity)
                    Reallocate(ref Spans, ref SpanCapacity, index + 1);
                return ref Spans[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Box Box (uint index) {
                if (index >= BoxCapacity)
                    Reallocate(ref Boxes, ref BoxCapacity, index + 1);
                return ref Boxes[index];
            }

            private static void Reallocate<T> (ref T* ptr, ref uint currentCapacity, uint desiredCapacity)
                where T : unmanaged {
                // HACK: Detect underflow
                if (desiredCapacity > 1024000)
                    throw new ArgumentOutOfRangeException(nameof(desiredCapacity));
                if (desiredCapacity <= currentCapacity)
                    return;

                var newCapacity = Math.Max(currentCapacity, 32);
                while (newCapacity < desiredCapacity)
                    newCapacity = (newCapacity * 17 / 10);

                if (ptr != null)
                    ptr = (T*)Marshal.ReAllocHGlobal((IntPtr)ptr, (IntPtr)(newCapacity * sizeof(T)));
                else
                    ptr = (T*)Marshal.AllocHGlobal((IntPtr)(newCapacity * sizeof(T)));
                currentCapacity = newCapacity;
            }

            private static void Deallocate<T> (ref T* ptr, ref uint size)
                where T : unmanaged
            {
                if (ptr != null)
                    Marshal.FreeHGlobal((IntPtr)ptr);
                ptr = null;
                size = 0;
            }

            public void Dispose () {
                Deallocate(ref DrawCalls, ref DrawCallCapacity);
                Deallocate(ref Lines, ref LineCapacity);
                Deallocate(ref Spans, ref SpanCapacity);
                Deallocate(ref Boxes, ref BoxCapacity);
            }

            internal void EnsureCapacity (uint drawCallCount) {
                Reallocate(ref DrawCalls, ref DrawCallCapacity, drawCallCount);
            }
        }

        // Internal state

        Vector2 WordOffset, LineOffset;
        Vector2 UnconstrainedSize, UnconstrainedLineSize;
        uint RowIndex, ColIndex, LineIndex, CharIndex, DrawCallIndex, SpanIndex, WordIndex;
        bool SuppressUntilNextLine, SuppressUntilEnd;

        bool ExternalBuffers;
        StagingBuffers Buffers;

        bool NewLinePending;
        Word CurrentWord;
        DenseList<uint> SpanStack;

        // Configuration

        public IStringLayoutListener Listener;

        public DenseList<uint> WrapCharacters;

        public bool MeasureOnly;
        public bool CharacterWrap, WordWrap, DisableDefaultWrapCharacters;
        public bool HideOverflow, IncludeTrailingWhitespace;
        public bool OverrideColor;

        public int CharacterSkipCount, LineSkipCount;
        public int? CharacterLimit, LineLimit, BreakLimit;

        public Vector2 Position;
        private Vector2 SpacingMinusOne, ScaleMinusOne;
        public Vector2 Spacing {
            get => SpacingMinusOne + Vector2.One;
            set => SpacingMinusOne = value - Vector2.One;
        }
        public Vector2 Scale {
            get => ScaleMinusOne + Vector2.One;
            set => ScaleMinusOne = value - Vector2.One;
        }

        public Color MultiplyColor, AddColor;
        public DrawCallSortKey SortKey;

        public HorizontalAlignment Alignment;
        public uint? MaskCodepoint;

        public float InitialIndentation, BreakIndentation; // FIXME: WrapIndentation
        public float AdditionalLineSpacing;
        public float MaximumWidth, DesiredWidth;
        public float MaxExpansionPerSpace;

        bool IsInitialized;

        public void Initialize () {
            // FIXME
            if (IsInitialized)
                throw new InvalidOperationException("A StringLayoutEngine2 instance cannot be used more than once");

            IsInitialized = false;

            WrapCharacters.SortNonRef(StringLayoutEngine.UintComparer.Instance);

            Listener?.Initializing(ref this);

            IsInitialized = true;
            Buffers.Span(0) = default;
            CurrentLine = default;
            CurrentWord = new Word {
                FirstDrawCall = uint.MaxValue,
                LeadingWhitespace = InitialIndentation
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref BitmapDrawCall AppendDrawCall (ref BitmapDrawCall dead) {
            if (SuppressUntilEnd || SuppressUntilNextLine || MeasureOnly)
                return ref dead;

            if (CurrentWord.FirstDrawCall == uint.MaxValue)
                CurrentWord.FirstDrawCall = DrawCallIndex;
            CurrentWord.DrawCallCount++;
            return ref Buffers.DrawCall(DrawCallIndex++);
        }

        private ref Line CurrentLine {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Buffers.Line(LineIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BitmapDrawCall GetDrawCall (uint index) {
            if (index > DrawCallIndex)
                index = DrawCallIndex;
            return ref Buffers.DrawCall(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Span GetSpan (uint index) {
            if (index > SpanIndex)
                index = SpanIndex;
            return ref Buffers.Span(index);
        }

        public bool TryGetSpanBoundingBoxes (uint index, ref DenseList<Bounds> output) {
            if (index > SpanIndex)
                return false;

            ref var span = ref Buffers.Span(index);
            if (span.LineCount == 0)
                return true;
            if (span.DrawCallCount == 0)
                return true;
            if (span.FirstLineIndex == uint.MaxValue)
                throw new Exception("Corrupt internal state");

            uint l = span.FirstLineIndex, l2 = l + span.LineCount - 1;
            if ((l2 >= LineIndex) || (l2 < l))
                throw new Exception("Corrupt internal state");

            for (; l <= l2; l++) {
                if (!TryGetLineBounds(l, out var lineBounds))
                    continue;
                ref var line = ref Buffers.Line(l);

                // HACK: Earlier when constructing draw calls, we used UserData.X/Y to store X offsets for
                //  the left and right edges of the character bounding box. We now recover them for the
                //  first and last line(s) in the span in order to trim the line's bounding box.
                if (l == span.FirstLineIndex) {
                    ref var dc1 = ref Buffers.DrawCall(span.FirstDrawCall);
                    // The draw call's position probably changed due to things like alignment, so we
                    //  instead stored an offset in the UserData, which allows us to construct the
                    //  correct left edge for this draw call based on its new position.
                    lineBounds.TopLeft.X = dc1.Position.X - dc1.UserData.X;
                }
                if (l == l2) {
                    ref var dc2 = ref Buffers.DrawCall(span.FirstDrawCall + span.DrawCallCount - 1);
                    // Similarly we construct the correct right edge from this draw call's position.
                    lineBounds.BottomRight.X = dc2.Position.X + dc2.UserData.Y;
                }

                output.Add(ref lineBounds);
            }

            return true;
        }

        public bool TryGetLineBounds (uint index, out Bounds bounds) {
            if (index > LineIndex) {
                bounds = default;
                return false;
            }

            ref var line = ref Buffers.Line(index);
            bounds = Bounds.FromPositionAndSize(line.Location.X + Position.X, line.Location.Y + Position.Y, line.Width, line.Height);
            return true;
        }
        
        public ref Span BeginSpan (bool push) {
            var index = SpanIndex++;
            if (push || SpanStack.Count == 0)
                SpanStack.Add(index);
            else
                SpanStack.Last() = index;

            ref var result = ref Buffers.Span(index);
            result.Index = index;
            // Initialize FirstLineIndex with a dummy value, it will be filled in by
            //  either FinishWord or EndSpan.
            result.FirstLineIndex = uint.MaxValue;
            result.FirstDrawCall = DrawCallIndex;
            return ref result;
        }

        public ref Span EndSpan () {
            if (!SpanStack.TryRemoveLast(out var index))
                return ref Buffers.Span(0);

            ref var result = ref Buffers.Span(index);
            // It's possible EndSpan will be called in the middle of a word, in which
            //  case FirstLineIndex will never get filled in by FinishWord. So fill it
            //  in here if that happens.
            if (result.FirstLineIndex == uint.MaxValue)
                result.FirstLineIndex = LineIndex;
            result.LineCount = (LineIndex - result.FirstLineIndex) + 1;
            result.DrawCallCount = DrawCallIndex - result.FirstDrawCall;
            // FIXME: If we ended a span in the middle of a word, our bounding box will become
            //  incorrect in the event that the word later gets wrapped.
            return ref result;
        }

        public void AppendText<TGlyphSource> (
            TGlyphSource glyphSource, AbstractString text            
        ) where TGlyphSource : IGlyphSource {
            if (!IsInitialized)
                throw new InvalidOperationException("Call Initialize first");

            if (!typeof(TGlyphSource).IsValueType) {
                if (glyphSource == null)
                    throw new ArgumentNullException(nameof(glyphSource));
            }

            if (text.IsNull)
                throw new ArgumentNullException(nameof(text));

            BitmapDrawCall dead = default;
            KerningData thisKerning = default, nextKerning = default;
            bool hasKerningNow = false, hasKerningNext = false;
            Buffers.EnsureCapacity((uint)(DrawCallIndex + text.Length));

            var effectiveScale = Scale * (1.0f / glyphSource.DPIScaleFactor);

            for (int i = 0, l = text.Length; i < l; i++) {
                if (LineLimit.HasValue && LineLimit.Value <= 0)
                    SuppressUntilEnd = true;

                DecodeCodepoint(text, ref i, l, out char ch1, out int currentCodepointSize, out uint codepoint);

                AnalyzeWhitespace(
                    ch1, codepoint, out bool isWhiteSpace, out bool forcedWrap, out bool lineBreak, 
                    out bool deadGlyph, out bool isWordWrapPoint, out bool didWrapWord
                );

                BuildGlyphInformation(
                    glyphSource, effectiveScale, Spacing, ch1, codepoint,
                    out deadGlyph, out Glyph glyph, out float glyphLineSpacing, out float glyphBaseline
                );

                // FIXME: Kerning across multiple AppendText calls
                if ((glyph.KerningProvider != null) && (i < l - 2)) {
                    var temp = i + 1;
                    DecodeCodepoint(text, ref temp, l, out _, out _, out var codepoint2);
                    // FIXME: Also do adjustment for next glyph!
                    // FIXME: Cache the result of this GetGlyph call and use it next iteration to reduce CPU usage
                    if (
                        glyphSource.GetGlyph(codepoint2, out var glyph2) &&
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

                bool suppressThisCharacter = false;
                float w = (glyph.WidthIncludingBearing * effectiveScale.X) + (glyph.CharacterSpacing * effectiveScale.X),
                    h = glyph.LineSpacing * effectiveScale.Y,
                    baseline = glyph.Baseline * effectiveScale.Y,
                    xBasis = CurrentWord.LeadingWhitespace + LineOffset.X,
                    x1 = xBasis + WordOffset.X,
                    x2 = x1 + w;

                if (x2 >= MaximumWidth) {
                    if (!isWhiteSpace) {
                        forcedWrap = true;
                    } else if (CharacterWrap)
                        // HACK: If word wrap is disabled and character wrap is enabled,
                        //  without this our bounding box can extend beyond MaximumWidth.
                        suppressThisCharacter = true;
                    else if (HideOverflow)
                        SuppressUntilNextLine = true;
                }

                if (forcedWrap)
                    // HACK: Manually calculate the new width of the word so that we can decide
                    //  whether it needs to be character wrapped
                    PerformForcedWrap(CurrentWord.Width + w);
                else if (lineBreak)
                    PerformLineBreak();

                if (DisableDefaultWrapCharacters) {
                    // FIXME
                    if (isWordWrapPoint)
                        FinishWord();
                } else if (isWhiteSpace)
                    FinishWord();

                if (baseline > CurrentWord.Baseline)
                    IncreaseBaseline(ref CurrentWord, baseline);

                float alignmentToBaseline = CurrentWord.Baseline - baseline;

                if (!deadGlyph && !isWhiteSpace) {
                    ref var drawCall = ref AppendDrawCall(ref dead);
                    short wordIndex;
                    unchecked {
                        // It's fine for this to wrap around, we just != it later to find word boundaries
                        wordIndex = (short)WordIndex;
                    }

                    float x = WordOffset.X + (glyph.XOffset * effectiveScale.X) + (glyph.LeftSideBearing * effectiveScale.X),
                        // Used to compute bounding box offsets
                        wx = xBasis + x,
                        y = WordOffset.Y + (glyph.YOffset * effectiveScale.Y) + alignmentToBaseline;
                    drawCall = new BitmapDrawCall {
                        MultiplyColor = OverrideColor 
                            ? MultiplyColor
                            : (glyph.DefaultColor ?? MultiplyColor),
                        AddColor = AddColor,
                        Position = new Vector2(
                            x, y
                        ),
                        Scale = effectiveScale * glyph.RenderScale,
                        Textures = new TextureSet(glyph.Texture),
                        TextureRegion = glyph.BoundsInTexture,
                        LocalData1 = wordIndex,
                        // HACK: Used when computing bounding boxes later
                        UserData = new Vector4(wx - x1, x2 - wx, 0, 0),
                    };
                }

                if (suppressThisCharacter)
                    ;
                else if (isWhiteSpace && (CurrentWord.DrawCallCount == 0)) {
                    CurrentWord.LeadingWhitespace += w;
                } else {
                    WordOffset.X += w;
                    CurrentWord.Width += w;
                }

                UnconstrainedLineSize.X += w;
                CurrentWord.Height = Math.Max(CurrentWord.Height, h);
                UnconstrainedLineSize.Y = Math.Max(UnconstrainedLineSize.Y, h);
            }
        }

        private void IncreaseBaseline (ref Word word, float newBaseline) {
            float adjustment = newBaseline - word.Baseline;
            word.Baseline = newBaseline;
            if (word.DrawCallCount == 0)
                return;

            for (uint d = word.FirstDrawCall, d2 = d + word.DrawCallCount - 1; d <= d2; d++) {
                ref var dc = ref Buffers.DrawCall(d);
                dc.Position.Y += adjustment;
            }
        }

        private void IncreaseBaseline (ref Line line, float newBaseline) {
            float adjustment = newBaseline - line.Baseline;
            line.Baseline = newBaseline;
            if (line.DrawCallCount == 0)
                return;

            for (uint d = line.FirstDrawCall, d2 = d + line.DrawCallCount - 1; d <= d2; d++) {
                ref var dc = ref Buffers.DrawCall(d);
                dc.Position.Y += adjustment;
            }
        }

        private void PerformForcedWrap (float wordWidth) {
            if (WordWrap && (wordWidth <= MaximumWidth)) {
                FinishLine(true);
                ;
            } else if (CharacterWrap) {
                FinishLine();
            } else if (HideOverflow)
                SuppressUntilNextLine = true;
            else
                ;
        }

        private void PerformLineBreak () {
            UnconstrainedSize.X = Math.Max(UnconstrainedSize.X, UnconstrainedLineSize.X);
            UnconstrainedSize.Y += UnconstrainedLineSize.Y;
            UnconstrainedLineSize = default;
            FinishLine();
        }

        private void FinishWord () {
            ref var word = ref CurrentWord;
            ref var line = ref CurrentLine;

            if (line.Width <= 0) {
                // FIXME: WrapIndentation
                word.LeadingWhitespace = (LineIndex == 0)
                    ? InitialIndentation
                    : BreakIndentation;
            }

            // HACK: It's possible a span will start in the middle of a word, then the word gets
            //  wrapped immediately. So we fill in FirstLineIndex lazily to compensate for that,
            //  setting it the first time a word is actually finished.
            foreach (var spanIndex in SpanStack) {
                ref var span = ref Buffers.Span(spanIndex);
                if (span.FirstLineIndex == uint.MaxValue)
                    span.FirstLineIndex = LineIndex;
            }

            if (word.Baseline > line.Baseline)
                IncreaseBaseline(ref line, word.Baseline);

            float baselineAdjustment = line.Baseline - word.Baseline;

            if (word.DrawCallCount > 0) {
                for (uint i = word.FirstDrawCall, i2 = i + word.DrawCallCount - 1; i <= i2; i++) {
                    ref var drawCall = ref Buffers.DrawCall(i);
                    drawCall.Position.X += Position.X + LineOffset.X + word.LeadingWhitespace;
                    drawCall.Position.Y += Position.Y + LineOffset.Y + baselineAdjustment;
                }
            }

            LineOffset.X += word.TotalWidth;
            line.Width += word.TotalWidth;
            line.Height = Math.Max(line.Height, word.Height);
            if (line.DrawCallCount == 0)
                line.FirstDrawCall = word.FirstDrawCall;
            if (word.DrawCallCount > 0) {
                line.DrawCallCount += word.DrawCallCount;
                line.VisibleWordCount++;
            }
            WordOffset = default;

            // FIXME
            WordIndex++;
            CurrentWord = new Word {
                FirstDrawCall = DrawCallIndex,
            };
        }

        private void FinishLine (bool preserveWord = false) {
            if (!preserveWord)
                FinishWord();

            ref var line = ref CurrentLine;

            LineOffset.X = 0;
            LineOffset.Y += CurrentLine.Height;

            SuppressUntilNextLine = false;
            var index = LineIndex++;
            CurrentLine = new Line {
                Index = index,
                Location = LineOffset,
            };
        }

        public void CreateEmptyBox (float width, float height, Vector2 margins) {
            // FIXME
        }

        public void Advance (
            float width, float height, 
            // FIXME
            bool doNotAdjustLineSpacing = false, bool considerBoxes = true
        ) {
            ref var line = ref CurrentLine;
            LineOffset.X += width;
            line.Width += width;
            line.Height = Math.Max(CurrentLine.Height, height);
            UnconstrainedLineSize.X += width;
            UnconstrainedLineSize.Y = Math.Max(UnconstrainedLineSize.Y, height);
        }

        public void AppendImage (ref RichImage image) {
            // FIXME
        }

        private void AlignLines (float totalWidth) {
            if (Alignment == HorizontalAlignment.Left)
                return;

            for (uint i = 0; i <= LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                if (line.DrawCallCount == 0)
                    continue;

                int wordCountMinusOne = (line.VisibleWordCount > 1) 
                    ? (int)line.VisibleWordCount - 1 
                    : (int)line.VisibleWordCount;
                float whitespace = totalWidth - line.Width,
                    wordWhitespace = 0f,
                    characterWhitespace = 0f;
                switch (Alignment) {
                    case HorizontalAlignment.Center:
                        whitespace *= 0.5f;
                        break;
                    case HorizontalAlignment.Right:
                        break;
                    case HorizontalAlignment.JustifyWords:
                        wordWhitespace = whitespace / wordCountMinusOne;
                        if (wordWhitespace > MaxExpansionPerSpace)
                            wordWhitespace = 0f;
                        whitespace = 0f;
                        break;
                    case HorizontalAlignment.JustifyWordsCentered:
                        if (line.VisibleWordCount > 1) {
                            wordWhitespace = whitespace / wordCountMinusOne;
                            if (wordWhitespace > MaxExpansionPerSpace)
                                wordWhitespace = 0f;
                            whitespace -= (wordWhitespace * wordCountMinusOne);
                        }
                        whitespace *= 0.5f;
                        break;
                    case HorizontalAlignment.JustifyCharacters:
                        characterWhitespace = whitespace / (line.DrawCallCount - 1);
                        if (characterWhitespace > MaxExpansionPerSpace)
                            characterWhitespace = 0f;
                        whitespace = 0f;
                        break;
                    case HorizontalAlignment.JustifyCharactersCentered:
                        if (line.DrawCallCount > 1) {
                            characterWhitespace = whitespace / (line.DrawCallCount - 1);
                            if (characterWhitespace > MaxExpansionPerSpace)
                                characterWhitespace = 0f;
                            whitespace -= (characterWhitespace * (line.DrawCallCount - 1));
                        }
                        whitespace *= 0.5f;
                        break;
                }

                line.Location.X += whitespace;
                line.Width += (wordWhitespace * wordCountMinusOne) + (characterWhitespace * (line.DrawCallCount - 1));

                short lastWordIndex = -1;
                for (uint j = line.FirstDrawCall, j2 = j + line.DrawCallCount - 1; j <= j2; j++) {
                    ref var drawCall = ref Buffers.DrawCall(j);
                    // LocalData2 being set means that this draw call is absolutely positioned.
                    // FIXME: Should this ever happen?
                    if (drawCall.LocalData2 != 0)
                        continue;

                    if ((drawCall.LocalData1 != lastWordIndex) && (lastWordIndex >= 0))
                        whitespace += wordWhitespace;
                    lastWordIndex = drawCall.LocalData1;

                    drawCall.Position.X += whitespace;
                    whitespace += characterWhitespace;
                }
            }
        }

        public void ComputeConstrainedSize (out Vector2 constrainedSize) {
            constrainedSize = Vector2.Zero;
            for (uint i = 0; i <= LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                constrainedSize.X = Math.Max(constrainedSize.X, line.Width);
                constrainedSize.Y += line.Height;
            }
            constrainedSize.X = Math.Max(constrainedSize.X, DesiredWidth);
        }

        public unsafe void Finish (ArraySegment<BitmapDrawCall> buffer, out StringLayout result) {
            UnconstrainedSize.X = Math.Max(UnconstrainedSize.X, UnconstrainedLineSize.X);
            UnconstrainedSize.Y += UnconstrainedLineSize.Y;
            FinishLine();

            Listener?.Finishing(ref this);

            ComputeConstrainedSize(out var constrainedSize);
            AlignLines(constrainedSize.X);

            // HACK
            int bufferSize = Math.Max((int)DrawCallIndex, 512);
            if ((buffer.Array == null) || (buffer.Count < bufferSize))
                buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[bufferSize]);
            else
                buffer = new ArraySegment<BitmapDrawCall>(buffer.Array, buffer.Offset, (int)DrawCallIndex);

            var lastDrawCallIndex = DrawCallIndex > 0 ? DrawCallIndex - 1 : 0;
            fixed (BitmapDrawCall* dest = buffer.Array) {
                Buffer.MemoryCopy(Buffers.DrawCalls, dest, buffer.Count * sizeof(BitmapDrawCall), DrawCallIndex * sizeof(BitmapDrawCall));
                // HACK: Zero the UserData. We used it to store offsets for bounding box computation.
                // The draw calls in our internal buffers will still have the UserData.
                // While we're doing this scan, notify the listener of used textures too.
                var lastTextures = TextureSet.Invalid;
                for (uint i = 0; i < DrawCallIndex; i++) {
                    ref var dc = ref dest[i];
                    dc.UserData = default;
                    // We attempt to reduce the number of virtual function calls by only calling
                    //  RecordTexture if the current texture has changed.
                    if (!dc.Textures.Equals(lastTextures)) {
                        Listener?.RecordTexture(ref this, dc.Textures);
                        lastTextures = dc.Textures;
                    }
                }
            }

            result = new StringLayout(
                Vector2.Zero, constrainedSize, UnconstrainedSize, 0f,
                Buffers.DrawCall(0).EstimateDrawBounds(),
                Buffers.DrawCall(lastDrawCallIndex).EstimateDrawBounds(),
                buffer, false, (int)WordIndex, (int)LineIndex
            );

            Listener?.Finished(ref this, SpanIndex, LineIndex, ref result);
        }

        private void AnalyzeWhitespace (char ch1, uint codepoint, out bool isWhiteSpace, out bool forcedWrap, out bool lineBreak, out bool deadGlyph, out bool isWordWrapPoint, out bool didWrapWord) {
            isWhiteSpace = Unicode.IsWhiteSpace(ch1) && !MaskCodepoint.HasValue;
            forcedWrap = false;
            lineBreak = false;
            deadGlyph = false;
            didWrapWord = false;

            if (DisableDefaultWrapCharacters)
                isWordWrapPoint = WrapCharacters.BinarySearchNonRef(codepoint, StringLayoutEngine.UintComparer.Instance) >= 0;
            else
                isWordWrapPoint = isWhiteSpace || char.IsSeparator(ch1) ||
                    MaskCodepoint.HasValue || WrapCharacters.BinarySearchNonRef(codepoint, StringLayoutEngine.UintComparer.Instance) >= 0;

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
                if (LineLimit.HasValue) {
                    LineLimit--;
                    if (LineLimit.Value <= 0)
                        SuppressUntilEnd = true;
                }
                if (BreakLimit.HasValue) {
                    BreakLimit--;
                    if (BreakLimit.Value <= 0)
                        SuppressUntilEnd = true;
                }
                if (!SuppressUntilEnd && IncludeTrailingWhitespace)
                    NewLinePending = true;
            } else if (LineLimit.HasValue && LineLimit.Value <= 0) {
                SuppressUntilEnd = true;
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
                CharIndex++;
                i++;
            } else if (ch1 == '\r') {
                if (ch2 == '\n') {
                    currentCodepointSize = 2;
                    ch1 = ch2;
                    i++;
                    CharIndex++;
                }
            }

            codepoint = MaskCodepoint ?? codepoint;
        }

        private void BuildGlyphInformation<TGlyphSource> (
            in TGlyphSource font, Vector2 scale, Vector2 spacing, 
            char ch1, uint codepoint, out bool deadGlyph, out Glyph glyph, out float glyphLineSpacing, out float glyphBaseline
        ) where TGlyphSource : IGlyphSource {
            deadGlyph = !font.GetGlyph(codepoint, out glyph);

            glyphLineSpacing = glyph.LineSpacing * scale.Y;
            glyphLineSpacing += AdditionalLineSpacing;
            glyphBaseline = glyph.Baseline * scale.Y;
            if (deadGlyph) {
                // FIXME
                /*
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
                */
            }

            // glyph.LeftSideBearing *= effectiveSpacing;
            float leftSideDelta = 0;
            if (spacing.X >= 0)
                glyph.LeftSideBearing *= spacing.X;
            else
                leftSideDelta = Math.Abs(glyph.LeftSideBearing * spacing.X);
            glyph.RightSideBearing *= spacing.X;
            glyph.RightSideBearing -= leftSideDelta;

            /*
            if (initialLineSpacing <= 0)
                initialLineSpacing = glyphLineSpacing;
            ProcessLineSpacingChange(buffer, glyphLineSpacing, glyphBaseline);
            */

            // MonoGame#1355 rears its ugly head: If a character with negative left-side bearing is at the start of a line,
            //  we need to compensate for the bearing to prevent the character from extending outside of the layout bounds
            if (ColIndex <= 0) {
                if (glyph.LeftSideBearing < 0)
                    glyph.LeftSideBearing = 0;
            }
        }

        public void Dispose () {
            if (!ExternalBuffers)
                Buffers.Dispose();
        }
    }
}

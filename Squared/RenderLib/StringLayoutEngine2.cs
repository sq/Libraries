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

namespace Squared.Render.TextLayout2 {
    public struct Line {
        public uint FirstDrawCall, DrawCallCount, VisibleWordCount;
        public uint FirstBoxIndex, BoxCount;
        public float Width, Height;
    }

    public struct Span {
        public uint FirstDrawCall, DrawCallCount;
        public uint FirstBoxIndex, BoxCount;
    }

    public struct Word {
        public uint FirstDrawCall, DrawCallCount;
        public float LeadingWhitespace, Width, Height;
        public float TotalWidth {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => LeadingWhitespace + Width;
        }
    }

    public interface IStringLayoutListener {
        void RecordTexture (AbstractTextureReference texture);
    }

    public struct StringLayoutEngine2 : IDisposable {
        private unsafe struct StagingBuffers : IDisposable {
            public BitmapDrawCall* DrawCalls;
            public Line* Lines;
            public Span* Spans;
            public Bounds* Boxes;
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
            public ref Bounds Box (uint index) {
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

        Vector2 WordOffset, LineOffset;
        Vector2 UnconstrainedSize;
        uint RowIndex, ColIndex, LineIndex, CharIndex, DrawCallIndex, SpanIndex, WordIndex;
        bool SuppressUntilNextLine, SuppressUntilEnd;

        bool ExternalBuffers;
        StagingBuffers Buffers;

        bool NewLinePending;
        Word CurrentWord;


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

        public float InitialIndentation, BreakIndentation; // WrapIndentation
        public float AdditionalLineSpacing;
        public float MaximumWidth, DesiredWidth;
        public float MaxExpansionPerSpace;

        bool IsInitialized;

        public void Initialize () {
            WrapCharacters.SortNonRef(StringLayoutEngine.UintComparer.Instance);

            IsInitialized = true;
            CurrentWord.FirstDrawCall = uint.MaxValue;
            CurrentWord.LeadingWhitespace = InitialIndentation;
            CurrentLine = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref BitmapDrawCall AppendDrawCall (ref BitmapDrawCall dead, bool isWhiteSpace) {
            if (isWhiteSpace || SuppressUntilEnd || SuppressUntilNextLine || MeasureOnly)
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
        private ref Span CurrentSpan {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Buffers.Span(SpanIndex);
        }

        public void AppendText<TGlyphSource> (
            TGlyphSource glyphSource, AbstractString text,
            IStringLayoutListener listener = null
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

                // FIXME: Kerning

                bool suppressThisCharacter = false;
                float w = (glyph.WidthIncludingBearing * effectiveScale.X) + (glyph.CharacterSpacing * effectiveScale.X),
                    h = glyph.LineSpacing * effectiveScale.Y,
                    x1 = CurrentWord.LeadingWhitespace + WordOffset.X + LineOffset.X,
                    x2 = x1 + w;

                if (x2 >= MaximumWidth) {
                    if (!isWhiteSpace)
                        forcedWrap = true;
                    else 
                        // HACK: If word wrap is disabled and character wrap is enabled,
                        //  without this our bounding box can extend beyond MaximumWidth.
                        suppressThisCharacter = true;
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

                if (!deadGlyph) {
                    ref var drawCall = ref AppendDrawCall(ref dead, isWhiteSpace);
                    short wordIndex;
                    unchecked {
                        // It's fine for this to wrap around, we just != it later to find word boundaries
                        wordIndex = (short)WordIndex;
                    }

                    drawCall = new BitmapDrawCall {
                        MultiplyColor = OverrideColor 
                            ? MultiplyColor
                            : (glyph.DefaultColor ?? MultiplyColor),
                        AddColor = AddColor,
                        Position = new Vector2(
                            WordOffset.X + (glyph.XOffset * effectiveScale.X) + (glyph.LeftSideBearing * effectiveScale.X),
                            WordOffset.Y + (glyph.YOffset * effectiveScale.Y)
                        ),
                        Scale = effectiveScale * glyph.RenderScale,
                        Textures = new TextureSet(glyph.Texture),
                        TextureRegion = glyph.BoundsInTexture,
                        LocalData1 = wordIndex,
                    };

                    listener?.RecordTexture(glyph.Texture);
                }

                if (suppressThisCharacter)
                    ;
                else if (isWhiteSpace && (CurrentWord.DrawCallCount == 0)) {
                    CurrentWord.LeadingWhitespace += w;
                } else {
                    WordOffset.X += w;
                    CurrentWord.Width += w;
                }

                CurrentWord.Height = Math.Max(CurrentWord.Height, h);
            }
        }

        private void PerformForcedWrap (float wordWidth) {
            if (WordWrap && (wordWidth <= MaximumWidth)) {
                FinishLine(true);
                ;
            } else if (CharacterWrap) {
                FinishLine();
            } else
                ;
        }

        private void PerformLineBreak () {
            FinishLine();
        }

        private void FinishWord () {
            if (CurrentLine.Width <= 0) {
                // FIXME: WrapIndentation
                CurrentWord.LeadingWhitespace = (LineIndex == 0)
                    ? InitialIndentation
                    : BreakIndentation;
            }

            if (CurrentWord.DrawCallCount > 0) {
                for (uint i = CurrentWord.FirstDrawCall, i2 = i + CurrentWord.DrawCallCount - 1; i <= i2; i++) {
                    ref var drawCall = ref Buffers.DrawCall(i);
                    drawCall.Position += Position + LineOffset;
                    drawCall.Position.X += CurrentWord.LeadingWhitespace;
                }
            }

            LineOffset.X += CurrentWord.TotalWidth;
            CurrentLine.Width += CurrentWord.TotalWidth;
            CurrentLine.Height = Math.Max(CurrentLine.Height, CurrentWord.Height);
            if (CurrentLine.DrawCallCount == 0)
                CurrentLine.FirstDrawCall = CurrentWord.FirstDrawCall;
            if (CurrentWord.DrawCallCount > 0) {
                CurrentLine.DrawCallCount += CurrentWord.DrawCallCount;
                CurrentLine.VisibleWordCount++;
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

            UnconstrainedSize.X = Math.Max(CurrentLine.Width, UnconstrainedSize.X);
            UnconstrainedSize.Y += CurrentLine.Height;
            LineOffset.X = 0;
            LineOffset.Y += CurrentLine.Height;

            LineIndex++;
            CurrentLine = new Line {
            };
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
                        wordWhitespace = Math.Min(whitespace / wordCountMinusOne, MaxExpansionPerSpace);
                        whitespace = 0f;
                        break;
                    case HorizontalAlignment.JustifyWordsCentered:
                        if (line.VisibleWordCount > 1) {
                            wordWhitespace = Math.Min(whitespace / wordCountMinusOne, MaxExpansionPerSpace);
                            whitespace -= (wordWhitespace * wordCountMinusOne);
                        }
                        whitespace *= 0.5f;
                        break;
                    case HorizontalAlignment.JustifyCharacters:
                        characterWhitespace = Math.Min(whitespace / (line.DrawCallCount - 1), MaxExpansionPerSpace);
                        whitespace = 0f;
                        break;
                    case HorizontalAlignment.JustifyCharactersCentered:
                        if (line.DrawCallCount > 1) {
                            characterWhitespace = Math.Min(whitespace / (line.DrawCallCount - 1), MaxExpansionPerSpace);
                            whitespace -= (characterWhitespace * (line.DrawCallCount - 1));
                        }
                        whitespace *= 0.5f;
                        break;
                }

                short lastWordIndex = -1;
                for (uint j = line.FirstDrawCall, j2 = j + line.DrawCallCount - 1; j <= j2; j++) {
                    ref var drawCall = ref Buffers.DrawCall(j);
                    if ((drawCall.LocalData1 != lastWordIndex) && (lastWordIndex >= 0))
                        whitespace += wordWhitespace;
                    lastWordIndex = drawCall.LocalData1;

                    drawCall.Position.X += whitespace;
                    whitespace += characterWhitespace;
                }
            }
        }

        public unsafe void Finish (ArraySegment<BitmapDrawCall> buffer, out StringLayout result) {
            FinishLine();

            var constrainedSize = Vector2.Zero;
            for (uint i = 0; i <= LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                constrainedSize.X = Math.Max(constrainedSize.X, line.Width);
                constrainedSize.Y += line.Height;
            }

            constrainedSize.X = Math.Max(constrainedSize.X, DesiredWidth);
            AlignLines(constrainedSize.X);

            // HACK
            int bufferSize = Math.Max((int)DrawCallIndex, 512);
            if ((buffer.Array == null) || (buffer.Count < bufferSize))
                buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[bufferSize]);
            else
                buffer = new ArraySegment<BitmapDrawCall>(buffer.Array, buffer.Offset, (int)DrawCallIndex);

            var lastDrawCallIndex = DrawCallIndex > 0 ? DrawCallIndex - 1 : 0;
            fixed (BitmapDrawCall* dest = buffer.Array)
                Buffer.MemoryCopy(Buffers.DrawCalls, dest, buffer.Count * sizeof(BitmapDrawCall), DrawCallIndex * sizeof(BitmapDrawCall));

            result = new StringLayout(
                Vector2.Zero, constrainedSize, UnconstrainedSize, 0f,
                Buffers.DrawCall(0).EstimateDrawBounds(),
                Buffers.DrawCall(lastDrawCallIndex).EstimateDrawBounds(),
                buffer, false, (int)WordIndex, (int)LineIndex
            );
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

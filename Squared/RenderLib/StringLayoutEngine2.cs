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
        public uint FirstDrawCall, DrawCallCount;
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
        public float TotalWidth => LeadingWhitespace + Width;
    }

    public struct StringLayoutEngine2 : IDisposable {
        private unsafe struct StagingBuffers : IDisposable {
            private bool OwnsDrawCalls, OwnsLines, OwnsSpans, OwnsBoxes;
            public BitmapDrawCall* DrawCalls;
            public uint DrawCallCapacity;
            public Line* Lines;
            public uint LineCapacity;
            public Span* Spans;
            public uint SpanCapacity;
            public Bounds* Boxes;
            public uint BoxCapacity;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref BitmapDrawCall DrawCall (uint index) {
                if ((DrawCalls == null) || (index >= DrawCallCapacity))
                    Reallocate(ref OwnsDrawCalls, ref DrawCalls, ref DrawCallCapacity, index + 1);
                return ref DrawCalls[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Line Line (uint index) {
                if ((Lines == null) || (index >= LineCapacity))
                    Reallocate(ref OwnsLines, ref Lines, ref LineCapacity, index + 1);
                return ref Lines[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Span Span (uint index) {
                if ((Spans == null) || (index >= SpanCapacity))
                    Reallocate(ref OwnsSpans, ref Spans, ref SpanCapacity, index + 1);
                return ref Spans[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Bounds Box (uint index) {
                if ((Boxes == null) || (index >= BoxCapacity))
                    Reallocate(ref OwnsBoxes, ref Boxes, ref BoxCapacity, index + 1);
                return ref Boxes[index];
            }

            private static void Reallocate<T> (ref bool ownsPointer, ref T* ptr, ref uint currentCapacity, uint desiredCapacity)
                where T : unmanaged {
                if (desiredCapacity > 1024000)
                    throw new ArgumentOutOfRangeException(nameof(desiredCapacity));
                if (desiredCapacity <= currentCapacity)
                    return;

                var newCapacity = Math.Max(currentCapacity, 64);
                while (newCapacity < desiredCapacity)
                    newCapacity = (newCapacity * 17 / 10);

                if ((ptr != null) && ownsPointer)
                    ptr = (T*)Marshal.ReAllocHGlobal((IntPtr)ptr, (IntPtr)(newCapacity * sizeof(T)));
                else
                    ptr = (T*)Marshal.AllocHGlobal((IntPtr)(newCapacity * sizeof(T)));
                ownsPointer = true;
                currentCapacity = newCapacity;
            }

            private static void Deallocate<T> (ref bool ownsPointer, ref T* ptr, ref uint size)
                where T : unmanaged
            {
                if (ownsPointer && (ptr != null))
                    Marshal.FreeHGlobal((IntPtr)ptr);
                ownsPointer = false;
                ptr = null;
                size = 0;
            }

            public void Dispose () {
                Deallocate(ref OwnsDrawCalls, ref DrawCalls, ref DrawCallCapacity);
                Deallocate(ref OwnsLines, ref Lines, ref LineCapacity);
                Deallocate(ref OwnsSpans, ref Spans, ref SpanCapacity);
                Deallocate(ref OwnsBoxes, ref Boxes, ref BoxCapacity);
            }

            internal void SetDrawCalls (void* buffer, uint size) {
                Deallocate(ref OwnsDrawCalls, ref DrawCalls, ref DrawCallCapacity);
                DrawCalls = (BitmapDrawCall*)buffer;
                DrawCallCapacity = (uint)(size / sizeof(BitmapDrawCall));
            }

            internal void EnsureCapacity (uint drawCallCount) {
                Reallocate(ref OwnsDrawCalls, ref DrawCalls, ref DrawCallCapacity, drawCallCount);
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

        public float InitialIndentation, WrapIndentation, BreakIndentation;
        public float MaximumWidth, DesiredWidth;
        public float MaxExpansionPerSpace;

        bool IsInitialized;

        public void Initialize () {
            WrapCharacters.SortNonRef(StringLayoutEngine.UintComparer.Instance);

            IsInitialized = true;
            CurrentWord.FirstDrawCall = uint.MaxValue;
            CurrentLine = default;
        }

        public unsafe void SetScratchBuffer (void * buffer, uint size) {
            Buffers.SetDrawCalls(buffer, size);
        }

        private ref BitmapDrawCall AppendDrawCall (ref BitmapDrawCall dead, bool isWhiteSpace) {
            if (isWhiteSpace || SuppressUntilEnd || SuppressUntilNextLine || MeasureOnly)
                return ref dead;

            if (CurrentWord.FirstDrawCall == uint.MaxValue)
                CurrentWord.FirstDrawCall = DrawCallIndex;
            CurrentWord.DrawCallCount++;
            return ref Buffers.DrawCall(DrawCallIndex++);
        }

        private ref Line CurrentLine => ref Buffers.Line(LineIndex);
        private ref Span CurrentSpan => ref Buffers.Span(SpanIndex);

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

                var w = (glyph.WidthIncludingBearing * effectiveScale.X) + (glyph.CharacterSpacing * effectiveScale.X);
                var h = glyph.LineSpacing * effectiveScale.Y;
                var x2 = WordOffset.X + LineOffset.X + w;

                if (x2 >= MaximumWidth)
                    forcedWrap = true;

                if (forcedWrap)
                    PerformForcedWrap();
                else if (lineBreak)
                    PerformLineBreak();

                if (isWhiteSpace)
                    FinishWord();

                if (!deadGlyph) {
                    ref var drawCall = ref AppendDrawCall(ref dead, isWhiteSpace);

                    drawCall = new BitmapDrawCall {
                        MultiplyColor = OverrideColor ? MultiplyColor : (glyph.DefaultColor ?? MultiplyColor),
                        AddColor = AddColor,
                        Position = new Vector2(
                            WordOffset.X + (glyph.XOffset * effectiveScale.X) + (glyph.LeftSideBearing * effectiveScale.X),
                            WordOffset.Y + (glyph.YOffset * effectiveScale.Y)
                        ),
                        Scale = effectiveScale * glyph.RenderScale,
                        Textures = new TextureSet(glyph.Texture),
                        TextureRegion = glyph.BoundsInTexture,                    
                    };
                }

                if (isWhiteSpace && (CurrentWord.DrawCallCount == 0)) {
                    CurrentWord.LeadingWhitespace += w;
                } else {
                    WordOffset.X += w;
                    CurrentWord.Width += w;
                }

                CurrentWord.Height = Math.Max(CurrentWord.Height, h);

                if (isWordWrapPoint)
                    FinishWord();
            }
        }

        private void PerformForcedWrap () {
            if (WordWrap && (CurrentWord.TotalWidth <= MaximumWidth)) {
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
            if (CurrentLine.Width <= 0)
                CurrentWord.LeadingWhitespace = 0f;

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
            CurrentLine.DrawCallCount += CurrentWord.DrawCallCount;
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
                FirstDrawCall = DrawCallIndex,
            };
        }

        private void AlignLines (float totalWidth) {
            // FIXME
        }

        public unsafe void Finish (ArraySegment<BitmapDrawCall> buffer, out StringLayout result) {
            FinishLine();

            var constrainedSize = Vector2.Zero;
            for (uint i = 0; i <= LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                constrainedSize.X = Math.Max(constrainedSize.X, line.Width);
                constrainedSize.Y += line.Height;
            }

            AlignLines(constrainedSize.X);

            // HACK
            int bufferSize = Math.Max((int)DrawCallIndex, 8192);
            if ((buffer.Array == null) || (buffer.Count < bufferSize))
                buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[bufferSize]);
            else
                buffer = new ArraySegment<BitmapDrawCall>(buffer.Array, buffer.Offset, bufferSize);

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
            // glyphLineSpacing += additionalLineSpacing;
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

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
        public uint FirstChar, CharCount;
        public uint FirstBoxIndex, BoxCount;
    }

    public struct Span {
        public uint FirstChar, CharCount;
        public uint FirstBoxIndex, BoxCount;
    }

    public struct Word {
        public uint FirstChar, CharCount;
    }

    public struct StringLayoutEngine2 : IDisposable {
        private unsafe struct StagingBuffers : IDisposable {
            private bool OwnsChars, OwnsLines, OwnsSpans, OwnsBoxes;
            public BitmapDrawCall* Chars;
            public uint CharCapacity;
            public Line* Lines;
            public uint LineCapacity;
            public Span* Spans;
            public uint SpanCapacity;
            public Bounds* Boxes;
            public uint BoxCapacity;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref BitmapDrawCall Char (uint index) {
                if (index >= CharCapacity)
                    Reallocate(ref OwnsChars, ref Chars, ref CharCapacity, index);
                return ref Chars[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Line Line (uint index) {
                if (index >= LineCapacity)
                    Reallocate(ref OwnsLines, ref Lines, ref LineCapacity, index);
                return ref Lines[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Span Span (uint index) {
                if (index >= SpanCapacity)
                    Reallocate(ref OwnsSpans, ref Spans, ref SpanCapacity, index);
                return ref Spans[index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Bounds Box (uint index) {
                if (index >= BoxCapacity)
                    Reallocate(ref OwnsBoxes, ref Boxes, ref BoxCapacity, index);
                return ref Boxes[index];
            }

            private static void Reallocate<T> (ref bool ownsPointer, ref T* ptr, ref uint currentCapacity, uint desiredCapacity)
                where T : unmanaged {
                var newCapacity = Math.Max(currentCapacity, 32);
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
                Deallocate(ref OwnsChars, ref Chars, ref CharCapacity);
                Deallocate(ref OwnsLines, ref Lines, ref LineCapacity);
                Deallocate(ref OwnsSpans, ref Spans, ref SpanCapacity);
                Deallocate(ref OwnsBoxes, ref Boxes, ref BoxCapacity);
            }

            internal void SetChars (void* buffer, uint size) {
                Deallocate(ref OwnsChars, ref Chars, ref CharCapacity);
                Chars = (BitmapDrawCall*)buffer;
                CharCapacity = size;
            }
        }

        private struct Location {
            public float X, Y, Baseline;
        }

        Location Unconstrained, Constrained;
        uint RowIndex, ColIndex, LineIndex, CharIndex;
        bool SuppressUntilNextLine, SuppressUntilEnd;

        bool ExternalBuffers;
        StagingBuffers Buffers;
        uint CharsInBuffer, LinesInBuffer, SpansInBuffer, BoxesInBuffer;

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
        }

        public unsafe void SetScratchBuffer (void * buffer, uint size) {
            Buffers.SetChars(buffer, size);
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

            var drawCall = new BitmapDrawCall {
                MultiplyColor = MultiplyColor,
                SortKey = SortKey,
                AddColor = AddColor,
                Scale = Scale,
            };

            for (int i = 0, l = text.Length; i < l; i++) {
                if (LineLimit.HasValue && LineLimit.Value <= 0)
                    SuppressUntilEnd = true;

                DecodeCodepoint(text, ref i, l, out char ch1, out int currentCodepointSize, out uint codepoint);

                AnalyzeWhitespace(
                    ch1, codepoint, out bool isWhiteSpace, out bool forcedWrap, out bool lineBreak, 
                    out bool deadGlyph, out bool isWordWrapPoint, out bool didWrapWord
                );

                // FIXME: Wrap point detection

                BuildGlyphInformation(
                    glyphSource, Scale, Spacing, ch1, codepoint,
                    out deadGlyph, out Glyph glyph, out float glyphLineSpacing, out float glyphBaseline
                );

                // FIXME: Kerning
            }
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

﻿#define TRACK_CODEPOINTS

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
using System.Security.Cryptography;
using static System.Net.Mime.MediaTypeNames;
using System.Diagnostics.Eventing.Reader;

namespace Squared.Render.TextLayout2 {
    public enum FragmentCategory : byte {
        Unknown = 0b0,
        Regular = 0b1,
        WrapPoint = 0b10,
        Box = 0b100,
        Whitespace = 0b1000,
        NonPrintable = 0b10000,
    }

    public struct Line {
        public uint Index;
        public uint FirstFragmentIndex, FragmentCount;
        public uint GapCount;
        public uint FirstDrawCall, DrawCallCount;
        public Vector2 Location;
        public float Width, TrailingWhitespace, Height, Baseline;

        public float ActualWidth {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Width + TrailingWhitespace;
        }
    }

    public struct Span {
        public uint Index;
        public uint FirstDrawCall, DrawCallCount;
        public uint FirstFragmentIndex, LastFragmentIndex;
        public float FirstRelativeX, LastRelativeX;
    }

    public struct Fragment {
        public uint LineIndex, BoxIndex;
        public uint FirstDrawCall, DrawCallCount;
        public float Left, Width, Height, Baseline;
        public FragmentCategory Category;
        public bool WasSuppressed;
        public bool WasFullySuppressed {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WasSuppressed && (DrawCallCount == 0) && (Width <= 0);
        }
        public bool ContainsContent {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Category < FragmentCategory.Whitespace;
        }
    }

    public struct Box {
        public Bounds Bounds;
        public Vector2 Alignment;
        public uint FragmentIndex;
        // Optional
        public uint DrawCallIndex;
    }

    public interface IStringLayoutListener {
        void Initializing (ref StringLayoutEngine2 engine);
        void RecordTexture (ref StringLayoutEngine2 engine, AbstractTextureReference texture);
        void Finishing (ref StringLayoutEngine2 engine);
        void Finished (ref StringLayoutEngine2 engine, ref StringLayout result);
    }

    public struct StringLayoutEngine2 : IDisposable {
        private unsafe struct StagingBuffers : IDisposable {
            public BitmapDrawCall* DrawCalls;
            public Line* Lines;
            public Span* Spans;
            public Box* Boxes;
            public Fragment* Fragments;
            public uint DrawCallCapacity, LineCapacity, SpanCapacity, BoxCapacity, FragmentCapacity;

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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref Fragment Fragment (uint index) {
                if (index >= FragmentCapacity)
                    Reallocate(ref Fragments, ref FragmentCapacity, index + 1);
                return ref Fragments[index];
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
                Deallocate(ref Fragments, ref FragmentCapacity);
            }

            internal void EnsureCapacity (uint drawCallCount, uint fragmentCount) {
                Reallocate(ref DrawCalls, ref DrawCallCapacity, drawCallCount);
                Reallocate(ref Fragments, ref FragmentCapacity, fragmentCount);
            }
        }

        // Internal state
        Vector2 FragmentOffset;
        float Y;
        Vector2 UnconstrainedSize, UnconstrainedLineSize;
        uint ColIndex, LineIndex, CharIndex, 
            DrawCallIndex, SpanIndex, FragmentIndex,
            BoxIndex;
        bool SuppressUntilEnd;

        StagingBuffers Buffers;

        // FIXME: Make this do something
        bool NewLinePending;
        // Push/pop span stack
        DenseList<uint> SpanStack;

        AbstractTextureReference MostRecentTexture;

#if TRACK_CODEPOINTS
        DenseList<uint> CurrentFragmentCodepoints;
        string CurrentFragmentText => string.Join("", CurrentFragmentCodepoints.Select(c => (char)c));
#endif

        // Configuration
        public IStringLayoutListener Listener;

        public DenseList<uint> WrapCharacters;

        public bool MeasureOnly;
        public bool CharacterWrap, WordWrap, SplitAtWrapCharactersOnly;
        public bool HideOverflow, IncludeTrailingWhitespace;
        public bool OverrideColor;

        // FIXME: Not implemented
        public int CharacterSkipCount;
        // FIXME: Not implemented
        public int? CharacterLimit, 
            // FIXME: These seem buggy
            LineLimit, 
            BreakLimit;

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
        public uint? TerminatorCodepoint;

        public float InitialIndentation, BreakIndentation, WrapIndentation;
        public float AdditionalLineSpacing, ExtraBreakSpacing;
        public float MaximumWidth, DesiredWidth;
        public float MaximumHeight;
        public float MaxExpansionPerSpace;

        public Pair<int>? MarkedRange;
        public Vector2? HitTestLocation;

        // Output
        public uint MarkedRangeSpanIndex;
        public LayoutHitTest HitTestResult;
        public bool AnyCharactersSuppressed;

        bool IsInitialized;
        public bool IsTruncated =>
            // FIXME: < 0 instead of <= 0?
            ((LineLimit ?? int.MaxValue) <= 0) ||
            ((BreakLimit ?? int.MaxValue) <= 0) ||
            ((CharacterLimit ?? int.MaxValue) <= 0);

        public void Initialize () {
            // FIXME
            if (IsInitialized)
                throw new InvalidOperationException("A StringLayoutEngine2 instance cannot be used more than once");

            IsInitialized = false;

            WrapCharacters.SortNonRef(StringLayoutEngine.UintComparer.Instance);

            Listener?.Initializing(ref this);

            IsInitialized = true;
            Buffers.Span(0) = default;
            CurrentLine = new Line {
                Location = new Vector2(InitialIndentation, 0f),
            };
            Y = 0f;
            CurrentFragment = default;
            MarkedRangeSpanIndex = uint.MaxValue;
            HitTestResult.Position = HitTestLocation ?? default;
            MostRecentTexture = AbstractTextureReference.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref BitmapDrawCall AppendDrawCall () {
            if (CurrentFragment.FirstDrawCall == uint.MaxValue)
                CurrentFragment.FirstDrawCall = DrawCallIndex;
            CurrentFragment.DrawCallCount++;
            return ref Buffers.DrawCall(DrawCallIndex++);
        }

        private ref Fragment CurrentFragment {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Buffers.Fragment(FragmentIndex);
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
            if (span.LastFragmentIndex == uint.MaxValue)
                return true;

            ref var fragment1 = ref Buffers.Fragment(span.FirstFragmentIndex);
            ref var fragment2 = ref Buffers.Fragment(span.LastFragmentIndex);
            if (fragment1.WasFullySuppressed)
                return false;

            // FIXME: This whole algorithm doesn't work right if there are spaces at the start or end
            //  of the span being measured.

            for (uint l = fragment1.LineIndex, l2 = fragment2.LineIndex; l <= l2; l++) {
                if (!TryGetLineBounds(l, out var lineBounds))
                    continue;
                ref var line = ref Buffers.Line(l);

                if (l == fragment1.LineIndex)
                    lineBounds.TopLeft.X = fragment1.Left + span.FirstRelativeX;

                if (l == l2)
                    lineBounds.BottomRight.X = Arithmetic.Clamp(
                        fragment2.Left + span.LastRelativeX, 
                        lineBounds.TopLeft.X, lineBounds.BottomRight.X
                    );

                if (lineBounds.Size.X > 0f)
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
            bounds = Bounds.FromPositionAndSize(
                line.Location.X + Position.X, line.Location.Y + Position.Y, 
                line.Width, line.Height
            );
            return true;
        }

        public bool TryGetBoxBounds (uint index, out Bounds bounds) {
            if (index >= BoxIndex) {
                bounds = default;
                return false;
            }

            ref var box = ref Buffers.Box(index);
            bounds = box.Bounds;
            return true;
        }
        
        public ref Span BeginSpan (bool push) {
            // HACK: Split whitespace fragments when beginning a span,
            //  so that relative positioning is accurate in justified mode.
            if (!CurrentFragment.ContainsContent && CurrentFragment.Width > 0)
                FinishFragment();

            var index = SpanIndex++;
            if (push || SpanStack.Count == 0)
                SpanStack.Add(index);
            else
                SpanStack.Last() = index;

            ref var result = ref Buffers.Span(index);
            result = new Span {
                Index = index,
                FirstDrawCall = DrawCallIndex,
                FirstFragmentIndex = FragmentIndex,
                FirstRelativeX = FragmentOffset.X,
                LastFragmentIndex = uint.MaxValue,
            };
            return ref result;
        }

        public ref Span EndCurrentSpan () {
            if (!SpanStack.TryRemoveLast(out var index))
                throw new Exception("No span active");

            return ref EndSpan(index);
        }

        public ref Span EndSpanByIndex (uint index) {
            var offset = SpanStack.IndexOf(index, StringLayoutEngine.UintComparer.Instance);
            if (offset < 0)
                throw new Exception("Span not active");

            SpanStack.RemoveAt(offset);
            return ref EndSpan(index);
        }

        private ref Span EndSpan (uint index) {
            ref var result = ref Buffers.Span(index);
            result.LastRelativeX = FragmentOffset.X;
            result.LastFragmentIndex = FragmentIndex;
            result.DrawCallCount = DrawCallIndex - result.FirstDrawCall;
            return ref result;
        }

        private void UpdateMarkedRange () {
            if (MarkedRange?.First == CharIndex)
                MarkedRangeSpanIndex = BeginSpan(true).Index;

            if (MarkedRange?.Second == CharIndex) {
                if (MarkedRangeSpanIndex != uint.MaxValue) {
                    EndSpan(MarkedRangeSpanIndex);
                    SpanStack.Remove(MarkedRangeSpanIndex);
                }
            }
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

            KerningData thisKerning = default, nextKerning = default;
            bool hasKerningNow = false, hasKerningNext = false;
            Buffers.EnsureCapacity(
                MeasureOnly ? 0 : (uint)(DrawCallIndex + text.Length),
                (uint)(FragmentIndex + (text.Length / 8))
            );

            var effectiveScale = Scale * (1.0f / glyphSource.DPIScaleFactor);
            float defaultLineSpacing = glyphSource.LineSpacing * effectiveScale.Y;

            for (int i = 0, l = text.Length; i < l; i++) {
                if (LineLimit.HasValue && LineLimit.Value <= 0)
                    SuppressLayoutForLimit();
                else if (Y > MaximumHeight)
                    SuppressLayoutForLimit();

                UpdateMarkedRange();

                DecodeCodepoint(text, ref i, l, out char ch1, out int currentCodepointSize, out uint codepoint);
                if (codepoint == TerminatorCodepoint)
                    SuppressLayoutForLimit();

                AnalyzeWhitespace(
                    ch1, codepoint, out bool lineBreak, out bool deadGlyph, out var category
                );

                BuildGlyphInformation(
                    // FIXME: This will be wrong after recalc
                    glyphSource, effectiveScale, Spacing, ch1, codepoint, CurrentLine.DrawCallCount == 0,
                    out deadGlyph, out Glyph glyph, out float glyphLineSpacing, out float glyphBaseline
                );

                if (category == FragmentCategory.NonPrintable) {
                    ;
                } else {
                    if (CurrentFragment.Category == FragmentCategory.Unknown)
                        CurrentFragment.Category = category;

                    if (category != CurrentFragment.Category)
                        FinishFragment();

                    if (CurrentFragment.Category == FragmentCategory.Unknown)
                        CurrentFragment.Category = category;
                }

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

                bool allowBreaking = true;

            recalc:
                float w = (glyph.WidthIncludingBearing * effectiveScale.X) + (glyph.CharacterSpacing * effectiveScale.X),
                    h = glyphLineSpacing,
                    xBasis = CurrentLine.Location.X + CurrentLine.ActualWidth,
                    x1 = xBasis + FragmentOffset.X,
                    x2 = x1 + w,
                    y2 = Y + h;
                bool overflowX = (x2 > MaximumWidth),
                    overflowY = (y2 > MaximumHeight);

                if (allowBreaking) {
                    allowBreaking = false;

                    if (lineBreak) {
                        PerformLineBreak(defaultLineSpacing);
                        goto recalc;
                    } else if (overflowX) {
                        if (PerformForcedWrap(w))
                            goto recalc;
                    }
                }

                if (HideOverflow) {
                    if (overflowY)
                        SuppressLayoutForLimit();
                }

                ref var fragment = ref CurrentFragment;
                if (glyphBaseline > fragment.Baseline)
                    IncreaseBaseline(glyphBaseline);

                float alignmentToBaseline = fragment.Baseline - glyphBaseline;
                bool suppressThisCharacter = SuppressUntilEnd || (overflowX && HideOverflow);

#if TRACK_CODEPOINTS
                CurrentFragmentCodepoints.Add(codepoint);
#endif

                if (
                    !deadGlyph && 
                    !suppressThisCharacter && 
                    (category < FragmentCategory.Whitespace)
                ) {
                    if (MeasureOnly) {
                        // HACK: There is code that uses DrawCallCount != 0 to make decisions,
                        //  so we need to maintain a fake draw call counter in order to keep that working
                        if (CurrentFragment.FirstDrawCall == uint.MaxValue)
                            CurrentFragment.FirstDrawCall = DrawCallIndex;
                        CurrentFragment.DrawCallCount++;
                    } else {
                        ref var drawCall = ref AppendDrawCall();

                        float x = FragmentOffset.X + (glyph.XOffset * effectiveScale.X) + (glyph.LeftSideBearing * effectiveScale.X),
                            // Used to compute bounding box offsets
                            wx = xBasis + x,
                            y = FragmentOffset.Y + (glyph.YOffset * effectiveScale.Y) + alignmentToBaseline;
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
                        };

                        if (glyph.Texture != MostRecentTexture) {
                            Listener?.RecordTexture(ref this, glyph.Texture);
                            MostRecentTexture = glyph.Texture;
                        }

                        if (fragment.WasSuppressed)
                            fragment.WasSuppressed = false;
                    }
                }

                if (suppressThisCharacter && !deadGlyph && (category < FragmentCategory.Whitespace)) {
                    fragment.WasSuppressed = true;
                    AnyCharactersSuppressed = true;
                }

                FragmentOffset.X += w;
                if (!SuppressUntilEnd) {
                    fragment.Width += w;
                    fragment.Height = Math.Max(fragment.Height, h);
                }

                if (HitTestLocation.HasValue && (HitTestLocation.Value.X >= x1) && (HitTestLocation.Value.X <= x2)) {
                    if (!HitTestResult.FirstCharacterIndex.HasValue)
                        HitTestResult.FirstCharacterIndex = (int)CharIndex;
                    HitTestResult.LastCharacterIndex = (int)CharIndex;
                    HitTestResult.LeaningRight = HitTestLocation.Value.X >= ((x1 + x2) * 0.5f);
                }

                UnconstrainedLineSize.X += w;
                UnconstrainedLineSize.Y = Math.Max(UnconstrainedLineSize.Y, h);

                CharIndex++;
            }
        }

        private void IncreaseBaseline (float newBaseline) {
            ref var fragment = ref CurrentFragment;
            float adjustment = newBaseline - fragment.Baseline;
            fragment.Baseline = newBaseline;
            if (fragment.DrawCallCount == 0)
                return;

            for (uint d = fragment.FirstDrawCall, d2 = d + fragment.DrawCallCount - 1; d <= d2; d++) {
                ref var dc = ref Buffers.DrawCall(d);
                dc.Position.Y += adjustment;
            }
        }

        private bool PerformForcedWrap (float characterWidth) {
            ref var fragment = ref CurrentFragment;
            // Determine whether wrapping the entire fragment will work.
            if (WordWrap) {
                // If we're about to wrap a whitespace fragment, just say we succeeded but don't do anything.
                if (!fragment.ContainsContent)
                    return true;

                if (fragment.Width + characterWidth <= MaximumWidth) {
                } else if (CharacterWrap) {
                    // We can't cleanly word wrap so we should try to character wrap instead.
                    FinishFragment();
                    FinishLine(false);
                    return (characterWidth <= MaximumWidth);
                }
                FinishLine(false);
                return true;
            }
            
            if (CharacterWrap) {
                // Fragment wrapping didn't work, so split the fragment to a new line.
                FinishFragment();
                FinishLine(false);
                return (characterWidth <= MaximumWidth);
            }

            return false;
        }

        private void PerformLineBreak (float defaultLineSpacing) {
            var height = UnconstrainedLineSize.Y == 0f ? defaultLineSpacing : UnconstrainedLineSize.Y;
            UnconstrainedSize.X = Math.Max(UnconstrainedSize.X, UnconstrainedLineSize.X);
            UnconstrainedSize.Y += height + ExtraBreakSpacing;
            UnconstrainedLineSize = default;
            FinishFragment();
            if (CurrentLine.Height == 0f)
                CurrentLine.Height = defaultLineSpacing;
            FinishLine(true);
        }

        private void SuppressLayoutForLimit () {
            if (!SuppressUntilEnd)
                EraseFragment(ref CurrentFragment);
            SuppressUntilEnd = true;
        }

        private void EraseFragment (ref Fragment fragment) {
            fragment.WasSuppressed = true;
            fragment.Width = 0;
            fragment.Height = 0;
            fragment.Baseline = 0;

            if (fragment.DrawCallCount == 0)
                return;

            for (uint f = fragment.FirstDrawCall, f2 = f + fragment.DrawCallCount - 1; f <= f2; f++)
                Buffers.DrawCall(f) = default;

            fragment.DrawCallCount = 0;
        }

        private void FinishFragment () {
            ref var fragment = ref CurrentFragment;
            fragment.LineIndex = LineIndex;

            ref var line = ref CurrentLine;

            line.Baseline = Math.Max(line.Baseline, fragment.Baseline);

            float baselineAdjustment = line.Baseline - fragment.Baseline;

            switch (fragment.Category) {
                case FragmentCategory.Regular:
                case FragmentCategory.Box:
                    line.Width += fragment.Width + line.TrailingWhitespace;
                    line.TrailingWhitespace = 0f;
                    break;
                case FragmentCategory.WrapPoint:
                    if (SplitAtWrapCharactersOnly)
                        line.GapCount++;
                    line.Width += fragment.Width + line.TrailingWhitespace;
                    line.TrailingWhitespace = 0f;
                    break;
                case FragmentCategory.Whitespace:
                    if (!SplitAtWrapCharactersOnly)
                        line.GapCount++;
                    line.TrailingWhitespace += fragment.Width;
                    break;
                case FragmentCategory.NonPrintable:
                    line.Width += fragment.Width;
                    break;
            }

            if (!fragment.WasFullySuppressed)
                line.Height = Math.Max(line.Height, fragment.Height);

            FragmentOffset = default;

            if (fragment.DrawCallCount > 0) {
                if (line.DrawCallCount == 0)
                    line.FirstDrawCall = fragment.FirstDrawCall;
                line.DrawCallCount += fragment.DrawCallCount;
            }

            line.FragmentCount++;
            FragmentIndex++;

            CurrentFragment = new Fragment {
                FirstDrawCall = DrawCallIndex,
                LineIndex = LineIndex,
                WasSuppressed = SuppressUntilEnd
            };

#if TRACK_CODEPOINTS
            CurrentFragmentCodepoints.Clear();
#endif
        }

        private void FinishLine (bool forLineBreak) {
            ref var line = ref CurrentLine;

            if (!SuppressUntilEnd)
                Y += CurrentLine.Height + (forLineBreak ? ExtraBreakSpacing : 0f);

            float indentation = forLineBreak ? BreakIndentation : WrapIndentation;
            LineIndex++;
            CurrentLine = new Line {
                Index = LineIndex,
                Location = new Vector2(indentation, Y),
                FirstFragmentIndex = FragmentIndex,
            };

            if (LineLimit.HasValue) {
                LineLimit--;
                if (LineLimit.Value <= 0)
                    SuppressLayoutForLimit();
            }
        }

        public void CreateEmptyBox (float width, float height) {
            FinishFragment();

            var boxIndex = BoxIndex++;

            ref var fragment = ref CurrentFragment;
            fragment.BoxIndex = boxIndex;
            fragment.Category = FragmentCategory.Box;
            fragment.Width = width;
            fragment.Height = height;

            ref var box = ref Buffers.Box(boxIndex);
            box = new Box {
                FragmentIndex = FragmentIndex,
                DrawCallIndex = uint.MaxValue,
            };

            FinishFragment();
        }

        public void AppendImage (ref RichImage image) {
            FinishFragment();

            var drawCallIndex = DrawCallIndex;
            var drawCall = new BitmapDrawCall(
                new TextureSet(image.Texture), Vector2.Zero
            ) {
                ScaleF = image.Scale,
            };
            var bounds = drawCall.EstimateDrawBounds();
            if (!MeasureOnly)
                AppendDrawCall() = drawCall;

            var boxIndex = BoxIndex++;
            ref var fragment = ref CurrentFragment;
            fragment.BoxIndex = boxIndex;
            fragment.Category = FragmentCategory.Box;
            fragment.Width = bounds.Size.X;
            fragment.Height = image.DoNotAdjustLineSpacing ? CurrentLine.Height : bounds.Size.Y;

            ref var box = ref Buffers.Box(boxIndex);
            box = new Box {
                FragmentIndex = FragmentIndex,
                DrawCallIndex = drawCallIndex,
                Alignment = image.Alignment,
            };

            FinishFragment();
        }

        private void ArrangeFragments (ref Vector2 constrainedSize) {
            var gapCategory = SplitAtWrapCharactersOnly 
                ? FragmentCategory.WrapPoint 
                : FragmentCategory.Whitespace;

            for (uint i = 0; i <= LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                int gapCount = (int)line.GapCount;
                if (line.FragmentCount > 1) {
                    ref var lastFragment = ref Buffers.Fragment(line.FirstFragmentIndex + line.FragmentCount - 1);
                    // Don't enlarge trailing whitespace at the end of a line in justification mode.
                    // Crush it instead.
                    if (!lastFragment.ContainsContent) {
                        if (!SplitAtWrapCharactersOnly)
                            gapCount -= 1;
                        lastFragment.Width = 0f;
                    }
                }

                float totalWidth = constrainedSize.X, inset = 0f, crush = 0f;
                var interval = new Interval(line.Location.Y, line.Location.Y + line.Height);
                AnalyzeEdgeBoxes(constrainedSize, ref line, totalWidth, ref inset, ref crush, interval, 0, line.FirstFragmentIndex);
                totalWidth = constrainedSize.X - crush - inset;

                float y = Position.Y + line.Location.Y;

                // If we have any boxes, we need to scan this line for absolutely-positioned boxes so we can
                //  determine how much they're crowding the left/right edges, which means we need to arrange them
                if (BoxIndex > 0) {
                    for (uint f = line.FirstFragmentIndex, f2 = f + line.FragmentCount - 1; f <= f2; f++) {
                        ref var fragment = ref Buffers.Fragment(f);
                        if (fragment.Category != FragmentCategory.Box)
                            continue;

                        ref var box = ref Buffers.Box(fragment.BoxIndex);
                        if (box.Alignment.X == 0.5f)
                            continue;

                        if (box.DrawCallIndex != uint.MaxValue) {
                            ref var drawCall = ref Buffers.DrawCall(box.DrawCallIndex);
                            var estimatedBounds = drawCall.EstimateDrawBounds();
                            float boxX = Arithmetic.Lerp(Position.X, constrainedSize.X - estimatedBounds.Size.X + Position.X, box.Alignment.X);
                            box.Bounds = Bounds.FromPositionAndSize(boxX, y, estimatedBounds.Size.X, estimatedBounds.Size.Y);
                            drawCall.Position = new Vector2(boxX, y);
                        } else {
                            float boxX = Arithmetic.Lerp(Position.X, constrainedSize.X - fragment.Width + Position.X, box.Alignment.X);
                            box.Bounds = Bounds.FromPositionAndSize(boxX, y, fragment.Width, fragment.Height);
                        }

                        constrainedSize.Y = Math.Max(constrainedSize.Y, box.Bounds.BottomRight.Y - Position.Y);
                    }

                    // Afterwards, we scan all the boxes from this line too
                    AnalyzeEdgeBoxes(constrainedSize, ref line, totalWidth, ref inset, ref crush, interval, line.FirstFragmentIndex, line.FirstFragmentIndex + line.FragmentCount + 1);
                }

                float whitespace = totalWidth - line.Width, gapWhitespace = 0f;

                switch (Alignment) {
                    case HorizontalAlignment.Left:
                        whitespace = 0f;
                        break;
                    case HorizontalAlignment.Center:
                        whitespace *= 0.5f;
                        break;
                    case HorizontalAlignment.Right:
                        break;
                    case HorizontalAlignment.JustifyWords:
                        if (gapCount > 0) {
                            gapWhitespace = whitespace / gapCount;
                            if (gapWhitespace > MaxExpansionPerSpace)
                                gapWhitespace = 0f;
                        }
                        whitespace = 0f;
                        break;
                    case HorizontalAlignment.JustifyWordsCentered:
                        if (gapCount > 0) {
                            gapWhitespace = whitespace / gapCount;
                            if (gapWhitespace > MaxExpansionPerSpace)
                                gapWhitespace = 0f;
                            whitespace -= (gapWhitespace * gapCount);
                        }
                        whitespace *= 0.5f;
                        break;
                }

                line.Location.X += inset + whitespace;
                line.Width += (gapWhitespace * gapCount);
                if (line.FragmentCount == 0)
                    continue;

                float x = Position.X + line.Location.X;

                for (uint f = line.FirstFragmentIndex, f2 = f + line.FragmentCount - 1; f <= f2; f++) {
                    ref var fragment = ref Buffers.Fragment(f);
                    float fragmentY = y + (line.Baseline - fragment.Baseline);
                    fragment.Left = x;

                    if (fragment.Category == FragmentCategory.Box) {
                        ref var box = ref Buffers.Box(fragment.BoxIndex);
                        if (box.Alignment.X != 0.5f)
                            continue;

                        if (box.DrawCallIndex != uint.MaxValue) {
                            ref var drawCall = ref Buffers.DrawCall(box.DrawCallIndex);
                            var estimatedBounds = drawCall.EstimateDrawBounds();
                            box.Bounds = Bounds.FromPositionAndSize(x, y, estimatedBounds.Size.X, estimatedBounds.Size.Y);
                            drawCall.Position = new Vector2(x, y);
                        } else {
                            box.Bounds = Bounds.FromPositionAndSize(x, y, fragment.Width, fragment.Height);
                        }

                        constrainedSize.Y = Math.Max(constrainedSize.Y, box.Bounds.BottomRight.Y - Position.Y);

                        x += fragment.Width;
                    } else {
                        if (fragment.Category == gapCategory)
                            x += gapWhitespace;

                        if (fragment.DrawCallCount > 0) {
                            for (uint dc = fragment.FirstDrawCall, dc2 = dc + fragment.DrawCallCount - 1; dc <= dc2; dc++) {
                                ref var drawCall = ref Buffers.DrawCall(dc);
                                drawCall.Position.X += x;
                                drawCall.Position.Y += fragmentY;
                            }
                        }

                        x += fragment.Width;
                    }
                }
            }
        }

        private void AnalyzeEdgeBoxes (
            Vector2 constrainedSize, ref Line line, 
            float totalWidth, ref float inset, ref float crush, 
            Interval interval, uint firstIndex, uint indexLimit
        ) {
            for (uint b = 0; b < BoxIndex; b++) {
                ref var box = ref Buffers.Box(b);
                // FIXME: Optimize this
                if (box.FragmentIndex < firstIndex)
                    continue;
                if (box.FragmentIndex >= indexLimit)
                    break;
                if (box.Alignment.X == 0.5f)
                    continue;

                if (!box.Bounds.Y.Intersects(interval, 0.1f))
                    continue;

                if (box.Bounds.TopLeft.X <= (Position.X + inset)) {
                    inset += box.Bounds.Size.X;
                } else if (box.Bounds.BottomRight.X >= (Position.X + totalWidth)) {
                    crush += box.Bounds.Size.X;
                }
            }
        }

        public void ComputeConstrainedSize (out Vector2 constrainedSize) {
            constrainedSize = Vector2.Zero;
            for (uint i = 0; i <= LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                constrainedSize.X = Math.Max(constrainedSize.X, line.Width);

                // HACK: Fixes a single-line size overhang from line limiting
                // FIXME: This will break trailing whitespace from extra line breaks.
                if ((line.DrawCallCount == 0) && (line.Width <= 0))
                    continue;

                // We have to use Max here because of things like ExtraBreakSpacing that don't
                //  alter the line's height. If we were to just sum heights it would be too small
                constrainedSize.Y = Math.Max(constrainedSize.Y, line.Location.Y + line.Height);
            }
            constrainedSize.X = Math.Max(constrainedSize.X, DesiredWidth);
        }

        public uint DrawCallCount => DrawCallIndex;
        public uint LineCount => LineIndex;
        public uint BoxCount => BoxIndex;
        public uint SpanCount => SpanIndex;

        public unsafe void Finish (ArraySegment<BitmapDrawCall> buffer, out StringLayout result) {
            UpdateMarkedRange();

            // HACK
            while (SpanStack.Count > 0)
                EndCurrentSpan();

            FinishFragment();
            UnconstrainedSize.X = Math.Max(UnconstrainedSize.X, UnconstrainedLineSize.X);
            UnconstrainedSize.Y += UnconstrainedLineSize.Y;
            FinishLine(false);

            Listener?.Finishing(ref this);

            // First, scan all the lines to compute our actual constrained size
            ComputeConstrainedSize(out var constrainedSize);
            // Now scan through and sequentially arrange all our fragments.
            // Boxes are attached to fragments, so those will get arranged as we go too.
            // Boxes may overhang the bottom and change our height.
            ArrangeFragments(ref constrainedSize);

            var lastDrawCallIndex = DrawCallIndex > 0 ? DrawCallIndex - 1 : 0;
            if (MeasureOnly) {
                // FIXME: Can we skip arranging fragments too?
            } else {
                // HACK
                int bufferSize = (int)DrawCallIndex;
                if ((buffer.Array == null) || (buffer.Count < bufferSize))
                    buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[bufferSize]);
                else
                    buffer = new ArraySegment<BitmapDrawCall>(buffer.Array, buffer.Offset, (int)DrawCallIndex);

                fixed (BitmapDrawCall* dest = buffer.Array)
                    Buffer.MemoryCopy(Buffers.DrawCalls, dest, buffer.Count * sizeof(BitmapDrawCall), DrawCallIndex * sizeof(BitmapDrawCall));
            }

            result = new StringLayout(
                Position, constrainedSize, UnconstrainedSize, 
                Buffers.Line(0).Height,
                MeasureOnly ? default : Buffers.DrawCall(0).EstimateDrawBounds(),
                MeasureOnly ? default : Buffers.DrawCall(lastDrawCallIndex).EstimateDrawBounds(),
                MeasureOnly ? default : buffer, 
                AnyCharactersSuppressed, 
                /* FIXME */ 0, (int)LineIndex
            );

            Listener?.Finished(ref this, ref result);
        }

        public void Desuppress () {
            SuppressUntilEnd = false;
            BreakLimit = CharacterLimit = LineLimit = null;
        }

        private void AnalyzeWhitespace (char ch1, uint codepoint, out bool lineBreak, out bool deadGlyph, out FragmentCategory category) {
            bool isWhitespace = Unicode.IsWhiteSpace(codepoint) && !MaskCodepoint.HasValue,
                isWordWrapPoint = false, isNonPrintable = codepoint < 32;
            lineBreak = false;
            deadGlyph = false;

            if (SplitAtWrapCharactersOnly)
                isWordWrapPoint = WrapCharacters.BinarySearchNonRef(codepoint, StringLayoutEngine.UintComparer.Instance) >= 0;
            else
                isWordWrapPoint = isWhitespace || char.IsSeparator(ch1) ||
                    MaskCodepoint.HasValue || WrapCharacters.BinarySearchNonRef(codepoint, StringLayoutEngine.UintComparer.Instance) >= 0;

            if (codepoint > 255) {
                // HACK: Attempt to word-wrap at "other" punctuation in non-western character sets, which will include things like commas
                // This is less than ideal but .NET does not appear to expose the classification tables needed to do this correctly
                var uniCategory = CharUnicodeInfo.GetUnicodeCategory(ch1);
                if (uniCategory == UnicodeCategory.OtherPunctuation)
                    isWordWrapPoint = true;
                else if (uniCategory == UnicodeCategory.Control)
                    isNonPrintable = true;
                else if (uniCategory == UnicodeCategory.Format)
                    isNonPrintable = true;
            }
            // Wrapping and justify expansion should never occur for a non-breaking space
            if (codepoint == 0x00A0)
                isWordWrapPoint = false;

            if (ch1 == '\n')
                lineBreak = true;

            if (lineBreak) {
                if (BreakLimit.HasValue) {
                    BreakLimit--;
                    if (BreakLimit.Value <= 0)
                        SuppressLayoutForLimit();
                }
                if (!SuppressUntilEnd && IncludeTrailingWhitespace)
                    NewLinePending = true;
            } else if (LineLimit.HasValue && LineLimit.Value <= 0) {
                SuppressLayoutForLimit();
            }

            if (SplitAtWrapCharactersOnly) {
                if (isWordWrapPoint)
                    category = FragmentCategory.WrapPoint;
                else if (isNonPrintable)
                    category = FragmentCategory.NonPrintable;
                else if (isWhitespace)
                    category = FragmentCategory.Whitespace;
                else
                    category = FragmentCategory.Regular;
            } else {
                if (isNonPrintable)
                    category = FragmentCategory.NonPrintable;
                else if (isWhitespace)
                    category = FragmentCategory.Whitespace;
                else if (isWordWrapPoint)
                    category = FragmentCategory.WrapPoint;
                else
                    category = FragmentCategory.Regular;
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
            char ch1, uint codepoint, bool startOfLine, 
            out bool deadGlyph, out Glyph glyph, 
            out float glyphLineSpacing, out float glyphBaseline
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
            if (startOfLine) {
                if (glyph.LeftSideBearing < 0)
                    glyph.LeftSideBearing = 0;
            }
        }

        public void Dispose () {
            Buffers.Dispose();
        }
    }
}

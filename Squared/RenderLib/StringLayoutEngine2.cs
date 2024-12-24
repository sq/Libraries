#if DEBUG
#define TRACK_CODEPOINTS
#endif

using System;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Util.Text;
using Squared.Render.Text;
using Squared.Util;
using Squared.Game;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Globalization;

namespace Squared.Render.TextLayout2 {
    public enum FragmentCategory : byte {
        Unknown = 0b0,
        Regular = 0b1,
        WrapPoint = 0b10,
        Box = 0b100,
        RubyText = 0b1000,
        Whitespace = 0b10000,
        NonPrintable = 0b100000,
        CombiningCharacter = 0b1000000,
    }

    public struct Line {
        public uint Index;
        public uint FirstFragmentIndex, FragmentCount;
        public uint GapCount;
        public uint FirstDrawCall, DrawCallCount;
        public Vector2 Location;
        public float Width, TrailingWhitespace, 
            Height, Baseline, 
            Inset, Crush,
            TopDecorations, BottomDecorations;
        public HorizontalAlignment Alignment;

        public float ActualWidth {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Width + TrailingWhitespace + Inset + Crush;
        }
    }

    public struct Span {
        public uint Index;
        public uint FirstDrawCall, DrawCallCount;
        public uint FirstFragmentIndex, LastFragmentIndex;
        public float FirstRelativeX, LastRelativeX;
    }

    public struct Fragment {
        public uint LineIndex, BoxIndex, AnchorSpanIndex;
        public uint FirstDrawCall, DrawCallCount;
        public float Left, Width, Height, Baseline, Overhang;
        public FragmentCategory Category;
        public bool WasSuppressed, RTL;
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
        public Vector2 Margin;
        public ImageHorizontalAlignment HorizontalAlignment;
        public float BaselineAlignment;
        public uint FragmentIndex;
        // Optional
        public uint DrawCallIndex;
    }

    public struct RubyConfiguration {
        public IGlyphSource GlyphSource;
        public AbstractString Text;
        private float ScaleMinusOne;
        public float Scale {
            get => ScaleMinusOne + 1;
            set => ScaleMinusOne = value - 1;
        }
    }

    public interface IStringLayoutListener {
        void Initializing (ref StringLayoutEngine2 engine);
        void RecordTexture (ref StringLayoutEngine2 engine, AbstractTextureReference texture);
        void Finishing (ref StringLayoutEngine2 engine);
        void Finished (ref StringLayoutEngine2 engine, ref StringLayout result);
        void Error (ref StringLayoutEngine2 engine, string message);
    }

    public interface IStringLayoutListener2 {
        /// <summary>
        /// Invoked before a character is appended.
        /// </summary>
        /// <returns>Return true to append the character, or false to skip processing it.</returns>
        bool BeforeAppendCharacter (ref StringLayoutEngine2 engine, uint codepoint, uint wordIndex, uint characterIndex, uint drawCallIndex);
        void FragmentArranged (ref StringLayoutEngine2 engine, ref readonly Fragment fragment);
    }

    public struct StringLayoutEngine2 : IDisposable {
        private struct CombiningCharacterInfo {
            public uint Codepoint, DrawCallIndex;
            public Glyph Glyph;
        }

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
        float Y, UnconstrainedY, UnconstrainedMaxWidth, UnconstrainedMaxY,
            UnconstrainedLineTopDecorations, UnconstrainedLineBottomDecorations,
            CurrentDefaultLineSpacing;
        Vector2 UnconstrainedLineSize;
        uint ColIndex, LineIndex, CharIndex, WordIndex,
            DrawCallIndex, SpanIndex, FragmentIndex,
            BoxIndex;
        bool SuppressUntilEnd;

        StagingBuffers Buffers;

        // FIXME: Make this do something
        bool NewLinePending;
        DenseList<uint> SpanStack;
        DenseList<bool> RTLStack;
        DenseList<RubyConfiguration> RubyStack;

        uint CombiningCharacterAnchorCodepoint;
        DenseList<CombiningCharacterInfo> CombiningCharacterStack;

        AbstractTextureReference MostRecentTexture;

#if TRACK_CODEPOINTS
        DenseList<uint> CurrentFragmentCodepoints;
        string CurrentFragmentText => string.Join("", CurrentFragmentCodepoints.Select(c => (char)c));
#endif

        // Mixed Configuration/State
        public bool RightToLeft;

        // Configuration
        public object UserData;
        public IStringLayoutListener Listener;
        public IStringLayoutListener2 Listener2;

        public DenseList<uint> WrapCharacters;

        public bool MeasureOnly;
        public bool CharacterWrap, WordWrap, SplitAtWrapCharactersOnly;
        public bool HideOverflow, IncludeTrailingWhitespace;
        public bool OverrideColor;

        // FIXME: Not implemented
        public int CharacterSkipCount;
        // FIXME: Not implemented
        public int CharacterLimit, 
            // FIXME: These seem buggy
            LineLimit, 
            BreakLimit;
        public int TabSize;

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

        public HorizontalAlignment DefaultAlignment;
        private HorizontalAlignment CurrentAlignment;
        public uint MaskCodepoint;
        public uint? TerminatorCodepoint;

        public float InitialIndentation, BreakIndentation, WrapIndentation;
        public float AdditionalLineSpacing, ExtraBreakSpacing;
        public float MaximumWidth, DesiredWidth;
        public float MaximumHeight;
        public float MaxExpansionPerSpace;
        public float MinRubyScale;

        private Pair<int> _MarkedRange;
        public Pair<int>? MarkedRange {
            get => _MarkedRange.First == int.MaxValue
                ? null
                : _MarkedRange;
            set {
                if (value == null)
                    _MarkedRange = new Pair<int>(int.MaxValue, int.MaxValue);
                else
                    _MarkedRange = value.Value;
            }
        }
        public Vector2? HitTestLocation;

        public Vector4 CharacterUserData, ImageUserData;

        // Output
        public uint MarkedRangeSpanIndex;
        public LayoutHitTest HitTestResult;
        public bool AnyCharactersSuppressed;
        public StringBuilder TextOutput;

        bool IsInitialized;
        public bool IsTruncated =>
            // FIXME: < 0 instead of <= 0?
            (LineLimit <= 0) ||
            (BreakLimit <= 0) ||
            (CharacterLimit <= 0);

        public void Initialize () {
            // FIXME
            if (IsInitialized)
                throw new InvalidOperationException("A StringLayoutEngine2 instance cannot be used more than once");

            IsInitialized = false;

            WrapCharacters.SortNonRef(UintComparer.Instance);

            Listener?.Initializing(ref this);

            IsInitialized = true;
            Buffers.Span(0) = default;
            CurrentAlignment = DefaultAlignment;
            CurrentLine = new Line {
                Location = new Vector2(InitialIndentation, 0f),
                Alignment = CurrentAlignment,
            };
            UnconstrainedLineSize = new Vector2(InitialIndentation, 0f);
            Y = 0f;
            CurrentFragment = new Fragment {
                RTL = RightToLeft,
                AnchorSpanIndex = uint.MaxValue,
            };
            MarkedRangeSpanIndex = uint.MaxValue;
            HitTestResult.Position = HitTestLocation ?? default;
            MostRecentTexture = AbstractTextureReference.Invalid;
            CombiningCharacterAnchorCodepoint = uint.MaxValue;
        }

        public HorizontalAlignment Alignment {
            get => CurrentAlignment;
            set {
                if (CurrentAlignment == value)
                    return;

                CurrentLine.Alignment = value;
                CurrentAlignment = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref BitmapDrawCall AppendDrawCall (ref Fragment fragment) {
            if (fragment.FirstDrawCall == uint.MaxValue)
                fragment.FirstDrawCall = DrawCallIndex;
            fragment.DrawCallCount++;
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
        public readonly ref BitmapDrawCall GetDrawCall (uint index) {
            if (index > DrawCallIndex)
                index = DrawCallIndex;
            return ref Buffers.DrawCall(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ref Span GetSpan (uint index) {
            if (index > SpanIndex)
                index = SpanIndex;
            return ref Buffers.Span(index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly ref Line GetLine (uint index) {
            if (index > LineIndex)
                index = LineIndex;
            return ref Buffers.Line(index);
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

                if (
                    // If the line bounds were clamped into nothingness, don't report them.
                    (lineBounds.Size.X > 0f) || 
                    (
                        // Unless the span itself was 0 pixels wide originally,
                        //  in which case the caller wants that!
                        (span.FirstRelativeX == span.LastRelativeX) &&
                        (span.FirstFragmentIndex == span.LastFragmentIndex)
                    )
                )
                    output.Add(ref lineBounds);
            }

            return true;
        }

        public bool TryGetLineBounds (uint index, out Bounds bounds, bool includeDecorations = true) {
            if (index > LineIndex) {
                bounds = default;
                return false;
            }

            ref var line = ref Buffers.Line(index);
            bounds = Bounds.FromPositionAndSize(
                line.Location.X + Position.X, line.Location.Y + Position.Y, 
                line.Width + (IncludeTrailingWhitespace ? line.TrailingWhitespace : 0f), line.Height
            );
            if (includeDecorations)
                bounds.TopLeft.Y -= line.TopDecorations;
            else
                bounds.BottomRight.Y -= line.BottomDecorations;
            return true;
        }

        public bool TryGetBox (uint index, out Bounds bounds, out Vector2 margin) {
            if (index >= BoxIndex) {
                bounds = default;
                margin = default;
                return false;
            }

            ref var box = ref Buffers.Box(index);
            bounds = box.Bounds;
            margin = box.Margin;
            return true;
        }
        
        public ref Span BeginSpan (bool push) {
            // HACK: Split whitespace fragments when beginning a span,
            //  so that relative positioning is accurate in justified mode.
            if (!CurrentFragment.ContainsContent && CurrentFragment.Width > 0)
                FinishFragment(true);

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
            var offset = SpanStack.IndexOf(index, UintComparer.Instance);
            if (offset < 0)
                throw new Exception("Span not active");

            SpanStack.RemoveAt(offset);
            return ref EndSpan(index);
        }

        /// <summary>
        /// Attaches ruby text to the current span.
        /// </summary>
        public void PushRubyText (IGlyphSource glyphSource, AbstractString text, float scale) {
            if (glyphSource == null)
                throw new ArgumentNullException(nameof(glyphSource));

            RubyStack.Add(new RubyConfiguration {
                GlyphSource = glyphSource,
                Text = text,
                Scale = scale,
            });
        }

        private ref Span EndSpan (uint index) {
            ref var result = ref Buffers.Span(index);
            result.LastRelativeX = FragmentOffset.X;
            result.LastFragmentIndex = FragmentIndex;
            result.DrawCallCount = DrawCallIndex - result.FirstDrawCall;

            int rubies = RubyStack.Count;
            if (rubies > 0) {
                var rs = RubyStack;
                RubyStack.Clear();
                for (int i = 0; i < rubies; i++)
                    ProcessRuby(ref result, rs[i]);
            }

            return ref result;
        }

        private void ProcessRuby (ref Span anchor, RubyConfiguration ruby) {
            FinishFragment(false);
            ref var rubySpan = ref BeginSpan(true);
            var oldScale = Scale;
            try {
                Scale *= ruby.Scale;
                ref var line = ref CurrentLine;
                AppendText(ruby.GlyphSource, ruby.Text, FragmentCategory.RubyText);
            } finally {
                CurrentFragment.AnchorSpanIndex = anchor.Index;
                EndSpan(rubySpan.Index);
                Scale = oldScale;
            }
        }

        private void UpdateMarkedRange () {
            if (_MarkedRange.First == int.MaxValue)
                return;

            if (_MarkedRange.First == CharIndex)
                MarkedRangeSpanIndex = BeginSpan(true).Index;

            if (_MarkedRange.Second == CharIndex) {
                if (MarkedRangeSpanIndex != uint.MaxValue) {
                    EndSpan(MarkedRangeSpanIndex);
                    SpanStack.Remove(MarkedRangeSpanIndex);
                }
            }
        }

        public void EnsureCapacity (uint drawCalls, uint fragments) {
            Buffers.EnsureCapacity(drawCalls, fragments);
        }

        public void AppendText<TGlyphSource> (
            TGlyphSource glyphSource, AbstractString text, FragmentCategory? singleFragment = null
        ) where TGlyphSource : IGlyphSource {
            if (!IsInitialized)
                throw new InvalidOperationException("Call Initialize first");

            if (TextOutput != null)
                text.CopyTo(TextOutput);

            var t = typeof(TGlyphSource);
            if (!t.IsValueType) {
                if (glyphSource == null) {
                    // HACK: Allow text output only measurement without a glyph source
                    if (TextOutput != null)
                        return;
                    else
                        throw new ArgumentNullException(nameof(glyphSource));
                }
            }

            if (text.IsNull)
                throw new ArgumentNullException(nameof(text));

            KerningData thisKerning = default, nextKerning = default;
            bool hasKerningNow = false, hasKerningNext = false;
            Buffers.EnsureCapacity(
                MeasureOnly ? 0 : (uint)(DrawCallIndex + text.Length),
                (uint)(FragmentIndex + (text.Length / 7))
            );

            Vector2 effectiveScale = Scale * (1.0f / glyphSource.DPIScaleFactor),
                effectiveSpacing = Spacing;
            // HACK: GlyphSource.LineSpacing does not apply the DPI scaling like glyph metrics do.
            CurrentDefaultLineSpacing = glyphSource.LineSpacing * Scale.Y;

            for (int i = 0, l = text.Length; i < l; i++) {
                if (LineLimit <= 0)
                    SuppressLayoutForLimit();
                else if (Y > MaximumHeight)
                    SuppressLayoutForLimit();

                UpdateMarkedRange();

                var codepoint = DecodeCodepoint(text, ref i, l, out char ch1, out int currentCodepointSize);
                if (currentCodepointSize == 2)
                    CharIndex++;

                if (codepoint == TerminatorCodepoint)
                    SuppressLayoutForLimit();
                if (MaskCodepoint > 0)
                    codepoint = MaskCodepoint;

                var isTab = ch1 == '\t';
                var category = AnalyzeCharacter(
                    ch1, codepoint, out bool lineBreak
                );
                var isCombiningCharacter = category == FragmentCategory.CombiningCharacter;

                if (category == FragmentCategory.NonPrintable)
                    ProcessControlCharacter(codepoint);

                // Do this after AnalyzeCharacter so that we don't break custom wrap characters
                if (isTab)
                    codepoint = ch1 = ' ';

                var deadGlyph = !glyphSource.GetGlyph(codepoint, out var glyph);
                // HACK: In some cases we will end up with size metrics and stuff for \r or \n.
                // We don't want to actually treat them like characters, it screws up layout.
                // So just erase all their data.
                if (lineBreak) {
                    // HACK: We don't want combiners to cross lines, it makes no sense.
                    // FIXME: Go back and delete them?
                    CombiningCharacterStack.UnsafeFastClear();
                    glyph = default;
                }

                glyph.LineSpacing = (glyph.LineSpacing * effectiveScale.Y) + AdditionalLineSpacing;
                glyph.Baseline *= effectiveScale.Y;

                if (singleFragment.HasValue) {
                    category = singleFragment.Value;
                } else if (isCombiningCharacter) {
                    category = FragmentCategory.Regular;
                } else if (!deadGlyph) {
                    if (CombiningCharacterStack.Count > 0) {
                        glyphSource.GetGlyph(CombiningCharacterAnchorCodepoint, out var anchorGlyph);
                        ResolveCombiningCharacters(ref anchorGlyph, ref effectiveScale);
                    }
                    CombiningCharacterAnchorCodepoint = codepoint;
                }

                if (category == FragmentCategory.NonPrintable) {
                    ;
                } else {
                    ref var cf = ref CurrentFragment;
                    if (cf.Category == FragmentCategory.Unknown)
                        cf.Category = category;

                    if (!singleFragment.HasValue && (category != cf.Category))
                        cf = ref FinishFragment(true);

                    if (cf.Category == FragmentCategory.Unknown)
                        cf.Category = category;
                }

                if (Listener2?.BeforeAppendCharacter(ref this, codepoint, WordIndex, CharIndex, DrawCallIndex) == false) {
                    CharIndex++;
                    continue;
                }

                if (glyph.LigatureProvider != null) {
                    var charsConsumed = glyph.LigatureProvider.TryGetLigature(ref glyph, text, i);
                    if (charsConsumed > 0) {
                        // Advance our decoding offset based on the number of chars consumed by the ligature
                        i += charsConsumed;
                        // FIXME: Carry-over kerning from the previous glyph isn't valid due to the ligature
                        hasKerningNow = hasKerningNext = false;
                    }
                }

                // FIXME: Kerning across multiple AppendText calls
                if (!isCombiningCharacter && !hasKerningNow && (glyph.KerningProvider != null) && (i < l - 2)) {
                    var temp = i + 1;
                    var codepoint2 = DecodeCodepoint(text, ref temp, l, out _, out _);
                    // FIXME: Also do adjustment for next glyph!
                    var glyphId2 = glyphSource.GetGlyphIndex(codepoint2);
                    hasKerningNow = glyph.KerningProvider.TryGetKerning(glyph.GlyphIndex, glyphId2, ref thisKerning, ref nextKerning);
                    hasKerningNext = !nextKerning.Equals(default);
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

                // In obscure scenarios, we may need to first word-wrap/line-break, and then
                //  perform a forced character wrap of the current fragment afterward
                int breaksAllowed = 2;
                bool lineBreakPending = lineBreak;

                if (isTab) {
                    glyph.LeftSideBearing *= TabSize;
                    glyph.XOffset *= TabSize;
                    glyph.Width *= TabSize;
                    glyph.RightSideBearing *= TabSize;
                } else if (lineBreak) {
                    // HACK: Suppress spacing for line breaks, some fonts have them and it messes up
                    //  the position of text after a line break
                    glyph = default;
                }

                // MonoGame#1355 rears its ugly head: If a character with negative left-side bearing is at the start of a line,
                //  we need to compensate for the bearing to prevent the character from extending outside of the layout bounds
                if (ColIndex <= 0) {
                    if (glyph.LeftSideBearing < 0)
                        glyph.LeftSideBearing = 0;
                }

recalc:
                ref var line = ref CurrentLine;

                float leftSideDelta = 0;
                if (effectiveSpacing.X < 0) {
                    leftSideDelta = Math.Abs(glyph.LeftSideBearing * effectiveSpacing.X);
                    glyph.RightSideBearing -= leftSideDelta;
                }

                float glyphSpacing = glyph.CharacterSpacing * effectiveScale.X,
                    inlineW = glyph.WidthIncludingBearing * effectiveScale.X * effectiveSpacing.X,
                    worstW = glyph.WorstCaseWidth * effectiveScale.X * effectiveSpacing.X,
                    h = glyph.LineSpacing,
                    xBasis = line.Location.X + line.ActualWidth,
                    x1 = xBasis + FragmentOffset.X,
                    x2 = x1 + glyphSpacing + worstW,
                    y2 = Y + h;
                bool overflowX = (x2 > MaximumWidth),
                    overflowY = (y2 > MaximumHeight);

                if (isCombiningCharacter)
                    ProcessCombiningCharacter(codepoint, ref glyph);

                if (breaksAllowed > 0) {
                    breaksAllowed--;

                    if (lineBreakPending) {
                        lineBreakPending = false;
                        PerformLineBreak(CurrentDefaultLineSpacing, singleFragment.HasValue);
                        goto recalc;
                    } else if (
                        overflowX &&
                        // HACK: Don't perform automatic word/character wrap for combining characters.
                        // We should normally wrap them along with the glyph they're attached to instead.
                        !isCombiningCharacter &&
                        PerformForcedWrap(worstW, MaximumWidth - line.Inset - line.Crush, singleFragment.HasValue)
                    ) {
                        goto recalc;
                    }
                }

                if (HideOverflow) {
                    if (overflowY)
                        SuppressLayoutForLimit();
                }

                ref var fragment = ref CurrentFragment;
                if (glyph.Baseline > fragment.Baseline)
                    IncreaseBaseline(ref fragment, glyph.Baseline);

                bool suppressThisCharacter = SuppressUntilEnd || (overflowX && HideOverflow);

#if TRACK_CODEPOINTS
                CurrentFragmentCodepoints.Add(codepoint);
#endif
                if (RightToLeft)
                    FragmentOffset.X += inlineW;

                if (
                    !deadGlyph && 
                    !suppressThisCharacter && 
                    (category < FragmentCategory.Whitespace)
                ) {
                    if (MeasureOnly) {
                        // HACK: There is code that uses DrawCallCount != 0 to make decisions,
                        //  so we need to maintain a fake draw call counter in order to keep that working
                        if (fragment.FirstDrawCall == uint.MaxValue)
                            fragment.FirstDrawCall = DrawCallIndex;
                        fragment.DrawCallCount++;

                        if (fragment.WasSuppressed)
                            fragment.WasSuppressed = false;
                    } else {
                        ref var drawCall = ref AppendDrawCall(ref fragment);

                        float x = FragmentOffset.X + 
                            ((
                                RightToLeft
                                    // FIXME: Is this right? It produces output that looks mostly correct.
                                    ? (glyph.RightSideBearing - glyph.XOffset - glyph.LeftSideBearing)
                                    : (glyph.LeftSideBearing + glyph.XOffset)
                            ) * effectiveScale.X),
                            // Used to compute bounding box offsets
                            wx = xBasis + x,
                            alignmentToBaseline = fragment.Baseline - glyph.Baseline,
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
                            SortKey = SortKey,
                            UserData = CharacterUserData,
                        };

                        if (glyph.Texture != MostRecentTexture) {
                            Listener?.RecordTexture(ref this, glyph.Texture);
                            MostRecentTexture = glyph.Texture;
                        }

                        fragment.WasSuppressed = false;
                    }
                }

                if (suppressThisCharacter && !deadGlyph && (category < FragmentCategory.Whitespace)) {
                    fragment.WasSuppressed = true;
                    AnyCharactersSuppressed = true;
                }

                if (!RightToLeft)
                    FragmentOffset.X += inlineW;

                if (!SuppressUntilEnd) {
                    fragment.Width += inlineW;
                    fragment.Overhang = worstW - inlineW;
                    fragment.Height = Math.Max(fragment.Height, h);
                }

                if (HitTestLocation.HasValue && (HitTestLocation.Value.X >= x1) && (HitTestLocation.Value.X <= x2)) {
                    if (!HitTestResult.FirstCharacterIndex.HasValue)
                        HitTestResult.FirstCharacterIndex = (int)CharIndex;
                    HitTestResult.LastCharacterIndex = (int)CharIndex;
                    HitTestResult.LeaningRight = HitTestLocation.Value.X >= ((x1 + x2) * 0.5f);
                }

                if (!suppressThisCharacter && !lineBreak)
                    ColIndex++;
                CharIndex++;
            }
        }

        private void ProcessCombiningCharacter (uint codepoint, ref Glyph glyph) {
            // Heuristic
            CombiningCharacterStack.Add(new CombiningCharacterInfo {
                Codepoint = codepoint,
                DrawCallIndex = DrawCallIndex,
                Glyph = glyph
            });
        }

        private void ResolveCombiningCharacters (ref Glyph anchorGlyph, ref Vector2 scale) {
            if (CombiningCharacterAnchorCodepoint != uint.MaxValue) {
                ref var line = ref CurrentLine;
                float topSpaceEaten = 0, bottomSpaceEaten = 0,
                    aLB = anchorGlyph.LeftSideBearing * scale.X,
                    aW = anchorGlyph.Width * scale.X,
                    aRB = anchorGlyph.RightSideBearing * scale.X;
                var yBbox = anchorGlyph.YBounds;

                for (int i = 0, c = CombiningCharacterStack.Count; i < c; i++) {
                    ref var cc = ref CombiningCharacterStack.Item(i);
                    ref var dc = ref Buffers.DrawCall(cc.DrawCallIndex);

                    // We scan through the stack of combining characters, and for each one,
                    //  we test its bounding interval (on the y axis) against the bounding
                    //  interval for the anchor character. If they intersect, we shift the
                    //  combining character up or down (depending on its position) to make
                    //  room, and then combine its bounding interval with the anchor's, so
                    //  that we can continue to shift each additional character into place.
                    var onTop = cc.Glyph.VerticalBearing > 0f;
                    var ccBbox = cc.Glyph.YBounds;
                    if (onTop) {
                        dc.Position.Y -= topSpaceEaten;
                        ccBbox -= topSpaceEaten;
                        topSpaceEaten += cc.Glyph.Height;
                    } else {
                        dc.Position.Y += bottomSpaceEaten;
                        ccBbox += bottomSpaceEaten;
                        bottomSpaceEaten += cc.Glyph.Height;
                    }

                    float displacement = 0;
                    if (yBbox.GetIntersection(ccBbox, out var yIntersection))
                        displacement = yIntersection.Max - yIntersection.Min;

                    if (onTop) {
                        dc.Position.Y -= displacement;
                        ccBbox -= displacement;
                    } else {
                        dc.Position.Y += displacement;
                        ccBbox += displacement;
                    }

                    yBbox.GetUnion(ccBbox, out yBbox);

                    if (RightToLeft) {
                        // Align our left edge with the left edge of the previous character. I think?
                        dc.Position.X -= cc.Glyph.RightSideBearing - cc.Glyph.LeftSideBearing;
                        // Center ourselves relative to the previous character.
                        dc.Position.X -= (aW - cc.Glyph.Width) * 0.5f;
                    } else {
                        // Align our left edge with the right edge of the previous character.
                        // Align our left edge with the left edge of the previous character's body.
                        dc.Position.X -= cc.Glyph.LeftSideBearing + aW + aRB;
                        // Center ourselves relative to the previous character.
                        dc.Position.X += (aW - cc.Glyph.Width) * 0.5f;
                    }
                }

                // FIXME: Store this per-fragment so it doesn't cause glitches when wrapping.
                // Not sure if this feature is important enough to justify making fragments 8 bytes bigger.
                line.TopDecorations = Math.Max(line.TopDecorations, topSpaceEaten);
                line.BottomDecorations = Math.Max(line.BottomDecorations, bottomSpaceEaten);
                UnconstrainedLineTopDecorations = Math.Max(UnconstrainedLineTopDecorations, Math.Abs(topSpaceEaten));
                UnconstrainedLineBottomDecorations = Math.Max(UnconstrainedLineBottomDecorations, bottomSpaceEaten);
            }

            CombiningCharacterStack.UnsafeFastClear();
            CombiningCharacterAnchorCodepoint = uint.MaxValue;
        }

        private void ChangeRTL (bool rtl, bool push, bool isolate) {
            if (rtl != RightToLeft)
                FinishFragment(false);

            // FIXME: isolate
            if (push)
                RTLStack.Add(RightToLeft);
            RightToLeft = rtl;

            CurrentFragment.RTL = rtl;
        }

        private void PopRTL (bool isolate) {
            // FIXME: How does an isolate mismatch work?
            if (!RTLStack.TryRemoveLast(out bool rtl)) {
                Listener?.Error(ref this, "Right-to-left state pop encountered without paired push");
                return;
            }

            if (rtl != RightToLeft)
                FinishFragment(false);

            RightToLeft = rtl;
            CurrentFragment.RTL = rtl;
        }

        private void ProcessControlCharacter (uint codepoint) {
            switch (codepoint) {
                // Arabic letter mark
                case 0x061C:
                    // FIXME
                    break;
                // LTR mark
                case 0x200E:
                    ChangeRTL(false, push: false, isolate: false);
                    break;
                // RTL mark
                case 0x200F:
                    ChangeRTL(true, push: false, isolate: false);
                    break;
                // LTR embedding
                case 0x202A:
                    ChangeRTL(false, push: true, isolate: false);
                    break;
                // RTL embedding
                case 0x202B:
                    ChangeRTL(true, push: true, isolate: false);
                    break;
                // Pop directional formatting
                case 0x202C:
                    PopRTL(isolate: false);
                    break;
                // LTR override (FIXME: How is this distinct from LTR embedding?)
                case 0x202D:
                    ChangeRTL(false, push: true, isolate: false);
                    break;
                // RTL override (FIXME: How is this distinct from RTL embedding?)
                case 0x202E:
                    ChangeRTL(true, push: true, isolate: false);
                    break;
                // LTR isolate
                case 0x2066:
                    ChangeRTL(false, push: true, isolate: true);
                    break;
                // RTL isolate
                case 0x2067:
                    ChangeRTL(true, push: true, isolate: true);
                    break;
                // First strong isolate
                case 0x2068:
                    // FIXME
                    break;
                // Pop directional isolate
                case 0x2069:
                    PopRTL(isolate: true);
                    break;
                default:
                    break;
            }
        }

        public void IncreaseBaseline (float newBaseline) {
            ref var fragment = ref CurrentFragment;
            if (newBaseline <= fragment.Baseline)
                return;

            IncreaseBaseline(ref fragment, newBaseline);
        }

        private void IncreaseBaseline (ref Fragment fragment, float newBaseline) {
            float adjustment = newBaseline - fragment.Baseline;
            fragment.Baseline = newBaseline;
            if (fragment.DrawCallCount == 0)
                return;

            for (uint d = fragment.FirstDrawCall, d2 = d + fragment.DrawCallCount - 1; d <= d2; d++) {
                ref var dc = ref Buffers.DrawCall(d);
                dc.Position.Y += adjustment;
            }
        }

        private bool PerformForcedWrap (float characterWidth, float currentMaximumWidth, bool singleFragment) {
            ref var fragment = ref CurrentFragment;
            if (fragment.Category == FragmentCategory.RubyText)
                return false;

            // Determine whether wrapping the entire fragment will work.
            if (WordWrap) {
                // If we're about to wrap a whitespace fragment, just say we succeeded but don't do anything.
                if (!fragment.ContainsContent)
                    return true;

                if (fragment.Width + characterWidth <= currentMaximumWidth) {
                } else if (CharacterWrap && !singleFragment) {
                    // We can't cleanly word wrap so we should try to character wrap instead.
                    FinishFragment(false);
                    FinishLine(false);
                    return (characterWidth <= currentMaximumWidth);
                }
                FinishLine(false);
                return true;
            }
            
            if (CharacterWrap) {
                // Fragment wrapping didn't work, so split the fragment to a new line.
                FinishFragment(false);
                FinishLine(false);
                return (characterWidth <= currentMaximumWidth);
            }

            return false;
        }

        private void PerformLineBreak (float defaultLineSpacing, bool singleFragmentMode) {
            // FIXME: Single fragment mode
            FinishFragment(false);
            if (CurrentLine.Height == 0f)
                CurrentLine.Height = defaultLineSpacing;
            if (UnconstrainedLineSize.Y == 0f)
                UnconstrainedLineSize.Y = defaultLineSpacing;
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

        private ref Fragment FinishFragment (bool discardOverhang) {
            ref var fragment = ref CurrentFragment;
            fragment.LineIndex = LineIndex;

            ref var line = ref CurrentLine;

            line.Baseline = Math.Max(line.Baseline, fragment.Baseline);

            float baselineAdjustment = line.Baseline - fragment.Baseline;

            switch (fragment.Category) {
                case FragmentCategory.RubyText:
                    WordIndex++;
                    UnconstrainedLineTopDecorations = Math.Max(UnconstrainedLineTopDecorations, fragment.Height);
                    line.TopDecorations = Math.Max(line.TopDecorations, fragment.Height);
                    break;
                case FragmentCategory.Regular:
                    WordIndex++;
                    UnconstrainedLineSize.X += fragment.Width; 
                    line.Width += fragment.Width + line.TrailingWhitespace;
                    line.TrailingWhitespace = 0f;
                    if (!discardOverhang) {
                        UnconstrainedLineSize.X += fragment.Overhang;
                        line.Width += fragment.Overhang;
                    }
                    break;
                case FragmentCategory.Box:
                    // HACK: Most of this is implemented in AppendImage
                    // HACK: We invert the meaning of the overhang flag here, so that
                    //  we can collapse image margins at the end of lines but not in the
                    //  middle of lines.
                    if (discardOverhang)
                        line.Width += fragment.Overhang;
                    break;
                case FragmentCategory.WrapPoint:
                    if (SplitAtWrapCharactersOnly)
                        line.GapCount++;
                    UnconstrainedLineSize.X += fragment.Width;
                    line.Width += fragment.Width + line.TrailingWhitespace;
                    line.TrailingWhitespace = 0f;
                    if (!discardOverhang) {
                        UnconstrainedLineSize.X += fragment.Overhang;
                        line.Width += fragment.Overhang;
                    }
                    break;
                case FragmentCategory.Whitespace:
                    if (!SplitAtWrapCharactersOnly)
                        line.GapCount++;
                    UnconstrainedLineSize.X += fragment.Width;
                    line.TrailingWhitespace += fragment.Width;
                    if (!discardOverhang) {
                        UnconstrainedLineSize.X += fragment.Overhang;
                        line.TrailingWhitespace += fragment.Overhang;
                    }
                    break;
                case FragmentCategory.NonPrintable:
                    UnconstrainedLineSize.X += fragment.Width;
                    line.Width += fragment.Width;
                    if (!discardOverhang) {
                        UnconstrainedLineSize.X += fragment.Overhang;
                        line.Width += fragment.Overhang;
                    }
                    break;
            }

            if (!fragment.WasFullySuppressed)
                IncreaseLineHeight(ref line, ref fragment);                

            FragmentOffset = default;

            if (fragment.DrawCallCount > 0) {
                if (line.DrawCallCount == 0)
                    line.FirstDrawCall = fragment.FirstDrawCall;
                line.DrawCallCount += fragment.DrawCallCount;
            }

            line.FragmentCount++;
            FragmentIndex++;

            ref var result = ref CurrentFragment;
            result = new Fragment {
                FirstDrawCall = DrawCallIndex,
                LineIndex = LineIndex,
                WasSuppressed = SuppressUntilEnd,
                RTL = RightToLeft,
                AnchorSpanIndex = uint.MaxValue,
            };

#if TRACK_CODEPOINTS
            CurrentFragmentCodepoints.UnsafeFastClear();
#endif

            return ref result;
        }

        private void CalculateLineInsetAndCrush (ref Line line) {
            if (BoxIndex == 0)
                return;

            var interval = new Interval(line.Location.Y, line.Location.Y + Math.Max(line.Height, 1f));
            float inset = 0f, crush = 0f;
            for (uint b = 0; b < BoxIndex; b++) {
                ref var box = ref Buffers.Box(b);
                if (box.HorizontalAlignment == ImageHorizontalAlignment.Inline)
                    continue;

                var boxInterval = box.Bounds.Y;
                boxInterval.Min -= box.Margin.Y;
                boxInterval.Max += box.Margin.Y;
                if (!boxInterval.Intersects(interval))
                    continue;

                if (box.HorizontalAlignment == ImageHorizontalAlignment.Left)
                    inset = Math.Max(inset, box.Bounds.Size.X + box.Margin.X);
                else if (box.HorizontalAlignment == ImageHorizontalAlignment.Right)
                    crush = Math.Max(crush, box.Bounds.Size.X + box.Margin.X);
            }

            line.Inset = inset;
            line.Crush = crush;
        }

        public void IncreaseLineHeight (float newHeight) {
            ref var line = ref CurrentLine;

            UnconstrainedLineSize.Y = Math.Max(UnconstrainedLineSize.Y, newHeight);
            CalculateLineInsetAndCrush(ref line);
        }

        private void IncreaseLineHeight (ref Line line, ref Fragment fragment) {
            UnconstrainedLineSize.Y = Math.Max(UnconstrainedLineSize.Y, fragment.Height);

            if (line.Height >= fragment.Height)
                return;

            line.Height = fragment.Height;
            CalculateLineInsetAndCrush(ref line);
        }

        private void FinishLine (bool forLineBreak) {
            ref var line = ref CurrentLine;

            CurrentLine.Height += CurrentLine.BottomDecorations;

            if (!SuppressUntilEnd) {
                CurrentLine.Location.Y += CurrentLine.TopDecorations;
                Y += CurrentLine.TopDecorations;
                Y += CurrentLine.Height + (forLineBreak ? ExtraBreakSpacing : 0f);
            }

            UnconstrainedMaxWidth = Math.Max(UnconstrainedLineSize.X, UnconstrainedMaxWidth);
            if (forLineBreak) {
                UnconstrainedLineSize.Y += UnconstrainedLineBottomDecorations;
                UnconstrainedMaxY = Math.Max(UnconstrainedMaxY, UnconstrainedY + UnconstrainedLineSize.Y);
                UnconstrainedY += UnconstrainedLineTopDecorations + UnconstrainedLineSize.Y + ExtraBreakSpacing;
                UnconstrainedLineSize = new Vector2(BreakIndentation, 0f);
                UnconstrainedLineBottomDecorations = 0f;
                UnconstrainedLineTopDecorations = 0f;
            }

            float indentation = forLineBreak ? BreakIndentation : WrapIndentation;
            LineIndex++;
            ref var newLine = ref CurrentLine;
            newLine = new Line {
                Index = LineIndex,
                Location = new Vector2(indentation, Y),
                FirstFragmentIndex = FragmentIndex,
                Alignment = CurrentAlignment,
            };

            ColIndex = 0;

            LineLimit--;
            if (LineLimit <= 0)
                SuppressLayoutForLimit();

            CalculateLineInsetAndCrush(ref newLine);
        }

        public void CreateEmptyBox (
            float width, float height, 
            Vector2 margin, ImageHorizontalAlignment alignment,
            bool doNotAdjustLineSpacing
        ) {
            ref var fragment = ref FinishFragment(true);

            var boxIndex = BoxIndex++;

            ref var line = ref CurrentLine;
            fragment.BoxIndex = boxIndex;
            fragment.Category = FragmentCategory.Box;
            fragment.Width = width;
            fragment.Height = doNotAdjustLineSpacing ? line.Height : height;

            switch (alignment) {
                case ImageHorizontalAlignment.Inline:
                    fragment.Width += margin.X;
                    fragment.Overhang = margin.X;
                    line.Width += fragment.Width;
                    UnconstrainedLineSize.X += fragment.Width;
                    break;
                case ImageHorizontalAlignment.Left:
                    line.Inset += width + margin.X;
                    break;
                case ImageHorizontalAlignment.Right:
                    line.Crush += width + margin.X;
                    break;
            }

            ref var box = ref Buffers.Box(boxIndex);
            box = new Box {
                FragmentIndex = FragmentIndex,
                DrawCallIndex = uint.MaxValue,
                // FIXME
                HorizontalAlignment = alignment,
                // FIXME
                Margin = margin,
                Bounds = Bounds.FromPositionAndSize(0f, line.Location.Y, width, height),
            };

            FinishFragment(true);
        }

        public void AppendImage (ref RichImage image) {
            Listener?.RecordTexture(ref this, image.Texture);

            ref var fragment = ref FinishFragment(true);

            var drawCallIndex = DrawCallIndex;
            var availWidth = Math.Max(0, Math.Max(MaximumWidth, DesiredWidth) - (image.Margin.X * 2));
            float maximumWidth = image.MaxWidthPercent.HasValue
                ? availWidth * image.MaxWidthPercent.Value / 100f
                : float.MaxValue,
                maximumScale = Math.Min(1f, maximumWidth / image.Texture.Instance.Width),
                effectiveScale = Math.Min(image.Scale, maximumScale);

            var drawCall = new BitmapDrawCall(
                new TextureSet(image.Texture), Vector2.Zero
            ) {
                Scale = effectiveScale * Scale,
                TextureRegion = image.Bounds ?? Bounds.Unit,
                UserData = ImageUserData,
                MultiplyColor = OverrideColor ? MultiplyColor : Color.White,
                AddColor = AddColor,
                SortKey = SortKey,
            };
            var bounds = drawCall.EstimateDrawBounds();
            if ((image.HorizontalAlignment == ImageHorizontalAlignment.Inline) && (WordWrap || CharacterWrap)) {
                ref var line1 = ref CurrentLine;
                float effectiveMaxWidth = MaximumWidth - line1.Inset - line1.Crush;
                if (
                    (bounds.Size.X < effectiveMaxWidth) &&
                    (line1.ActualWidth + bounds.Size.X + image.Margin.X) >= effectiveMaxWidth
                )
                    FinishLine(false);
            }

            if (!MeasureOnly)
                AppendDrawCall(ref fragment) = SuppressUntilEnd ? default : drawCall;

            // FIXME: Something about inline image box placement is wrong in MeasureOnly mode.

            var boxIndex = BoxIndex++;
            ref var line = ref CurrentLine;
            fragment.BoxIndex = boxIndex;
            fragment.Category = FragmentCategory.Box;
            fragment.Width = bounds.Size.X;
            switch (image.HorizontalAlignment) {
                case ImageHorizontalAlignment.Inline:
                    // FIXME: Y margin for inline images
                    fragment.Width += image.Margin.X;
                    fragment.Overhang = image.Margin.X;
                    line.Width += fragment.Width;
                    UnconstrainedLineSize.X += fragment.Width;
                    break;
                case ImageHorizontalAlignment.Left:
                    line.Inset += fragment.Width + image.Margin.X;
                    break;
                case ImageHorizontalAlignment.Right:
                    line.Crush += fragment.Width + image.Margin.X;
                    break;
            }
            fragment.Height = image.DoNotAdjustLineSpacing 
                ? line.Height 
                : bounds.Size.Y;

            var baselineAlignment = Arithmetic.Saturate(
                image.BaselineAlignment ?? 
                ((image.HorizontalAlignment == ImageHorizontalAlignment.Inline) ? 1.0f : 0.0f)
            );
            fragment.Baseline = fragment.Height * baselineAlignment;

            ref var box = ref Buffers.Box(boxIndex);
            box = new Box {
                FragmentIndex = FragmentIndex,
                DrawCallIndex = drawCallIndex,
                HorizontalAlignment = image.HorizontalAlignment,
                BaselineAlignment = baselineAlignment,
                Margin = image.Margin,
                // FIXME: Baseline alignment
                Bounds = Bounds.FromPositionAndSize(0f, line.Location.Y, bounds.Size.X, bounds.Size.Y),
            };

            FinishFragment(true);

            // FIXME: SingleFragmentMode?
            if (image.Clear)
                PerformLineBreak(CurrentDefaultLineSpacing, false);
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

                float totalWidth = constrainedSize.X - line.Crush - line.Inset - line.Location.X;
                float y = Position.Y + line.Location.Y;
                float whitespace = totalWidth - line.Width, gapWhitespace = 0f;

                switch (line.Alignment) {
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
                    case HorizontalAlignment.JustifyWordsRight:
                        if (gapCount > 0) {
                            gapWhitespace = whitespace / gapCount;
                            if (gapWhitespace > MaxExpansionPerSpace)
                                gapWhitespace = 0f;
                            whitespace -= (gapWhitespace * gapCount);
                        }
                        break;
                }

                whitespace = Math.Max(whitespace, 0f);
                gapWhitespace = Math.Max(gapWhitespace, 0f);
                line.Location.X += line.Inset + whitespace;
                line.Width += (gapWhitespace * gapCount);
                if (line.FragmentCount == 0)
                    continue;

                float x = Position.X + line.Location.X,
                    boxRightEdge = Math.Max(constrainedSize.X, MaximumWidth);
                if (DesiredWidth > 0)
                    boxRightEdge = DesiredWidth;

                bool foundAnchoredFragments = false;
                for (uint f = line.FirstFragmentIndex, f2 = f + line.FragmentCount - 1; f <= f2; f++) {
                    ref var fragment = ref Buffers.Fragment(f);
                    float fragmentY = y + (line.Baseline - fragment.Baseline);
                    if (fragment.AnchorSpanIndex == uint.MaxValue)
                        ArrangeSingleFragment(
                            ref constrainedSize, gapCategory, ref line, ref x, y, gapWhitespace,
                            boxRightEdge, ref fragment, fragmentY, 1f
                        );
                    else
                        foundAnchoredFragments = true;
                }

                if (!foundAnchoredFragments)
                    continue;

                for (uint f = line.FirstFragmentIndex, f2 = f + line.FragmentCount - 1; f <= f2; f++) {
                    ref var fragment = ref Buffers.Fragment(f);
                    if (fragment.AnchorSpanIndex == uint.MaxValue)
                        continue;

                    DenseList<Bounds> anchorBoxes = default;
                    // FIXME
                    if (!TryGetSpanBoundingBoxes(fragment.AnchorSpanIndex, ref anchorBoxes))
                        continue;

                    var lastBox = anchorBoxes.LastOrDefault();
                    float scale =
                        lastBox.Size.X >= fragment.Width
                            ? 1.0f
                            : Math.Max(lastBox.Size.X / fragment.Width, MinRubyScale);
                    // HACK: Ensure that we don't push ruby fragments too far to the left, if their anchor is
                    //  at the start of a line.
                    float anchoredX = Math.Max(Position.X, lastBox.Center.X - (fragment.Width * 0.5f * scale)),
                        anchoredY = y - (fragment.Height * scale);
                    ArrangeSingleFragment(
                        ref constrainedSize, FragmentCategory.Regular,
                        ref line, ref anchoredX, anchoredY,
                        0f, boxRightEdge, ref fragment, anchoredY,
                        scale
                    );
                }
            }
        }

        private void ArrangeSingleFragment (
            ref Vector2 constrainedSize, FragmentCategory gapCategory, ref Line line, 
            ref float x, float y, float gapWhitespace, float boxRightEdge, 
            ref Fragment fragment, float fragmentY, float scale
        ) {
            var hasScale = scale != 1.0f;
            fragment.Left = x;

            if (fragment.Category == FragmentCategory.Box) {
                ref var box = ref Buffers.Box(fragment.BoxIndex);

                float boxX = x, boxBaseline = box.Bounds.Size.Y * box.BaselineAlignment,
                    alignmentToBaseline = line.Baseline - boxBaseline;
                if (MeasureOnly) {
                    // We don't have draw call information so we can only align the existing box.
                    // Its size should be fairly accurate though.
                    switch (box.HorizontalAlignment) {
                        case ImageHorizontalAlignment.Left:
                            boxX = Position.X;
                            alignmentToBaseline = 0f;
                            break;
                        case ImageHorizontalAlignment.Right:
                            constrainedSize.X = boxRightEdge; // :(
                            boxX = boxRightEdge - box.Bounds.Size.X + Position.X;
                            alignmentToBaseline = 0f;
                            break;
                    }
                    box.Bounds = Bounds.FromPositionAndSize(boxX, y + alignmentToBaseline, box.Bounds.Size.X, box.Bounds.Size.Y);
                } else if (box.DrawCallIndex != uint.MaxValue) {
                    ref var drawCall = ref Buffers.DrawCall(box.DrawCallIndex);
                    var estimatedBounds = drawCall.EstimateDrawBounds();
                    switch (box.HorizontalAlignment) {
                        case ImageHorizontalAlignment.Left:
                            boxX = Position.X;
                            alignmentToBaseline = 0f;
                            break;
                        case ImageHorizontalAlignment.Right:
                            constrainedSize.X = boxRightEdge; // :(
                            boxX = boxRightEdge - estimatedBounds.Size.X + Position.X;
                            alignmentToBaseline = 0f;
                            break;
                        case ImageHorizontalAlignment.Inline:
                            boxX += box.Margin.X;
                            break;
                    }
                    box.Bounds = Bounds.FromPositionAndSize(boxX, y + alignmentToBaseline, estimatedBounds.Size.X, estimatedBounds.Size.Y);
                    drawCall.Position = new Vector2(boxX, y + alignmentToBaseline);
                } else {
                    switch (box.HorizontalAlignment) {
                        case ImageHorizontalAlignment.Left:
                            boxX = Position.X;
                            alignmentToBaseline = 0f;
                            break;
                        case ImageHorizontalAlignment.Right:
                            constrainedSize.X = boxRightEdge; // :(
                            boxX = boxRightEdge - fragment.Width + Position.X;
                            alignmentToBaseline = 0f;
                            break;
                    }
                    box.Bounds = Bounds.FromPositionAndSize(boxX, y + alignmentToBaseline, fragment.Width, fragment.Height);
                }

                constrainedSize.Y = Math.Max(constrainedSize.Y, box.Bounds.BottomRight.Y - Position.Y);

                if (box.HorizontalAlignment == ImageHorizontalAlignment.Inline) {
                    x += fragment.Width;
                    x += fragment.Overhang;
                }
            } else {
                if (fragment.Category == gapCategory)
                    x += gapWhitespace;

                var rtl = fragment.RTL;
                if (fragment.DrawCallCount > 0) {
                    for (uint dc = fragment.FirstDrawCall, dc2 = dc + fragment.DrawCallCount - 1; dc <= dc2; dc++) {
                        ref var drawCall = ref Buffers.DrawCall(dc);

                        if (hasScale) {
                            if (rtl)
                                drawCall.Position.X = x + (fragment.Width - drawCall.Position.X) * scale;
                            else
                                drawCall.Position.X = x + (drawCall.Position.X * scale);

                            drawCall.Scale *= scale;
                        } else {
                            if (rtl)
                                drawCall.Position.X = x + (fragment.Width - drawCall.Position.X);
                            else
                                drawCall.Position.X = x + drawCall.Position.X;
                        }

                        drawCall.Position.Y += fragmentY;
                    }
                }

                x += fragment.Width;
            }

            Listener2?.FragmentArranged(ref this, ref fragment);
        }

        public void ComputeConstrainedSize (out Vector2 constrainedSize) {
            constrainedSize = Vector2.Zero;
            for (uint i = 0; i < LineIndex; i++) {
                ref var line = ref Buffers.Line(i);
                // Omit trailing whitespace.
                float w = line.Location.X + line.Width + line.Inset + line.Crush;
                if (IncludeTrailingWhitespace)
                    w += line.TrailingWhitespace;
                constrainedSize.X = Math.Max(constrainedSize.X, w);

                // HACK: Fixes a single-line size overhang from line limiting
                // FIXME: This will break trailing whitespace from extra line breaks.
                if ((line.DrawCallCount == 0) && (w <= 0))
                    continue;

                // We have to use Max here because of things like ExtraBreakSpacing that don't
                //  alter the line's height. If we were to just sum heights it would be too small
                constrainedSize.Y = Math.Max(constrainedSize.Y, line.Location.Y + line.Height);
            }

            // HACK: In some cases a box may force our size to increase
            for (uint i = 0; i < BoxIndex; i++) {
                ref var box = ref Buffers.Box(i);
                var extent = box.Bounds.BottomRight + box.Margin;
                constrainedSize.X = Math.Max(constrainedSize.X, extent.X);
                constrainedSize.Y = Math.Max(constrainedSize.Y, extent.Y);
                UnconstrainedMaxWidth = Math.Max(UnconstrainedMaxWidth, extent.X);
                UnconstrainedMaxY = Math.Max(UnconstrainedMaxY, extent.Y);
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

            FinishFragment(false);
            UnconstrainedY += UnconstrainedLineTopDecorations;
            UnconstrainedLineSize.Y += UnconstrainedLineBottomDecorations;
            FinishLine(false);
            UnconstrainedMaxY = Math.Max(UnconstrainedMaxY, UnconstrainedY + UnconstrainedLineSize.Y);

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
                Position, constrainedSize, new Vector2(UnconstrainedMaxWidth, UnconstrainedMaxY), 
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
            BreakLimit = CharacterLimit = LineLimit = int.MaxValue;
        }

        private FragmentCategory AnalyzeCharacter (char ch1, uint codepoint, out bool lineBreak) {
            bool isWhitespace = Unicode.IsWhiteSpace(codepoint) && (MaskCodepoint == 0),
                isWordWrapPoint, isNonPrintable = codepoint < 32;

            lineBreak = false;

            if (SplitAtWrapCharactersOnly)
                isWordWrapPoint = WrapCharacters.BinarySearchNonRef(codepoint, UintComparer.Instance) >= 0;
            else
                isWordWrapPoint = isWhitespace || Unicode.IsSeparator(ch1) ||
                    (MaskCodepoint != 0) || WrapCharacters.BinarySearchNonRef(codepoint, UintComparer.Instance) >= 0;

            if (codepoint == 0x00A0) {
                // Wrapping and justify expansion should never occur for a non-breaking space
                isWordWrapPoint = false;
                isNonPrintable = true;
            } else if (codepoint > 127) {
                if (codepoint == '\u2007') {
                    // Figure spaces should be treated as printable characters, not whitespace,
                    //  since they're meant for use in numerical figures. No wrapping either.
                    isWhitespace = false;
                    isNonPrintable = false;
                    isWordWrapPoint = false;
                } else {
                    // HACK: Attempt to word-wrap at "other" punctuation in non-western character sets, which will include things like commas
                    // This is less than ideal but .NET does not appear to expose the classification tables needed to do this correctly
                    // FIXME: This won't work for surrogate pairs, no public API is exposed for them
                    var uniCategory = Unicode.GetCategory(codepoint);
                    if (uniCategory == UnicodeCategory.OtherPunctuation)
                        isWordWrapPoint = true;
                    else if (uniCategory == UnicodeCategory.Control)
                        isNonPrintable = true;
                    else if (uniCategory == UnicodeCategory.Format)
                        isNonPrintable = true;
                    else if (uniCategory == UnicodeCategory.NonSpacingMark)
                        return FragmentCategory.CombiningCharacter;
                }
            } else if (ch1 == '\n')
                lineBreak = true;

            if (lineBreak) {
                BreakLimit--;
                if (BreakLimit <= 0)
                    SuppressLayoutForLimit();
                if (!SuppressUntilEnd && IncludeTrailingWhitespace)
                    NewLinePending = true;
            } else if (LineLimit <= 0) {
                SuppressLayoutForLimit();
            }

            if (SplitAtWrapCharactersOnly) {
                if (isWordWrapPoint)
                    return FragmentCategory.WrapPoint;
                else if (isNonPrintable)
                    return FragmentCategory.NonPrintable;
                else if (isWhitespace)
                    return FragmentCategory.Whitespace;
                else
                    return FragmentCategory.Regular;
            } else {
                if (isNonPrintable)
                    return FragmentCategory.NonPrintable;
                else if (isWhitespace)
                    return FragmentCategory.Whitespace;
                else if (isWordWrapPoint)
                    return FragmentCategory.WrapPoint;
                else
                    return FragmentCategory.Regular;
            }
        }

        public static uint DecodeCodepoint (in AbstractString text, ref int i, int l, out char ch1, out int currentCodepointSize) {
            char ch2 = i < (l - 1)
                    ? text[i + 1]
                    : '\0';
            ch1 = text[i];
            currentCodepointSize = 1;

            if (char.IsHighSurrogate(ch1)) {
                // FIXME: Detect missing low surrogate and generate replacement char
                currentCodepointSize = 2;
                i++;
                return (uint)char.ConvertToUtf32(ch1, ch2);
            } else if (char.IsLowSurrogate(ch1))
                // Corrupt text: unpaired low surrogate
                return 0xFFFD;

            if (ch1 == '\r') {
                if (ch2 == '\n') {
                    currentCodepointSize = 2;
                    ch1 = ch2;
                    i++;
                }
            }

            return ch1;
        }

        public void Dispose () {
            Buffers.Dispose();
        }
    }
}

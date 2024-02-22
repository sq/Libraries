using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public struct StringLayout {
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
        public ArraySegment<BitmapDrawCall> DrawCalls;
        public DenseList<AbstractTextureReference> UsedTextures;
        // TODO: Find a smaller representation for these, because this makes DynamicStringLayout big
        public DenseList<Bounds> Boxes;
        public readonly int WordCount, LineCount;
        public readonly bool WasLineLimited;

        public StringLayout (
            in Vector2 position, in Vector2 size, in Vector2 unconstrainedSize, 
            float lineHeight, in Bounds firstCharacter, in Bounds lastCharacter, 
            ArraySegment<BitmapDrawCall> drawCalls, bool wasLineLimited,
            int wordCount, int lineCount
        ) {
            Position = position;
            Size = size;
            UnconstrainedSize = unconstrainedSize;
            LineHeight = lineHeight;
            FirstCharacterBounds = firstCharacter;
            LastCharacterBounds = lastCharacter;
            DrawCalls = drawCalls;
            WasLineLimited = wasLineLimited;
            Boxes = default;
            UsedTextures = default;
            WordCount = wordCount;
            LineCount = lineCount;
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
    }

    public enum HorizontalAlignment : byte {
        Left,
        Center,
        Right,
        JustifyWords,
        JustifyWordsCentered
    }

    public struct LayoutMarker {
        public sealed class Comparer : IRefComparer<LayoutMarker> {
            public static readonly Comparer Instance = new Comparer();

            public int Compare (ref LayoutMarker lhs, ref LayoutMarker rhs) {
                var result = lhs.FirstCharacterIndex.CompareTo(rhs.FirstCharacterIndex);
                if (result == 0)
                    result = lhs.LastCharacterIndex.CompareTo(rhs.LastCharacterIndex);
                return result;
            }
        }

        // Inputs
        public AbstractString OriginalText;
        public AbstractString ActualText;
        public AbstractString ID;
        public int FirstCharacterIndex, LastCharacterIndex;

        // Outputs
        internal uint SpanIndex;
        // FIXME: Remove this
        internal ushort CurrentSplitGlyphCount;
        // FIXME: Remove this
        public ushort GlyphCount;
        public int? FirstDrawCallIndex, LastDrawCallIndex;
        public DenseList<Bounds> Bounds;

        public LayoutMarker (int firstIndex, int lastIndex) : this() {
            FirstCharacterIndex = firstIndex;
            LastCharacterIndex = lastIndex;
        }

        public Bounds UnionBounds {
            get {
                if (Bounds.Count <= 1)
                    return Bounds.LastOrDefault();
                var b = Bounds[0];
                for (int i = 1; i < Bounds.Count; i++)
                    b = Squared.Game.Bounds.FromUnion(b, Bounds[i]);
                return b;
            }
        }

        public override string ToString () {
            return $"{(ID.IsNull ? "marker" : ID)} [{FirstCharacterIndex} - {LastCharacterIndex}] -> [{FirstDrawCallIndex} - {LastDrawCallIndex}] {Bounds.FirstOrDefault()}";
        }
    }

    public struct LayoutHitTest {
        public sealed class Comparer : IRefComparer<LayoutHitTest> {
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

        public LayoutHitTest (Vector2 position) {
            Position = position;
            FirstCharacterIndex = LastCharacterIndex = null;
            LeaningRight = false;
        }

        public override string ToString () {
            return $"hitTest {Position} -> {FirstCharacterIndex} leaning {(LeaningRight ? "right" : "left")}";
        }
    }
}

namespace Squared.Render {
    public static class SpriteFontExtensions {
        public static StringLayout LayoutString (
            this SpriteFont font, in AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            in Vector2? position = null, Color? color = null, float scale = 1, 
            DrawCallSortKey sortKey = default(DrawCallSortKey),
            int characterSkipCount = 0, int? characterLimit = null,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            GlyphPixelAlignment alignToPixels = default(GlyphPixelAlignment),
            bool wordWrap = false,
            bool reverseOrder = false, HorizontalAlignment? horizontalAlignment = null,
            Color? addColor = null
        ) {
            var state = new StringLayoutEngine {
                position = position,
                defaultColor = color ?? Color.White,
                scale = scale,
                sortKey = sortKey,
                characterSkipCount = characterSkipCount,
                characterLimit = characterLimit,
                xOffsetOfFirstLine = xOffsetOfFirstLine,
                lineBreakAtX = lineBreakAtX,
                alignToPixels = alignToPixels,
                characterWrap = lineBreakAtX.HasValue,
                wordWrap = wordWrap,
                reverseOrder = reverseOrder,
                addColor = addColor ?? Color.Transparent
            };
            state.SetBuffer(buffer.GetValueOrDefault(default), true);
            var gs = new SpriteFontGlyphSource(font);

            if (horizontalAlignment.HasValue)
                state.alignment = horizontalAlignment.Value;

            state.Initialize();

            using (state) {
                state.AppendText(
                    gs, text
                );

                return state.Finish();
            }
        }

        // Yuck :(
        public static StringLayout LayoutString<TGlyphSource> (
            this TGlyphSource glyphSource, in AbstractString text, ArraySegment<BitmapDrawCall>? buffer = null,
            in Vector2? position = null, Color? color = null, float scale = 1, 
            DrawCallSortKey sortKey = default(DrawCallSortKey),
            int characterSkipCount = 0, int? characterLimit = null,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null,
            bool alignToPixels = false,
            bool wordWrap = false,
            bool reverseOrder = false, HorizontalAlignment? horizontalAlignment = null,
            Color? addColor = null
        ) where TGlyphSource : IGlyphSource {
            var state = new StringLayoutEngine {
                position = position,
                defaultColor = color ?? Color.White,
                scale = scale,
                sortKey = sortKey,
                characterSkipCount = characterSkipCount,
                characterLimit = characterLimit,
                xOffsetOfFirstLine = xOffsetOfFirstLine,
                lineBreakAtX = lineBreakAtX,
                alignToPixels = alignToPixels,
                characterWrap = lineBreakAtX.HasValue,
                wordWrap = wordWrap,
                reverseOrder = reverseOrder,
                addColor = addColor ?? Color.Transparent
            };
            state.SetBuffer(buffer.GetValueOrDefault(default), true);

            if (horizontalAlignment.HasValue)
                state.alignment = horizontalAlignment.Value;

            state.Initialize();

            using (state) {
                state.AppendText(
                    glyphSource, text
                );

                return state.Finish();
            }
        }
    }

    namespace Text {
        public enum PixelAlignmentMode {
            None = 0,
            /// <summary>
            /// Snaps positions to 0 or 1
            /// </summary>
            Floor = 1,
            /// <summary>
            /// Allows [0, 0.5, 1] instead of [0, 1]
            /// </summary>
            FloorHalf = 2,
            /// <summary>
            /// Allows [0, 0.25, 0.5, 0.75, 1]
            /// </summary>
            FloorQuarter = 4,
            /// <summary>
            /// Rounds to 0 or 1
            /// </summary>
            Round = 5,
            /// <summary>
            /// Rounds to 0, 0.5, or 1
            /// </summary>
            RoundHalf = 6,
            /// <summary>
            /// Uses the default value set on the glyph source, if possible, otherwise None
            /// </summary>
            Default = 10,
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

            public static readonly GlyphPixelAlignment None = new GlyphPixelAlignment(PixelAlignmentMode.None);
            public static readonly GlyphPixelAlignment Default = new GlyphPixelAlignment(PixelAlignmentMode.Default);
            public static readonly GlyphPixelAlignment RoundXY = new GlyphPixelAlignment(PixelAlignmentMode.Round);
            public static readonly GlyphPixelAlignment FloorXY = new GlyphPixelAlignment(PixelAlignmentMode.Floor);
            public static readonly GlyphPixelAlignment FloorY = new GlyphPixelAlignment(PixelAlignmentMode.None, PixelAlignmentMode.Floor);

            public bool Equals (GlyphPixelAlignment other) {
                return (other.Horizontal == Horizontal) && (other.Vertical == Vertical);
            }

            public override int GetHashCode () {
                return Horizontal.GetHashCode() ^ Vertical.GetHashCode();
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

            internal GlyphPixelAlignment Or (GlyphPixelAlignment? defaultAlignment) {
                return new GlyphPixelAlignment {
                    Horizontal = (Horizontal == PixelAlignmentMode.Default) 
                        ? (defaultAlignment?.Horizontal ?? PixelAlignmentMode.None)
                        : Horizontal,
                    Vertical = (Vertical == PixelAlignmentMode.Default) 
                        ? (defaultAlignment?.Vertical ?? PixelAlignmentMode.None)
                        : Vertical,
                };
            }
        }
    }
}
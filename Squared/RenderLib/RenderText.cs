using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render.Evil;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace Squared.Render {
    public class DynamicStringLayout {
        private GrowableBuffer<BitmapDrawCall> _Buffer = new GrowableBuffer<BitmapDrawCall>(); 
        private StringLayout? _CachedStringLayout;

        private SpriteFont _Font;
        private string _Text;
        private Vector2 _Position = Vector2.Zero;
        private Color _Color = Color.White;
        private float _Scale = 1;
        private float _SortKey = 0;
        private int _CharacterSkipCount = 0;
        private int _CharacterLimit = int.MaxValue;
        private float _XOffsetOfFirstLine = 0;
        private float? _LineBreakAtX = null;
        private bool _WordWrap = false;

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

        public string Text {
            get {
                return _Text;
            }
            set {
                InvalidatingReferenceAssignment(ref _Text, value);
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

        public StringLayout Get () {
            if (_Text == null)
                return new StringLayout();

            if (!_CachedStringLayout.HasValue) {
                int length = _Text.Length;

                int capacity = length;
                if (_WordWrap)
                    capacity *= 2;

                _Buffer.EnsureCapacity(capacity);

                _CachedStringLayout = Font.LayoutString(
                    _Text, _Buffer.Buffer, 
                    _Position, _Color, 
                    _Scale, _SortKey, 
                    _CharacterSkipCount, _CharacterLimit, 
                    _XOffsetOfFirstLine, _WordWrap ? null : _LineBreakAtX
                );

                if (_WordWrap && _LineBreakAtX.HasValue) {
                    _CachedStringLayout = _CachedStringLayout.Value.WordWrap(
                        _Text, _LineBreakAtX.Value, new ArraySegment<BitmapDrawCall>(
                            _Buffer.Buffer.Array, _Buffer.Buffer.Offset + length, length
                        ), 0
                    );
                }
            }

            return _CachedStringLayout.Value;
        }
    }

    public struct StringLayout {
        public readonly Vector2 Position;
        public readonly Vector2 Size;
        public readonly Bounds FirstCharacterBounds;
        public readonly Bounds LastCharacterBounds;
        public readonly ArraySegment<BitmapDrawCall> DrawCalls;

        public StringLayout (Vector2 position, Vector2 size, Bounds firstCharacter, Bounds lastCharacter, ArraySegment<BitmapDrawCall> drawCalls) {
            Position = position;
            Size = size;
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

        public StringLayout WordWrap (string text, float wrapAtX, ArraySegment<BitmapDrawCall>? buffer = null, float wrapIndentation = 0f) {
            int? lastWordStartIndex = null, thisWordStartIndex = null;
            float thisWordWidth = 0;
            int lastWordEndIndex = 0;
            float maxWordWidth = wrapAtX;
            var lineHeight = Size.Y;
            var newSize = new Vector2();

            ArraySegment<BitmapDrawCall> _buffer;
            if (buffer.HasValue)
                _buffer = buffer.Value;
            else
                _buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[Count]);

            Array.Copy(this.DrawCalls.Array, this.DrawCalls.Offset, _buffer.Array, _buffer.Offset, Count);

            for (var i = 0; i < Count; i++) {
                var ch = text[i];
                var dc = _buffer.Array[_buffer.Offset + i];

                var isWordChar = Char.IsLetterOrDigit(ch) || (ch == '\'');
                if (!isWordChar) {
                    if (thisWordStartIndex.HasValue) {
                        lastWordStartIndex = thisWordStartIndex;
                        lastWordEndIndex = i - 1;
                        thisWordStartIndex = null;
                    }
                } else {
                    if (!thisWordStartIndex.HasValue) {
                        thisWordStartIndex = i;
                        thisWordWidth = 0f;
                    }

                    thisWordWidth += dc.TextureRegion.Size.X * dc.Texture.Width;
                }

                var needWrap = (dc.Position.X >= wrapAtX);
                if (needWrap) {
                    int fromOffset = i;

                    // Character wrap if we have no current word or the current word is too wide.
                    if (thisWordStartIndex.HasValue && (thisWordWidth <= wrapAtX))
                        fromOffset = thisWordStartIndex.Value;

                    float xDelta = Position.X - _buffer.Array[_buffer.Offset + fromOffset].Position.X + wrapIndentation;
                    // After we've done an indent, the maximum width is reduced.
                    maxWordWidth = wrapAtX - wrapIndentation;

                    for (var j = fromOffset; j < Count; j++) {
                        var dc2 = _buffer.Array[_buffer.Offset + j];
                        dc2.Position.X += xDelta;
                        dc2.Position.Y += lineHeight;
                        _buffer.Array[_buffer.Offset + j] = dc2;
                    }
                }
            }

            for (var i = 0; i < Count; i++) {
                var dc = _buffer.Array[_buffer.Offset + i];
                newSize.X = Math.Max(
                    dc.Position.X + (dc.Texture.Width * dc.TextureRegion.Size.X) - Position.X, newSize.X
                );
                newSize.Y = Math.Max(
                    dc.Position.Y + (dc.Texture.Height * dc.TextureRegion.Size.Y) - Position.Y, newSize.Y
                );
            }

            return new StringLayout(
                Position, newSize,
                // FIXME
                FirstCharacterBounds,
                LastCharacterBounds,
                _buffer
            );
        }
    }

    public static class SpriteFontExtensions {
        public static StringLayout LayoutString (
            this SpriteFont font, string text, ArraySegment<BitmapDrawCall>? buffer,
            Vector2? position = null, Color? color = null, float scale = 1, float sortKey = 0,
            int characterSkipCount = 0, int characterLimit = int.MaxValue,
            float xOffsetOfFirstLine = 0, float? lineBreakAtX = null
        ) {
            if (text == null)
                throw new ArgumentNullException("text");

            ArraySegment<BitmapDrawCall> _buffer;
            if (buffer.HasValue)
                _buffer = buffer.Value;
            else
                _buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[text.Length]);

            if (_buffer.Count < text.Length)
                throw new ArgumentException("buffer too small", "buffer");

            var spacing = font.Spacing;
            var lineSpacing = font.LineSpacing;
            var glyphSource = font.GetGlyphSource();

            var actualPosition = position.GetValueOrDefault(Vector2.Zero);
            var characterOffset = new Vector2(xOffsetOfFirstLine, 0);
            var totalSize = Vector2.Zero;

            Bounds firstCharacterBounds = default(Bounds), lastCharacterBounds = default(Bounds);

            var drawCall = new BitmapDrawCall(
                glyphSource.Texture, default(Vector2), default(Bounds), color.GetValueOrDefault(Color.White), scale
            );
            drawCall.SortKey = sortKey;

            float rectScaleX = 1f / glyphSource.Texture.Width;
            float rectScaleY = 1f / glyphSource.Texture.Height;

            int bufferWritePosition = _buffer.Offset;
            int drawCallsWritten = 0;

            bool firstCharacterEver = true;
            bool firstCharacterOfLine = true;

            for (int i = 0, l = text.Length; i < l; i++) {
                var ch = text[i];

                var lineBreak = false;
                if (ch == '\r') {
                    if (((i + 1) < l) && (text[i + 1] == '\n'))
                        i += 1;

                    lineBreak = true;
                } else if (ch == '\n') {
                    lineBreak = true;
                }

                bool deadGlyph;

                Glyph glyph;
                deadGlyph = !glyphSource.GetGlyph(ch, out glyph);

                if (!deadGlyph) {
                    var x = characterOffset.X + glyph.LeftSideBearing + glyph.RightSideBearing + glyph.Width + spacing;
                    if (x >= lineBreakAtX)
                        lineBreak = true;
                }

                if (lineBreak) {
                    characterOffset.X = 0;
                    characterOffset.Y += lineSpacing;
                    firstCharacterOfLine = true;
                }

                if (deadGlyph) {
                    characterSkipCount--;
                    characterLimit--;
                    continue;
                }

                characterOffset.X += spacing;

                lastCharacterBounds = Bounds.FromPositionAndSize(
                    characterOffset, new Vector2(
                        glyph.LeftSideBearing + glyph.Width + glyph.RightSideBearing,
                        font.LineSpacing
                    )
                );

                if (firstCharacterEver) {
                    firstCharacterBounds = lastCharacterBounds;
                    firstCharacterEver = false;
                }

                characterOffset.X += glyph.LeftSideBearing;

                if (firstCharacterOfLine) {
                    characterOffset.X = Math.Max(characterOffset.X, 0);
                    firstCharacterOfLine = false;
                }

                if (characterSkipCount <= 0) {
                    if (characterLimit <= 0)
                        break;

                    drawCall.TextureRegion = glyphSource.Texture.BoundsFromRectangle(ref glyph.BoundsInTexture);
                    drawCall.Position = new Vector2(
                        actualPosition.X + (glyph.Cropping.X + characterOffset.X) * scale,
                        actualPosition.Y + (glyph.Cropping.Y + characterOffset.Y) * scale
                    );

                    _buffer.Array[bufferWritePosition] = drawCall;

                    bufferWritePosition += 1;
                    drawCallsWritten += 1;

                    characterLimit--;
                } else {
                    characterSkipCount--;
                }

                characterOffset.X += (glyph.Width + glyph.RightSideBearing);

                totalSize.X = Math.Max(totalSize.X, characterOffset.X);
                totalSize.Y = Math.Max(totalSize.Y, characterOffset.Y + font.LineSpacing);
            }

            return new StringLayout(
                position.GetValueOrDefault(), totalSize, 
                firstCharacterBounds, lastCharacterBounds,
                new ArraySegment<BitmapDrawCall>(
                    _buffer.Array, _buffer.Offset, drawCallsWritten
                )
            );
        }
    }

    [Obsolete("Use ImperativeRenderer.DrawString instead.")]
    public class StringBatch : ListBatch<StringDrawCall> {
        protected static FieldInfo _FontTexture;

        public SpriteBatch SpriteBatch; // :(
        public SpriteFont Font;
        public Matrix? TransformMatrix;
        public Rectangle? ScissorRect;

        static StringBatch () {
            _FontTexture = typeof(SpriteFont).GetField("textureValue", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public void Initialize (IBatchContainer container, int layer, Material material, SpriteBatch spriteBatch, SpriteFont font, Matrix? transformMatrix) {
            base.Initialize(container, layer, material);
            SpriteBatch = spriteBatch;
            Font = font;
            TransformMatrix = transformMatrix;
            ScissorRect = null;
        }

        public void SetScissorBounds (Bounds bounds) {
            if (TransformMatrix.HasValue) {
                var matrix = TransformMatrix.Value;
                bounds.TopLeft = Vector2.Transform(bounds.TopLeft, matrix);
                bounds.BottomRight = Vector2.Transform(bounds.BottomRight, matrix);
            }

            ScissorRect = new Rectangle(
                (int)Math.Floor(bounds.TopLeft.X),
                (int)Math.Floor(bounds.TopLeft.Y),
                (int)Math.Ceiling(bounds.BottomRight.X - bounds.TopLeft.X),
                (int)Math.Ceiling(bounds.BottomRight.Y - bounds.TopLeft.Y)
            );
        }

        public override void Prepare () {
        }

        public override void Issue (DeviceManager manager) {
            if (_DrawCalls.Count == 0)
                return;

            var m = manager.ApplyMaterial(Material);
            var fontTexture = _FontTexture.GetValue(Font) as Texture2D;
            manager.CurrentParameters["BitmapTextureSize"].SetValue(new Vector2(
                fontTexture.Width, fontTexture.Height
            ));
            m.Dispose();

            manager.Finish();

            var oldRect = manager.Device.ScissorRectangle;
            if (ScissorRect.HasValue) {
                var viewport = manager.Device.Viewport;
                var viewportWidth = viewport.Width;
                var viewportHeight = viewport.Height;
                var scissorRect = ScissorRect.Value;

                if (scissorRect.X < 0)
                    scissorRect.X = 0;
                if (scissorRect.Y < 0)
                    scissorRect.Y = 0;

                if (scissorRect.X >= viewportWidth) {
                    scissorRect.X = 0;
                    scissorRect.Width = 0;
                }

                if (scissorRect.Y >= viewportHeight) {
                    scissorRect.Y = 0;
                    scissorRect.Height = 0;
                }

                if (scissorRect.Width < 0)
                    scissorRect.Width = 0;
                if (scissorRect.Height < 0)
                    scissorRect.Height = 0;
                if (scissorRect.Width > viewportWidth - scissorRect.X)
                    scissorRect.Width = viewportWidth - scissorRect.X;
                if (scissorRect.Height > viewportHeight - scissorRect.Y)
                    scissorRect.Height = viewportHeight - scissorRect.Y;

                manager.Device.ScissorRectangle = scissorRect;
            }

            if (TransformMatrix.HasValue) {
                SpriteBatch.Begin(
                    SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.LinearClamp,
                    DepthStencilState.None, RasterizerState.CullNone, null, TransformMatrix.Value
                );
            } else
                SpriteBatch.Begin(
                    SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.LinearClamp,
                    DepthStencilState.None, RasterizerState.CullNone
                );

            try {
                float z = 0.0f;
                float zstep = 1.0f / _DrawCalls.Count;
                foreach (var call in _DrawCalls) {
                    var position = call.Position;
                    position = position.Round();
                    SpriteBatch.DrawString(Font, call.Text, position, call.Color, 0.0f, Vector2.Zero, call.Scale, SpriteEffects.None, z);
                    z += zstep;
                }
            } finally {
                SpriteBatch.End();
            }

            if (ScissorRect.HasValue)
                manager.Device.ScissorRectangle = oldRect;

            base.Issue(manager);
        }

        public Vector2 Measure (ref StringDrawCall drawCall) {
            return Font.MeasureString(drawCall.Text) * drawCall.Scale;
        }

        protected override void OnReleaseResources () {
            SpriteBatch = null;
            Font = null;
            TransformMatrix = null;
            ScissorRect = null;

            base.OnReleaseResources();
        }

        public void Add (StringDrawCall sdc) {
            Add(ref sdc);
        }

        new public void Add (ref StringDrawCall sdc) {
            base.Add(ref sdc);
        }

        public static StringBatch New (IBatchContainer container, int layer, Material material, SpriteBatch spriteBatch, SpriteFont font, Matrix? transformMatrix = null) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = container.RenderManager.AllocateBatch<StringBatch>();
            result.Initialize(container, layer, material, spriteBatch, font, transformMatrix);
            result.CaptureStack(0);
            return result;
        }
    }

    public struct StringDrawCall {
        public string Text;
        public Vector2 Position;
        public Color Color;
        public float Scale;

        public StringDrawCall (string text, Vector2 position)
            : this(text, position, Color.White) {
        }

        public StringDrawCall (string text, Vector2 position, Color color)
            : this(text, position, color, 1.0f) {
        }

        public StringDrawCall (string text, Vector2 position, Color color, float scale) {
            Text = text;
            Position = position;
            Color = color;
            Scale = scale;
        }

        public StringDrawCall Shadow (Color color, float offset) {
            return new StringDrawCall(
                Text, new Vector2(Position.X + offset, Position.Y + offset), 
                color, Scale
            );
        }
    }
}
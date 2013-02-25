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
    public static class SpriteFontExtensions {
        public static ArraySegment<BitmapDrawCall> LayoutString (
            this SpriteFont font, string text, ArraySegment<BitmapDrawCall>? buffer,
            Vector2? position = null, Color? color = null, float scale = 1, float sortKey = 0,
            int characterSkipCount = 0, int characterLimit = int.MaxValue
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
            var characterOffset = Vector2.Zero;

            var drawCall = new BitmapDrawCall(
                glyphSource.Texture, default(Vector2), default(Bounds), color.GetValueOrDefault(Color.White), scale
            );
            drawCall.SortKey = sortKey;

            float rectScaleX = 1f / glyphSource.Texture.Width;
            float rectScaleY = 1f / glyphSource.Texture.Height;

            int bufferWritePosition = _buffer.Offset;
            int drawCallsWritten = 0;

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

                if (lineBreak) {
                    characterOffset.X = 0;
                    characterOffset.Y += lineSpacing;
                    firstCharacterOfLine = true;
                }

                Glyph glyph;
                if (!glyphSource.GetGlyph(ch, out glyph)) {
                    characterSkipCount--;
                    characterLimit--;
                    continue;
                }

                characterOffset.X += spacing;

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
            }

            return new ArraySegment<BitmapDrawCall>(
                _buffer.Array, _buffer.Offset, drawCallsWritten
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
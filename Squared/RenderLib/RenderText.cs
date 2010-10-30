#pragma warning disable 0420 // a reference to a volatile field will not be treated as volatile

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace Squared.Render {
    public class StringBatch : ListBatch<StringDrawCall> {
        protected static FieldInfo _FontTexture;

        public SpriteBatch SpriteBatch; // :(
        public SpriteFont Font;
        public Matrix? TransformMatrix;
        public Rectangle? ScissorRect;

        static StringBatch () {
            _FontTexture = typeof(SpriteFont).GetField("textureValue", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public void Initialize (Frame frame, int layer, Material material, SpriteBatch spriteBatch, SpriteFont font, Matrix? transformMatrix) {
            base.Initialize(frame, layer, material);
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
        }

        public Vector2 Measure (ref StringDrawCall drawCall) {
            return Font.MeasureString(drawCall.Text) * drawCall.Scale;
        }

        public override void ReleaseResources () {
            base.ReleaseResources();

            SpriteBatch = null;
            Font = null;
            TransformMatrix = null;
            ScissorRect = null;
        }

        public static StringBatch New (Frame frame, int layer, Material material, SpriteBatch spriteBatch, SpriteFont font) {
            return New(frame, layer, material, spriteBatch, font, null);
        }

        public static StringBatch New (Frame frame, int layer, Material material, SpriteBatch spriteBatch, SpriteFont font, Matrix? transformMatrix) {
            if (frame == null)
                throw new ArgumentNullException("frame");
            if (material == null)
                throw new ArgumentNullException("material");

            var result = frame.RenderManager.AllocateBatch<StringBatch>();
            result.Initialize(frame, layer, material, spriteBatch, font, transformMatrix);
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
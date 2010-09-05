using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Storage;
using Squared.Render;
using Squared.Game;

namespace Pong {
    public class PongExample : MultithreadedGame {
        GraphicsDeviceManager graphics;
        RenderManager renderManager;
        DefaultMaterialSet materials;
        Paddle paddle;

        public PongExample() {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
        }

        public override RenderManager RenderManager {
            get { return renderManager; }
        }

        protected override void Initialize() {
            base.Initialize();

            renderManager = new RenderManager(graphics.GraphicsDevice);

            var rs = graphics.GraphicsDevice.RenderState;
            rs.DepthBufferEnable = false;
            rs.DepthBufferWriteEnable = false;
            rs.AlphaBlendEnable = true;
            rs.SourceBlend = Blend.SourceAlpha;
            rs.DestinationBlend = Blend.InverseSourceAlpha;
            rs.BlendFunction = BlendFunction.Add;
            rs.CullMode = CullMode.None;
            rs.ScissorTestEnable = false;

            paddle = new Paddle {
                Bounds = new Bounds(new Vector2(32, 32), new Vector2(48, 128)),
                Velocity = Vector2.Zero
            };
        }

        protected override void LoadContent() {
            materials = new DefaultMaterialSet(Content) {
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };
        }

        protected override void UnloadContent() {
        }

        protected override void Update(GameTime gameTime) {
            var padState = GamePad.GetState(PlayerIndex.One);
            var keyboardState = Keyboard.GetState();

            if ((padState.DPad.Down == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.Down)))
                paddle.Velocity = new Vector2(0, 4);
            else if ((padState.DPad.Up == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.Up)))
                paddle.Velocity = new Vector2(0, -4);
            else
                paddle.Velocity = Vector2.Zero;

            paddle.Update();

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Color.Red, materials.Clear);

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 1, materials.ScreenSpaceGeometry)) {
                Primitives.GradientFilledQuad(
                    gb, Vector2.Zero, new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                    Color.DarkSlateGray, Color.DarkSlateGray,
                    Color.SlateBlue, Color.SlateBlue
                );
            }

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 2, materials.ScreenSpaceGeometry)) {
                var transparentBlack = new Color(0, 0, 0, 127);

                Primitives.FilledQuad(
                    gb, 
                    new Vector2(24, 0), 
                    new Vector2(GraphicsDevice.Viewport.Width, 24), 
                    transparentBlack
                );
                Primitives.FilledQuad(
                    gb, 
                    new Vector2(0, 0), 
                    new Vector2(24, GraphicsDevice.Viewport.Height), 
                    transparentBlack
                );
                Primitives.FilledQuad(
                    gb,
                    new Vector2(GraphicsDevice.Viewport.Width - 24, 24),
                    new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                    transparentBlack
                );
                Primitives.FilledQuad(
                    gb,
                    new Vector2(24, GraphicsDevice.Viewport.Height - 24),
                    new Vector2(GraphicsDevice.Viewport.Width - 24, GraphicsDevice.Viewport.Height),
                    transparentBlack
                );
            }

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 3, materials.ScreenSpaceGeometry)) {
                Primitives.FilledBorderedBox(
                    gb, paddle.Bounds.TopLeft, paddle.Bounds.BottomRight, Color.White, Color.Gray, 4
                );
            }
        }
    }

    public class Paddle {
        public Bounds Bounds;
        public Vector2 Velocity;

        public void Update() {
            var newBounds = Bounds.Translate(Velocity);

            if (newBounds.TopLeft.Y < 32)
                return;

            Bounds = newBounds;
        }
    }
}

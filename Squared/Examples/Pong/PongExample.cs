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
        GraphicsDeviceManager Graphics;
        DefaultMaterialSet Materials;
        Playfield Playfield;
        Paddle[] Paddles;
        int[] Scores = new int[2];
        Ball Ball;
        SpriteFont Font;
        SpriteBatch SpriteBatch;

        public PongExample() {
            Graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = true;
            IsFixedTimeStep = false;
        }

        protected override void Initialize() {
            base.Initialize();

            var rs = Graphics.GraphicsDevice.RenderState;
            rs.DepthBufferEnable = false;
            rs.AlphaBlendEnable = true;
            rs.SourceBlend = Blend.SourceAlpha;
            rs.DestinationBlend = Blend.InverseSourceAlpha;

            Playfield = new Playfield {
                Bounds = new Bounds(new Vector2(-32, 24), new Vector2(Graphics.GraphicsDevice.Viewport.Width + 32, Graphics.GraphicsDevice.Viewport.Height - 24))
            };

            ResetPlayfield(0);
        }

        protected override void LoadContent() {
            Materials = new DefaultMaterialSet(Content) {
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };

            Font = Content.Load<SpriteFont>("Tahoma");

            SpriteBatch = new SpriteBatch(GraphicsDevice);
        }

        public void ResetPlayfield (int loserIndex) {
            Paddles = new Paddle[] { 
                new Paddle {
                    Bounds = new Bounds(
                        new Vector2(24, Playfield.Bounds.Center.Y - 48),
                        new Vector2(24 + 16, Playfield.Bounds.Center.Y + 48)
                    ),
                    Playfield = Playfield
                },
                new Paddle {
                    Bounds = new Bounds(
                        new Vector2(GraphicsDevice.Viewport.Width - 24 - 16, Playfield.Bounds.Center.Y - 48), 
                        new Vector2(GraphicsDevice.Viewport.Width - 24, Playfield.Bounds.Center.Y + 48)
                    ),
                    Playfield = Playfield
                }
            };

            var random = new Random();
            var velocity = new Vector2(loserIndex == 0 ? 1 : -1, (float)random.NextDouble(-1, 1));
            velocity.Normalize();
            velocity *= 4;

            Ball = new Ball {
                Position = Playfield.Bounds.Center,
                Velocity = velocity,
                Playfield = Playfield,
                Radius = 8.0f
            };
        }

        protected override void UnloadContent() {
        }

        protected override void Update(GameTime gameTime) {
            var padState1 = GamePad.GetState(PlayerIndex.One);
            var padState2 = GamePad.GetState(PlayerIndex.Two);
            var keyboardState = Keyboard.GetState();

            if ((padState1.DPad.Down == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.S)))
                Paddles[0].Velocity = new Vector2(0, 4);
            else if ((padState1.DPad.Up == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.W)))
                Paddles[0].Velocity = new Vector2(0, -4);
            else
                Paddles[0].Velocity = Vector2.Zero;

            if ((padState2.DPad.Down == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.Down)))
                Paddles[1].Velocity = new Vector2(0, 4);
            else if ((padState2.DPad.Up == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.Up)))
                Paddles[1].Velocity = new Vector2(0, -4);
            else
                Paddles[1].Velocity = Vector2.Zero;

            foreach (var paddle in Paddles)
                paddle.Update();

            Ball.Update(Paddles);

            if (Ball.Position.X <= Paddles[0].Bounds.TopLeft.X) {
                Scores[1] += 1;
                ResetPlayfield(0);
            } else if (Ball.Position.X >= Paddles[1].Bounds.BottomRight.X) {
                Scores[0] += 1;
                ResetPlayfield(1);
            }

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Color.Black, Materials.Clear);

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 1, Materials.ScreenSpaceGeometry)) {
                Primitives.GradientFilledQuad(
                    gb, Vector2.Zero, new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                    Color.DarkSlateGray, Color.DarkSlateGray,
                    Color.SlateBlue, Color.SlateBlue
                );
            }

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 2, Materials.ScreenSpaceGeometry)) {
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

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 3, Materials.ScreenSpaceGeometry)) {
                foreach (var paddle in Paddles)
                    Primitives.FilledBorderedBox(
                        gb, paddle.Bounds.TopLeft, paddle.Bounds.BottomRight, Color.White, Color.TransparentWhite, 4
                    );

                Primitives.FilledRing(gb, Ball.Position, 0.0f, Ball.Radius, Color.TransparentWhite, Color.White);
                Primitives.FilledRing(gb, Ball.Position, Ball.Radius - 0.8f, Ball.Radius + 0.8f, Color.White, Color.White);
            }

            using (var sb = StringBatch.New(frame, 4, Materials.ScreenSpaceBitmap, SpriteBatch, Font)) {
                var drawCall = new StringDrawCall(
                    String.Format("Player 1 Score: {0:00}", Scores[0]),
                    new Vector2(16, 16),
                    Color.White
                );

                sb.Add(drawCall.Shadow(Color.Black, 1));
                sb.Add(drawCall);

                drawCall.Text = String.Format("Player 2 Score: {0:00}", Scores[1]);
                drawCall.Position.X = GraphicsDevice.Viewport.Width - 16 - sb.Measure(ref drawCall).X;

                sb.Add(drawCall.Shadow(Color.Black, 1));
                sb.Add(drawCall);
            }
        }
    }

    public class Playfield {
        public Bounds Bounds;
    }

    public class Ball {
        public Playfield Playfield;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius;

        public void Update (Paddle[] paddles) {
            Vector2 surfaceNormal;
            Vector2 orientation = Vector2.Normalize(Velocity);
            var oldEdge = Position + (Radius * orientation);
            var newEdge = oldEdge + Velocity;

            foreach (var poly in new[] { 
                Polygon.FromBounds(paddles[0].Bounds),
                Polygon.FromBounds(paddles[1].Bounds),
                Polygon.FromBounds(Playfield.Bounds),
            }) {
                var intersection = Geometry.LineIntersectPolygon(oldEdge, newEdge, poly, out surfaceNormal);

                if (!intersection.HasValue)
                    continue;

                Velocity = Vector2.Reflect(Velocity, surfaceNormal);

                return;
            }

            Position += Velocity;
        }
    }

    public class Paddle {
        public Playfield Playfield;
        public Bounds Bounds;
        public Vector2 Velocity;

        public void Update () {
            var newBounds = Bounds.Translate(Velocity);

            if (Playfield.Bounds.Contains(newBounds))
                Bounds = newBounds;
        }
    }
}

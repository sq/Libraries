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
using Squared.Render;
using Squared.Render.Convenience;

namespace RenderPrecisionTest {
    public class RenderPrecisionTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 96, 220, 255);

        Texture2D TestTexture;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        public RenderPrecisionTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1280;
            Graphics.PreferredBackBufferHeight = 720;
            Content.RootDirectory = "Content";
        }

        protected override void Initialize () {
            Materials = new DefaultMaterialSet(Services);

            base.Initialize();
        }

        protected override void LoadContent () {
            TestTexture = Content.Load<Texture2D>("test");
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            DrawRow(ref ir, 16f, 16f, SamplerState.PointClamp);
            DrawRow(ref ir, 16.5f, 16 + 64f, SamplerState.PointClamp);
            DrawRow(ref ir, 16f, (16 + 128) + 0.5f, SamplerState.PointClamp);

            DrawRow(ref ir, 16f, 16 + 192f, SamplerState.LinearClamp);
            DrawRow(ref ir, 16.5f, 16 + 256f, SamplerState.LinearClamp);
            DrawRow(ref ir, 16f, (16 + 320) + 0.5f, SamplerState.LinearClamp);
        }

        private void DrawRow (ref ImperativeRenderer ir, float x, float y, SamplerState samplerState) {
            var sourceRect = new Rectangle(1, 1, 30, 30);

            ir.Draw(TestTexture, x, y, sourceRect, samplerState: samplerState);
            x += 64f;
            ir.Draw(TestTexture, x, y, sourceRect, scaleX: 2f, scaleY: 2f, samplerState: samplerState);
            x += 96f;

            for (float r = 0.1f; r < Math.PI / 2f; r += 0.2f) {
                ir.Draw(TestTexture, x, y, sourceRect, rotation: r, samplerState: samplerState);

                x += 64f;
            }
        }
    }
}

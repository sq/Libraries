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
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;

namespace FontTest {
    public class FontTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 36, 40, 255);

        public string TestText =
            "The quick brown fox jumped over the lazy dogs.\r\n" +
            "Long woooooooooooooooooooooooooooooooooooooord\r\n" +
            "a b c d e f g h i j k l m n o p q r s t u v w x y z\r\n" +
            "\r\n\r\nHello";

        SpriteFont Font;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text;

        public Vector2 Margin = new Vector2(24, 24);
        public Vector2? BottomRight;

        public FontTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1024;
            Graphics.PreferredBackBufferHeight = 1024;
            Content.RootDirectory = "Content";
            BottomRight = new Vector2(1024, 1024) - Margin;
            IsMouseVisible = true;
        }

        protected override void Initialize () {
            Materials = new DefaultMaterialSet(Services);

            base.Initialize();
        }

        protected override void LoadContent () {
            Font = Content.Load<SpriteFont>("font");

            Text = new DynamicStringLayout(Font, TestText) {
            };
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);

            var ms = Mouse.GetState();
            if (ms.LeftButton == ButtonState.Pressed)
                BottomRight = new Vector2(ms.X, ms.Y);
            else if (ms.RightButton == ButtonState.Pressed)
                BottomRight = null;
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, blendState: BlendState.AlphaBlend);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            if (BottomRight.HasValue)
                ir.OutlineRectangle(new Bounds(Margin, BottomRight.Value), Color.Red);

            Text.CharacterWrap = true;
            Text.WordWrap = true;
            if (BottomRight.HasValue)
                Text.LineBreakAtX = BottomRight.Value.X - Margin.X;
            else
                Text.LineBreakAtX = null;

            var layout = Text.Get();
            ir.OutlineRectangle(Bounds.FromPositionAndSize(Margin, layout.Size), Color.Yellow);

            ir.DrawMultiple(layout, Margin);
        }
    }
}

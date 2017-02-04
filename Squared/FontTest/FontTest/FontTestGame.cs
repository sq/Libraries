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
using Squared.Util;

namespace FontTest {
    public class FontTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 36, 40, 255);

        public string TestText =
            "The quick brown fox jumped over the lazy dogs.\r\n" +
            "Long woooooooooooooooooooooooord\r\n" +
            "a b c d e f g h i j k l m n o p q r s t u v w x y z";

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

            Text.CharacterWrap = true;
            Text.WordWrap = true;
            if (BottomRight.HasValue)
                Text.LineBreakAtX = BottomRight.Value.X - Margin.X;
            else
                Text.LineBreakAtX = Arithmetic.PulseSine((float)(gameTime.TotalGameTime.TotalSeconds / 14), 0, 1024);

            ir.OutlineRectangle(new Bounds(Margin, new Vector2(Text.LineBreakAtX.Value + Margin.X, 1024 - Margin.Y)), Color.Red);

            var layout = Text.Get();
            var scale = 1024f / layout.Size.Y;
            scale = Arithmetic.Clamp(scale, 0.33f, 1f);

            ir.OutlineRectangle(Bounds.FromPositionAndSize(Margin, layout.Size * scale), Color.Yellow);

            ir.DrawMultiple(layout, Margin, scale: new Vector2(scale));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
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
            "The quick brown fox jumped over the lazy dogs." +
            "\r\nLong woooooooooooooooooooooooord" +
            "\r\nこの体は、無限のチェイサーで出来ていた" +
            "\r\na b c d e f g h i j k l m n o p q r s t u v w x y z" +
            "\r\nはいはい！";

        IGlyphSource SpriteFont, LatinFont, UniFont, FallbackFont;

        IGlyphSource ActiveFont;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text;

        public Vector2 TopLeft = new Vector2(24, 24);
        public Vector2 BottomRight = new Vector2(512, 512);

        PressableKey Alignment = new PressableKey(Keys.A);
        PressableKey CharacterWrap = new PressableKey(Keys.C);
        PressableKey WordWrap = new PressableKey(Keys.W);
        PressableKey FreeType = new PressableKey(Keys.F);

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

            Alignment.Pressed += (s, e) => {
                Text.Alignment = (HorizontalAlignment)(((int)Text.Alignment + 1) % 3);
            };
            CharacterWrap.Pressed += (s, e) => {
                Text.CharacterWrap = !Text.CharacterWrap;
            };
            WordWrap.Pressed += (s, e) => {
                Text.WordWrap = !Text.WordWrap;
            };
            FreeType.Pressed += (s, e) => {
                if (Text.GlyphSource == SpriteFont)
                    Text.GlyphSource = FallbackFont;
                else
                    Text.GlyphSource = SpriteFont;
            };
        }

        protected override void LoadContent () {
            SpriteFont = new SpriteFontGlyphSource(Content.Load<SpriteFont>("font"));
            LatinFont = new FreeTypeFont(RenderCoordinator, "FiraSans-Regular.otf") { SizePoints = 40 };
            UniFont = new FreeTypeFont(RenderCoordinator, @"\Windows\Fonts\ArialUni.ttf") { SizePoints = 20 };
            FallbackFont = new FallbackGlyphSource(LatinFont, UniFont);

            ActiveFont = SpriteFont;

            Text = new DynamicStringLayout(ActiveFont, TestText) {
                // Alignment = HorizontalAlignment.Right,
                CharacterWrap = true,
                WordWrap = true,
                Scale = 0.9f
            };
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);

            if (!IsActive)
                return;

            var ms = Mouse.GetState();
            if (ms.LeftButton == ButtonState.Pressed)
                BottomRight = new Vector2(ms.X, ms.Y);
            else if (ms.RightButton == ButtonState.Pressed)
                TopLeft = new Vector2(ms.X, ms.Y);

            var ks = Keyboard.GetState();
            Alignment.Update(ref ks);
            CharacterWrap.Update(ref ks);
            WordWrap.Update(ref ks);
            FreeType.Update(ref ks);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, blendState: BlendState.AlphaBlend);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            Text.Position = TopLeft;
            Text.LineBreakAtX = BottomRight.X - TopLeft.X;

            ir.OutlineRectangle(new Bounds(TopLeft, BottomRight), Color.Red);

            var layout = Text.Get();

            foreach (var dc in layout.DrawCalls)
                ir.OutlineRectangle(dc.EstimateDrawBounds(), Color.Blue);

            ir.OutlineRectangle(Bounds.FromPositionAndSize(TopLeft, layout.Size), Color.Yellow * 0.75f);

            ir.DrawMultiple(layout);
        }
    }

    public class PressableKey {
        public readonly Keys Key;
        public event EventHandler Pressed;

        private bool previousState;

        public PressableKey (Keys key, EventHandler pressed = null) {
            Key = key;
            Pressed = pressed;
        }

        public void Update (ref KeyboardState ks) {
            var state = ks.IsKeyDown(Key);
            if ((state != previousState) && (Pressed != null) && state)
                Pressed(this, EventArgs.Empty);
            previousState = state;
        }
    }
}

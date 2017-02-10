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
            "The quick brown fox jumped over the lazy dogs" +
            "\r\nこの体は、無限のチェイサーで出来ていた" +
            "\r\n\r\nEmpty line before this one";

        public string TestText2 =
            "\r\na b c d e f g h i j k l m n o p q r s t u v w x y z" +
            "\r\nはいはい！おつかれさまでした！" +
            "\r\n\tIndented\tText";

        IGlyphSource SpriteFont, LatinFont, UniFont, FallbackFont;

        IGlyphSource ActiveFont;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text, Text2;

        public Vector2 TopLeft = new Vector2(24, 24);
        public Vector2 BottomRight = new Vector2(512, 512);

        PressableKey Alignment = new PressableKey(Keys.A);
        PressableKey CharacterWrap = new PressableKey(Keys.C);
        PressableKey WordWrap = new PressableKey(Keys.W);
        PressableKey FreeType = new PressableKey(Keys.F);
        PressableKey ShowOutlines = new PressableKey(Keys.O);
        PressableKey Hinting = new PressableKey(Keys.H);
        PressableKey Which = new PressableKey(Keys.Space);
        PressableKey Margin = new PressableKey(Keys.M);
        PressableKey Indent = new PressableKey(Keys.I);

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
                Text2.Alignment = Text.Alignment = (HorizontalAlignment)(((int)Text.Alignment + 1) % 3);
            };
            CharacterWrap.Pressed += (s, e) => {
                Text2.CharacterWrap = Text.CharacterWrap = !Text.CharacterWrap;
            };
            WordWrap.Pressed += (s, e) => {
                Text2.WordWrap = Text.WordWrap = !Text.WordWrap;
            };
            FreeType.Pressed += (s, e) => {
                if (Text.GlyphSource == SpriteFont)
                    Text2.GlyphSource = Text.GlyphSource = FallbackFont;
                else
                    Text2.GlyphSource = Text.GlyphSource = SpriteFont;
            };
            Hinting.Pressed += (s, e) => {
                var ftf = (FreeTypeFont)LatinFont;
                ftf.Hinting = !ftf.Hinting;
                ftf.Invalidate();
                ftf = (FreeTypeFont)UniFont;
                ftf.Hinting = !ftf.Hinting;
                ftf.Invalidate();
                Text.Invalidate();
                Text2.Invalidate();
            };
            Margin.Pressed += (s, e) => {
                var ftf = (FreeTypeFont)LatinFont;
                ftf.GlyphMargin = (ftf.GlyphMargin + 1) % 6;
                ftf.Invalidate();
                Text.Invalidate();
                Text2.Invalidate();
            };
        }

        protected override void LoadContent () {
            SpriteFont = new SpriteFontGlyphSource(Content.Load<SpriteFont>("font"));
            LatinFont = new FreeTypeFont(RenderCoordinator, "FiraSans-Regular.otf") { SizePoints = 40, DPIPercent = 200 };
            UniFont = new FreeTypeFont(RenderCoordinator, @"\Windows\Fonts\ArialUni.ttf") { SizePoints = 30, DPIPercent = 200 };
            FallbackFont = new FallbackGlyphSource(LatinFont, UniFont);

            ActiveFont = FallbackFont;

            Text = new DynamicStringLayout(ActiveFont, TestText) {
                // Alignment = HorizontalAlignment.Right,
                CharacterWrap = true,
                WordWrap = true,
                Scale = 1.5f
            };
            Text2 = new DynamicStringLayout(ActiveFont, TestText2) {
                CharacterWrap = true,
                WordWrap = true,
                Scale = 1.5f
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
            ShowOutlines.Update(ref ks);
            Hinting.Update(ref ks);
            Which.Update(ref ks);
            Margin.Update(ref ks);
            Indent.Update(ref ks);

            var newSize = Arithmetic.Clamp(20 + (ms.ScrollWheelValue / 56f), 6, 200);
            var font = ((FreeTypeFont)LatinFont);
            if (newSize != font.SizePoints) {
                font.SizePoints = newSize;
                Text.Invalidate();
                Text2.Invalidate();
            }
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, blendState: BlendState.AlphaBlend);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            Text.Position = TopLeft;
            Text2.LineBreakAtX = Text.LineBreakAtX = BottomRight.X - TopLeft.X;
            Text.WrapIndentation = Text2.WrapIndentation = Indent.Value ? 64 : 0;

            ir.OutlineRectangle(new Bounds(TopLeft, BottomRight), Color.Red);

            var layout = Text.Get();

            if (ShowOutlines.Value)
            foreach (var dc in layout.DrawCalls)
                ir.OutlineRectangle(dc.EstimateDrawBounds(), Color.Blue);

            ir.OutlineRectangle(Bounds.FromPositionAndSize(Text.Position, layout.Size), Color.Yellow * 0.75f);
            ir.DrawMultiple(layout);

            if (Which.Value) {
                Text2.Position = TopLeft + new Vector2(0, layout.Size.Y + 20);
                layout = Text2.Get();

                if (ShowOutlines.Value)
                foreach (var dc in layout.DrawCalls)
                    ir.OutlineRectangle(dc.EstimateDrawBounds(), Color.Blue);

                ir.OutlineRectangle(Bounds.FromPositionAndSize(Text2.Position, layout.Size), Color.Yellow * 0.75f);
                ir.DrawMultiple(layout);
            }

        }
    }

    public class PressableKey {
        public bool Value;
        public readonly Keys Key;
        public event EventHandler Pressed;

        private bool previousState;

        public PressableKey (Keys key, EventHandler pressed = null) {
            Key = key;
            Pressed = pressed;
        }

        public void Update (ref KeyboardState ks) {
            var state = ks.IsKeyDown(Key);
            if ((state != previousState) && state) {
                if (Pressed != null)
                    Pressed(this, EventArgs.Empty);
                Value = !Value;
            }
            previousState = state;
        }
    }
}

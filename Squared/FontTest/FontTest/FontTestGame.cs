﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace FontTest {
    public class FontTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 36, 40, 255);

        public string TestText =
            "The $[.quick]quick $[color:brown;scale:2.0;spacing:1.5]b$[scale:1.75]r$[scale:1.5]o$[scale:1.25]w$[scale:1.0]n$[] fox $[font:small]jum$[font:large]ped$[] $[color:#FF00FF]over$[] the lazy dogs" +
            "\r\nこの体は、無限のチェイサーで出来ていた" +
            "\r\n\r\nEmpty line before this one";

        public string TestText2 =
            "\r\na b c d e f g h i j k l m n o p q r s t u v w x y z" +
            "\r\nはいはい！おつかれさまでした！" +
            "\r\n\tIndented\tText";

        IGlyphSource LatinFont, SmallLatinFont, UniFont, FallbackFont;

        IGlyphSource ActiveFont;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text, Text2;

        const float TextScale = 2f;

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
        PressableKey Monochrome = new PressableKey(Keys.R);

        public FontTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1024;
            Graphics.PreferredBackBufferHeight = 1024;
            IsMouseVisible = true;
        }

        protected override void Initialize () {
            base.Initialize();

            Materials = new DefaultMaterialSet(RenderCoordinator);

            Alignment.Pressed += (s, e) => {
                Text2.Alignment = Text.Alignment = (HorizontalAlignment)(((int)Text.Alignment + 1) % 3);
            };
            CharacterWrap.Pressed += (s, e) => {
                Text2.CharacterWrap = Text.CharacterWrap = !Text.CharacterWrap;
            };
            WordWrap.Pressed += (s, e) => {
                Text2.WordWrap = Text.WordWrap = !Text.WordWrap;
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
            Monochrome.Pressed += (s, e) => {
                var ftf = (FreeTypeFont)LatinFont;
                ftf.Monochrome = !ftf.Monochrome;
                ftf.Invalidate();
                ftf = (FreeTypeFont)UniFont;
                ftf.Monochrome = !ftf.Monochrome;
                ftf.Invalidate();
                Text.Invalidate();
                Text2.Invalidate();
            };
        }

        protected override void OnLoadContent (bool isReloading) {
            var margin = 6;
            LatinFont = new FreeTypeFont(RenderCoordinator, "FiraSans-Regular.otf") {
                SizePoints = 40, DPIPercent = 200, GlyphMargin = margin, Gamma = 1.6,
                DefaultGlyphColors = {
                    { (uint)'h', Color.Red }
                }
            };
            if (false)
                LatinFont = new FreeTypeFont(RenderCoordinator, "cambria.ttc") {
                    SizePoints = 40, DPIPercent = 200, GlyphMargin = margin, Gamma = 1.6
                };
            UniFont = new FreeTypeFont(RenderCoordinator, @"C:\Windows\Fonts\msgothic.ttc") {
                SizePoints = 30, DPIPercent = 200, GlyphMargin = margin, Gamma = 1.6
            };
            FallbackFont = new FallbackGlyphSource(LatinFont, UniFont);
            SmallLatinFont = new FreeTypeFont.FontSize((FreeTypeFont)LatinFont, 40 * 0.75f);

            ActiveFont = FallbackFont;

            Text = new DynamicStringLayout(ActiveFont, TestText) {
                // Alignment = HorizontalAlignment.Right,
                AlignToPixels = GlyphPixelAlignment.FloorXY,
                CharacterWrap = true,
                WordWrap = true,
                Scale = TextScale,
                ReverseOrder = true,
                RichText = true,
                HideOverflow = true,
                RichTextConfiguration = new RichTextConfiguration {
                    Styles = new Dictionary<string, RichStyle> {
                        {"quick", new RichStyle { Color = Color.Yellow } },
                        {"brown", new RichStyle { Color = Color.Brown, Scale = 2 } }
                    },
                    GlyphSources = new Dictionary<string, IGlyphSource> {
                        {"large", LatinFont },
                        {"small", SmallLatinFont}
                    }
                }
            };
            Text2 = new DynamicStringLayout(ActiveFont, TestText2) {
                AlignToPixels = GlyphPixelAlignment.FloorXY,
                CharacterWrap = true,
                WordWrap = true,
                Scale = TextScale,
                ReverseOrder = true
            };
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
            Monochrome.Update(ref ks);

            var newSize = Arithmetic.Clamp(20 + (ms.ScrollWheelValue / 56f), 6, 200);
            newSize = Arithmetic.Clamp(9 + (ms.ScrollWheelValue / 100f), 4, 200);
            var font = ((FreeTypeFont)LatinFont);
            var sfont = ((FreeTypeFont.FontSize)SmallLatinFont);
            if (newSize != font.SizePoints) {
                font.SizePoints = newSize;
                sfont.SizePoints = newSize * 0.75f;
                Text.Invalidate();
                Text2.Invalidate();
            }
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, samplerState: SamplerState.LinearClamp);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            Text.Position = TopLeft;
            Text2.LineBreakAtX = Text.LineBreakAtX = BottomRight.X - TopLeft.X;
            Text2.StopAtY = Text.StopAtY = BottomRight.Y - TopLeft.Y;
            Text.WrapIndentation = Text2.WrapIndentation = Indent.Value ? 64 : 0;

            ir.OutlineRectangle(new Bounds(TopLeft, BottomRight), Color.Red);

            var layout = Text.Get();

            if (ShowOutlines.Value)
            foreach (var dc in layout.DrawCalls)
                ir.OutlineRectangle(dc.EstimateDrawBounds(), Color.Blue);

            var m = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            m.Parameters.ShadowColor.SetValue(Color.Red.ToVector4());
            m.Parameters.ShadowOffset.SetValue(new Vector2(1f, 1f));

            ir.OutlineRectangle(Bounds.FromPositionAndSize(Text.Position, layout.Size), Color.Yellow * 0.75f);
            ir.DrawMultiple(layout, material: m, samplerState: RenderStates.Text, userData: new Vector4(0, 0, 0, 0.66f));

            if (Which.Value) {
                Text2.Position = TopLeft + new Vector2(0, layout.Size.Y + 20);
                layout = Text2.Get();

                if (ShowOutlines.Value)
                foreach (var dc in layout.DrawCalls)
                    ir.OutlineRectangle(dc.EstimateDrawBounds(), Color.Blue);

                ir.OutlineRectangle(Bounds.FromPositionAndSize(Text2.Position, layout.Size), Color.Yellow * 0.75f);
                ir.DrawMultiple(layout, material: m, samplerState: RenderStates.Text);
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

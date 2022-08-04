using System;
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
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace FontTest {
    public class FontTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 36, 40, 255);

        public string[] TestStrings = new[] {
            // FIXME: The bounding box for 'dogs' is wrong unless there's a trailing space inside the marked region
            "$[img:topright]$[img:bottomright]The $[.quick]$(quick) $[color:brown;scale:2.0;spacing:1.5]b$[scale:1.75]r$[scale:1.5]o$[scale:1.25]w$[scale:1.0]n$[] $(fox) $[font:small]jum$[font:large]ped$[] $[color:#FF00FF]over$[]$( )$(t)he$( )$(lazy dogs )" +
            "\r\n$[img:bottomleft]$[img:left]この体は、無限のチェイサーで出来ていた $(marked)" +
            "\r\n\r\nEmpty line before this one $(marked)\r\n$(rich substring)",

            "\r\na b c d e f g h i j k l m n o p q r s t u v w x y z" +
            "\r\nはいはい！おつかれさまでした！" +
            "\r\n\tIndented\tText",

            "The quick brown fox jumped over the lazy dogs. Sphinx of black quartz, judge my vow. Welcome to the circus, " +
            "we've got fun and games, here's\\a\\very-long-path\\without-spaces\\that-should-get-broken\\ok",

            "This line ends with a very long string of characters: asmfkjalshasklmrasklrjhalksrmjaslkaslrklsmrs\n\n" + 
            "Then is followed by a line break and short lines.\n" +
            "The word-wrap of the long string should produce a small bounding box.",

            "$(Airburst Shot)\n.1  $(Ammo) x 1  Cooldown: 1\n" +
            "Fire a $(Piercing Round) overhead to $(Ambush) all foes (damage decreases based on number of targets).\n" +
            "\t$(Ambush) target for $(75%) piercing Physical damage."
        };

        SpriteFont DutchAndHarley;

        IGlyphSource LatinFont, SmallLatinFont, UniFont, FallbackFont;

        IGlyphSource ActiveFont;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text;
        int StringIndex;
        string SelectedString => TestStrings[StringIndex];

        float TextScale = 2f;

        public Vector2 TopLeft = new Vector2(24, 24);
        public Vector2 BottomRight = new Vector2(1012, 512);

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
        PressableKey Expand = new PressableKey(Keys.E);
        PressableKey LimitExpansion = new PressableKey(Keys.X);

        Texture2D[] Images = new Texture2D[4];

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
                Text.Alignment = (HorizontalAlignment)(((int)Text.Alignment + 1) % 7);
            };
            CharacterWrap.Pressed += (s, e) => {
                Text.CharacterWrap = !Text.CharacterWrap;
            };
            WordWrap.Pressed += (s, e) => {
                Text.WordWrap = !Text.WordWrap;
            };
            Hinting.Pressed += (s, e) => {
                var ftf = (FreeTypeFont)LatinFont;
                ftf.Hinting = !ftf.Hinting;
                ftf.Invalidate();
                ftf = (FreeTypeFont)UniFont;
                ftf.Hinting = !ftf.Hinting;
                ftf.Invalidate();
                Text.Invalidate();
            };
            Margin.Pressed += (s, e) => {
                var ftf = (FreeTypeFont)LatinFont;
                ftf.GlyphMargin = (ftf.GlyphMargin + 1) % 6;
                ftf.Invalidate();
                Text.Invalidate();
            };
            Monochrome.Pressed += (s, e) => {
                var ftf = (FreeTypeFont)LatinFont;
                ftf.Monochrome = !ftf.Monochrome;
                ftf.Invalidate();
                ftf = (FreeTypeFont)UniFont;
                ftf.Monochrome = !ftf.Monochrome;
                ftf.Invalidate();
                Text.Invalidate();
            };
            FreeType.Pressed += (s, e) => {
                if (ActiveFont == FallbackFont) {
                    ActiveFont = new SpriteFontGlyphSource(DutchAndHarley);
                    TextScale = 1f;
                } else {
                    ActiveFont = FallbackFont;
                    TextScale = 2f;
                }
                Text.GlyphSource = ActiveFont;
                Text.Scale = TextScale;
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

            Content.RootDirectory = "";
            DutchAndHarley = Content.Load<SpriteFont>("DutchAndHarley");

            Text = new DynamicStringLayout(ActiveFont, SelectedString) {
                AlignToPixels = GlyphPixelAlignment.RoundXY,
                CharacterWrap = true,
                WordWrap = true,
                Scale = TextScale,
                ReverseOrder = true,
                RichText = true,
                HideOverflow = true,
                RichTextConfiguration = new RichTextConfiguration {
                    MarkedStringProcessor = ProcessMarkedString,
                    Styles = new Dictionary<ImmutableAbstractString, RichStyle> {
                        {"quick", new RichStyle { Color = Color.Yellow } },
                        {"brown", new RichStyle { Color = Color.Brown, Scale = 2 } }
                    },
                    GlyphSources = new Dictionary<ImmutableAbstractString, IGlyphSource> {
                        {"large", LatinFont },
                        {"small", SmallLatinFont}
                    },
                    ImageProvider = Text_ImageProvider 
                },
                WordWrapCharacters = new uint[] {
                    '\\', '/', ':', ','
                },
            };

            for (int i = 0; i < Images.Length; i++)
                using (var s = File.OpenRead($"{i + 1}.png"))
                    Images[i] = Texture2D.FromStream(Graphics.GraphicsDevice, s);
        }

        private AsyncRichImage Text_ImageProvider (AbstractString arg) {
            int i;
            float x;
            float? y;
            if (arg == "img:left") {
                x = 0f;
                y = null;
                i = 0;
            } else if (arg == "img:bottomleft") {
                x = 0f;
                y = 1f;
                i = 3;
            } else if (arg == "img:bottomright") {
                x = 1f;
                y = 1f;
                i = 1;
            } else if (arg == "img:topright") {
                x = 1f;
                y = 0f;
                i = 2;
            } else
                return default;
            var tex = Images[i];
            var ri = new RichImage {
                Texture = tex,
                HardHorizontalAlignment = x,
                HardVerticalAlignment = y,
                DoNotAdjustLineSpacing = true,
                Margin = Vector2.One * 3f,
                CreateBox = true,
                VerticalAlignment = y ?? 0f
            };
            return new AsyncRichImage(ref ri);
        }

        private MarkedStringAction ProcessMarkedString (ref AbstractString text, string id, ref RichTextLayoutState state, ref StringLayoutEngine layoutEngine) {
            if (text.TextEquals("quick")) {
                layoutEngine.overrideColor = Color.GreenYellow;
                text = "slow";
            } else if (text.TextEquals("rich substring")) {
                text = "<$[scale:2.0]b$[scale:1.66]i$[scale:1.33]g$[scale:1.0] rich substring>";
                return MarkedStringAction.RichText;
            }
            return default;
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);

            if (!IsActive)
                return;

            var ms = Mouse.GetState();
            var mousePos = new Vector2(ms.X, ms.Y);
            if (
                (mousePos.X >= 0) && (mousePos.Y >= 0)
            ) {
                if (ms.LeftButton == ButtonState.Pressed)
                    BottomRight = mousePos;
                else if (ms.RightButton == ButtonState.Pressed)
                    TopLeft = mousePos;
            }

            var ks = Keyboard.GetState();
            for (int i = 0; i < TestStrings.Length; i++) {
                var k = Keys.D1 + i;
                if (ks.IsKeyDown(k))
                    StringIndex = i;
            }

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
            Expand.Update(ref ks);
            LimitExpansion.Update(ref ks);

            if (!Text.Text.TextEquals(SelectedString)) {
                Text.Text = SelectedString;
                Text.Invalidate();
            }

            var newSize = Arithmetic.Clamp(20 + (ms.ScrollWheelValue / 56f), 6, 200);
            newSize = Arithmetic.Clamp(9 + (ms.ScrollWheelValue / 100f), 4, 200);
            var font = ((FreeTypeFont)LatinFont);
            var sfont = ((FreeTypeFont.FontSize)SmallLatinFont);
            if (newSize != font.SizePoints) {
                font.SizePoints = newSize;
                sfont.SizePoints = newSize * 0.75f;
                Text.Invalidate();
            }

            Text.Invalidate();
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, samplerState: SamplerState.LinearClamp);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            Text.Position = TopLeft;
            var targetX = BottomRight.X - TopLeft.X;
            Text.ExpandHorizontallyWhenAligning = !Expand.Value;
            Text.LineBreakAtX = targetX;
            Text.DesiredWidth = Expand.Value ? targetX : 0;
            Text.MaxExpansion = LimitExpansion.Value ? 100 : (float?)null;
            Text.StopAtY = BottomRight.Y - TopLeft.Y;
            Text.WrapIndentation = Indent.Value ? 64 : 0;

            ir.OutlineRectangle(new Bounds(TopLeft, BottomRight), Color.Red);

            var layout = Text.Get();

            foreach (var rm in Text.RichMarkers) {
                // Console.WriteLine(rm);
                if (!rm.FirstDrawCallIndex.HasValue)
                    continue;
                layout.DrawCalls.Array[layout.DrawCalls.Offset + rm.FirstDrawCallIndex.Value].Color = Color.Purple;
                layout.DrawCalls.Array[layout.DrawCalls.Offset + rm.LastDrawCallIndex.Value].Color = Color.Purple;
            }

            if (ShowOutlines.Value)
            foreach (var dc in layout.DrawCalls)
                ir.OutlineRectangle(dc.EstimateDrawBounds(), Color.Blue);

            var m = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            m.Parameters.ShadowColor.SetValue(Color.Red.ToVector4());
            m.Parameters.ShadowOffset.SetValue(new Vector2(1f, 1f));

            ir.OutlineRectangle(Bounds.FromPositionAndSize(Text.Position, layout.Size), Color.Yellow * 0.75f);
            ir.DrawMultiple(layout, material: m, blendState: BlendState.NonPremultiplied, samplerState: RenderStates.Text, userData: new Vector4(0, 0, 0, 0.66f));

            foreach (var b in Text.Boxes) {
                ir.OutlineRectangle(b, Color.Orange);
            }

            foreach (var rm in Text.RichMarkers) {
                foreach (var b in rm.Bounds)
                    ir.OutlineRectangle(b, Color.Green);
            }

            var state = $"align {Text.Alignment} char-wrap {Text.CharacterWrap} word-wrap {Text.WordWrap} expand {Expand.Value}";
            var stateLayout = Text.GlyphSource.LayoutString(state);
            ir.DrawMultiple(stateLayout, new Vector2(0, 1024 - stateLayout.UnconstrainedSize.Y));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Render.TextLayout2;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace FontTest {
    public class FontTestGame : MultithreadedGame, IStringLayoutListener {
        public static readonly Color ClearColor = new Color(24, 36, 40, 255);

        SpriteFont DutchAndHarley;
        List<IGlyphSource> Fonts = new ();
        FallbackGlyphSource FallbackFont;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        DynamicStringLayout Text;
        int StringIndex;
        string SelectedString => TestStrings[StringIndex];

        const int DPIFactor = 2;
        float TextScale = DPIFactor;

        public Vector2 TopLeft = new Vector2(24, 24);
        public Vector2 BottomRight = new Vector2(1012, 512);

        PressableKey Alignment = new PressableKey(Keys.A);
        PressableKey CharacterWrap = new PressableKey(Keys.C);
        PressableKey WordWrap = new PressableKey(Keys.W);
        PressableKey FreeType = new PressableKey(Keys.F);
        PressableKey ShowOutlines = new PressableKey(Keys.O);
        PressableKey Hinting = new PressableKey(Keys.H);
        PressableKey Which = new PressableKey(Keys.Space);
        PressableKey MeasureOnly = new PressableKey(Keys.M);
        PressableKey Indent = new PressableKey(Keys.I);
        PressableKey Monochrome = new PressableKey(Keys.R);
        PressableKey Expand = new PressableKey(Keys.E);
        PressableKey LimitExpansion = new PressableKey(Keys.X);
        PressableKey Kerning = new PressableKey(Keys.K);
        PressableKey Ligatures = new PressableKey(Keys.L);
        PressableKey HideOverflow = new PressableKey(Keys.D);

        Texture2D[] Images = new Texture2D[5];
        List<(Bounds bounds, Vector2 margin)> Boxes = new ();
        List<Bounds> Lines = new ();

        public FontTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1024;
            Graphics.PreferredBackBufferHeight = 1024;
            Graphics.SynchronizeWithVerticalRetrace = false;
            IsMouseVisible = true;
        }

        protected override void Initialize () {
            base.Initialize();

            IsFixedTimeStep = false;

            Materials = new DefaultMaterialSet(RenderCoordinator);

            Alignment.Pressed += (s, e) => {
                Text.Alignment = (HorizontalAlignment)(((int)Text.Alignment + 1) % 6);
            };
            CharacterWrap.Pressed += (s, e) => {
                Text.CharacterWrap = !Text.CharacterWrap;
            };
            WordWrap.Pressed += (s, e) => {
                Text.WordWrap = !Text.WordWrap;
            };
            Hinting.Pressed += (s, e) => {
                foreach (var font in Fonts) {
                    if (font is FreeTypeFont ftf) {
                        ftf.Hinting = !ftf.Hinting;
                        ftf.Invalidate();
                    }
                }
            };
            MeasureOnly.Pressed += (s, e) => {
                Text.MeasureOnly = !Text.MeasureOnly;
                Text.Invalidate();
            };
            Monochrome.Pressed += (s, e) => {
                foreach (var font in Fonts) {
                    if (font is FreeTypeFont ftf) {
                        ftf.Monochrome = !ftf.Monochrome;
                        ftf.Invalidate();
                    }
                }
            };
            FreeType.Pressed += (s, e) => {
                if (Text.GlyphSource == FallbackFont) {
                    Text.GlyphSource = new SpriteFontGlyphSource(DutchAndHarley);
                    TextScale = 1f;
                } else {
                    Text.GlyphSource = FallbackFont;
                    TextScale = DPIFactor;
                }
                Text.Scale = TextScale;
            };
            Kerning.Pressed += (s, e) => {
                foreach (var font in Fonts) {
                    if (font is FreeTypeFont ftf) {
                        ftf.EnableKerning = !ftf.EnableKerning;
                        ftf.Invalidate();
                    }
                }
            };            
            Ligatures.Pressed += (s, e) => {
                foreach (var font in Fonts) {
                    if (font is FreeTypeFont ftf) {
                        ftf.EnableLigatures = !ftf.EnableLigatures;
                        ftf.Invalidate();
                    }
                }
            };
            HideOverflow.Pressed += (s, e) => {
                Text.HideOverflow = !Text.HideOverflow;
                Text.Invalidate();
            };
        }

        protected FreeTypeFont LoadFont (string path, float sizePoints) {
            var margin = 6;
            var result = new FreeTypeFont(RenderCoordinator, path) {
                DPIPercent = 100 * DPIFactor,
                GlyphMargin = margin,
                Gamma = 1.6f,
                DefaultGlyphColors = {
                    { 'h', Color.Red }
                },
                EqualizeNumberWidths = true,
                SizePoints = sizePoints
            };
            Fonts.Add(result);
            return result;
        }

        protected override void OnLoadContent (bool isReloading) {
            var latinFont = LoadFont("FiraSans-Regular.otf", 40f);
            var kanjiFont = LoadFont(@"C:\Windows\Fonts\msgothic.ttc", 30f);
            var smallLatinFont = new FreeTypeFont.FontSize(latinFont, 40 * 0.75f);
            Fonts.Add(smallLatinFont);
            var arabicFont = LoadFont("NotoSansArabic-Regular.otf", 40f);
            var hebrewFont = LoadFont("NotoSansHebrew-Regular.otf", 40f);
            FallbackFont = new FallbackGlyphSource(Fonts.ToArray());

            Content.RootDirectory = "";
            DutchAndHarley = Content.Load<SpriteFont>("DutchAndHarley");

            Text = new DynamicStringLayout(FallbackFont, SelectedString) {
                AlignToPixels = GlyphPixelAlignment.RoundXY,
                CharacterWrap = true,
                WordWrap = true,
                Scale = TextScale,
                ReverseOrder = true,
                RichText = true,
                HideOverflow = false,
                IncludeTrailingWhitespace = true,
                RichTextConfiguration = new RichTextConfiguration {
                    MarkedStringProcessor = ProcessMarkedString,
                    Styles = new ImmutableAbstractStringLookup<RichStyle> {
                        {"quick", new RichStyle { Color = Color.Yellow } },
                        {"brown", new RichStyle { Color = Color.Brown, Scale = 2 } }
                    },
                    GlyphSources = new RichTextConfiguration.GlyphSourceCollection {
                        {"large", latinFont },
                        {"small", smallLatinFont }
                    },
                    ImageProvider = Text_ImageProvider 
                },
                WordWrapCharacters = new uint[] {
                    '\\', '/',
                },
                Listener = this,
                // ExtraLineBreakSpacing = -12f,
            };

            for (int i = 0; i < Images.Length; i++)
                using (var s = File.OpenRead($"{i + 1}.png"))
                    Images[i] = Texture2D.FromStream(Graphics.GraphicsDevice, s);
        }

        private AsyncRichImage Text_ImageProvider (AbstractString arg, RichTextConfiguration config) {
            int i;
            var x = ImageHorizontalAlignment.Inline;
            float s = 0.5f, m = 16f;
            float? y = null;
            if (arg == "img:left") {
                x = ImageHorizontalAlignment.Left;
                i = 0;
            } else if (arg == "img:bottomleft") {
                x = ImageHorizontalAlignment.Left;
                i = 3;
            } else if (arg == "img:bottomright") {
                x = ImageHorizontalAlignment.Right;
                i = 1;
            } else if (arg == "img:topright") {
                x = ImageHorizontalAlignment.Right;
                i = 2;
            } else if (arg.StartsWith("img:inline@")) {
                int offset = arg.IndexOf("@");
                x = ImageHorizontalAlignment.Inline;
                arg.Substring(offset + 1).TryParse(out float f);
                y = f;
                s = 0.33f;
                i = 4;
                m = 8f;
            } else
                return default;
            var tex = Images[i];
            var ri = new RichImage {
                Texture = tex,
                HorizontalAlignment = x,
                BaselineAlignment = y,
                DoNotAdjustLineSpacing = (x != ImageHorizontalAlignment.Inline),
                Margin = Vector2.One * m,
                Scale = s
            };
            return new AsyncRichImage(ref ri);
        }

        private MarkedStringConfiguration ProcessMarkedString (
            ref AbstractString text, ref AbstractString id, 
            ref RichTextLayoutState state, ref StringLayoutEngine2 layoutEngine
        ) {
            if (text.TextEquals("quick")) {
                layoutEngine.OverrideColor = true;
                layoutEngine.MultiplyColor = Color.GreenYellow;
                text = "slow";
            } else if (text.TextEquals("rich substring")) {
                text = "<$[scale:2.0]b$[scale:1.66]i$[scale:1.33]g$[scale:1.0] rich substring>";
                return MarkedStringAction.RichText;
            } else if (text.TextEquals("anchor string", StringComparison.OrdinalIgnoreCase)) {
                return MarkedStringConfiguration.Ruby("ruby text");
            } else if (text.TextEquals("anchor", StringComparison.OrdinalIgnoreCase)) {
                return MarkedStringConfiguration.Ruby("very long ruby text");
            } else if (text.TextEquals("無限")) {
                return MarkedStringConfiguration.Ruby("むげん");
            } else if (text.TextEquals("出来")) {
                return MarkedStringConfiguration.Ruby("でき");
            } else if (text.TextEquals("体")) {
                return MarkedStringConfiguration.Ruby("からだ");
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
            MeasureOnly.Update(ref ks);
            Indent.Update(ref ks);
            Monochrome.Update(ref ks);
            Expand.Update(ref ks);
            LimitExpansion.Update(ref ks);
            Kerning.Update(ref ks);
            Ligatures.Update(ref ks);
            HideOverflow.Update(ref ks);

            if (!Text.Text.TextEquals(SelectedString)) {
                Text.Text = SelectedString;
                Text.Invalidate();
            }

            var newSize = Arithmetic.Clamp(20 + (ms.ScrollWheelValue / 56f), 6, 200);
            newSize = Arithmetic.Clamp(9 + (ms.ScrollWheelValue / 100f), 4, 200);
            foreach (var font in Fonts) {
                if (font is FreeTypeFont ftf)
                    ftf.SizePoints = newSize;
                else if (font is FreeTypeFont.FontSize fs)
                    fs.SizePoints = newSize * 0.66f;
            }

            // HACK: Override line spacing so that empty lines have the spacing of the latin font,
            //  not the larger spacing of the unicode font
            ((FallbackGlyphSource)FallbackFont).OverrideLineSpacing = Fonts[0].LineSpacing;

            Text.Invalidate();
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var ir = new ImperativeRenderer(frame, Materials, samplerState: SamplerState.LinearClamp);
            ir.AutoIncrementLayer = true;

            ir.Clear(color: ClearColor);

            Text.Position = TopLeft;
            var targetX = BottomRight.X - TopLeft.X;
            Text.ExpandHorizontallyWhenAligning = Expand.Value;
            Text.LineBreakAtX = targetX;
            Text.DesiredWidth = Expand.Value ? targetX : 0;
            Text.MaxExpansionPerSpace = LimitExpansion.Value ? 16 : (float?)null;
            Text.StopAtY = Text.HideOverflow ? BottomRight.Y - TopLeft.Y : null;
            Text.WrapIndentation = Indent.Value ? 64 : 0;
            Text.XOffsetOfFirstLine = Indent.Value ? 16 : 0;
            Text.XOffsetOfNewLine = Indent.Value ? 32 : 0;

            ir.RasterizeRectangle(TopLeft, BottomRight, 0f, 1f, Color.Transparent, Color.Transparent, Color.Red);

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

            var m = Materials.Get(Materials.ShadowedBitmap, blendState: BlendState.AlphaBlend);
            m.Parameters.ShadowColor.SetValue(Color.Red.ToVector4());
            m.Parameters.ShadowOffset.SetValue(new Vector2(1f, 1f));

            ir.RasterizeRectangle(Text.Position, Text.Position + layout.Size, 0f, 1f, Color.Transparent, Color.Transparent,Color.Yellow * 0.75f);
            ir.RasterizeRectangle(Text.Position, Text.Position + layout.UnconstrainedSize, 0f, 1f, Color.Transparent, Color.Transparent,Color.Blue * 0.75f);

            var l1 = ir.Layer++;
            ir.DrawMultiple(layout, material: m, blendState: BlendState.NonPremultiplied, samplerState: RenderStates.Text, userData: new Vector4(0, 0, 0, 0.66f));

            if (true) {
                for (int i = 0; i < Text.Boxes.Count; i++) {
                    var b = Text.Boxes[i];
                    var margin = Boxes[i].margin;
                    ir.RasterizeRectangle(b.TopLeft, b.BottomRight, 0f, 1f, Color.Transparent, Color.Transparent, Color.Orange);
                    ir.RasterizeRectangle(b.TopLeft - margin, b.BottomRight + margin, 0f, 1f, Color.Transparent, Color.Transparent, Color.Orange * 0.5f);
                }

                foreach (var line in Lines)
                    ir.RasterizeRectangle(line.TopLeft, line.BottomRight, 1f, Color.Black * 0.5f, layer: l1);

                foreach (var rm in Text.RichMarkers) {
                    foreach (var b in rm.Bounds)
                        ir.RasterizeRectangle(b.TopLeft, b.BottomRight, 0f, 1f, Color.Transparent, Color.Transparent, Color.Green);
                }
            }

            var state = $"a:{Text.Alignment} cw:{Text.CharacterWrap} ww:{Text.WordWrap} ex:{Expand.Value} hint:{Hinting.Value} kern:{Kerning.Value} lig:{Ligatures.Value}";
            var stateLayout = Text.GlyphSource.LayoutString(state);
            ir.DrawMultiple(stateLayout, new Vector2(0, 1024 - stateLayout.UnconstrainedSize.Y));
        }

        void IStringLayoutListener.Initializing (ref StringLayoutEngine2 engine) {
        }

        void IStringLayoutListener.RecordTexture (ref StringLayoutEngine2 engine, AbstractTextureReference texture) {
        }

        void IStringLayoutListener.Finishing (ref StringLayoutEngine2 engine) {
        }

        void IStringLayoutListener.Error (ref StringLayoutEngine2 engine, string message) {
        }

        void IStringLayoutListener.Finished (ref StringLayoutEngine2 engine, ref StringLayout result) {
            Boxes.Clear();
            for (int i = 0; i < engine.BoxCount; i++) {
                if (engine.TryGetBox((uint)i, out var bounds, out var margin))
                    Boxes.Add((bounds, margin));
            }

            Lines.Clear();
            for (int i = 0; i < engine.LineCount; i++) {
                if (engine.TryGetLineBounds((uint)i, out var lineBounds))
                    Lines.Add(lineBounds);
            }
        }

        public string[] TestStrings = new[] {
            "$<img:left>$<img:topright>The $[.quick]$(quick) $[color:brown;scale:2.0;spacing:1.5]b$[scale:1.75]r$[scale:1.5]o$[scale:1.25]w$[scale:1.0]n$[] $(fox) $[font:small]jum$[font:large]ped$[] $[color:#FF00FF]over$[]$( )$(t)he$( )$(lazy dogs)" +
            "\r\nこの$(体)は、$(無限)のチェイサーで$(出来)ていた $(marked)" +
            "\r\nThis fish flowed off the office desk looking baffled." +
            "\r\nTesting$<img:inline@0.0>Baseline$<img:inline@0.5>Alignment$<img:inline@1.0>" +
            "\r\n\r\nEmpty line before this one $(marked)\r\n$<img:bottomleft>$<img:bottomright>$(rich substring)",

            "\r\na b c d e f g h i j k l m n o p q r s t u v w x y z" +
            "\r\nはいはい！おつかれさまでした！" +
            "\r\n\tIndented\tText",

            "The quick brown fox jumped over the lazy dogs. Sphinx of black quartz, judge my vow. Welcome to the circus, " +
            "we've got fun and games, here's\\a\\very-long-path\\without-spaces\\that-should-get-broken\\ok",

            // Test "bad unicode string" that contains lots of problem characters
            "表ポあA鷗ŒéＢ逍Üßªąñ丂㐀𠀀\n" +
            // Test unicode string that mixes arabic, latin and hebrew
            // NOTE: For the next line a true bidi shaping algorithm would do automatic LTR<->RTL transitions. We don't.
            "$(\u200Fהָיְתָה\u200E) $(test) $(\u200Fالصفحات التّحول\u200E)\n" +
            // Test RTL push/pop
            "rtl w/ltr embed:\u2067$(test) \u2066$(test)\u2069 $(test)\u2069\n",

            "C̀́̂̃omb̀́̂̃inì́̂̃ng̠̤̥̀́̂̃ c̀́̂̃har̀́̂̃a̠̤̥ct̀́̂̃er̠̤̥s̀́̂̃ à́̂̃re̠̤̥ pret̀́̂̃ty̠̤̥ Ć̀̂o̤̥ol,\ndon't y̠̤̥ou think?\n" +
            "Ruby: $(Anchor String) <- should wrap as one box\n" +
            "$(anchor)",

            "$(Airburst Shot)\n.1  $(Ammo) x 1  Cooldown: 1\n" +
            "Fire a $(Piercing Round) overhead to $(Ambush) all foes (damage decreases based on number of targets).\n" +
            "\t$(Ambush) target for $(75%) piercing Physical damage.",
            
            "Test$( )Abc\n" +
            "Test $(A)bc\n" +
            "Test A$(b)c\n" +
            "Test Ab$(c)\n" +
            "Test Test A$(bc)d\n" +
            "Test Test Test $(Abcd)",

            "{Paradise Isle}",

            @"In Congress, July 4, 1776

The unanimous Declaration of the thirteen united States of America, When in the Course of human events, it becomes necessary for one people to dissolve the political bands which have connected them with another, and to assume among the powers of the earth, the separate and equal station to which the Laws of Nature and of Nature's God entitle them, a decent respect to the opinions of mankind requires that they should declare the causes which impel them to the separation.

We hold these truths to be self-evident, that all men are created equal, that they are endowed by their Creator with certain unalienable Rights, that among these are Life, Liberty and the pursuit of Happiness.--That to secure these rights, Governments are instituted among Men, deriving their just powers from the consent of the governed, --That whenever any Form of Government becomes destructive of these ends, it is the Right of the People to alter or to abolish it, and to institute new Government, laying its foundation on such principles and organizing its powers in such form, as to them shall seem most likely to effect their Safety and Happiness. Prudence, indeed, will dictate that Governments long established should not be changed for light and transient causes; and accordingly all experience hath shewn, that mankind are more disposed to suffer, while evils are sufferable, than to right themselves by abolishing the forms to which they are accustomed. But when a long train of abuses and usurpations, pursuing invariably the same Object evinces a design to reduce them under absolute Despotism, it is their right, it is their duty, to throw off such Government, and to provide new Guards for their future security.--Such has been the patient sufferance of these Colonies; and such is now the necessity which constrains them to alter their former Systems of Government. The history of the present King of Great Britain is a history of repeated injuries and usurpations, all having in direct object the establishment of an absolute Tyranny over these States. To prove this, let Facts be submitted to a candid world.

He has refused his Assent to Laws, the most wholesome and necessary for the public good.

He has forbidden his Governors to pass Laws of immediate and pressing importance, unless suspended in their operation till his Assent should be obtained; and when so suspended, he has utterly neglected to attend to them.

He has refused to pass other Laws for the accommodation of large districts of people, unless those people would relinquish the right of Representation in the Legislature, a right inestimable to them and formidable to tyrants only.

He has called together legislative bodies at places unusual, uncomfortable, and distant from the depository of their public Records, for the sole purpose of fatiguing them into compliance with his measures.

He has dissolved Representative Houses repeatedly, for opposing with manly firmness his invasions on the rights of the people.

He has refused for a long time, after such dissolutions, to cause others to be elected; whereby the Legislative powers, incapable of Annihilation, have returned to the People at large for their exercise; the State remaining in the mean time exposed to all the dangers of invasion from without, and convulsions within.

He has endeavoured to prevent the population of these States; for that purpose obstructing the Laws for Naturalization of Foreigners; refusing to pass others to encourage their migrations hither, and raising the conditions of new Appropriations of Lands.

He has obstructed the Administration of Justice, by refusing his Assent to Laws for establishing Judiciary powers.

He has made Judges dependent on his Will alone, for the tenure of their offices, and the amount and payment of their salaries.

He has erected a multitude of New Offices, and sent hither swarms of Officers to harrass our people, and eat out their substance.

He has kept among us, in times of peace, Standing Armies without the Consent of our legislatures.

He has affected to render the Military independent of and superior to the Civil power.

He has combined with others to subject us to a jurisdiction foreign to our constitution, and unacknowledged by our laws; giving his Assent to their Acts of pretended Legislation:

For Quartering large bodies of armed troops among us:

For protecting them, by a mock Trial, from punishment for any Murders which they should commit on the Inhabitants of these States:

For cutting off our Trade with all parts of the world:

For imposing Taxes on us without our Consent:

For depriving us in many cases, of the benefits of Trial by Jury:

For transporting us beyond Seas to be tried for pretended offences

For abolishing the free System of English Laws in a neighbouring Province, establishing therein an Arbitrary government, and enlarging its Boundaries so as to render it at once an example and fit instrument for introducing the same absolute rule into these Colonies:

For taking away our Charters, abolishing our most valuable Laws, and altering fundamentally the Forms of our Governments:

For suspending our own Legislatures, and declaring themselves invested with power to legislate for us in all cases whatsoever.

He has abdicated Government here, by declaring us out of his Protection and waging War against us.

He has plundered our seas, ravaged our Coasts, burnt our towns, and destroyed the lives of our people.

He is at this time transporting large Armies of foreign Mercenaries to compleat the works of death, desolation and tyranny, already begun with circumstances of Cruelty & perfidy scarcely paralleled in the most barbarous ages, and totally unworthy the Head of a civilized nation.

He has constrained our fellow Citizens taken Captive on the high Seas to bear Arms against their Country, to become the executioners of their friends and Brethren, or to fall themselves by their Hands.

He has excited domestic insurrections amongst us, and has endeavoured to bring on the inhabitants of our frontiers, the merciless Indian Savages, whose known rule of warfare, is an undistinguished destruction of all ages, sexes and conditions.

In every stage of these Oppressions We have Petitioned for Redress in the most humble terms: Our repeated Petitions have been answered only by repeated injury. A Prince whose character is thus marked by every act which may define a Tyrant, is unfit to be the ruler of a free people.

Nor have We been wanting in attentions to our Brittish brethren. We have warned them from time to time of attempts by their legislature to extend an unwarrantable jurisdiction over us. We have reminded them of the circumstances of our emigration and settlement here. We have appealed to their native justice and magnanimity, and we have conjured them by the ties of our common kindred to disavow these usurpations, which, would inevitably interrupt our connections and correspondence. They too have been deaf to the voice of justice and of consanguinity. We must, therefore, acquiesce in the necessity, which denounces our Separation, and hold them, as we hold the rest of mankind, Enemies in War, in Peace Friends.

We, therefore, the Representatives of the united States of America, in General Congress, Assembled, appealing to the Supreme Judge of the world for the rectitude of our intentions, do, in the Name, and by Authority of the good People of these Colonies, solemnly publish and declare, That these United Colonies are, and of Right ought to be Free and Independent States; that they are Absolved from all Allegiance to the British Crown, and that all political connection between them and the State of Great Britain, is and ought to be totally dissolved; and that as Free and Independent States, they have full Power to levy War, conclude Peace, contract Alliances, establish Commerce, and to do all other Acts and Things which Independent States may of right do. And for the support of this Declaration, with a firm reliance on the protection of divine Providence, we mutually pledge to each other our Lives, our Fortunes and our sacred Honor."
        };
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

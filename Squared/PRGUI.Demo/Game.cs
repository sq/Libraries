using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Input;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Task;
using Squared.Util;
using Squared.Util.Text;

namespace PRGUI.Demo {
    public class DemoGame : MultithreadedGame {
        // ABXY
        public const string ButtonChars = "";

        public TaskScheduler Scheduler;
        public UIContext Context;

        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }

        public EmbeddedTexture2DProvider TextureLoader { get; private set; }
        public EmbeddedFreeTypeFontProvider FontLoader { get; private set; }

        public KeyboardInputSource Keyboard = new KeyboardInputSource();
        public MouseInputSource Mouse = new MouseInputSource();
        public GamepadVirtualKeyboardAndCursor GamePad = new GamepadVirtualKeyboardAndCursor();

        public Material TextMaterial { get; private set; }

        public IGlyphSource Font;
        public AutoRenderTarget UIRenderTarget;

        Button LoginButton;

        public const float DPIFactor = 0.5f;

        public bool IsMouseOverUI = false, TearingTest = false;
        public long LastTimeOverUI;
        private bool IsFirstUpdate = true, IsFirstDraw = true;
        private int UpdatesToSkip = 0, DrawsToSkip = 0;

        public const SurfaceFormat RenderTargetFormat = SurfaceFormat.Color;

        public DefaultDecorations Decorations;

        // public ControlKey MasterList, ContentView;

        public DemoGame () {
            // UniformBinding.ForceCompatibilityMode = true;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Graphics.PreferredBackBufferFormat = RenderTargetFormat;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
#if DEBUG
            Graphics.SynchronizeWithVerticalRetrace = true;
#else
            Graphics.SynchronizeWithVerticalRetrace = false;
#endif
            Graphics.PreferMultiSampling = false;
            Graphics.IsFullScreen = false;

            Content.RootDirectory = "Content";

            IsFixedTimeStep = false;

            if (IsFixedTimeStep) {
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 2f);
                GamePad.FixedTimeStep = 1.0f / 60f;
            }

            InputID.GamePadButtonLabels = new Dictionary<Buttons, string> {
                { Buttons.A, "" },
                { Buttons.B, "" },
                { Buttons.X, "" },
                { Buttons.Y, "" },
                { Buttons.LeftShoulder, "" },
                { Buttons.RightShoulder, "" },
                { Buttons.DPadLeft, "" },
                { Buttons.DPadRight, "" },
                { Buttons.DPadUp, "" },
                { Buttons.DPadDown, "" },
                { Buttons.Back, "" },
                { Buttons.Start, "" },
                { Buttons.LeftStick, "" },
                { Buttons.RightStick, "" },
            };

            Scheduler = new TaskScheduler(JobQueue.WindowsMessageBased);
        }

        private FreeTypeFont LoadFont (string name) {
            var result = FontLoader.Load(name);
            result.sRGB = false;
            result.Gamma = 2.2f;
            // High-DPI offscreen surface so the text is sharp even at subpixel positions
            result.DPIPercent = (int)(100f / DPIFactor);
            // Big margin on glyphs so shadows aren't clipped
            result.GlyphMargin = 4;
            // Enable mips for soft shadows
            result.MipMapping = true;
            return result;
        }

        private Color FilterButtonColor (Color c) {
            return Color.Lerp(c, Color.White, 0.3f);
        }

        private FreeTypeFont FiraSans,
            NotoSans,
            Kenney;
        private FreeTypeFont.FontSize TitleFont, TooltipFont;

        private void SetFontScale (float fontScale) {
            FiraSans.SizePoints = 20f * fontScale;
            NotoSans.SizePoints = 16f * fontScale;
            // FIXME: The chinese glyphs are below the baseline, but with the recent baseline changes
            //  the japanese glyphs no longer need an offset
            // NotoSans.VerticalOffset = 0f * fontScale;
            Kenney.SizePoints = 17f * fontScale;
            Kenney.VerticalOffset = 2 * fontScale;
            Kenney.ExtraLineSpacing = 3 * fontScale;
            TitleFont.SizePoints = 16f * fontScale;
            TooltipFont.SizePoints = 14f * fontScale;
        }

        protected override void OnLoadContent (bool isReloading) {
            RenderCoordinator.EnableThreading = false;

            TextureLoader = new EmbeddedTexture2DProvider(RenderCoordinator) {
                DefaultOptions = new TextureLoadOptions {
                    Premultiply = true,
                    GenerateMips = true
                }
            };
            FontLoader = new EmbeddedFreeTypeFontProvider(RenderCoordinator);

            float fontScale = 1.2f;
            FiraSans = LoadFont("FiraSans-Medium");
            NotoSans = LoadFont("NotoSansCJKjp-Regular");
            Kenney = LoadFont("kenney-icon-font");

            TitleFont = new FreeTypeFont.FontSize(FiraSans, 14f * fontScale);
            TooltipFont = new FreeTypeFont.FontSize(FiraSans, 14f * fontScale);
            var tooltipFont = new FallbackGlyphSource(
                TooltipFont, Kenney
            );

            Kenney.DefaultGlyphColors = new Dictionary<uint, Color> {
                { ButtonChars[0], FilterButtonColor(Color.Green) },
                { ButtonChars[1], FilterButtonColor(Color.DarkRed) },
                { ButtonChars[2], FilterButtonColor(Color.Blue) },
                { ButtonChars[3], FilterButtonColor(Color.Yellow) }
            };

            SetFontScale(fontScale);

            Font = new FallbackGlyphSource(FiraSans, NotoSans, Kenney);

            Materials = new DefaultMaterialSet(RenderCoordinator);

            TextMaterial = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.66f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 1.75f * DPIFactor);
            TextMaterial.Parameters.ShadowMipBias.SetValue(1.33f);

            Decorations = new Squared.PRGUI.DefaultDecorations(Materials) {
                DefaultFont = Font,
                TitleFont = TitleFont,
                TooltipFont = tooltipFont,
                AcceleratorFont = tooltipFont,
            };

            UIRenderTarget = new AutoRenderTarget(
                RenderCoordinator,
                Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, RenderTargetFormat, DepthFormat.Depth24Stencil8, 1
            );

            Context = new UIContext(Materials, Decorations) {
                InputSources = {
                    Mouse, Keyboard, GamePad
                },
                RichTextConfiguration = {
                    Images = new Dictionary<string, RichImage> {
                        {
                            "ghost", new RichImage {
                                Texture = TextureLoader.Load("ghost"),
                                Scale = 0.4f,
                                Margin = new Vector2(0, -2)
                            }
                        }
                    }
                },
                SurfaceFormat = RenderTargetFormat,
                AllowNullFocus = false
            };

            LastTimeOverUI = Time.Ticks;

            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += Window_ClientSizeChanged;
            Window_ClientSizeChanged(null, EventArgs.Empty);

            BuildUI();
        }

        private void BuildUI () {
            var hoveringCtl = new StaticText {
                LayoutFlags = ControlFlags.Layout_Fill,
                AutoSize = false,
                Wrap = false,
                Text = "Hovering: None",
                TooltipContent = "The control the mouse is currently hovering over"
            };

            var lastClickedCtl = new StaticText {
                LayoutFlags = ControlFlags.Layout_Fill,
                AutoSize = false,
                Wrap = false,
                Text = "",
                TooltipContent = "The control most recently clicked with the mouse"
            };

            var focusedCtl = new StaticText {
                LayoutFlags = ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak,
                AutoSize = false,
                Wrap = false,
                Text = "",
                TooltipContent = "The control with keyboard focus"
            };

            var capturedCtl = new StaticText {
                LayoutFlags = ControlFlags.Layout_Fill,
                AutoSize = false,
                Wrap = false,
                Text = "",
                TooltipContent = "The control with mouse capture"
            };

            var testString = "Καλημέρα こんにちは \U0002F8B6\U0002F8CD\U0002F8D3 Hello";

            var textfield = new EditableText {
                Text = testString,
                Appearance = {
                    BackgroundColor = new Color(40, 56, 60),
                },
                // FIXME: This should be at least partially automatic
                Width = { Minimum = 400 },
                Selection = new Pair<int>(1, testString.Length - 4),
                ScrollOffset = new Vector2(128, 32),
                Description = "Message"
            };

            var numberField = new ParameterEditor<double> {
                Appearance = {
                    BackgroundColor = new Color(40, 56, 60),
                },
                LayoutFlags = ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak,
                // MinimumWidth = 200,
                Description = "A number",
                HorizontalAlignment = HorizontalAlignment.Right,
                Minimum = -10,
                Maximum = 1000,
                Value = 73.50,
                ValueFilter = (d) => Math.Round(d, 2, MidpointRounding.AwayFromZero),
                Exponent = 2,
                Increment = 5,
                FastIncrementRate = 5
            };

            var hideButton = new Button {
                Text = "$[ghost] Hide",
                Margins = default(Margins),
                LayoutFlags = ControlFlags.Layout_Anchor_Right | ControlFlags.Layout_Fill_Column | ControlFlags.Layout_ForceBreak,
                Appearance = {
                    BackgroundColor = new Color(128, 16, 16),
                },
                // TextAlignment = HorizontalAlignment.Right,
                TooltipContent = "Hide this window temporarily"
            };

            var toppleButton = new Button {
                Text = "T$[scale:0.9]o$[scale:0.8]p$[scale:0.7]p$[scale:0.6]l$[scale:0.5]e",
                TooltipContent = "I'm a top-heavy window!"
            };

            LoginButton = new Button {
                Text = "Login"
            };

            var volumeSlider = new Slider {
                LayoutFlags = ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak,
                Value = 80,
                NotchInterval = 25f,
                NotchMagnetism = 2.99f,
                Integral = true,
                SnapToNotch = true,
                TooltipFormat = "{3:P0}",
                TooltipContent = "Volume",
                Appearance = {
                    BackgroundImage = TextureLoader.Load("volume")
                },
            };

            var window = new ContainerBuilder(new Window {
                Appearance = {
                    BackgroundColor = new Color(70, 86, 90),
                    Compositor = new WindowCompositor(Materials)
                },
                Title = "Floating Panel",
                DisplayOrder = 1,
                Collapsible = true,
                AllowMaximize = true
            });

            window.New<StaticText>()
                .SetText("→")
                .SetFocusBeneficiary(textfield)
                .SetTooltip("Clicking this label will focus the textfield");

            window.AddRange(
                textfield,
                numberField,
                volumeSlider,
                // FIXME: If we don't group these into a container, the last two get vertically centered.
                // Is that right? It might be
                new ControlGroup (forceBreak: true) {
                    hideButton,
                    toppleButton,
                    LoginButton
                },
                // FIXME: We need this spacer to fill the empty space at the bottom of the window when it's maximized.
                // Should it really work this way?
                new Spacer {
                    ForceBreak = true
                }
            );

            FloatingWindow = (Window)window.Container;

            var changePaintOrder = new Button {
                // MinimumWidth = 400,
                Text = "Change Paint Order",
                Appearance = {
                    BackgroundColor = Color.LightSeaGreen,
                },
                TooltipContent = "This button toggles whether the floating panel is above or below the main container"
            };

            var testMenu = new Menu {
                DynamicContents = BuildTestMenu,
                TooltipContent = "Surprise! I'm a pop-up menu!",
                CloseWhenItemChosen = false
            };

            var button1 = new Button {
                AutoSizeWidth = false,
                Width = { Fixed = 220 },
                Text = "Button 1",
                TooltipContent = "Click me for a surprise!",
                Menu = testMenu
            };

            const int menuItemCount = 50;
            const int itemCount = 2000;

            var dropdown = new Dropdown<StaticText> {
                Label = "Dropdown: {0}",
                TooltipContent = "Click me for a big dropdown menu"
            };
            for (var i = 0; i < menuItemCount; i++)
                dropdown.Items.Add(new StaticText { Text = $"Item {i}", TooltipContent = $"Item {i} tooltip" });

            var virtualCheckbox = new Checkbox {
                Text = "Virtual List",
                Checked = true
            };
            var columnCount = new Dropdown<int> {
                Label = "Columns: {0}",
                Items = { 1, 2, 3, 4 },
                SelectedItem = 1
            };
            var listBox = new ListBox<string> {
                ForceBreak = true,
                Description = "Big List",
                Width = { Fixed = 600 },
                Height = { Fixed = 600 },
                Virtual = virtualCheckbox.Checked,
                ColumnCount = columnCount.SelectedItem
            };
            for (var i = 0; i < itemCount; i++)
                listBox.Items.Add($"# {i}");

            var supernestedGroup = new ControlGroup {
                LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak,
                DynamicContents = BuildSupernestedGroup
            };

            var increaseGaugeButton = new Button {
                Text = "Increase Gauge Value",
                LayoutFlags = ControlFlags.Layout_ForceBreak | ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top,
                Width = { Fixed = 450 },
                AutoSizeWidth = false,
                EnableRepeat = true
            };

            var gauge = new Gauge {
                Description = "Test Gauge",
                LayoutFlags = ControlFlags.Layout_ForceBreak,
                Width = { Fixed = 450 }
            };

            var scrollableClipTest = new Container {
                LayoutFlags = ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_Anchor_Top,
                ContainerFlags = ControlFlags.Container_Wrap | ControlFlags.Container_Row,
                Children = {
                    new Container {
                        ClipChildren = true,
                        LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top,
                        Height = { Maximum = 500 },
                        Width = { Fixed = 450 },
                        Scrollable = true,
                        Children = {
                            new StaticText { Text = "Testing nested clips" },
                            new StaticText {
                                Text = "Long multiline static text inside of clipped region that should be wrapped/clipped instead of overflowing",
                                Wrap = true, AutoSizeWidth = false, LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak
                            },
                            new Checkbox { Text = "Checkbox 1", LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak },
                            new Checkbox { Text = "Checkbox 2", Checked = true },
                            new RadioButton { Text = "Radio 1", GroupId = "radio", LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak, Checked = true },
                            new RadioButton { Text = "Radio 2", GroupId = "radio" },
                            new RadioButton { Text = "Radio 3", GroupId = "radio", Checked = true },
                            supernestedGroup
                        }
                    },
                    increaseGaugeButton,
                    gauge
                }
            };

            var listboxContainer = new ControlGroup {
                LayoutFlags = ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_Anchor_Top,
                Children = {
                    virtualCheckbox,
                    columnCount,
                    listBox
                }
            };

            var tabs = new TabContainer {
                { scrollableClipTest, "Scrollable" },
                { listboxContainer, "Listbox" }
            };
            tabs.LayoutFlags = ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_Anchor_Top;

            var bigScrollableContainer = new Container {
                ClipChildren = true,
                LayoutFlags = ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak,
                Height = { Maximum = 1200 },
                Scrollable = true,
                ShowHorizontalScrollbar = true,
                ShowVerticalScrollbar = true,
                ScrollOffset = new Vector2(0, 22),
                Children = {
                    // FIXME: This should probably expand to the full width of the container's content, instead of the width of the container as it does now
                    new StaticText {
                        Text = "Clipped container",
                        AutoSizeWidth = false,
                        Appearance = {
                            BackgroundColor = new Color(32, 60, 32),
                        }
                    },
                    new ControlGroup {
                        LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak,
                        ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Wrap | ControlFlags.Container_Align_Start,
                        Children = {
                            new Button {
                                Text = "Clipped huge button\r\nSecond line\r\n" + ButtonChars,
                                Width = { Fixed = 600 },
                                Height = { Fixed = 1800 },
                                LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_Anchor_Top | ControlFlags.Layout_ForceBreak
                            },
                            tabs
                        }
                    },
                    new Button {
                        LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak,
                        Text = "Another button at the bottom to test clipped hit tests"
                    }
                }
            };

            var largeText = new Checkbox {
                Text = "Big Text",
                LayoutFlags = ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_ForceBreak,
            };

            var fastAnimations = new Checkbox {
                Text = "Fast Fades"
            };

            var readAloud = new Checkbox {
                Text = "Narrate",
            };

            var readingSpeed = new Slider {
                TooltipContent = "Speed",
                Minimum = 0,
                Maximum = 7,
                Value = 0,
                Integral = true,
                Width = { Maximum = 150 },
                TooltipFormat = "{0}"
            };

            var topLevelContainer = new Container {
                Appearance = {
                    BackgroundColor = new Color(60, 60, 60) * 0.9f,
                },
                LayoutFlags = ControlFlags.Layout_Fill,
                // FIXME: We shouldn't need to set Wrap here since we're setting explicit breaks
                ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Align_End | ControlFlags.Container_Wrap | ControlFlags.Container_Constrain_Size,
                Children = {
                    hoveringCtl,
                    lastClickedCtl,
                    button1,
                    focusedCtl,
                    capturedCtl,
                    new StaticText {
                        Text = "A Button:",
                        TooltipContent = "Nice label.\r\nThis label has a very long tooltip that has embedded line breaks and is just generally long, so that it will get word wrapped and stuff, testing the layout constraints for tooltips",
                        FocusBeneficiary = changePaintOrder
                    },
                    changePaintOrder,
                    largeText,
                    fastAnimations,
                    readAloud,
                    readingSpeed,
                    new Spacer (),
                    new Button {
                        Width = { Minimum = 200 },
                        Text = "Disabled Button",
                        Enabled = false,
                        LayoutFlags = ControlFlags.Layout_Fill_Row,
                        Appearance = {
                            BackgroundColor = Color.LightPink
                        }
                    },
                    dropdown,
                    new StaticText {
                        AutoSize = false,
                        Text = "Static Text 2\r\nLine 2",
                        LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak,
                        Width = { Maximum = 130 },
                        Height = { Minimum = Font.LineSpacing + Context.Decorations.StaticText.Padding.Y },
                        Multiline = true,
                        Wrap = false,
                        Appearance = {
                            BackgroundColor = Color.DarkRed,
                        },
                        ScaleToFit = true
                    },
                    new StaticText {
                        AutoSizeWidth = false,
                        Text = "Static Text 3",
                        TextAlignment = HorizontalAlignment.Right,
                        Appearance = {
                            BackgroundColor = Tween.StartNow(Color.DarkGreen, Color.DarkRed, 1f, repeatCount: int.MaxValue, repeatMode: TweenRepeatMode.Pulse)
                        }
                    },
                    new StaticText {
                        Text = "Static Text 4",
                        Width = { Minimum = 300 },
                        AutoSizeWidth = true,
                        Appearance = {
                            BackgroundColor = Color.DarkBlue,
                            TextColor = Tween.StartNow(Color.White, Color.White * 0.5f, 2f, repeatCount: int.MaxValue, repeatMode: TweenRepeatMode.PulseExp)
                        }
                    },
                    bigScrollableContainer
                }
            };

            Context.EventBus.Subscribe(columnCount, UIEvents.ValueChanged, (ei) => {
                listBox.ColumnCount = columnCount.SelectedItem;
            });

            Context.EventBus.Subscribe(null, UIEvents.LostFocus, (ei) => {
                focusedCtl.Text = "Focused: " + ei.Arguments;
            });

            Context.EventBus.Subscribe(null, UIEvents.GotFocus, (ei) => {
                focusedCtl.Text = "Focused: " + ei.Source;
            });

            Context.EventBus.Subscribe(null, UIEvents.MouseEnter, (ei) => {
                hoveringCtl.Text = "Hovering: " + ei.Source;
            });

            Context.EventBus.Subscribe(null, UIEvents.MouseLeave, (ei) => {
                hoveringCtl.Text = "Hovering: " + ei.Arguments;
            });

            Context.EventBus.Subscribe(null, UIEvents.MouseCaptureChanged, (ei) => {
                if (ei.Source != Control.None)
                    capturedCtl.Text = "Captured: " + ei.Source;
                else
                    capturedCtl.Text = "";
            });

            Context.EventBus.Subscribe(null, UIEvents.Click, (ei) => {
                var ma = (MouseEventArgs)ei.Arguments;
                lastClickedCtl.Text = $"Clicked (#{ma.SequentialClickCount}): {ei.Source}";
            });

            Context.EventBus.Subscribe(virtualCheckbox, UIEvents.CheckedChanged, (ei) => {
                listBox.Virtual = virtualCheckbox.Checked;
            });

            Context.EventBus.Subscribe(largeText, UIEvents.CheckedChanged, (ei) => {
                Decorations.SizeScaleRatio = new Vector2(
                    largeText.Checked ? 1.1f : 1.0f,
                    largeText.Checked ? 1.045f : 1.0f
                );
                SetFontScale(largeText.Checked ? 1.75f : 1.1f);
            });

            Context.EventBus.Subscribe(fastAnimations, UIEvents.CheckedChanged, (ei) => {
                Decorations.AnimationDurationMultiplier = fastAnimations.Checked ? 0.25f : 1f;
            });

            Context.EventBus.Subscribe(readAloud, UIEvents.CheckedChanged, (ei) => {
                Context.ReadAloudOnFocus = readAloud.Checked;
                Context.ReadAloudOnClickIfNotFocusable = readAloud.Checked;
                Context.ReadAloudOnValueChange = readAloud.Checked;
                Context.TTS.Stop();
                Context.TTS.Speak($"Reading {(readAloud.Checked ? "Enabled" : "Disabled")}");
            });

            Context.EventBus.Subscribe(volumeSlider, UIEvents.ValueChanged, (ei) => {
                Context.TTS.Volume = (int)Math.Round(volumeSlider.Value, MidpointRounding.AwayFromZero);
            });

            Context.EventBus.Subscribe(readingSpeed, UIEvents.ValueChanged, (ei) => {
                Context.TTSDescriptionReadingSpeed = (int)readingSpeed.Value;
                Context.TTSValueReadingSpeed = (int)readingSpeed.Value + 2;
            });

            Context.EventBus.Subscribe(changePaintOrder, UIEvents.Click, (ei) => {
                FloatingWindow.DisplayOrder = -FloatingWindow.DisplayOrder;
            });

            Context.EventBus.Subscribe(hideButton, UIEvents.Click, (ei) => {
                FloatingWindow.Intangible = true;
                FloatingWindow.Appearance.Opacity = Tween.StartNow(1f, 0f, seconds: 1, now: Context.NowL);
            });

            Context.EventBus.Subscribe(increaseGaugeButton, UIEvents.Click, (ei) => {
                if (gauge.Value >= 0.999)
                    gauge.Value = 0;
                else
                    gauge.Value += 0.025f;
            });

            Context.EventBus.Subscribe(toppleButton, UIEvents.Click, (ei) => {
                var wbox = FloatingWindow.GetRect();
                var sz = wbox.Size;
                float o = wbox.Width / 2f;
                Weird2DTransform(
                    sz, new Vector2(0, 0), new Vector2(sz.X - o, 0), new Vector2(0, sz.Y), sz, out Matrix temp
                );

                FloatingWindow.Appearance.Transform =
                    Tween.StartNow(
                        Matrix.Identity, temp,
                        seconds: 5f, now: Context.NowL,
                        repeatCount: 1, repeatMode: TweenRepeatMode.PulseSine
                    );
            });

            Context.EventBus.Subscribe(LoginButton, UIEvents.Click, (ei) => {
                ShowLoginWindow();
            });

            Context.EventBus.Subscribe(FloatingWindow, UIEvents.OpacityTweenEnded, (ei) => {
                if (FloatingWindow.Appearance.Opacity.To >= 1)
                    return;

                Context.Controls.Remove(FloatingWindow);

                var f = Scheduler.Start(new Sleep(1f));

                f.RegisterOnComplete((_) => {
                    FloatingWindow.Appearance.Opacity = Tween.StartNow(0f, 1f, seconds: 0.25f, delay: 1f, now: Context.NowL);
                    FloatingWindow.Intangible = false;
                    Context.Controls.Add(FloatingWindow);
                });
            });

            Context.Controls.Add(topLevelContainer);
            Context.Controls.Add(window.Control);
        }

        private void BuildLoginWindow (ref ContainerBuilder builder) {
            var window = (ModalDialog)builder.Control;

            builder.New<EditableText>()
                .SetForceBreak(true)
                .SetDescription("Username")
                .SetBackgroundColor(new Color(40, 56, 60))
                .SetMinimumSize(width: 400)
                .GetText(out AbstractString username);
            builder.New<EditableText>()
                .SetForceBreak(true)
                .SetDescription("Password")
                .SetPassword(true)
                .SetBackgroundColor(new Color(40, 56, 60));

            window.AcceptControl = 
                builder.Text<Button>("OK")
                .SetForceBreak(true);
            window.CancelControl =
                builder.Text<Button>("Cancel");

            if (builder.GetEvent(UIEvents.Click, out Button button))
                window.Close(
                    button == window.AcceptControl 
                    ? username.ToString()
                    : null);
        }

        private void ShowLoginWindow () {
            var dialog = new ModalDialog {
                Title = "Login",
                Appearance = {
                    BackgroundColor = new Color(70, 86, 90),
                    Transform = Tween.StartNow(
                        Matrix.CreateScale(0f),
                        Matrix.Identity, seconds: 0.15f * Decorations.AnimationDurationMultiplier, now: Context.NowL
                    )
                },
                DynamicContents = BuildLoginWindow,
                ContainerFlags = ControlFlags.Container_Align_Middle | ControlFlags.Container_Wrap | ControlFlags.Container_Row,
            };
            var fUsername = (dialog).Show(Context, LoginButton);
            LoginButton.Appearance.Overlay = true;
            fUsername.RegisterOnComplete((f) => {
                LoginButton.Appearance.Overlay = false;
                Console.WriteLine(
                    (f.Result != null)
                        ? "Login with username " + fUsername.Result
                        : "Login cancelled"
                );
            });
        }

        private void BuildTestMenu (ref ContainerBuilder builder) {
            builder.Text("Item 1").SetBackgroundColor(Color.Red);
            builder.Text("Item 2");
            builder.Text("Extremely long menu item with a bunch of text in it. This thing should be truncated pretty aggressively")
                .SetTooltip("This menu item has a custom tooltip")
                .SetBackgroundColor(Color.Blue)
                .SetWrap(false);
            var c = builder.NewContainer();
            c.Text("Item 4A");
            c.Text("Item 4B");
        }

        private void BuildSupernestedGroup (ref ContainerBuilder builder) {
            builder.Text<Checkbox>("Checkbox 3");
            builder.Text<Checkbox>("Checkbox 4")
                .SetDecorator(Decorations.Button);
            builder.Text<Checkbox>("Checkbox 5")
                .SetDecorator(Decorations.RadioButton);
            builder.Text<Checkbox>("Checkbox 6");
            builder.Text<Checkbox>("Checkbox 7");

            var tc = builder.NewContainer<TitledContainer>();
            tc.Properties
                .SetLayoutFlags(ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak)
                .SetCollapsible(true)
                .SetTitle("Test");
            tc.Text<Button>("Button A");
            tc.Text<Button>("Button B");

            var tc2 = builder.NewContainer<TitledContainer>();
            tc2.Properties
                .SetLayoutFlags(ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak)
                // .SetFixedSize(null, 432)
                .SetCollapsible(true);
            tc2.Text<Button>("Button C");
            tc2.Text<Button>("Button D");
            tc2.Text<Button>("Button E");
            tc2.Text<Button>("Button F");
            tc2.Text<Button>("Button G");
        }

        private void Window_ClientSizeChanged (object sender, EventArgs e) {
            var pp = GraphicsDevice.PresentationParameters;
            RenderCoordinator.WaitForActiveDraws();
            Materials.ViewTransform = ViewTransform.CreateOrthographic(pp.BackBufferWidth, pp.BackBufferHeight);
            Context.CanvasSize = new Vector2(pp.BackBufferWidth, pp.BackBufferHeight);
            Context.Update();
            UIRenderTarget.Resize(pp.BackBufferWidth, pp.BackBufferHeight);
        }

        protected override void OnUnloadContent () {
            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);
        }

        private void SetActiveScene (int index) {
            RenderCoordinator.WaitForActiveDraws();
        }

        private readonly List<double> DrawHistory = new List<double>(),
            WaitHistory = new List<double>();

        protected override void Update (GameTime gameTime) {
            var started = Time.Ticks;

            Context.UpdateInput(IsActive);

            if (IsFirstUpdate || (UpdatesToSkip <= 0)) {
                IsFirstUpdate = false;
                Context.Update();
            } else 
                UpdatesToSkip--;

            IsMouseVisible = !IsActive || (Context.InputSources.IndexOf(Mouse) == 0);

            if (Context.IsActive)
                LastTimeOverUI = Time.Ticks;

            var ks = Keyboard.CurrentState;
            var pks = Keyboard.PreviousState;

            if (IsActive) {
                var alt = ks.IsKeyDown(Keys.LeftAlt) || ks.IsKeyDown(Keys.RightAlt);
                var wasAlt = pks.IsKeyDown(Keys.LeftAlt) || pks.IsKeyDown(Keys.RightAlt);

                if (ks.IsKeyDown(Keys.OemTilde) && !pks.IsKeyDown(Keys.OemTilde)) {
                    Graphics.SynchronizeWithVerticalRetrace = !Graphics.SynchronizeWithVerticalRetrace;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                } else if (
                    (ks.IsKeyDown(Keys.Enter) && alt) &&
                    (!pks.IsKeyDown(Keys.Enter) || !wasAlt)
                ) {
                    Graphics.IsFullScreen = !Graphics.IsFullScreen;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                } else if (ks.IsKeyDown(Keys.OemPipe) && !pks.IsKeyDown(Keys.OemPipe)) {
                    UniformBinding.ForceCompatibilityMode = !UniformBinding.ForceCompatibilityMode;
                }
            }

            base.Update(gameTime);

            var ended = Time.Ticks;
            PerformanceStats.Record(this.PreviousFrameTiming, ended - started);
        }

        private void Weird2DTransform (
            Vector2 size,
            Vector2 topLeft, Vector2 topRight,
            Vector2 bottomLeft, Vector2 bottomRight,
            out Matrix result
        ) {
            const float x = 0, y = 0;
            float width = size.X, height = size.Y;

            float x1a = topLeft.X,     y1a = topLeft.Y;
            float x2a = topRight.X,    y2a = topRight.Y;
            float x3a = bottomLeft.X,  y3a = bottomLeft.Y;
            float x4a = bottomRight.X, y4a = bottomRight.Y;

            float y21 = y2a - y1a;
            float y32 = y3a - y2a;
            float y43 = y4a - y3a;
            float y14 = y1a - y4a;
            float y31 = y3a - y1a;
            float y42 = y4a - y2a;

            float a = -height*(x2a*x3a*y14 + x2a*x4a*y31 - x1a*x4a*y32 + x1a*x3a*y42);
            float b = width*(x2a*x3a*y14 + x3a*x4a*y21 + x1a*x4a*y32 + x1a*x2a*y43);
            float c = height*x*(x2a*x3a*y14 + x2a*x4a*y31 - x1a*x4a*y32 + x1a*x3a*y42) - height*width*x1a*(x4a*y32 - x3a*y42 + x2a*y43) - width*y*(x2a*x3a*y14 + x3a*x4a*y21 + x1a*x4a*y32 + x1a*x2a*y43);

            float d = height*(-x4a*y21*y3a + x2a*y1a*y43 - x1a*y2a*y43 - x3a*y1a*y4a + x3a*y2a*y4a);
            float e = width*(x4a*y2a*y31 - x3a*y1a*y42 - x2a*y31*y4a + x1a*y3a*y42);
            float f = -(width*(x4a*(y*y2a*y31 + height*y1a*y32) - x3a*(height + y)*y1a*y42 + height*x2a*y1a*y43 + x2a*y*(y1a - y3a)*y4a + x1a*y*y3a*(-y2a + y4a)) - height*x*(x4a*y21*y3a - x2a*y1a*y43 + x3a*(y1a - y2a)*y4a + x1a*y2a*(-y3a + y4a)));

            float g = height*(x3a*y21 - x4a*y21 + (-x1a + x2a)*y43);
            float h = width*(-x2a*y31 + x4a*y31 + (x1a - x3a)*y42);
            float i = width*y*(x2a*y31 - x4a*y31 - x1a*y42 + x3a*y42) + height*(x*(-(x3a*y21) + x4a*y21 + x1a*y43 - x2a*y43) + width*(-(x3a*y2a) + x4a*y2a + x2a*y3a - x4a*y3a - x2a*y4a + x3a*y4a));

            const double kEpsilon = 0.0001;

            if (Math.Abs(i) < kEpsilon)
                i = (float)(kEpsilon * (i > 0 ? 1.0 : -1.0));

            result = new Matrix(
                a/i, d/i, 0, g/i, 
                b/i, e/i, 0, h/i, 
                0, 0, 1, 0, 
                c/i, f/i, 0, 1f
            );
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            var pp = GraphicsDevice.PresentationParameters;
            if (
                (pp.BackBufferWidth != UIRenderTarget.Width) ||
                (pp.BackBufferHeight != UIRenderTarget.Height)
            ) {
                Window_ClientSizeChanged(null, EventArgs.Empty);
            }

            if (IsFirstDraw || (DrawsToSkip <= 0)) {
                IsFirstDraw = false;
                Context.Rasterize(frame, UIRenderTarget, -9990);
            } else
                DrawsToSkip--;

            var ir = new ImperativeRenderer(frame, Materials);
            ir.Clear(color: Color.Transparent);
            ir.Layer += 1;

            /*
            var hoveringControl = Context.HitTest(new Vector2(MouseState.X, MouseState.Y), false);
            if (hoveringControl != null) {
                var hoveringBox = hoveringControl.GetRect(Context.Layout);

                if (false)
                    ir.RasterizeRectangle(
                        hoveringBox.Position, hoveringBox.Extent,
                        innerColor: new Color(64, 64, 64), outerColor: Color.Black, radius: 4f,
                        fillMode: RasterFillMode.Angular, fillOffset: (float)(Time.Seconds / 6),
                        fillSize: -0.2f, fillAngle: 55,
                        annularRadius: 1.75f, outlineRadius: 0f, outlineColor: Color.Transparent,
                        blendState: BlendState.Additive, blendInLinearSpace: false
                    );
            }
            */

            var elapsedSeconds = TimeSpan.FromTicks(Time.Ticks - LastTimeOverUI).TotalSeconds;
            float uiOpacity = Arithmetic.Lerp(1.0f, 0.66f, (float)((elapsedSeconds - 1.5) * 2.25f));

            ir.Draw(UIRenderTarget, Vector2.Zero, multiplyColor: Color.White * uiOpacity);

            DrawPerformanceStats(ref ir);

            if (TearingTest) {
                var x = (Time.Ticks / 20000) % Graphics.PreferredBackBufferWidth;
                ir.FillRectangle(Bounds.FromPositionAndSize(
                    x, 0, 6, Graphics.PreferredBackBufferHeight
                ), Color.Red);
            }
        }

        private int LastPerformanceStatPrimCount;
        private Window FloatingWindow;

        private void DrawPerformanceStats (ref ImperativeRenderer ir) {
            // return;

            const float scale = 0.5f;
            var text = PerformanceStats.GetText(-LastPerformanceStatPrimCount);
            text.Append(Context.LastPassCount);
            text.AppendLine(" passes");

            var s = (AbstractString)text;
            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(s.Length)) {
                var layout = Font.LayoutString(s, buffer, scale: scale);
                var layoutSize = layout.Size;
                var position = new Vector2(Window.ClientBounds.Width - (240 * scale), Window.ClientBounds.Height - (240 * scale)).Floor();
                var dc = layout.DrawCalls;

                // fill quad + text quads
                LastPerformanceStatPrimCount = (layout.Count * 2) + 2;

                ir.RasterizeRectangle(
                    position, position + layoutSize,
                    8, Color.Black * 0.4f, Color.Black * 0.4f
                );
                ir.Layer += 1;
                ir.DrawMultiple(dc, position, material: Materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend);
            }
        }
    }

    public class WindowCompositor : IControlCompositor {
        public DefaultMaterialSet Materials;
        public Material Material;

        public WindowCompositor (DefaultMaterialSet materials) {
            Materials = materials;
            Material = Materials.Get(Materials.WorldSpaceRadialGaussianBlur, blendState: RenderStates.PorterDuffOver);
        }

        public void AfterIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall) {
        }

        public void BeforeIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall) {
            var opacity = drawCall.MultiplyColor.A / 255.0f;
            var sigma = Arithmetic.Lerp(0f, 4f, 1.0f - opacity) + 1 + ((control.Context.TopLevelFocused != control) ? 1 : 0);
            Materials.SetGaussianBlurParameters(Material, sigma, 7, 0);
        }

        public void Composite (Control control, ref ImperativeRenderer renderer, ref BitmapDrawCall drawCall) {
            var opacity = drawCall.MultiplyColor.A / 255.0f;
            if ((opacity >= 1) && (control.Context.TopLevelFocused == control))
                renderer.Draw(ref drawCall, blendState: RenderStates.PorterDuffOver);
            else
                renderer.Draw(ref drawCall, material: Material, blendState: RenderStates.PorterDuffOver);
        }

        public bool WillComposite (Control control, float opacity) {
            return (opacity < 1) || (control.Context.TopLevelFocused != control);
        }
    }
}

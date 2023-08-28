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
using Squared.Util.Event;
using Squared.Util.Text;

namespace PRGUI.Demo {
    public class DemoGame : MultithreadedGame {
        static string SavedTreePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "prgui.xml");
        bool UseSavedTree = true;
        ControlKey? HighlightRecord = null;

        // ABXY
        public const string ButtonChars = "";

        public TaskScheduler Scheduler;
        public UIContext Context;

        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }

        public Texture2DProvider TextureLoader { get; private set; }
        public FreeTypeFontProvider FontLoader { get; private set; }

        public KeyboardInputSource Keyboard = new KeyboardInputSource();
        public MouseInputSource Mouse = new MouseInputSource();
        public GamepadVirtualKeyboardAndCursor GamePad = new GamepadVirtualKeyboardAndCursor();

        public Material TextMaterial { get; private set; }
        public Material SelectedTextMaterial { get; private set; }

        StaticText DynamicStaticText, SpinningText;
        StaticImage SpinningImage;

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
            Graphics.PreferredBackBufferHeight = UseSavedTree ? 1440 : 1080;
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
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 4f);
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
            result.EqualizeNumberWidths = true;
            result.sRGB = false;
            // result.Gamma = 2.2f;
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
            // HACK: On my machine this makes release mode faster, but that's probably just because
            //  at 1ms/frame the overhead of the thread scheduling swamps any advantages from parallelism
            //  and the video driver is multithreaded anyway
            // RenderCoordinator.EnableThreading = false;

            TextureLoader = new Texture2DProvider(Assembly.GetExecutingAssembly(), RenderCoordinator) {
                DefaultOptions = new TextureLoadOptions {
                    Premultiply = true,
                    GenerateMips = true
                }
            };
            FontLoader = new FreeTypeFontProvider(Assembly.GetExecutingAssembly(), RenderCoordinator);

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
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.8f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 1.75f * DPIFactor);
            TextMaterial.Parameters.ShadowMipBias.SetValue(1.33f);

            SelectedTextMaterial = Materials.Get(Materials.OutlinedBitmap, blendState: BlendState.AlphaBlend);
            SelectedTextMaterial.Parameters.ShadowColor.SetValue(new Vector4(1, 1, 1, 1));
            SelectedTextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 1.33f * DPIFactor);
            SelectedTextMaterial.Parameters.ShadowMipBias.SetValue(1f);

            Decorations = new DefaultDecorations(Materials) {
                TextMaterial = TextMaterial,
                SelectedTextMaterial = SelectedTextMaterial,
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
                    Images = new ImmutableAbstractStringLookup<RichImage> {
                        {
                            "ghost", new RichImage {
                                Texture = TextureLoader.Load("ghost"),
                                Scale = 0.4f,
                                Margin = new Vector2(0, -2),
                                VerticalAlignment = 1f
                            }
                        }
                    },
                    MarkedStringProcessor = ProcessMarkedString
                },
                ScratchSurfaceFormat = RenderTargetFormat,
                AllowNullFocus = false
            };

            LastTimeOverUI = Time.Ticks;

            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += Window_ClientSizeChanged;
            Window_ClientSizeChanged(null, EventArgs.Empty);

            if (!isReloading) {
                if (UseSavedTree) {
                    if (File.Exists(SavedTreePath))
                        try {
                            Context.Engine?.LoadRecords(SavedTreePath);
                        } catch (Exception exc) {
                            UseSavedTree = false;
                        }
                    else
                        UseSavedTree = false;
                }
            }

            if (!UseSavedTree)
                LoadTestScene(0);
        }

        void LoadTestScene (int index) {
            Context.HideTooltip();
            Context.Controls.Clear();
            switch (index) {
                case 0:
                    BuildUI();
                    break;
                case 1:
                    BuildSimpleUI();
                    break;
                case 2:
                    MakeScrollingTest(out _, out _, out var container);
                    Context.Controls.Add(new Container {
                        Appearance = {
                            BackgroundColor = Color.Red * 0.5f,
                        },
                        Children = {
                            container
                        }
                    });
                    break;
                case 3:
                    BuildMenuTest();
                    break;
            }
            UseSavedTree = false;
        }

        private MarkedStringAction ProcessMarkedString (ref AbstractString text, ref AbstractString id, ref RichTextLayoutState state, ref StringLayoutEngine layoutEngine) {
            layoutEngine.overrideColor = Color.Teal;
            return default;
        }

        private void BuildMenuTest () {
            var testMenu = new Menu {
                new StaticText { Text = "Item 1" },
                "Long Item 2",
                "Item 3",
                "Long Item 4",
                "Even longer item 5. This item is so long it will hit the size constraint and may push Item 4B too far (but shouldn't)",
                new StaticText { Text = "Item 6" },
                "Item 7",
                new ControlGroup {
                    DebugLabel = "Menu item with two child items",
                    Children = {
                        new StaticText { Text = "Item 4A" },
                        new Spacer(),
                        new StaticText { Text = "Item 4B" },
                    },
                },
            };
            testMenu.Width.Maximum = 300f;
            testMenu.CloseOnClickOutside = false;
            testMenu.CloseOnEscapePress = false;
            testMenu.CloseWhenFocusLost = false;
            testMenu.CloseWhenItemChosen = false;
            testMenu.Show(Context);
        }

        private void BuildSimpleUI () {
            var topLevel = new Container {
                Layout = {
                    Fill = true,
                },
                Container = {
                    Row = true,
                    Wrap = true,
                },
                Appearance = {
                    Undecorated = true,
                    BackgroundColor = Color.Green * 0.33f,
                },
                Padding = new Margins(8),
                DebugLabel = "topLevel (green fill row wrap)"
            };
            var cb = new ContainerBuilder(topLevel, true);
            cb.Text("Text 1 should expand")
                .Control.AutoSizeIsMaximum = false;
            cb.Text("Text 2");
            cb.Text("Text 3 should expand")
                .Control.AutoSizeIsMaximum = false;
            cb.Text("Text 4");

            var colBox = cb.NewGroup(
                layoutFlags: ControlFlags.Layout_Fill | ControlFlags.Layout_ForceBreak,
                containerFlags: ControlFlags.Container_Column | ControlFlags.Container_Align_Start | ControlFlags.Container_No_Expansion
            );
            colBox.Control.DebugLabel = "colBox (blue fill column no_expansion)";
            colBox.Control.Padding = new Margins(8);
            colBox.Control.Appearance.BackgroundColor = Color.Blue * 0.33f;
            colBox.Text("Text 5");
            colBox.Text("Text 6")
                .Control.AutoSizeIsMaximum = false;

            var dd = new Dropdown<string> {
                Label = "Dropdown: {0}",
                TooltipContent = "Dropdown tooltip"
            };
            for (int i = 0; i < 100; i++)
                dd.Items.Add(i.ToString());

            colBox.Add(dd);

            var subBox = colBox.NewGroup(
                layoutFlags: ControlFlags.Layout_Fill,
                containerFlags: ControlFlags.Container_Row | ControlFlags.Container_Break_Auto | 
                    ControlFlags.Container_Align_Start | ControlFlags.Container_No_Expansion
            );
            subBox.Control.DebugLabel = "subBox (red fill row wrap no_expansion)";
            subBox.Control.Appearance.BackgroundColor = Color.Red * 0.4f;
            subBox.Text("Text 7");
            subBox.Text("Text 8")
                .SetAutoSize(false, true);
            subBox.Text("Text 9")
                .SetFixedSize(1024, null)
                .SetAutoSize(false, true);
            subBox.Text("Text 10 should wrap")
                .SetFixedSize(1024, null)
                .SetAutoSize(false, true);
            subBox.Text("Text 11")
                .SetAutoSize(false, true);

            var floatingBox = new Window {
                Title = "Floating box"
            };
            var fb = new ContainerBuilder(floatingBox, true);
            fb.Text("I have a tooltip", "This tooltip should not be enormous");
            fb.Text("Line 2").SetForceBreak(true);
            fb.Text("Line 2 text 2");

            Context.Controls.Add(topLevel);
            Context.Controls.Add(floatingBox);
        }

        private void BuildUI () {
            var hoveringCtl = new StaticText {
                Layout = { Fill = true },
                AutoSize = false,
                Wrap = false,
                Text = "Hovering: None",
                TooltipContent = "The control the mouse is currently hovering over",
                Appearance = {
                    BackgroundColor = Color.Red * 0.1f,
                }
            };

            var lastClickedCtl = new StaticText {
                Layout = { Fill = true },
                AutoSize = false,
                Wrap = false,
                Text = "",
                TooltipContent = "The control most recently clicked with the mouse",
                Appearance = {
                    BackgroundColor = Color.Green * 0.1f,
                }
            };

            var focusedCtl = new StaticText {
                Layout = { Fill = true, ForceBreak = true },
                AutoSize = false,
                Wrap = false,
                Text = "",
                TooltipContent = "The control with keyboard focus",
                Appearance = {
                    BackgroundColor = Color.Blue * 0.1f,
                }
            };

            var capturedCtl = new StaticText {
                Layout = { Fill = true },
                AutoSize = false,
                Wrap = false,
                Text = "",
                TooltipContent = "The control with mouse capture",
                Appearance = {
                    BackgroundColor = Color.Purple * 0.1f,
                }
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
                Layout = { Fill = true, ForceBreak = true },
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
                Layout = {
                    ForceBreak = true
                },
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
                Layout = { Fill = true, ForceBreak = true },
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
                    Compositor = new WindowCompositor(Materials),
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
                    /*
                    new ControlGroup (forceBreak: true) {
                    */
                    hideButton,
                    toppleButton,
                    LoginButton,
                // },
                // FIXME: We need this spacer to fill the empty space at the bottom of the window when it's maximized.
                // Should it really work this way?
                new Spacer {
                    Layout = {
                        ForceBreak = true
                    }
                },
                new UserResizeWidget()
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
                Text = "Virt",
                Checked = true
            };
            var multiselectCheckbox = new Checkbox {
                Text = "Multi",
                Checked = true
            };
            var toggleCheckbox = new Checkbox {
                Text = "Toggle",
                Checked = false
            };
            var columnCount = new Dropdown<int> {
                Label = "Columns: {0}",
                Items = { 1, 2, 3, 4 },
                SelectedItem = 1
            };
            var listBox = new ListBox<string> {
                Layout = {
                    ForceBreak = true,
                },
                Description = "Big List",
                Width = { Fixed = 600 },
                Height = { Fixed = 600 },
                Virtual = virtualCheckbox.Checked,
                MaxSelectedCount = multiselectCheckbox.Checked ? 10 : 1,
                DefaultToggleOnClick = toggleCheckbox.Checked,
                ColumnCount = columnCount.SelectedItem,
                CreateControlForValue = BigList_ControlForValue
            };
            for (var i = 0; i < itemCount; i++)
                listBox.Items.Add($"# {i}");

            Button increaseGaugeButton;
            Gauge gauge;
            Container scrollableClipTest;
            MakeScrollingTest(out increaseGaugeButton, out gauge, out scrollableClipTest);

            var listboxContainer = new ControlGroup {
                Layout = {
                    Anchor = { Left = true, Top = true },
                },
                Children = {
                    virtualCheckbox,
                    multiselectCheckbox,
                    toggleCheckbox,
                    columnCount,
                    listBox
                },
                DebugLabel = "listboxContainer"
            };

            var canvas = new Canvas {
                Appearance = {
                    BackgroundColor = Color.Green
                },
                Width = { Minimum = 200 },
                Height = { Minimum = 200 },
                Buffered = true,
                CacheContent = true,
                CompositingBlendState = BlendState.Additive
            };
            canvas.OnPaint += Canvas_OnPaint;

            Context.EventBus.Subscribe(canvas, UIEvents.MouseDown, Canvas_OnMouseEvent);
            Context.EventBus.Subscribe(canvas, UIEvents.MouseMove, Canvas_OnMouseEvent);

            var lfb = new Squared.PRGUI.Flags.LayoutFlags { ForceBreak = true };
            var la = new ControlAppearance { BackgroundColor = Color.DarkBlue };

            var textTab = new Container {
                Scrollable = true,
                ShowHorizontalScrollbar = false,
                Width = { Maximum = 500 },
                Appearance = {
                    SuppressDecorationPadding = true,
                },
                Children = {
                    (DynamicStaticText = new StaticText {
                        Appearance = la,
                        AutoSize = true,
                        // Wrap needs to be true to reproduce the autosize bug
                        Wrap = true,
                        ScaleToFit = false,
                        Text = "Dynamic text"
                    }),
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = true,
                        ScaleToFit = false,
                        MinScale = 0f,
                        Text = "StaticText 1: AutoSizeX = false, AutoSizeY = true, Wrap = true, ScaleToFit = false, MinScale = 0f",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSize = true,
                        Wrap = true,
                        ScaleToFit = false,
                        MinScale = 0f,
                        Text = "StaticText 2: AutoSize = true, Wrap = true, ScaleToFit = false, MinScale = 0f",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = true,
                        ScaleToFit = true,
                        MinScale = 0f,
                        Text = "StaticText 3: AutoSize = true, Wrap = true, ScaleToFit = true, MinScale = 0f",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = false,
                        ScaleToFit = true,
                        MinScale = 0f,
                        Text = "StaticText 4: AutoSize = true, Wrap = false, ScaleToFit = true, MinScale = 0f",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = false,
                        ScaleToFit = true,
                        MinScale = 0.7f,
                        Text = "StaticText 5: AutoSize = true, Wrap = false, ScaleToFit = true, MinScale = 0.7f",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = true,
                        ScaleToFit = true,
                        MinScale = 0.7f,
                        Text = "StaticText 6: AutoSize = true, Wrap = true, ScaleToFit = true, MinScale = 0.7f",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = false,
                        ScaleToFit = true,
                        MinScale = 0.6f,
                        Width = { Maximum = 450 },
                        Text = "StaticText 7: AutoSize = true, Wrap = false, ScaleToFit = true, MinScale = 0.6f, MaxWidth = 450",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Wrap = true,
                        ScaleToFit = true,
                        MinScale = 0.6f,
                        Width = { Maximum = 450 },
                        Text = "StaticText 8: AutoSize = true, Wrap = true, ScaleToFit = true, MinScale = 0.6f, MaxWidth = 450",
                    },
                    new StaticText {
                        Layout = lfb,
                        Appearance = la,
                        Width = 105,
                        Wrap = false,
                        Multiline = false,
                        MinScale = 0.2f,
                        AutoSizeIsMaximum = false,
                        ScaleToFitX = true,
                        TextAlignment = Squared.Render.Text.HorizontalAlignment.Center,
                        Text = "Revenge Capacitor",
                    },
                    new StaticText {
                        Appearance = la,
                        Width = 105,
                        Wrap = true,
                        Multiline = true,
                        MinScale = 0.2f,
                        AutoSizeIsMaximum = false,
                        ScaleToFitX = true,
                        TextAlignment = Squared.Render.Text.HorizontalAlignment.Center,
                        Text = "Revenge Capacitor",
                    },
                    new StaticText {
                        Appearance = la,
                        Width = 105,
                        Height = 75,
                        Wrap = true,
                        Multiline = true,
                        MinScale = 0.3f,
                        AutoSizeIsMaximum = false,
                        ScaleToFit = true,
                        TextAlignment = Squared.Render.Text.HorizontalAlignment.Center,
                        Text = "Revenge Capacitor",
                    },
                },
            };

            var displayOrdering = new Container {
                Children = {
                    new StaticText {
                        Text = "A [order=2]",
                        DisplayOrder = 2,
                        Appearance = {
                            BackgroundColor = Color.Red * 0.7f,
                        },
                        Layout = {
                            Floating = true
                        },
                        Width = 300,
                        Height = 300
                    },
                    new StaticText {
                        Text = "B [order=0]",
                        DisplayOrder = 0,
                        Appearance = {
                            BackgroundColor = Color.Green * 0.7f,
                        },
                        Layout = {
                            Floating = true,
                            FloatingPosition = new Vector2(96, 64)
                        },
                        Width = 300,
                        Height = 300
                    },
                    new StaticText {
                        Text = "C [order=1]",
                        DisplayOrder = 1,
                        Appearance = {
                            BackgroundColor = Color.Blue * 0.7f,
                        },
                        Layout = {
                            Floating = true,
                            FloatingPosition = new Vector2(32, 160)
                        },
                        Width = 300,
                        Height = 300
                    }
                }
            };

            var rich = new HyperText {
                HotspotAppearance = {
                    BackgroundColor = Color.Red
                },
                Text = "Hello World.\r\n$[color:red]Red$[], $[color:green]green$[], and $[color:blue]blue$[] $(internal id|are merely three colors) of the rainbow.\r\n" +
                    "Yet another $(color) you may encounter in the real world is $[color:black]black$[], though some may insist that it is not a color. They are liars.",
                AutoSize = false
            };

            SpinningText = new StaticText {
                Layout = {
                    Floating = true,
                    FloatingPosition = new Vector2(64, 64),
                },
                Text = "Yaaaaay! I'm spinning!",
            };
            SpinningImage = new StaticImage {
                Layout = {
                    Floating = true,
                    FloatingPosition = new Vector2(64, 256),
                },
                Image = TextureLoader.Load("stonks"),
                Width = 64,
                Height = 64
            };

            var transformTab = new ControlGroup {
                SpinningText, SpinningImage
            };

            var propAppearance = new ControlAppearance {
                BackgroundColor = Color.Black * 0.75f,
                SuppressDecorationMargins = true,
            };
            var proportionalTab = new ControlGroup {
                Width = {
                    Minimum = 64,
                    Maximum = 1000,
                },
                Height = {
                    Minimum = 128,
                    Maximum = 400,
                },
                Children = {
                    new StaticText {
                        Text = "33.3~%",
                        AutoSizeWidth = false,
                        Appearance = propAppearance,
                        Width = {
                            Percentage = 33.33333f,
                        }
                    },
                    new StaticText {
                        Text = "66.6~%",
                        AutoSizeWidth = false,
                        Appearance = propAppearance,
                        Width = {
                            Percentage = 66.66666f,
                        }
                    },
                    new StaticText {
                        Text = "20%",
                        AutoSizeIsMaximum = false,
                        Appearance = propAppearance,
                        Layout = {
                            ForceBreak = true,
                        },
                        Width = {
                            Percentage = 20f,
                        }
                    },
                    new StaticText {
                        Text = "20%",
                        AutoSizeIsMaximum = false,
                        Appearance = propAppearance,
                        Width = {
                            Percentage = 20f,
                        }
                    },
                    new StaticText {
                        Text = "40%",
                        AutoSizeIsMaximum = false,
                        Appearance = propAppearance,
                        // FIXME: This doesn't work
                        /*
                        Layout = {
                            Anchor = {
                                Right = true,
                            }
                        },
                        */
                        Width = {
                            Percentage = 40f,
                        }
                    },
                    new Spacer {
                        Layout = {
                            ForceBreak = true,
                        }
                    },
                    new UserResizeWidget()
                }
            };

            var tabs = new TabContainer {
                { scrollableClipTest, "Scroll" },
                { listboxContainer, "List" },
                { canvas, "Canvas" },
                { displayOrdering, "Z-Order" },
                { rich, "Rich\nText" },
                { textTab, "Text\nSize" },
                { transformTab, "Xform" },
                { proportionalTab, "%" },
            };
            tabs.SelectedIndex = 1;
            tabs.TabsOnLeft = false;
            tabs.ExpandToHoldAllTabs = true;
            tabs.LayoutFlags = ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_Anchor_Top;

            var bigScrollableContainer = new Container {
                ClipChildren = true,
                Layout = {
                    Fill = true,
                    ForceBreak = true,
                },
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
                        Layout = {
                            Fill = { Row = true },
                            ForceBreak = true
                        },
                        ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Break_Allow | ControlFlags.Container_Align_Start,
                        Children = {
                            new Button {
                                Text = "Clipped huge button\r\nSecond line\r\n" + ButtonChars,
                                Width = { Fixed = 600 },
                                Height = { Fixed = 1800 },
                                VerticalAlignment = 0f,
                                Layout = {
                                    Anchor = { Top = true },
                                    Fill = { Row = true },
                                    ForceBreak = true
                                },
                            },
                            tabs
                        },
                        DebugLabel = "button and tabstrip container"
                    },
                    new Button {
                        Layout = {
                            Fill = { Row = true },
                            ForceBreak = true
                        },
                        Text = "Another button at the bottom to test clipped hit tests"
                    }
                }
            };

            var largeText = new Checkbox {
                Text = "Big Text",
                Layout = {
                    Anchor = { Left = true },
                    ForceBreak = true
                },
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
                Layout = {
                    Fill = true
                },
                // FIXME: We shouldn't need to set Wrap here since we're setting explicit breaks
                ContainerFlags = ControlFlags.Container_Row | ControlFlags.Container_Align_End
                    | ControlFlags.Container_Break_Allow | ControlFlags.Container_Constrain_Size,
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
                        Layout = {
                            Fill = { Row = true }
                        },
                        Appearance = {
                            BackgroundColor = Color.LightPink
                        }
                    },
                    dropdown,
                    new StaticText {
                        AutoSize = false,
                        Text = "Static Text 2\r\nLine 2",
                        Layout = {
                            Fill = { Row = true },
                            ForceBreak = true
                        },
                        Width = { Maximum = 130 },
                        Height = { Minimum = Font.LineSpacing + Context.Decorations.StaticText.Padding.Y },
                        Multiline = true,
                        Wrap = false,
                        Appearance = {
                            BackgroundColor = Color.DarkRed,
                        },
                        ScaleToFitY = true
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
                },
                DebugLabel = "topLevelContainer",
                Width = {
                    Maximum = 1920
                },
                // HACK: Without this the container gets too tall, but that might be correct?
                Height = {
                    Maximum = 1080
                },
            };

            SomeText = new StaticText {
                Text = "Some text",
                Appearance = {
                    BackgroundColor = Color.DarkBlue,
                    Transform = Matrix.CreateRotationZ(MathHelper.ToRadians(5f)),
                    TransformOrigin = Vector2.Zero,
                },
                TooltipContent = "Tooltip for some text",
            };

            SpinTest = new Window {
                Title = "Wheeeeeeeeeee",
                Width = 450,
                Height = 350,
                TooltipContent = "Tooltip for the spinny window",
                Appearance = {
                    Transform = Tween.StartNow(
                        Matrix.Identity,
                        Matrix.CreateRotationZ(360) *
                        Matrix.CreateTranslation(300, 300, 0),
                        seconds: 20, repeatCount: 99999, repeatMode: TweenRepeatMode.Pulse
                    ),
                    Compositor = new MaskedCompositor(Materials, TextureLoader.Load("gauge-mask")),
                },
                // FIXME: Why doesn't this work?
                Position = Vector2.Zero,
                Alignment = Vector2.Zero,
                Children = {
                    SomeText
                },
            };

            Context.Controls.Add(topLevelContainer);
            Context.Controls.Add(window.Control);
            Context.Controls.Add(SpinTest);

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

            Context.EventBus.Subscribe(multiselectCheckbox, UIEvents.CheckedChanged, (ei) => {
                listBox.MaxSelectedCount = multiselectCheckbox.Checked ? 10 : 1;
            });

            Context.EventBus.Subscribe(toggleCheckbox, UIEvents.CheckedChanged, (ei) => {
                listBox.DefaultToggleOnClick = toggleCheckbox.Checked;
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

            var floatingWindowWithText = new Window {
                Alignment = new Vector2(0.5f, 0.5f),
                DynamicContents = FloatingWindowWithText_Content,
                AllowDrag = false,
                Appearance = {
                    Opacity = 1f,
                    Decorator = Decorations.Tooltip,
                },
                AcceptsFocus = false,
                Intangible = true,
                DisplayOrder = 10,
            };
            // Context.Controls.Add(floatingWindowWithText);
        }

        private Control BigList_ControlForValue (ref string value, Control existingControl) {
            var st = existingControl as StaticText;
            if (st == null)
                existingControl = st = new StaticText {
                    AutoSizeIsMaximum = false,
                    AutoSizeWidth = false,
                };

            st.SetText(value, true);
            Color c = default;
            unchecked {
                c.PackedValue = (uint)value.GetHashCode();
            }
            c.A = 255;
            // FIXME: The text decorator won't automatically change since there's a background color (thanks ListBox)
            st.Appearance.BackgroundColor = c * 0.5f;
            return st;
        }

        private void MakeScrollingTest (out Button increaseGaugeButton, out Gauge gauge, out Container scrollableClipTest) {
            var supernestedGroup = new ControlGroup {
                LayoutFlags = ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak,
                ContainerFlags = ControlFlags.Container_Align_Start | ControlFlags.Container_Row | ControlFlags.Container_Break_Auto,
                DynamicContents = BuildSupernestedGroup,
                DebugLabel = "supernestedGroup"
            };

            increaseGaugeButton = new Button {
                Text = "Increase Gauge Value",
                Layout = {
                    Fill = { Row = true },
                    Anchor = { Top = true },
                    ForceBreak = true
                },
                Width = { Fixed = 450 },
                AutoSizeWidth = false,
                EnableRepeat = true
            };
            gauge = new Gauge {
                Description = "Test Gauge",
                Layout = {
                    ForceBreak = true
                },
                Width = { Fixed = 450 },
                MarkedRanges = {
                    new Gauge.MarkedRange {
                        Start = 0.1f,
                        End = 0.7f,
                        Color = Color.Red,
                        Fill = {
                            Thickness = 0.3f,
                        },
                    },
                },
                /*
                Padding = new Margins(2, 2),
                Direction = GaugeDirection.Clockwise
                */
            };
            scrollableClipTest = new Container {
                Layout = {
                    Anchor = { Left = true, Top = true },
                },
                Container = {
                    Row = true,
                    Wrap = true,
                    Start = true,
                },
                Children = {
                    new Container {
                        ClipChildren = true,
                        Layout = {
                            Fill = { Row = true },
                            Anchor = { Top = true },
                        },
                        Container = {
                            AutoBreak = true,
                        },
                        Height = { Maximum = 500 },
                        Width = { Fixed = 450 },
                        Scrollable = true,
                        Children = {
                            new StaticText { Text = "Testing nested clips" },
                            new StaticText {
                                Text = "Long multiline static text inside of clipped region that should be wrapped/clipped instead of overflowing",
                                Wrap = true, AutoSizeWidth = false,
                                Layout = {
                                    Fill = { Row = true },
                                    ForceBreak = true
                                }
                            },
                            new Checkbox {
                                Text = "Checkbox 1",
                                Layout = {
                                    Fill = { Row = true },
                                    ForceBreak = true
                                }
                            },
                            new Checkbox { Text = "Checkbox 2", Checked = true },
                            new RadioButton {
                                Text = "Radio 1", GroupId = "radio", Checked = true,
                                Layout = {
                                    Fill = { Row = true },
                                    ForceBreak = true
                                }
                            },
                            new RadioButton { Text = "Radio 2", GroupId = "radio" },
                            new RadioButton { Text = "Radio 3", GroupId = "radio", Checked = true },
                            supernestedGroup
                        }
                    },
                    increaseGaugeButton,
                    gauge
                }
            };
        }

        private void FloatingWindowWithText_Content (ref ContainerBuilder builder) {
            builder.Text("Test text")
                .SetAutoSize(true, true);
        }

        Vector2? CanvasEllipsePosition;

        private void Canvas_OnMouseEvent (IEventInfo e) {
            var args = (MouseEventArgs)e.Arguments;
            if (args.Buttons == MouseButtons.None)
                return;
            ((Canvas)e.Source).Invalidate();
            CanvasEllipsePosition = args.LocalPosition;
        }

        private void Canvas_OnPaint (ref UIOperationContext context, ref ImperativeRenderer renderer, Squared.PRGUI.Decorations.DecorationSettings settings) {
            var contentRect = settings.ContentBox;
            var position = CanvasEllipsePosition ?? contentRect.Center;
            renderer.AutoIncrementLayer = true;
            renderer.RasterizeRectangle(contentRect.Position, contentRect.Extent, 0f, Color.Red);
            renderer.RasterizeEllipse(position, new Vector2(16f), 1f, Color.White, Color.Black, Color.Blue);
            // throw new Exception("test");
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
                        : null,
                    button == window.AcceptControl
                        ? ModalCloseReason.UserConfirmed
                        : ModalCloseReason.UserCancelled
                );
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
            // TODO: Right now the autosize algorithm causes this label to be super wide,
            //  and then the label isn't truncated and all the characters get drawn and overhang the right.
            // Clipping will fix this, but maybe this is bad default behavior?
            // It causes other items to grow to meet this absurd size.
            builder.Text("Extremely long menu item with a bunch of text in it. This thing should be truncated pretty aggressively")
                .SetTooltip("This menu item has a custom tooltip")
                .SetBackgroundColor(Color.Blue)
                .SetWrap(false);
            var c = builder.NewContainer<ControlGroup>();
            c.Control.DebugLabel = "Menu item with two child items";
            c.Text("Item 4A");
            c.Spacer();
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
                .SetContainer(autoBreak: true)
                .SetTitle("Test");
            tc.Text<Button>("Button A");
            tc.Text<Button>("Button B");

            var tc2 = builder.NewContainer<TitledContainer>();
            tc2.Properties
                .SetLayoutFlags(ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak)
                // .SetFixedSize(null, 432)
                .SetContainer(autoBreak: true)
                .SetCollapsible(true);
            tc2.Control.DebugLabel = "Titled container with no title";
            tc2.Text<Button>("Button C");
            tc2.Text<Button>("Button D");
            tc2.Text<Button>("Button E");
            tc2.Text<Button>("Button F");
            tc2.Text<Button>("Button G");
        }

        private void Window_ClientSizeChanged (object sender, EventArgs e) {
            var pp = GraphicsDevice.PresentationParameters;
            RenderCoordinator.WaitForActiveDraws();
            Materials.SetViewTransform(ViewTransform.CreateOrthographic(pp.BackBufferWidth, pp.BackBufferHeight));
            Context.CanvasSize = new Vector2(pp.BackBufferWidth, pp.BackBufferHeight);
            if (!UseSavedTree || UIContext.UseNewEngine)
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

        private int DynamicStringIndex;
        private string[] DynamicStaticStrings = new[] {
            "Dynamic",
            "Dynamic static text",
            "Dynamic static",
            "Dyn",
            "Dynamicstatictext",
            "123 456 789 012 345 678",
            "  hello  "
        };

        // HACK: This is here so i can easily inspect the disassembly to make sure things got inlined and the code isn't gross
        protected void DenseListTest (ref DenseList<int> dl) {
            ref int one = ref dl.Item(0);
            ref int two = ref dl.Item(1);
            // int one = dl[0], two = dl[1];
            if (one == two)
                throw new Exception();
            else if (one != 1)
                throw new Exception();
#if DEBUG
            var size = DenseList<int>.ElementTraits.ListSize;
#endif
            ;
        }

        int? SceneToLoad;

        protected override void Update (GameTime gameTime) {
            var started = Time.Ticks;

            var temp = new DenseList<int> { 1, 2 };
            DenseListTest(ref temp);

            if (SceneToLoad.HasValue) {
                LoadTestScene(SceneToLoad.Value);
                SceneToLoad = null;
            }

            if (DynamicStaticText != null)
                DynamicStaticText.Text = DynamicStaticStrings[(DynamicStringIndex++ / 16) % DynamicStaticStrings.Length];

            if (IsFirstUpdate || (UpdatesToSkip <= 0)) {
                IsFirstUpdate = false;
                if (UseSavedTree && (Context.Engine != null)) {
                    Context.Engine.Update();
                    // HACK
                    Keyboard.PreviousState = Keyboard.CurrentState;
                    Keyboard.CurrentState = Microsoft.Xna.Framework.Input.Keyboard.GetState();
                    Mouse.PreviousState = Mouse.CurrentState;
                    Mouse.CurrentState = Microsoft.Xna.Framework.Input.Mouse.GetState();

                    if (Context.Engine.DebugHitTest(
                        new Vector2(Mouse.CurrentState.X, Mouse.CurrentState.Y), out var record, out _, Keyboard.CurrentState.IsKeyDown(Keys.E)
                    ) && !record.Parent.IsInvalid)
                        HighlightRecord = record.Key;
                    else
                        HighlightRecord = null;

                    if (HighlightRecord.HasValue && IsActive) {
                        if ((Mouse.PreviousState.LeftButton == ButtonState.Released) && (Mouse.CurrentState.LeftButton == ButtonState.Pressed)) {
                            ref var item = ref Context.Engine[HighlightRecord.Value];
                            Console.WriteLine($"Clicked: {HighlightRecord}");
                            item.Width.Maximum = item.Width.HasMaximum ? (float?)null : 400;
                            item.Height.Maximum = item.Height.HasMaximum ? (float?)null : 400;
                        } else if ((Mouse.PreviousState.RightButton == ButtonState.Released) && (Mouse.CurrentState.RightButton == ButtonState.Pressed)) {
                            Console.WriteLine($"Deleting {HighlightRecord}");
                            Context.Engine.Remove(HighlightRecord.Value);
                            Context.Engine.Update();
                        }
                    }
                } else {
                    Context.Update();
                    Context.UpdateInput(IsActive);
                    if (
                        Context.IsActive && 
                        (Mouse.PreviousState.MiddleButton == ButtonState.Pressed) &&
                        (Mouse.CurrentState.MiddleButton == ButtonState.Released)
                    ) {
                        if ((Context.Hovering != null) && (Context.Hovering != Context.Controls[0]))
                            Context.Hovering.Visible = false;
                    }
                }
            } else 
                UpdatesToSkip--;

            IsMouseVisible = !IsActive || (Context.InputSources.IndexOf(Mouse) == 0);

            if (Context.IsActive || UseSavedTree)
                LastTimeOverUI = Time.Ticks;

            var ks = Keyboard.CurrentState;
            var pks = Keyboard.PreviousState;

            if (IsActive) {
                // Tweens don't work for this sort of transform animation
                var t = Time.Seconds;
                var angle = MathHelper.ToRadians((float)t * 90f);
                var matrix = Matrix.CreateRotationZ(angle);
                if (SpinningText != null) {
                    SpinningText.Appearance.Transform = matrix;
                    SpinningImage.Appearance.Transform = matrix;
                }

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
                } else if (ks.IsKeyDown(Keys.F10) && !pks.IsKeyDown(Keys.F10)) {
                    Context.Engine?.SaveRecords(SavedTreePath);
                    UseSavedTree = Context.Engine != null;
                }

                if (UIContext.UseNewEngine) {
                    for (int i = 0; i < 9; i++) {
                        var k = Keys.D0 + i;
                        if (ks.IsKeyDown(k) && !pks.IsKeyDown(k))
                            SceneToLoad = i;
                    }
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
                if (UseSavedTree && UIContext.UseNewEngine)
                    Context.RasterizeLayoutTree(frame, UIRenderTarget, -9990, Font, HighlightRecord);
                else
                    Context.Rasterize(frame, UIRenderTarget, -9990);
            } else
                DrawsToSkip--;

            var ir = new ImperativeRenderer(frame, Materials);
            ir.Clear(color: Color.Transparent);
            ir.Layer += 1;

            /*
            var hoveringControl = Context.HitTest(new Vector2(MouseState.X, MouseState.Y), false);
            if (hoveringControl != null) {
                var hoveringBox = hoveringControl.GetRect(context.Engine);

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

            if (SpinTest != null) {
                var dr = SpinTest.GetRect(displayRect: true);
                ir.RasterizeRectangle(dr.Position, dr.Extent, 1f, 2f, Color.Blue * 0.1f, Color.Blue * 0.1f, outlineColor: Color.Blue);
                dr = SomeText.GetRect(displayRect: true);
                ir.RasterizeRectangle(dr.Position, dr.Extent, 1f, 1.5f, Color.Green * 0.1f, Color.Green * 0.1f, outlineColor: Color.Green);
            }

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
        private StaticText SomeText;
        private Window SpinTest;

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

    public class MaskedCompositor : IControlCompositor {
        public DefaultMaterialSet Materials;
        public Material Material;
        public Texture2D Mask;
        public float? Padding => null;

        public MaskedCompositor (DefaultMaterialSet materials, Texture2D mask) {
            Materials = materials;
            Mask = mask;
            Material = Materials.Get(Materials.MaskedBitmap, blendState: RenderStates.PorterDuffOver);
        }

        public void AfterIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall) {
        }

        public void BeforeIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall) {
        }

        public void Composite (Control control, ref ImperativeRenderer renderer, ref BitmapDrawCall drawCall, float opacity, BlendState blendState) {
            var temp = drawCall;
            temp.Texture2 = Mask;
            temp.TextureRegion2 = Bounds.Unit.Translate(new Vector2(-0.1f, -0.25f));
            temp.AlignTexture2(control.GetRect().Width / Mask.Width);
            renderer.Draw(ref temp, material: Material, blendState: blendState);
        }

        public bool WillComposite (Control control, float opacity) {
            return true;
        }
    }

    public class WindowCompositor : IControlCompositor {
        public DefaultMaterialSet Materials;
        public Material Material;
        public float? Padding => null;

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

        public void Composite (Control control, ref ImperativeRenderer renderer, ref BitmapDrawCall drawCall, float opacity, BlendState blendState) {
            if ((opacity >= 1) && (control.Context.TopLevelFocused == control))
                renderer.Draw(ref drawCall, blendState: blendState ?? RenderStates.PorterDuffOver);
            else
                renderer.Draw(ref drawCall, material: Material, blendState: blendState);
        }

        public bool WillComposite (Control control, float opacity) {
            return (opacity < 1) || (control.Context.TopLevelFocused != control);
        }
    }
}

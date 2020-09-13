using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace PRGUI.Demo {
    public class DemoGame : MultithreadedGame {
        public UIContext Context;

        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }

        public EmbeddedTexture2DProvider TextureLoader { get; private set; }
        public EmbeddedFreeTypeFontProvider FontLoader { get; private set; }

        internal KeyboardInput KeyboardInputHandler;
        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public Material TextMaterial { get; private set; }

        public FreeTypeFont Font;
        public AutoRenderTarget UIRenderTarget;

        public bool IsMouseOverUI = false, TearingTest = false;
        public long LastTimeOverUI;

        // public ControlKey MasterList, ContentView;

        public DemoGame () {
            // UniformBinding.ForceCompatibilityMode = true;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = false;
            Graphics.IsFullScreen = false;

            Content.RootDirectory = "Content";

            IsFixedTimeStep = false;

            if (IsFixedTimeStep)
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60);

            PreviousKeyboardState = Keyboard.GetState();

            KeyboardInputHandler = new KeyboardInput();
            KeyboardInputHandler.Install();
        }

        protected override void Initialize () {
            base.Initialize();

            Window.AllowUserResizing = false;

            /*
            var layout = Context.Layout;
            var root = layout.Root;
            layout.SetSizeXY(root, 1280, 720);

            layout.SetContainerFlags(root, ControlFlags.Container_Row);

            MasterList = layout.CreateItem();
            layout.InsertAtEnd(root, MasterList);

            layout.SetSizeXY(MasterList, width: 400);

            layout.SetContainerFlags(MasterList, ControlFlags.Container_Column);
            layout.SetLayoutFlags(MasterList, ControlFlags.Layout_Fill_Column);

            ContentView = layout.CreateItem();
            layout.InsertAtEnd(root, ContentView);

            layout.SetLayoutFlags(ContentView, ControlFlags.Layout_Fill);
            */
        }

        public bool LeftMouse {
            get {
                return (MouseState.LeftButton == ButtonState.Pressed) && !IsMouseOverUI;
            }
        }

        public bool RightMouse {
            get {
                return (MouseState.RightButton == ButtonState.Pressed) && !IsMouseOverUI;
            }
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

            Font = FontLoader.Load("FiraSans-Medium");
            Font.SizePoints = 16f;
            Font.GlyphMargin = 2;

            Context = new UIContext {
                CanvasSize = new Vector2(1280, 720),
                Controls = {
                    new Container {
                        LayoutFlags = ControlFlags.Layout_Fill,
                        ContainerFlags = ControlFlags.Container_Align_End,
                        Children = {
                            new Button {
                                AutoSize = true,
                                Content = {
                                    GlyphSource = Font,
                                    Text = "Button 1"
                                }
                            },
                            new Button {
                                AutoSize = true,
                                Content = {
                                    GlyphSource = Font,
                                    Text = "Button 2"
                                }
                            },
                            new StaticText {
                                Content = {
                                    GlyphSource = Font,
                                    Text = "Static Text"
                                }
                            }
                        }
                    },
                }
            };

            Materials = new DefaultMaterialSet(RenderCoordinator);

            TextMaterial = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.5f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One);

            UIRenderTarget = new AutoRenderTarget(
                RenderCoordinator,
                Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, SurfaceFormat.Color, DepthFormat.None, 1
            );

            LastTimeOverUI = Time.Ticks;
        }

        protected override void OnUnloadContent () {
            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);
        }

        private void SetActiveScene (int index) {
            RenderCoordinator.WaitForActiveDraws();
        }

        protected override void Update (GameTime gameTime) {
            PreviousKeyboardState = KeyboardState;
            PreviousMouseState = MouseState;
            KeyboardState = Keyboard.GetState();
            MouseState = Mouse.GetState();

            this.IsMouseVisible = true;

            var mousePosition = new Vector2(MouseState.X, MouseState.Y);

            Context.Update();
            Context.UpdateInput(mousePosition);

            if (Context.Hovering != null)
                LastTimeOverUI = Time.Ticks;

            if (IsActive) {
                var alt = KeyboardState.IsKeyDown(Keys.LeftAlt) || KeyboardState.IsKeyDown(Keys.RightAlt);
                var wasAlt = PreviousKeyboardState.IsKeyDown(Keys.LeftAlt) || PreviousKeyboardState.IsKeyDown(Keys.RightAlt);

                if (KeyboardState.IsKeyDown(Keys.OemTilde) && !PreviousKeyboardState.IsKeyDown(Keys.OemTilde)) {
                    Graphics.SynchronizeWithVerticalRetrace = !Graphics.SynchronizeWithVerticalRetrace;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                } else if (
                    (KeyboardState.IsKeyDown(Keys.Enter) && alt) &&
                    (!PreviousKeyboardState.IsKeyDown(Keys.Enter) || !wasAlt)
                ) {
                    Graphics.IsFullScreen = !Graphics.IsFullScreen;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                } else if (KeyboardState.IsKeyDown(Keys.OemPipe) && !PreviousKeyboardState.IsKeyDown(Keys.OemPipe)) {
                    UniformBinding.ForceCompatibilityMode = !UniformBinding.ForceCompatibilityMode;
                }
            }

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            // Nuklear.UpdateInput(IsActive, PreviousMouseState, MouseState, PreviousKeyboardState, KeyboardState, IsMouseOverUI, KeyboardInputHandler.Buffer);

            KeyboardInputHandler.Buffer.Clear();

            ImperativeRenderer ir;

            using (KeyboardInputHandler.Deactivate())
            using (var group = BatchGroup.ForRenderTarget(
                frame, -9990, UIRenderTarget,
                name: "Render UI"
            )) {
                ir = new ImperativeRenderer(group, Materials, -1, blendState: BlendState.AlphaBlend);
                ir.Clear(color: Color.Transparent);

                ir.Layer = 1;

                Context.Rasterize(ref ir);

                /*

                float radius = 6;
                var masterListRect = ((Bounds)Context.Layout.GetRect(MasterList)).Expand(-radius, -radius);
                var contentViewRect = ((Bounds)Context.Layout.GetRect(ContentView)).Expand(-radius, -radius);

                ir.RasterizeRectangle(
                    masterListRect.TopLeft, masterListRect.BottomRight,
                    radius: radius, innerColor: Color.DarkRed
                );

                ir.RasterizeRectangle(
                    contentViewRect.TopLeft, contentViewRect.BottomRight,
                    radius: radius, innerColor: Color.ForestGreen
                );
                */
            }

            ClearBatch.AddNew(frame, -1, Materials.Clear, Color.Black);

            ir = new ImperativeRenderer(
                frame, Materials, 
                blendState: BlendState.AlphaBlend,
                samplerState: SamplerState.LinearClamp,
                worldSpace: false,
                layer: 9999
            );

            var elapsedSeconds = TimeSpan.FromTicks(Time.Ticks - LastTimeOverUI).TotalSeconds;
            float uiOpacity = Arithmetic.Lerp(1.0f, 0.4f, (float)((elapsedSeconds - 0.66) * 2.25f));

            ir.Draw(UIRenderTarget, Vector2.Zero, multiplyColor: Color.White * uiOpacity);

            // DrawPerformanceStats(ref ir);

            if (TearingTest) {
                var x = (Time.Ticks / 20000) % Graphics.PreferredBackBufferWidth;
                ir.FillRectangle(Bounds.FromPositionAndSize(
                    x, 0, 6, Graphics.PreferredBackBufferHeight
                ), Color.Red);
            }
        }
    }
}

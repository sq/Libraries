using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Storage;
using Microsoft.Xna.Framework.Media;
using Squared.Render;
using Squared.Render.Convenience;

namespace ThreadedPlatformer {
    /// <summary>
    /// This is the main type for your game
    /// </summary>
    public class PlatformerGame : MultithreadedGame {
        // Resources for drawing.
        private GraphicsDeviceManager graphics;
        private SpriteBatch spriteBatch;
        private DefaultMaterialSet materials;

        // Global content.
        private SpriteFont hudFont;

        private Texture2D winOverlay;
        private Texture2D loseOverlay;
        private Texture2D diedOverlay;

        // Meta-level game state.
        private int levelIndex = -1;
        private Level level;
        private bool wasContinuePressed;

        // When the time remaining is less than the warning time, it blinks on the hud
        private static readonly TimeSpan WarningTime = TimeSpan.FromSeconds(30);

        private const int TargetFrameRate = 60;
        private const int BackBufferWidth = 1280;
        private const int BackBufferHeight = 720;
        private const Buttons ContinueButton = Buttons.A;

        public PlatformerGame () {
            graphics = new GraphicsDeviceManager(this);
            graphics.PreferredBackBufferWidth = BackBufferWidth;
            graphics.PreferredBackBufferHeight = BackBufferHeight;

            Content.RootDirectory = "Content";

            // Framerate differs between platforms.
            TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / TargetFrameRate);
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected override void OnLoadContent (bool isReloading) {
            // Create a new SpriteBatch, which can be used to draw textures.
            spriteBatch = new SpriteBatch(GraphicsDevice);

            // Load fonts
            hudFont = Content.Load<SpriteFont>("Fonts/Hud");

            // Load overlay textures
            winOverlay = Content.Load<Texture2D>("Overlays/you_win");
            loseOverlay = Content.Load<Texture2D>("Overlays/you_lose");
            diedOverlay = Content.Load<Texture2D>("Overlays/you_died");

            // Load materials used by threaded renderer
            materials = new DefaultMaterialSet(RenderCoordinator) {
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0.0f, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, 0, 0, 1
                )
            };

            MediaPlayer.IsRepeating = true;
            MediaPlayer.Play(Content.Load<Song>("Sounds/Music"));

            LoadNextLevel();
        }

        /// <summary>
        /// Allows the game to run logic such as updating the world,
        /// checking for collisions, gathering input, and playing audio.
        /// </summary>
        /// <param name="gameTime">Provides a snapshot of timing values.</param>
        protected override void Update (GameTime gameTime) {
            HandleInput();

            level.Update(gameTime);

            base.Update(gameTime);
        }

        private void HandleInput () {
            KeyboardState keyboardState = Keyboard.GetState();
            GamePadState gamepadState = GamePad.GetState(PlayerIndex.One);

            // Exit the game when back is pressed.
            if (gamepadState.Buttons.Back == ButtonState.Pressed)
                Exit();

            bool continuePressed =
                keyboardState.IsKeyDown(Keys.Space) ||
                gamepadState.IsButtonDown(ContinueButton);

            // Perform the appropriate action to advance the game and
            // to get the player back to playing.
            if (!wasContinuePressed && continuePressed) {
                if (!level.Player.IsAlive) {
                    level.StartNewLife();
                } else if (level.TimeRemaining == TimeSpan.Zero) {
                    if (level.ReachedExit)
                        LoadNextLevel();
                    else
                        ReloadCurrentLevel();
                }
            }

            wasContinuePressed = continuePressed;
        }

        private void LoadNextLevel () {
            // Find the path of the next level.
            string levelPath;

            // Loop here so we can try again when we can't find a level.
            while (true) {
                // Try to find the next level. They are sequentially numbered txt files.
                levelPath = String.Format("Levels/{0}.txt", ++levelIndex);
                levelPath = Path.Combine("Content/", levelPath);

                try {
                    using (var stream = TitleContainer.OpenStream(levelPath))
                        break;
                } catch (FileNotFoundException) {
                }

                // If there isn't even a level 0, something has gone wrong.
                if (levelIndex == 0)
                    throw new Exception("No levels found.");

                // Whenever we can't find a level, start over again at 0.
                levelIndex = -1;
            }

            // Unloads the content for the current level before loading the next one.
            if (level != null)
                level.Dispose();

            // Load the level.
            level = new Level(Services, levelPath);
        }

        private void ReloadCurrentLevel () {
            --levelIndex;
            LoadNextLevel();
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            // HACK: We never set the blend state explicitly anywhere (unlike what SpriteBatch did),
            //  but it's sufficient to just set it once per frame here.
            // Normally you would do this in batch setup, but this is fine.
            graphics.GraphicsDevice.BlendState = BlendState.AlphaBlend;

            ClearBatch.AddNew(frame, -1, materials.Clear, clearColor: Color.CornflowerBlue);

            level.Draw(gameTime, frame, materials);

            DrawHud(frame);
        }

        private void DrawHud (Frame frame) {
            Rectangle titleSafeArea = GraphicsDevice.Viewport.TitleSafeArea;
            Vector2 hudLocation = new Vector2(titleSafeArea.X, titleSafeArea.Y);
            Vector2 center = new Vector2(titleSafeArea.X + titleSafeArea.Width / 2.0f,
                                         titleSafeArea.Y + titleSafeArea.Height / 2.0f);

            // Draw time remaining. Uses modulo division to cause blinking when the
            // player is running out of time.
            string timeString = "TIME: " + level.TimeRemaining.Minutes.ToString("00") + ":" + level.TimeRemaining.Seconds.ToString("00");
            Color timeColor;
            if (level.TimeRemaining > WarningTime ||
                level.ReachedExit ||
                (int)level.TimeRemaining.TotalSeconds % 2 == 0) {
                timeColor = Color.Yellow;
            } else {
                timeColor = Color.Red;
            }

            var renderer = new ImperativeRenderer(frame, materials, 100, blendState: BlendState.AlphaBlend);

            renderer.DrawString(hudFont, timeString, hudLocation, timeColor, sortKey: 1);
            renderer.DrawString(hudFont, timeString, hudLocation + Vector2.One, Color.Black, sortKey: 0);

            var timeHeight = hudFont.MeasureString(timeString).Y;
            hudLocation.Y = (float)Math.Floor(hudLocation.Y + (timeHeight * 1.2f));

            var scoreText = "SCORE: " + level.Score;
            renderer.DrawString(hudFont, scoreText, hudLocation, Color.Yellow, sortKey: 1);
            renderer.DrawString(hudFont, scoreText, hudLocation + Vector2.One, Color.Black, sortKey: 0);

            // Determine the status overlay message to show.
            Texture2D status = null;
            if (level.TimeRemaining == TimeSpan.Zero) {
                if (level.ReachedExit) {
                    status = winOverlay;
                } else {
                    status = loseOverlay;
                }
            } else if (!level.Player.IsAlive) {
                status = diedOverlay;
            }

            if (status != null) {
                // Draw status message.
                Vector2 statusSize = new Vector2(status.Width, status.Height);
                renderer.Draw(status, center - statusSize / 2);
            }
        }
    }
}

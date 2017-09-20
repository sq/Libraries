using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Render;
using Squared.Render.Convenience;

namespace LargeBufferTest {
    public class LargeBufferTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 96, 220, 255);

        Texture2D WhitePixel, GrayPixel;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        public LargeBufferTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1280;
            Graphics.PreferredBackBufferHeight = 720;
            Content.RootDirectory = "Content";

//            UseThreadedDraw = false;
        }

        protected override void Initialize () {
            Materials = new DefaultMaterialSet(Services);

            base.Initialize();
        }

        protected override void LoadContent () {
            WhitePixel = new Texture2D(GraphicsDevice, 1, 1);
            WhitePixel.SetData(new [] { Color.White });
            GrayPixel = new Texture2D(GraphicsDevice, 1, 1);
            GrayPixel.SetData(new [] { Color.Silver });
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, -1, Materials.Clear, clearColor: ClearColor);

            const int width = 1280;
            const int height = 500;

            var options = new ParallelOptions {
//                MaxDegreeOfParallelism = 1
            };
            int layer = 0;
            Parallel.For(
                0, height, options,
                // One batch per worker thread
                () => 
                    BitmapBatch.New(
                        frame, 
                        // Suppress batch combining
                        Interlocked.Increment(ref layer), 
                        Materials.ScreenSpaceBitmap
                    ),
                (y, loopState, bb) => {
                    var drawCall = new BitmapDrawCall(WhitePixel, new Vector2(0, y));
                    float fx = 0;
                    var range = bb.ReserveSpace(width);
                    var array = range.Array;
                    var offset = range.Offset;

                    for (int x = 0; x < width; x++, fx++) {
                        drawCall.Texture = ((x % 2) == 0) ? WhitePixel : GrayPixel;
                        drawCall.Position.X = fx;
                        drawCall.MultiplyColor = new Color(255, x % 255, y % 255);

                        array[offset + x] = drawCall;
                    }

                    return bb;
                },
                (bb) => 
                    bb.Dispose()
            );
        }
    }
}

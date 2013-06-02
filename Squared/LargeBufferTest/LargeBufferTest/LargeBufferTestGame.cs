using System;
using System.Collections.Generic;
using System.Linq;
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
            var ir = new ImperativeRenderer(frame, Materials);

            ir.Clear(color: ClearColor);
            ir.Layer += 1;

            const int width = 1280;
            const int height = 720;

            using (var bb = ir.GetBitmapBatch(null, null, null, null)) {
                var drawCalls = bb.ReserveSpace(width * height); 

                var array = drawCalls.Array;
                var offset = drawCalls.Offset;

                var options = new ParallelOptions {
//                    MaxDegreeOfParallelism = 1
                };
                Parallel.For(
                    0, height, options,
                    (y) => {
                        var drawCall = new BitmapDrawCall(WhitePixel, new Vector2(0, y));

                        int rowstart = (y * width);
                        float fx = 0;

                        for (int x = 0; x < width; x++, fx++) {
                            drawCall.Texture = ((x % 2) == 0) ? WhitePixel : GrayPixel;
                            drawCall.Position.X = fx;
                            drawCall.MultiplyColor = new Color(255, x % 255, y % 255);

                            array[offset + rowstart + x] = drawCall;
                        }
                    }
                );
            }
        }
    }
}

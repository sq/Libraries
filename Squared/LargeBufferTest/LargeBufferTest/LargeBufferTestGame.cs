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
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace LargeBufferTest {
    public class LargeBufferTestGame : MultithreadedGame {
        public static readonly Color ClearColor = new Color(24, 96, 220, 255);

        FreeTypeFont Font;
        Texture2D WhitePixel, GrayPixel;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        public LargeBufferTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1280;
            Graphics.PreferredBackBufferHeight = 720;
            Content.RootDirectory = "Content";

            BitmapBatch.AdjustPoolCapacities(
                null, 1024000,
                null, 16
            );

//            UseThreadedDraw = false;
        }

        protected override void Initialize () {
            base.Initialize();

            Materials = new DefaultMaterialSet(RenderCoordinator);
        }

        protected override void LoadContent () {
            Font = new FreeTypeFont(
                RenderCoordinator,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\Fonts\\arial.ttf"
            ) {
                DPIPercent = 100,
                SizePoints = 10f,
                Hinting = false
            };
            WhitePixel = new Texture2D(GraphicsDevice, 1, 1);
            WhitePixel.SetData(new [] { Color.White });
            GrayPixel = new Texture2D(GraphicsDevice, 1, 1);
            GrayPixel.SetData(new [] { Color.Silver });
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);

            PerformanceStats.Record(this);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            if (false) {
                var stats = RenderManager.GetMemoryStatistics();
                Console.WriteLine(
                    "managed: {0:0000000}kb    vertex: {1:0000000}kb    index: {2:0000000}kb",
                    (stats.ManagedIndexBytes + stats.ManagedVertexBytes) / 1024.0,
                    stats.UnmanagedVertexBytes / 1024.0,
                    stats.UnmanagedIndexBytes / 1024.0
                );
            }

            ClearBatch.AddNew(frame, -1, Materials.Clear, clearColor: ClearColor);

            const int width = 1280;
            const int height = 720;

            var options = new ParallelOptions {
            };
            int layer = 0;
            Parallel.For(
                0, height, options,
                // One batch per worker thread
                () => {
                    var bb = BitmapBatch.New(
                        frame,
                        // Suppress batch combining
                        Interlocked.Increment(ref layer),
                        Materials.ScreenSpaceBitmap,
                        capacity: width * height / 8
                    );
                    return bb;
                },
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

            var ir = new ImperativeRenderer(
                frame, Materials, 
                blendState: BlendState.Opaque, 
                depthStencilState: DepthStencilState.None, 
                rasterizerState: RasterizerState.CullNone,
                worldSpace: false,
                layer: 9999
            );

            DrawPerformanceStats(ref ir);
        }

        private void DrawPerformanceStats (ref ImperativeRenderer ir) {
            const float scale = 1f;
            var text = PerformanceStats.GetText(this);

            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = Font.LayoutString(text, buffer, scale: scale);
                var layoutSize = layout.Size;
                var position = new Vector2(30f, 30f);
                var dc = layout.DrawCalls;

                ir.FillRectangle(
                    Bounds.FromPositionAndSize(position, layoutSize),
                    Color.Black
                );
                ir.Layer += 1;
                ir.DrawMultiple(dc, position);
            }
        }
    }
}

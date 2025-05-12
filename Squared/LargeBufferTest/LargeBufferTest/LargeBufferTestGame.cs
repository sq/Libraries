using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace LargeBufferTest {
    public class LargeBufferTestGame : MultithreadedGame {
        public const bool UseSpriteBatch = false;

        public static readonly Color ClearColor = new Color(24, 96, 220, 255);

        SpriteBatch SpriteBatch;

        FreeTypeFont Font;
        Texture2D WhitePixel, GrayPixel;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;

        public LargeBufferTestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1280;
            Graphics.PreferredBackBufferHeight = 720;
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
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

        protected override void OnLoadContent (bool isReloading) {
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

            if (UseSpriteBatch)
                SpriteBatch = new SpriteBatch(GraphicsDevice);
        }

        protected override void Update (GameTime gameTime) {
            base.Update(gameTime);

            PerformanceStats.Record(this);
        }

        protected override void OnBeforeDraw (GameTime gameTime) {
            base.OnBeforeDraw(gameTime);
        }

        void PerformSpriteBatchDraw (GameTime gameTime) {
            GraphicsDevice.Clear(Color.Transparent);

            const int width = 1280;
            const int height = 720;
            var pos = Vector2.Zero;

            SpriteBatch.Begin(SpriteSortMode.Texture, BlendState.Opaque);
            for (int y = 0; y < height; y++) {
                float fx = 0;
                pos.Y = y;
                for (int x = 0; x < width; x++, fx++) {
                    var tex = ((x % 2) == 0) ? WhitePixel : GrayPixel;
                    pos.X = fx;
                    SpriteBatch.Draw(
                        tex, pos, new Color(255, x % 255, y % 255)
                    );
                }
            }

            DrawPerformanceStats(SpriteBatch);

            SpriteBatch.End();
        }

        int FrameIndex = 0;

        public override void Draw (GameTime gameTime, Frame frame) {
            if ((++FrameIndex % 60) == 0)
                GC.Collect();

            if (false) {
                var stats = RenderManager.GetMemoryStatistics();
                Console.WriteLine(
                    "managed: {0:0000000}kb    vertex: {1:0000000}kb    index: {2:0000000}kb",
                    (stats.ManagedIndexBytes + stats.ManagedVertexBytes) / 1024.0,
                    stats.UnmanagedVertexBytes / 1024.0,
                    stats.UnmanagedIndexBytes / 1024.0
                );
            }

            if (UseSpriteBatch) {
                PerformSpriteBatchDraw(gameTime);
                return;
            }

            ClearBatch.AddNew(frame, -1, Materials.Clear, clearColor: ClearColor);

            var white = new TextureSet(WhitePixel);
            var gray = new TextureSet(GrayPixel);
            const int width = 1280;
            const int height = 720;
            var options = new ParallelOptions {
                MaxDegreeOfParallelism = 8,
            };
            var bbs = new List<BitmapBatch>();
            Parallel.For(
                0, height, options,
                // One batch per worker thread
                () => MakeNewBitmapBatch(frame, Materials),
                (y, loopState, bb) => {
                    // Prevent wildly unbalanced batch sizes (it causes problems)
                    // We shouldn't need to do this but Parallel.For is intrinsically unbalanced
                    if (bb.Count >= (48 * 1280)) {
                        bb.Dispose();
                        bb = MakeNewBitmapBatch(frame, Materials);
                    }

                    var drawCall = new BitmapDrawCall(WhitePixel, new Vector2(0, y)) {
                        MultiplyColor = new Color(255, 0, y % 255),
                    };
                    float fx = 0;
                    var range = bb.ReserveSpace(width);
                    var array = range.Array;
                    var offset = range.Offset;

                    for (int x = 0; x < width; x++, fx++) {
                        drawCall.Textures = ((x % 2) == 0) ? white : gray;
                        drawCall.Position.X = fx;
                        drawCall.MultiplyColor.G = (byte)(x % 255);

                        array[offset + x] = drawCall;
                    }

                    return bb;
                },
                (bb) => {
                    lock (bbs)
                        bbs.Add(bb);
                    bb.Dispose();
                }
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

            static BitmapBatch MakeNewBitmapBatch (Frame frame, DefaultMaterialSet materials) {
                var result = BitmapBatch.New(
                    frame,
                    0,
                    materials.ScreenSpaceBitmap
                // capacity: width * height / 8
                );
                result.DisableSortKeys = true;
                return result;
            }
        }

        private void DrawPerformanceStats (SpriteBatch spriteBatch) {
            const float scale = 1f;
            var text = PerformanceStats.GetText(this);

            // FIXME
            Console.WriteLine(text);

            /*
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
            */
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

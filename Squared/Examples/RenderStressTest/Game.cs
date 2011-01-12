using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Render;
using Squared.Util;
using System.Threading;

#if !XBOX
using System.Threading.Tasks;
#endif

namespace RenderStressTest {
    public class Game : MultithreadedGame {
        public const int Width = 1280;
        public const int Height = 720;

#if XBOX
        // Floating-point computation is pretty slow on the XBox 360. :(
        public const int NumberOfOrbs = 8192;
#else
        public const int NumberOfOrbs = 32768;
#endif

        // The number of spheres to pack into a single GPU draw batch.
        public const int BatchSize = 256;

        // Controls whether we perform updates across multiple threads.
        public const bool ThreadedUpdate = true;
        // Controls whether we paint across multiple threads.
        public const bool ThreadedPaint = true;

        // Set to true to use quads instead of filled rings (looks uglier, runs faster)
        public const bool UseQuads = false;

        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;
        UnorderedList<Orb> Orbs = new UnorderedList<Orb>();

        UnorderedList<Random> RNGs = new UnorderedList<Random>();
        int TotalRNGs, RNGSeed;

        public class UpdateArgs {
            public float Now;
        }

        public class DrawArgs {
            public float Now;
            public Frame Frame;
        }

        ParallelInvoker<UpdateArgs> ParallelUpdater;
        ParallelInvoker<DrawArgs> ParallelDrawer;

        public Game () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = Width;
            Graphics.PreferredBackBufferHeight = Height;
            Graphics.PreferMultiSampling = false;
            Graphics.SynchronizeWithVerticalRetrace = true;

            UseThreadedDraw = ThreadedPaint;
            IsFixedTimeStep = false;
            Content.RootDirectory = "Content";

#if XBOX
            Components.Add(new KiloWatt.Runtime.Support.ThreadPoolComponent(this));
#endif

            ParallelUpdater = new ParallelInvoker<UpdateArgs>(
                this, ParallelUpdateOrbs, ThreadedUpdate
            );
            ParallelDrawer = new ParallelInvoker<DrawArgs>(
                this, ParallelPrepareOrbs, ThreadedPaint
            );
        }

        protected override void Initialize () {
            base.Initialize();

            var rng = new Random();

            float now = (float)Time.Seconds;
            for (int i = 0; i < NumberOfOrbs; i++)
                Orbs.Add(new Orb(rng, now - (float)rng.NextDouble()));

            RNGSeed = rng.Next();

            RNGs.Add(rng);
        }

        protected override void LoadContent () {
            // Load the default materials provided with the rendering library
            //  and provide a projection matrix
            Materials = new DefaultMaterialSet(Content) {
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0.0f, Width, Height, 0.0f, -1.0f, 1.0f
                )
            };

            // Attach a blend state setter to the geometry material so that we get alpha blending
            Materials.WorldSpaceGeometry = new DelegateMaterial(
                Materials.WorldSpaceGeometry, 
                new Action<DeviceManager>[] { 
                    Material.MakeDelegate(BlendState.AlphaBlend)
                },
                new Action<DeviceManager>[0]
            );
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
                this.Exit();

            ParallelUpdater.UserData.Now = (float)Time.Seconds;
            ParallelUpdater.Invoke();

            base.Update(gameTime);
        }

        protected Random GetRNG () {
            Random rng = null;
            lock (RNGs)
                RNGs.TryPopFront(out rng);

            if (rng == null) {
                rng = new Random(TotalRNGs + RNGSeed);
                Interlocked.Increment(ref TotalRNGs);
            }

            return rng;
        }

        protected void ReleaseRNG (Random rng) {
            lock (RNGs)
                RNGs.Add(rng);
        }

        private void ParallelUpdateOrbs (int partitionIndex, int partitionCount, UpdateArgs args) {
            var rng = GetRNG();

            Orb orb;
            using (var e = Orbs.GetParallelEnumerator(partitionIndex, partitionCount))
            while (e.GetNext(out orb)) {
                orb.Update(rng, args.Now);
                e.SetCurrent(ref orb);
            }

            ReleaseRNG(rng);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Color.Black, Materials.Clear);

            ParallelDrawer.UserData.Now = (float)Time.Seconds;
            ParallelDrawer.UserData.Frame = frame;

            ParallelDrawer.Invoke();
        }

        private void ParallelPrepareOrbs (int partitionIndex, int partitionCount, DrawArgs args) {
            Orb orb;
            int i = 0;
            GeometryBatch<VertexPositionColor> batch = null;

            using (var e = Orbs.GetParallelEnumerator(partitionIndex, partitionCount))
            while (e.GetNext(out orb)) {
                if (batch == null)
                    batch = GeometryBatch<VertexPositionColor>.New(args.Frame, 1, Materials.WorldSpaceGeometry);

                // Compute the position of this orb at the current time
                var position = Orb.Interpolator(
                    Orb.InterpolatorSource, ref orb, 
                    0, (args.Now - orb.LastUpdateAt) / orb.Duration
                );

                // AddFilledRing is actually pretty computationally intensive compared to drawing a quad
                //  so on XBox 360 you can get much higher performance by switching from filled rings to quads

                if (UseQuads)
                    batch.AddFilledQuad(
                        position - orb.Size, position + orb.Size, orb.Color
                    );
                else
                    batch.AddFilledRing(
                        position, Vector2.Zero, orb.Size, orb.Color, Color.Transparent
                    );

                i += 1;
                if ((i % BatchSize) == 0) {
                    // Once our batch contains enough objects, we 'dispose' it to tell the renderer that it is done being prepared.
                    // This normally looks like 'using (batch)', which is (IMO) easier to understand... still, kind of a nasty hack.
                    batch.Dispose();
                    batch = null;
                }
            }

            if (batch != null)
                batch.Dispose();
        }
    }

    public struct Orb {
        public const float PixelsPerSecond = 30f;
        public const float MinSize = 2f;
        public const float MaxSize = 10f;
        public const int Brightness = 70;

        public Vector2 LastPosition, NextPosition;
        public Vector2 Size;
        public Color Color;
        public float LastUpdateAt, Duration;

        public static BoundInterpolator<Vector2, Orb> Interpolator;
        public static BoundInterpolatorSource<Vector2, Orb> InterpolatorSource;

        static Orb () {
            Interpolator = Interpolators<Vector2>.Cosine;
            InterpolatorSource = _InterpolatorSource;
        }

        static Vector2 _InterpolatorSource (ref Orb orb, int index) {
            if (index == 0)
                return orb.LastPosition;
            else if (index == 1)
                return orb.NextPosition;
            else
                throw new ArgumentException("This source only provides values for 0 and 1", "index");
        }

        public Orb (Random rng, float now) {
            LastPosition = new Vector2(
                (float)(rng.NextDouble() * Game.Width), (float)(rng.NextDouble() * Game.Height)
            );
            NextPosition = new Vector2(
                (float)(rng.NextDouble() * Game.Width), (float)(rng.NextDouble() * Game.Height)
            );

            LastUpdateAt = now;
            Duration = (NextPosition - LastPosition).Length() / PixelsPerSecond;

            Size = new Vector2((float)rng.NextDouble() * (MaxSize - MinSize) + MinSize);
            int r = rng.Next(0, Brightness);
            int g = rng.Next(0, Brightness);
            int b = rng.Next(Brightness - (r + g), Brightness);
            Color = new Color(r, g, b, 0);
        }

        public void Update (Random rng, float now) {
            if ((now - LastUpdateAt) >= Duration) {
                LastPosition = NextPosition;
                LastUpdateAt = now;

                NextPosition = new Vector2(
                    (float)(rng.NextDouble() * Game.Width), (float)(rng.NextDouble() * Game.Height)
                );
                Duration = (NextPosition - LastPosition).Length() / PixelsPerSecond;
            }
        }
    }
}

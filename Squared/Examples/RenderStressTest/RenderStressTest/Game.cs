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
using System.Threading.Tasks;

namespace RenderStressTest {
    public class Game : MultithreadedGame {
        public const int Width = 1280;
        public const int Height = 720;
        public const int NumberOfOrbs = 32767;
        public const int BatchSize = 256;
        public const bool ThreadedUpdate = true;
        public const bool ThreadedPaint = true;

        public int RNGPermutation = 0;
        public int ThreadCount;
        DefaultMaterialSet Materials;
        GraphicsDeviceManager Graphics;
        UnorderedList<Orb> Orbs;

        public Game () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = Width;
            Graphics.PreferredBackBufferHeight = Height;
            Graphics.PreferMultiSampling = false;
            Graphics.SynchronizeWithVerticalRetrace = true;

            UseThreadedDraw = ThreadedPaint;
            IsFixedTimeStep = false;
            Content.RootDirectory = "Content";

            // Try to pick a good number of threads based on the current CPU count.
            // Too many and we'll get bogged down in scheduling overhead. Too few and we won't benefit from parallelism.
            ThreadCount = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
        }

        protected override void Initialize () {
            base.Initialize();

            Orbs = new UnorderedList<Orb>();

            var rng = new Random();

            float now = (float)Time.Seconds;
            for (int i = 0; i < NumberOfOrbs; i++)
                Orbs.Add(new Orb(rng, now - (float)rng.NextDouble()));
        }

        protected override void LoadContent () {
            Materials = new DefaultMaterialSet(Content);

            Materials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0.0f, Width, Height, 0.0f, -1.0f, 1.0f
            );
            Materials.ViewportPosition = Vector2.Zero;
            Materials.ViewportScale = Vector2.One;

            Materials.WorldSpaceGeometry = new DelegateMaterial(
                Materials.WorldSpaceGeometry, 
                new Action<DeviceManager>[] { (dm) => dm.Device.BlendState = BlendState.AlphaBlend },
                new Action<DeviceManager>[0]
            );
        }

        protected override void UnloadContent () {
        }

        protected override void Update (GameTime gameTime) {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
                this.Exit();

            float now = (float)Time.Seconds;

            if (ThreadedUpdate) {
                var actions = new List<Action>();

                for (int i = 0; i < ThreadCount; i++) {
                    // If we don't copy, all closures will share the same value of i
                    int j = i;
                    actions.Add(() => ParallelUpdateOrbs(j, ThreadCount, now));
                }

                Parallel.Invoke(new ParallelOptions {
                    MaxDegreeOfParallelism = ThreadCount
                }, actions.ToArray());
            } else {
                ParallelUpdateOrbs(0, 1, now);
            }

            RNGPermutation += ThreadCount;

            base.Update(gameTime);
        }

        private void ParallelUpdateOrbs (int partitionIndex, int partitionCount, float now) {
            var rng = new Random(partitionIndex + RNGPermutation);

            Orb orb;
            using (var e = Orbs.GetParallelEnumerator(partitionIndex, partitionCount))
            while (e.GetNext(out orb)) {
                orb.Update(rng, now);
                e.SetCurrent(ref orb);
            }
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Color.Black, Materials.Clear);

            float now = (float)Time.Seconds;

            if (ThreadedPaint) {
                var actions = new List<Action>();

                for (int i = 0; i < ThreadCount; i++) {
                    // If we don't copy, all closures will share the same value of i
                    int j = i;
                    actions.Add(() => ParallelPrepareOrbs(frame, j, ThreadCount, now));
                }

                Parallel.Invoke(new ParallelOptions {
                    MaxDegreeOfParallelism = ThreadCount
                }, actions.ToArray());
            } else {
                ParallelPrepareOrbs(frame, 0, 1, now);
            }
        }

        private void ParallelPrepareOrbs (Frame frame, int partitionIndex, int partitionCount, float now) {
            Orb orb;
            GeometryBatch<VertexPositionColor> batch = null;
            int i = 0;

            using (var e = Orbs.GetParallelEnumerator(partitionIndex, partitionCount))
            while (e.GetNext(out orb)) {
                if (batch == null)
                    batch = GeometryBatch<VertexPositionColor>.New(frame, 1, Materials.WorldSpaceGeometry);

                var position = Orb.Interpolator(Orb.InterpolatorSource, ref orb, 0, (now - orb.LastUpdateAt) / orb.Duration);

                batch.AddFilledRing(
                    position, Vector2.Zero, orb.Size, orb.Color, Color.Transparent
                );

                i += 1;
                if ((i % BatchSize) == 0) {
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
        public const float MinSize = 1.75f;
        public const float MaxSize = 9.0f;
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
            int g = rng.Next(Brightness - r, Brightness);
            int b = rng.Next(Brightness - Math.Max(r, g), Brightness);
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

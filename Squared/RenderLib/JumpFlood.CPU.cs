using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.CoreCLR;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render.DistanceField {
    public struct JumpFloodConfig {
        public const int DefaultChunkSize = 256;

        public Rectangle? Region;
        public ThreadGroup ThreadGroup;
        public int Width, Height;
        private int ChunkSizeOffset;
        public int ChunkSize {
            get => ChunkSizeOffset + DefaultChunkSize;
            set => ChunkSizeOffset = value - DefaultChunkSize;
        }
        private float DistanceScaleMinusOne;
        public float DistanceScale {
            get => DistanceScaleMinusOne + 1;
            set => DistanceScaleMinusOne = value - 1;
        }
        public int? MaxSteps;
        public float DistanceOffset;

        public Rectangle GetRegion () {
            var actualRect = new Rectangle(0, 0, Width, Height);
            if (!Region.HasValue)
                return actualRect;
            return Rectangle.Intersect(Region.Value, actualRect);
        }
    }

    public class JumpFlood {
        const float MaxDistance = 8192;

        unsafe struct InitializeChunkColor : IWorkItem {
            public Color* Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride;

            public void Execute () {
                unchecked {
                    var md2 = ScreenDistanceSquared(MaxDistance, MaxDistance);
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            Output[offset] = new Vector4(MaxDistance, MaxDistance, md2, Input[offset].A > 0 ? 1f : 0f);
                        }
                    }
                }
            }
        }

        struct Jump : IWorkItem {
            public Vector4[] Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride, Step;
            public int MinX, MaxX, MinIndex, MaxIndex;

            public unsafe void Execute () {
                int* neighborOffsets = stackalloc int[8];
                sbyte* neighborXOffsets = stackalloc sbyte[8];
                Vector2* neighborDeltas = stackalloc Vector2[8];
                {
                    for (int y = -1, j = 0; y < 2; y++) {
                        for (int x = -1; x < 2; x++) {
                            if ((x == 0) && (y == 0))
                                continue;

                            neighborDeltas[j] = new Vector2(x * Step, y * Step);
                            neighborXOffsets[j] = (sbyte)(x * Step);
                            neighborOffsets[j++] = (x * Step) + (y * Stride * Step);
                        }
                    }
                }

                unchecked {
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            var self = Input[offset];

                            for (int z = 0; z < 8; z++) {
                                // Detect Y hitting top or bottom
                                var neighborOffset = neighborOffsets[z] + offset;
                                if ((neighborOffset < MinIndex) || (neighborOffset >= MaxIndex))
                                    continue;
                                // Detect X hitting left or right
                                var neighborX = x + neighborXOffsets[z];
                                if ((neighborX < MinX) || (neighborX > MaxX))
                                    continue;

                                var n = Input[neighborOffset];
                                // If we crossed an inside/outside boundary we shouldn't use data from this neighbor
                                if (n.W != self.W)
                                    continue;

                                // Update neighbor x/y distances based on their distance from us
                                n.X += neighborDeltas[z].X;
                                n.Y += neighborDeltas[z].Y;

                                // Compute new distance squared based on the new computed distance
                                var distance = ScreenDistanceSquared(n.X, n.Y);

                                if (distance < self.Z)
                                    self = new Vector4(n.X, n.Y, distance, self.W);
                            }

                            Output[offset] = self;
                        }
                    }
                }
            }
        }

        struct ResolveChunk : IWorkItem {
            public Vector4[] Input;
            public float[] Output;
            public double DistanceScale;
            public double DistanceOffset;
            public int X, Y, Width, Height, Stride;

            public void Execute () {
                unchecked {
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            var input = Input[offset];
                            var distance = Math.Sqrt(input.Z);
                            if (distance < 256)
                                ;
                            if (input.W > 0f)
                                distance = -distance;
                            Output[offset] = (float)(DistanceOffset + (distance * DistanceScale));
                        }
                    }
                }
            }
        }

        static float ScreenDistanceSquared (float x, float y) {
            return (x * x) + (y * y);
        }

        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image.
        /// </summary>
        /// <param name="input">An RGBA image to act as the source for alpha</param>
        /// <returns>A signed distance field</returns>
        public static unsafe float[] GenerateDistanceField (Color* input, JumpFloodConfig config) {
            Vector4[] buf1 = new Vector4[config.Width * config.Height], buf2 = new Vector4[config.Width * config.Height];
            var result = new float[config.Width * config.Height];

            Initialize(input, buf1, config);
            Vector4[] inBuffer = buf1, outBuffer = buf2;
            var sw = Stopwatch.StartNew();
            for (
                int i = 0, numSteps = Math.Min(
                    config.MaxSteps ?? 32,
                    Math.Max(BitOperations.Log2Ceiling((uint)config.Width), BitOperations.Log2Ceiling((uint)config.Height))
                ), step = 1;
                i < numSteps; i++, step *= 2
            ) {
                PerformJump(inBuffer, outBuffer, config, step);

                var swap = inBuffer;
                inBuffer = outBuffer;
                outBuffer = swap;
            }
            Resolve(outBuffer, result, config);
            Debug.WriteLine($"Generating {config.Width}x{config.Height} distance field took {sw.ElapsedMilliseconds}ms");

            return result;
        }

        static unsafe void Initialize (Color* input, Vector4[] output, JumpFloodConfig config) {
            var rgn = config.GetRegion();
            var chunkSize = config.ChunkSize;
            var queue = config.ThreadGroup?.GetQueueForType<InitializeChunkColor>();
            for (int y = 0; y < rgn.Height; y += chunkSize) {
                for (int x = 0; x < rgn.Width; x += chunkSize) {
                    var workItem = new InitializeChunkColor {
                        X = x + rgn.Left, Y = y + rgn.Top,
                        Width = Math.Min(chunkSize, rgn.Width - x),
                        Height = Math.Min(chunkSize, rgn.Height - y),
                        Stride = config.Width,
                        Input = input,
                        Output = output
                    };
                    if (queue != null)
                        queue.Enqueue(ref workItem, false);
                    else
                        workItem.Execute();
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue.WaitUntilDrained();
        }

        static void PerformJump (Vector4[] input, Vector4[] output, JumpFloodConfig config, int step) {
            var rgn = config.GetRegion();
            var chunkSize = config.ChunkSize;
            var queue = config.ThreadGroup?.GetQueueForType<Jump>();
            for (int y = 0; y < rgn.Height; y += chunkSize) {
                for (int x = 0; x < rgn.Width; x += chunkSize) {
                    var workItem = new Jump {
                        Step = step,
                        X = x + rgn.Left, Y = y + rgn.Top,
                        Width = Math.Min(chunkSize, rgn.Width - x),
                        Height = Math.Min(chunkSize, rgn.Height - y),
                        MinX = rgn.Left, MaxX = rgn.Right - 1,
                        MinIndex = (rgn.Top * config.Width) + rgn.Left,
                        MaxIndex = ((rgn.Bottom - 1) * config.Width) + rgn.Right - 1,
                        Stride = config.Width,
                        Input = input,
                        Output = output
                    };
                    if (queue != null)
                        queue.Enqueue(ref workItem, false);
                    else
                        workItem.Execute();
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue.WaitUntilDrained();
        }

        static unsafe void Resolve (Vector4[] input, float[] output, JumpFloodConfig config) {
            var rgn = config.GetRegion();
            var chunkSize = config.ChunkSize;
            var queue = config.ThreadGroup?.GetQueueForType<ResolveChunk>();
            for (int y = 0; y < rgn.Height; y += chunkSize) {
                for (int x = 0; x < rgn.Width; x += chunkSize) {
                    var workItem = new ResolveChunk {
                        X = x + rgn.Left, Y = y + rgn.Top,
                        Width = Math.Min(chunkSize, rgn.Width - x),
                        Height = Math.Min(chunkSize, rgn.Height - y),
                        Stride = config.Width,
                        Input = input,
                        Output = output,
                        DistanceScale = config.DistanceScale,
                        DistanceOffset = config.DistanceOffset
                    };
                    if (queue != null)
                        queue.Enqueue(ref workItem, false);
                    else
                        workItem.Execute();
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue.WaitUntilDrained();
        }
    }
}

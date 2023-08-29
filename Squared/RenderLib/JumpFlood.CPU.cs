using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.CoreCLR;
using Squared.Render.Convenience;
using Squared.Threading;
using Squared.Util;

namespace Squared.Render.DistanceField {
    public struct JumpFloodConfig {
        // TODO: Select a better value - maybe a non-power-of-two one to minimize false cache sharing?
        // Basic testing in single and multi threaded scenarios shows little difference though
        public const int DefaultChunkSize = 64;

        public Rectangle? Region;
        public ThreadGroup ThreadGroup;
        public int Width, Height;
        private int ChunkSizeOffset;
        public int ChunkSize {
            get => ChunkSizeOffset + DefaultChunkSize;
            set => ChunkSizeOffset = value - DefaultChunkSize;
        }

        public Rectangle GetRegion () {
            var actualRect = new Rectangle(0, 0, Width, Height);
            if (!Region.HasValue)
                return actualRect;
            return Rectangle.Intersect(Region.Value, actualRect);
        }
    }

    public static partial class JumpFlood {
        const float MaxDistance = 2048;

        private static int GetStepCount (int width, int height) {
            int l2x = BitOperations.Log2Ceiling((uint)width),
                l2y = BitOperations.Log2Ceiling((uint)height),
                l2 = Math.Max(l2x, l2y),
                numSteps = Math.Min(l2, 16);
            return numSteps;
        }

        private static int GetStepSize (int width, int height, int index) {
            int l2x = BitOperations.Log2Ceiling((uint)width),
                l2y = BitOperations.Log2Ceiling((uint)height),
                l2 = Math.Max(l2x, l2y),
                numSteps = Math.Min(l2 + 1, 16);

            // 1+JFA: 1, N/2, N/4, ..., 1
            int result;
            if (index <= 0)
                result = 1;
            else
                result = 1 << (l2 - index - 1);

            if (result < 1)
                result = 1;

            return result;
        }

        unsafe struct InitializeChunkColor : IWorkItem {
            public Color* Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride;

            public void Execute (ThreadGroup group) {
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

        unsafe struct InitializeChunkVector4 : IWorkItem {
            public Vector4* Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride;

            public void Execute (ThreadGroup group) {
                unchecked {
                    var md2 = ScreenDistanceSquared(MaxDistance, MaxDistance);
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            Output[offset] = new Vector4(MaxDistance, MaxDistance, md2, Input[offset].W > 0 ? 1f : 0f);
                        }
                    }
                }
            }
        }

        unsafe struct InitializeChunkByte : IWorkItem {
            public byte* Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride;

            public void Execute (ThreadGroup group) {
                unchecked {
                    var md2 = ScreenDistanceSquared(MaxDistance, MaxDistance);
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            Output[offset] = new Vector4(MaxDistance, MaxDistance, md2, Input[offset] > 0 ? 1f : 0f);
                        }
                    }
                }
            }
        }

        unsafe struct InitializeChunkSingle : IWorkItem {
            public float* Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride;

            public void Execute (ThreadGroup group) {
                unchecked {
                    var md2 = ScreenDistanceSquared(MaxDistance, MaxDistance);
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            Output[offset] = new Vector4(MaxDistance, MaxDistance, md2, Input[offset] > 0 ? 1f : 0f);
                        }
                    }
                }
            }
        }

        struct Jump : IWorkItem {
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            struct Offsets {
                public int Offset;
                public short XOffset;
                public float XDelta;
                public float YDelta;
            }

            public Vector4[] Input;
            public Vector4[] Output;
            public int X, Y, Width, Height, Stride, Step;
            public int MinX, MaxX, MinIndex, MaxIndex;

            public unsafe void Execute (ThreadGroup group) {
                Offsets* offsets = stackalloc Offsets[8];
                {
                    for (int y = -1, j = 0; y < 2; y++) {
                        for (int x = -1; x < 2; x++) {
                            if ((x == 0) && (y == 0))
                                continue;

                            offsets[j++] = new Offsets {
                                Offset = ((x * Step) + (y * Stride * Step)),
                                XOffset = (short)(x * Step),
                                XDelta = x * Step,
                                YDelta = y * Step
                            };
                        }
                    }
                }

                unchecked {
                    for (int y = 0; y < Height; y++) {
                        var yW = (y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            ref var self = ref Output[offset];
                            self = Input[offset];

                            for (int z = 0; z < 8; z++) {
                                ref var data = ref offsets[z];
                                // Detect Y hitting top or bottom
                                var neighborOffset = data.Offset + offset;
                                // Detect X hitting left or right
                                var neighborX = (x + X) + data.XOffset;
                                if ((neighborOffset < MinIndex) || (neighborOffset >= MaxIndex) ||
                                    (neighborX < MinX) || (neighborX > MaxX))
                                    continue;

                                ref var n = ref Input[neighborOffset];
                                // If we crossed an inside/outside boundary, treat this sample like it has zero distance
                                if (n.W != self.W) {
                                    var distance = ScreenDistanceSquared(data.XDelta, data.YDelta);
                                    if (distance < self.Z) {
                                        self.X = data.XDelta;
                                        self.Y = data.YDelta;
                                        self.Z = distance;
                                    }
                                } else {
                                    float nx = n.X + data.XDelta, ny = n.Y + data.YDelta;
                                    var distance = ScreenDistanceSquared(nx, ny);
                                    if (distance < self.Z) {
                                        self.X = nx;
                                        self.Y = ny;
                                        self.Z = distance;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        struct ResolveChunk : IWorkItem {
            public Vector4[] Input;
            public float[] Output;
            public int X, Y, Width, Height, Stride;

            public void Execute (ThreadGroup group) {
                unchecked {
                    for (int _y = 0; _y < Height; _y++) {
                        var yW = (_y + Y) * Stride;
                        for (int x = 0; x < Width; x++) {
                            var offset = (x + X) + yW;
                            var input = Input[offset];
                            var distance = (float)Math.Sqrt(input.Z);
                            Output[offset] = distance * (input.W > 0f ? -1f : 1f);
                        }
                    }
                }
            }
        }

        [TargetedPatchingOptOut("")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ScreenDistanceSquared (float x, float y) {
            return (x * x) + (y * y);
        }

        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image, using the CPU.
        /// </summary>
        /// <param name="input">A grayscale image to act as the source for alpha</param>
        /// <returns>A signed distance field</returns>
        public static unsafe float[] GenerateDistanceField (byte* input, JumpFloodConfig config) {
            var buf1 = new Vector4[config.Width * config.Height];
            Initialize(input, buf1, config);
            return GenerateEpilogue(buf1, config);
        }

        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image, using the CPU.
        /// </summary>
        /// <param name="input">An RGBA image to act as the source for alpha</param>
        /// <returns>A signed distance field</returns>
        public static unsafe float[] GenerateDistanceField (Color* input, JumpFloodConfig config) {
            var buf1 = new Vector4[config.Width * config.Height];
            Initialize(input, buf1, config);
            return GenerateEpilogue(buf1, config);
        }

        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image, using the CPU.
        /// </summary>
        /// <param name="input">A grayscale image to act as the source for alpha</param>
        /// <returns>A signed distance field</returns>
        public static unsafe float[] GenerateDistanceField (float* input, JumpFloodConfig config) {
            var buf1 = new Vector4[config.Width * config.Height];
            Initialize(input, buf1, config);
            return GenerateEpilogue(buf1, config);
        }

        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image, using the CPU.
        /// </summary>
        /// <param name="input">An RGBA image to act as the source for alpha</param>
        /// <returns>A signed distance field</returns>
        public static unsafe float[] GenerateDistanceField (Vector4* input, JumpFloodConfig config) {
            var buf1 = new Vector4[config.Width * config.Height];
            Initialize(input, buf1, config);
            return GenerateEpilogue(buf1, config);
        }

        private static unsafe float[] GenerateEpilogue (Vector4[] buf1, JumpFloodConfig config) {
            var buf2 = new Vector4[config.Width * config.Height];
            var result = new float[config.Width * config.Height];
            Vector4[] inBuffer = buf1, outBuffer = buf2;
            var sw = Stopwatch.StartNew();
            for (int i = 0, stepCount = GetStepCount(config.Width, config.Height); i < stepCount; i++) {
                int step = GetStepSize(config.Width, config.Height, i);
                PerformJump(inBuffer, outBuffer, config, step);

                var swap = inBuffer;
                inBuffer = outBuffer;
                outBuffer = swap;
            }
            Resolve(outBuffer, result, config);
            Debug.WriteLine($"Generating {config.Width}x{config.Height} distance field took {sw.ElapsedMilliseconds}ms");
            return result;
        }

        static unsafe void Initialize (byte* input, Vector4[] output, JumpFloodConfig config) {
            var rgn = config.GetRegion();
            var chunkSize = config.ChunkSize;
            var queue = config.ThreadGroup?.GetQueueForType<InitializeChunkByte>();
            for (int y = 0; y < rgn.Height; y += chunkSize) {
                for (int x = 0; x < rgn.Width; x += chunkSize) {
                    var workItem = new InitializeChunkByte {
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
                        workItem.Execute(config.ThreadGroup);
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue?.WaitUntilDrained();
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
                        workItem.Execute(config.ThreadGroup);
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue?.WaitUntilDrained();
        }

        static unsafe void Initialize (float* input, Vector4[] output, JumpFloodConfig config) {
            var rgn = config.GetRegion();
            var chunkSize = config.ChunkSize;
            var queue = config.ThreadGroup?.GetQueueForType<InitializeChunkSingle>();
            for (int y = 0; y < rgn.Height; y += chunkSize) {
                for (int x = 0; x < rgn.Width; x += chunkSize) {
                    var workItem = new InitializeChunkSingle {
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
                        workItem.Execute(config.ThreadGroup);
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue?.WaitUntilDrained();
        }

        static unsafe void Initialize (Vector4* input, Vector4[] output, JumpFloodConfig config) {
            var rgn = config.GetRegion();
            var chunkSize = config.ChunkSize;
            var queue = config.ThreadGroup?.GetQueueForType<InitializeChunkVector4>();
            for (int y = 0; y < rgn.Height; y += chunkSize) {
                for (int x = 0; x < rgn.Width; x += chunkSize) {
                    var workItem = new InitializeChunkVector4 {
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
                        workItem.Execute(config.ThreadGroup);
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue?.WaitUntilDrained();
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
                        workItem.Execute(config.ThreadGroup);
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue?.WaitUntilDrained();
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
                        Output = output
                    };
                    if (queue != null)
                        queue.Enqueue(ref workItem, false);
                    else
                        workItem.Execute(config.ThreadGroup);
                }
                config.ThreadGroup?.NotifyQueuesChanged(false);
            }
            config.ThreadGroup?.NotifyQueuesChanged(true);
            queue?.WaitUntilDrained();
        }
    }
}

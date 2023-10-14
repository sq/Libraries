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
    public static partial class JumpFlood {
        public class GPUScratchSurfaces : IDisposable {
            public readonly RenderCoordinator Coordinator;

            internal RenderTarget2D InBuffer, OutBuffer;

            internal GPUScratchSurfaces (RenderCoordinator coordinator) {
                Coordinator = coordinator;
            }

            internal void Resize (int width, int height) {
                if (
                    (InBuffer != null) && 
                    ((InBuffer.Width < width) || (InBuffer.Height < height))
                ) {
                    var a = InBuffer;
                    var b = OutBuffer;
                    InBuffer = OutBuffer = null;
                    Coordinator.DisposeResource(a);
                    Coordinator.DisposeResource(b);
                }

                if (InBuffer != null)
                    return;

                lock (Coordinator.CreateResourceLock) {
                    InBuffer = new RenderTarget2D(Coordinator.Device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents) {
                        Name = "JumpFlood.InBuffer",
                    };
                    OutBuffer = new RenderTarget2D(Coordinator.Device, width, height, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents) {
                        Name = "JumpFlood.OutBuffer",
                    };
                }
            }

            public void Dispose () {
                if (Coordinator != null) {
                    Coordinator.DisposeResource(ref InBuffer);
                    Coordinator.DisposeResource(ref OutBuffer);
                } else {
                    InBuffer?.Dispose();
                    OutBuffer?.Dispose();
                    InBuffer = OutBuffer = null;
                }
            }
        }

        const int SizeRounding = 8, SizeRoundingMinusOne = SizeRounding - 1;

        public static GPUScratchSurfaces AllocateScratchSurfaces (RenderCoordinator coordinator, int width, int height) {
            var result = new GPUScratchSurfaces(coordinator);
            width = ((width + SizeRoundingMinusOne) / SizeRounding) * SizeRounding;
            height = ((height + SizeRoundingMinusOne) / SizeRounding) * SizeRounding;
            result.Resize(width, height);
            return result;
        }

        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image, using the GPU.
        /// </summary>
        /// <param name="scratchSurfaces">The scratch surfaces used by the generation process. You are responsible for disposing these next frame.</param>
        public static void GenerateDistanceField (
            ref ImperativeRenderer renderer, Texture2D input, RenderTarget2D output, ref GPUScratchSurfaces scratchSurfaces,
            int? layer = null, Rectangle? region = null, float minimumAlpha = 0.0f, float smoothingLevel = 0.0f
        ) {
            var _region = region ?? new Rectangle(0, 0, output.Width, output.Height);

            var coordinator = renderer.Container.Coordinator;
            scratchSurfaces = scratchSurfaces ?? new GPUScratchSurfaces(coordinator);
            int width = ((_region.Width + SizeRoundingMinusOne) / SizeRounding) * SizeRounding,
                height = ((_region.Height + SizeRoundingMinusOne) / SizeRounding) * SizeRounding;
            scratchSurfaces.Resize(width, height);

            var vt = ViewTransform.CreateOrthographic(scratchSurfaces.InBuffer.Width, scratchSurfaces.InBuffer.Height);

            var group = renderer.MakeSubgroup(layer: layer);
            group.Parameters.Clear();

            var initGroup = group.ForRenderTarget(scratchSurfaces.InBuffer, viewTransform: vt);
            initGroup.Clear(layer: -1, value: new Vector4(MaxDistance, MaxDistance, MaxDistance, 0f));
            var initMaterial = renderer.Materials.JumpFloodInit;
            initGroup.Parameters.Add("Smoothing", smoothingLevel > 0.01f);
            initGroup.Draw(
                input, new Vector2(-_region.Left, -_region.Top), material: initMaterial, userData: new Vector4(minimumAlpha, Math.Min(smoothingLevel, 0.99f - minimumAlpha), 0, 0),
                layer: 0
            );

            _region = new Rectangle(0, 0, _region.Width, _region.Height);

            RenderTarget2D result = null;

            for (int i = 0, stepCount = GetStepCount(_region.Width, _region.Height); i < stepCount; i++) {
                result = scratchSurfaces.OutBuffer;
                var jumpGroup = group.ForRenderTarget(result, viewTransform: vt);

                int step = GetStepSize(_region.Width, _region.Height, i);
                jumpGroup.Clear(layer: -1, color: new Color(step / 32f, 0, 0, 1f));
                jumpGroup.Draw(
                    scratchSurfaces.InBuffer, Vector2.Zero, sourceRectangle: _region,
                    userData: new Vector4(step / (float)scratchSurfaces.InBuffer.Width, step / (float)scratchSurfaces.InBuffer.Height, step, 0), 
                    samplerState: SamplerState.PointClamp,
                    material: renderer.Materials.JumpFloodJump,
                    layer: 0
                );

                var swap = scratchSurfaces.InBuffer;
                scratchSurfaces.InBuffer = scratchSurfaces.OutBuffer;
                scratchSurfaces.OutBuffer = swap;
            }

            var resolveGroup = group.ForRenderTarget(output, viewTransform: ViewTransform.CreateOrthographic(output.Width, output.Height));
            resolveGroup.Clear(layer: -1, color: Color.Transparent);
            resolveGroup.Draw(result, Vector2.Zero, material: renderer.Materials.JumpFloodResolve, layer: 0);
        }

        public static void GenerateDistanceField (
            ref ImperativeRenderer renderer, Texture2D input, RenderTarget2D output,
            int? layer = null, Rectangle? region = null, float minimumAlpha = 0.0f, float smoothingLevel = 0.0f
        ) {
            GPUScratchSurfaces scratch = null;
            GenerateDistanceField(ref renderer, input, output, ref scratch, layer, region, minimumAlpha, smoothingLevel);
            var coordinator = renderer.Container.Coordinator;
            coordinator.DisposeResource(scratch);
        }
    }
}

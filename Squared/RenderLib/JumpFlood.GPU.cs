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
        /// <summary>
        /// Generates a distance field populated based on the alpha channel of an input image, using the GPU.
        /// </summary>
        /// <param name="container"></param>
        /// <param name="layer"></param>
        /// <param name="texture"></param>
        /// <param name="textureRectangle"></param>
        /// <param name="output"></param>
        public static void GenerateDistanceField (
            ref ImperativeRenderer renderer, Texture2D input, RenderTarget2D output,
            int? layer = null, Rectangle? region = null, int? maxSteps = null, float minimumAlpha = 0.0f
        ) {
            var _region = region ?? new Rectangle(0, 0, output.Width, output.Height);

            var coordinator = renderer.Container.Coordinator;
            RenderTarget2D inBuffer, outBuffer;
            lock (coordinator.CreateResourceLock) {
                inBuffer = new RenderTarget2D(coordinator.Device, _region.Width, _region.Height, false, SurfaceFormat.Vector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                outBuffer = new RenderTarget2D(coordinator.Device, _region.Width, _region.Height, false, SurfaceFormat.Vector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            }

            var vt = ViewTransform.CreateOrthographic(_region.Width, _region.Height);

            var group = renderer.MakeSubgroup(layer: layer);
            var initGroup = group.ForRenderTarget(inBuffer, viewTransform: vt);
            initGroup.Draw(input, new Vector2(-_region.Left, -_region.Top), material: renderer.Materials.JumpFloodInit, userData: new Vector4(minimumAlpha, 0, 0, 0));

            for (
                int i = 0, 
                    l2x = BitOperations.Log2Ceiling((uint)_region.Width), 
                    l2y = BitOperations.Log2Ceiling((uint)_region.Height),
                    l2 = Math.Min(l2x, l2y),
                    numSteps = Math.Min(l2, maxSteps ?? 32); 
                i < numSteps; i++
            ) {
                int step = 1 << (l2 - i - 1);
                var jumpGroup = group.ForRenderTarget(outBuffer, viewTransform: vt);
                jumpGroup.Clear(layer: -1, color: new Color(step / 32f, 0, 0, 1f));
                jumpGroup.Draw(
                    inBuffer, Vector2.Zero, 
                    userData: new Vector4(step / (float)inBuffer.Width, step / (float)inBuffer.Height, step, 0), 
                    material: renderer.Materials.JumpFloodJump
                );

                var swap = inBuffer;
                inBuffer = outBuffer;
                outBuffer = swap;
            }

            var resolveGroup = group.ForRenderTarget(output, viewTransform: vt);
            resolveGroup.Clear(layer: -1, color: Color.Transparent);
            resolveGroup.Draw(outBuffer, Vector2.Zero, material: renderer.Materials.JumpFloodResolve);

            coordinator.AfterPresent(() => {
                coordinator.DisposeResource(inBuffer);
                coordinator.DisposeResource(outBuffer);
            });
        }
    }
}

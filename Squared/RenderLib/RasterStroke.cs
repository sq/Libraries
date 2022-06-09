using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.Internal;
using Squared.Util;
using GeometryVertex = Microsoft.Xna.Framework.Graphics.VertexPositionColor;

namespace Squared.Render.RasterStroke {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RasterStrokeVertex : IVertexType {
        public Vector4 PointsAB;
        public Vector4 ColorA, ColorB;
        public short   Unused, WorldSpace;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static RasterStrokeVertex () {
            var tThis = typeof(RasterStrokeVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "PointsAB").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "ColorA").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "ColorB").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Color, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Unused").ToInt32(),
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 1 )
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public override string ToString () {
            return string.Format("{0},{1} - {2},{3}", PointsAB.X, PointsAB.Y, PointsAB.Z, PointsAB.W);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public struct RasterBrush {
        public AbstractTextureReference NozzleAtlas;
        public SamplerState NozzleSamplerState;

        private int NozzleCountXMinusOne, NozzleCountYMinusOne;
        public int NozzleCountX {
            get => NozzleCountXMinusOne + 1;
            set => NozzleCountXMinusOne = value - 1;
        }
        public int NozzleCountY {
            get => NozzleCountYMinusOne + 1;
            set => NozzleCountYMinusOne = value - 1;
        }
        public float Size, Spacing, RotationRateRadians, Flow;
        public float RotationRateDegrees {
            get => MathHelper.ToDegrees(RotationRateRadians);
            set => RotationRateRadians = MathHelper.ToRadians(value);
        }
        // public AbstractTextureReference FillTexture;

        public override bool Equals (object obj) {
            if (obj is RasterBrush rb)
                return Equals(rb);
            else
                return false;
        }

        public override int GetHashCode () {
            return NozzleAtlas.GetHashCode();
        }

        public bool Equals (in RasterBrush rhs) {
            return (NozzleAtlas == rhs.NozzleAtlas) &&
                (NozzleCountXMinusOne == rhs.NozzleCountXMinusOne) &&
                (NozzleCountYMinusOne == rhs.NozzleCountYMinusOne) &&
                (Size == rhs.Size) &&
                (Spacing == rhs.Spacing);
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct RasterStrokeDrawCall {
        public bool WorldSpace;

        /// <summary>
        /// The beginning of the stroke.
        /// </summary>
        public Vector2 A;
        /// <summary>
        /// The end of the stroke.
        /// </summary>
        public Vector2 B;

        /// <summary>
        /// The premultiplied sRGB color of the start of the stroke.
        /// </summary>
        public Vector4 ColorA4;
        /// <summary>
        /// The premultiplied sRGB color of the end of the stroke.
        /// </summary>
        public Vector4 ColorB4;

        public pSRGBColor ColorA {
            get {
                return new pSRGBColor(ColorA4);
            }
            set {
                ColorA4 = value.ToVector4();
            }
        }

        public pSRGBColor ColorB {
            get {
                return new pSRGBColor(ColorB4);
            }
            set {
                ColorB4 = value.ToVector4();
            }
        }

        public int SortKey;
        /// <summary>
        /// If set, blending between inner/outer/outline colors occurs in linear space.
        /// </summary>
        public bool BlendInLinearSpace;

        internal int Index;
    }

    public class RasterStrokeBatch : ListBatch<RasterStrokeDrawCall> {
        private sealed class RasterStrokeDrawCallSorter : IRefComparer<RasterStrokeDrawCall>, IComparer<RasterStrokeDrawCall> {
            public int Compare (ref RasterStrokeDrawCall lhs, ref RasterStrokeDrawCall rhs) {
                unchecked {
                    var result = lhs.SortKey - rhs.SortKey;
                    /*
                    if ((result == 0) && !object.ReferenceEquals(lhs.Brush, rhs.Brush))
                        fixme
                    */
                    if (result == 0)
                        result = lhs.Index - rhs.Index;
                    return result;
                }
            }

            public int Compare (RasterStrokeDrawCall lhs, RasterStrokeDrawCall rhs) {
                return Compare(ref lhs, ref rhs);
            }
        }

        private BufferGenerator<RasterStrokeVertex> _BufferGenerator = null;
        private BufferGenerator<CornerVertex>.SoftwareBuffer _CornerBuffer = null;

        protected static ThreadLocal<VertexBufferBinding[]> _ScratchBindingArray = 
            new ThreadLocal<VertexBufferBinding[]>(() => new VertexBufferBinding[2]);

        internal ArrayPoolAllocator<RasterStrokeVertex> VertexAllocator;
        internal ISoftwareBuffer _SoftwareBuffer;

        public DefaultMaterialSet Materials;

        private static readonly RasterStrokeDrawCallSorter StrokeDrawCallSorter = new RasterStrokeDrawCallSorter();

        const int MaxVertexCount = 65535;

        const int CornerBufferRepeatCount = 1;
        const int CornerBufferVertexCount = CornerBufferRepeatCount * 4;
        const int CornerBufferIndexCount = CornerBufferRepeatCount * 6;
        const int CornerBufferPrimCount = CornerBufferRepeatCount * 2;

        // public SamplerState SamplerState;
        public bool BlendInLinearSpace;
        public DepthStencilState DepthStencilState;
        public BlendState BlendState;
        public RasterizerState RasterizerState;
        public RasterBrush Brush;

        static RasterStrokeBatch () {
            AdjustPoolCapacities(1024, null, 512, 16);
        }

        public void Initialize (IBatchContainer container, int layer, DefaultMaterialSet materials) {
            base.Initialize(container, layer, materials.RasterStrokeLineSegment, true);

            Materials = materials;

            BlendInLinearSpace = true;
            DepthStencilState = null;
            BlendState = null;
            RasterizerState = null;
            Brush = default;

            if (VertexAllocator == null)
                VertexAllocator = container.RenderManager.GetArrayAllocator<RasterStrokeVertex>();
        }

        new public static void AdjustPoolCapacities (
            int? smallItemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            ListBatch<RasterStrokeDrawCall>.AdjustPoolCapacities(smallItemSizeLimit, largeItemSizeLimit, smallPoolCapacity, largePoolCapacity);
        }

        protected override void Prepare (PrepareManager manager) {
            var count = _DrawCalls.Count;
            var vertexCount = count;
            if (count > 0) {
                _DrawCalls.Sort(StrokeDrawCallSorter);

                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<RasterStrokeVertex>>();
                _CornerBuffer = Container.Frame.PrepareData.GetCornerBuffer(Container, CornerBufferRepeatCount);
                var swb = _BufferGenerator.Allocate(vertexCount, 1);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<RasterStrokeVertex>(swb.Vertices);
                var vw = vb.GetWriter(count, clear: false);

                for (int i = 0, j = 0; i < count; i++, j+=4) {
                    ref var dc = ref _DrawCalls.Item(i);

                    var vert = new RasterStrokeVertex {
                        PointsAB = new Vector4(dc.A.X, dc.A.Y, dc.B.X, dc.B.Y),
                        ColorA = dc.ColorA4,
                        ColorB = dc.ColorB4,
                        WorldSpace = (short)(dc.WorldSpace ? 1 : 0)
                    };
                    vw.Write(vert);
                }

                NativeBatch.RecordPrimitives(count * CornerBufferPrimCount);
            }
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            var count = _DrawCalls.Count;
            if (count > 0) {
                // manager.Device.SetStringMarkerEXT(this.ToString());
                var device = manager.Device;

                VertexBuffer vb, cornerVb;
                DynamicIndexBuffer ib, cornerIb;

                var cornerHwb = _CornerBuffer.HardwareBuffer;
                cornerHwb.SetActive();
                cornerHwb.GetBuffers(out cornerVb, out cornerIb);
                if (device.Indices != cornerIb)
                    device.Indices = cornerIb;

                var hwb = _SoftwareBuffer.HardwareBuffer;
                if (hwb == null)
                    throw new ThreadStateException("Could not get a hardware buffer for this batch");

                hwb.SetActive();
                hwb.GetBuffers(out vb, out ib);

                var scratchBindings = _ScratchBindingArray.Value;

                scratchBindings[0] = cornerVb;
                // scratchBindings[1] = new VertexBufferBinding(vb, _SoftwareBuffer.HardwareVertexOffset, 1);

                // if the render target/backbuffer is sRGB, we need to generate output in the correct color space
                var format = (manager.CurrentRenderTarget?.Format ?? manager.Device.PresentationParameters.BackBufferFormat);
                var isSrgbRenderTarget = 
                    (format == Evil.TextureUtils.ColorSrgbEXT) && (format != SurfaceFormat.Color);

                var ep = Material.Effect.Parameters;
                ep["NozzleCountXy"].SetValue(new Vector2(Brush.NozzleCountX, Brush.NozzleCountY));
                ep["Params1"].SetValue(new Vector4(Brush.Size, Brush.Spacing, Brush.RotationRateRadians, Brush.Flow));
                ep["BlendInLinearSpace"].SetValue(BlendInLinearSpace);
                ep["OutputInLinearSpace"].SetValue(isSrgbRenderTarget);

                manager.ApplyMaterial(Material, ref MaterialParameters);

                if (BlendState != null)
                    device.BlendState = BlendState;
                if (DepthStencilState != null)
                    device.DepthStencilState = DepthStencilState;
                if (RasterizerState != null)
                    device.RasterizerState = RasterizerState;

                // FIXME: why the hell
                device.Textures[0] = Brush.NozzleAtlas.Instance;
                device.SamplerStates[0] = Brush.NozzleSamplerState ?? SamplerState.LinearWrap;

                scratchBindings[1] = new VertexBufferBinding(
                    vb, _SoftwareBuffer.HardwareVertexOffset, 1
                );

                device.SetVertexBuffers(scratchBindings);

                device.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList, 
                    0, _CornerBuffer.HardwareVertexOffset, CornerBufferVertexCount, 
                    _CornerBuffer.HardwareIndexOffset, CornerBufferPrimCount, 
                    Count
                );

                device.Textures[0] = null;

                NativeBatch.RecordCommands(Count);
                hwb.SetInactive();
                cornerHwb.SetInactive();

                device.SetVertexBuffer(null);
            }

            _SoftwareBuffer = null;
        }

        new public void Add (in RasterStrokeDrawCall dc) {
            var result = dc;
            // FIXME
            result.Index = _DrawCalls.Count;
            _DrawCalls.Add(in result);
        }

        public static RasterStrokeBatch New (
            IBatchContainer container, int layer, DefaultMaterialSet materials, RasterBrush brush,
            RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (materials == null)
                throw new ArgumentNullException("materials");

            var result = container.RenderManager.AllocateBatch<RasterStrokeBatch>();
            result.Initialize(container, layer, materials);
            result.Brush = brush;
            result.RasterizerState = rasterizerState;
            result.DepthStencilState = depthStencilState;
            result.BlendState = blendState;
            result.CaptureStack(0);
            return result;
        }

        protected override void OnReleaseResources () {
            base.OnReleaseResources();
        }
    }
}
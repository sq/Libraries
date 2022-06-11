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

namespace Squared.Render.RasterStroke {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RasterStrokeVertex : IVertexType {
        public Vector4 PointsAB;
        public Vector4 ColorA, ColorB;
        public Vector4 Seed;
        public Vector4 Taper;
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
                new VertexElement( Marshal.OffsetOf(tThis, "Seed").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Taper").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1 ),
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

    public struct BrushDynamics {
        public float Constant;
        public float TaperFactor;
        public float Increment;
        public float NoiseFactor;
        public float AngleFactor;

        public override string ToString () {
            return $"<dynamics(({Constant} * taper({TaperFactor})) + step({Increment}) + noise({NoiseFactor}) + angle({AngleFactor}))>";
        }

        public override int GetHashCode () {
            // FIXME
            return Constant.GetHashCode();
        }

        public bool Equals (in BrushDynamics rhs) {
            return (Constant == rhs.Constant) &&
                (TaperFactor == rhs.TaperFactor) &&
                (Increment == rhs.Increment) &&
                (NoiseFactor == rhs.NoiseFactor) &&
                (AngleFactor == rhs.AngleFactor);
        }        

        public override bool Equals (object obj) {
            if (obj is BrushDynamics bd)
                return Equals(bd);
            else
                return false;
        }

        public static bool operator != (BrushDynamics lhs, BrushDynamics rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (BrushDynamics lhs, BrushDynamics rhs) {
            return lhs.Equals(rhs);
        }

        public Vector4 ToVector4 () {
            return new Vector4(TaperFactor, Increment, NoiseFactor, AngleFactor);
        }

        public static implicit operator BrushDynamics (float constant) {
            return new BrushDynamics {
                Constant = constant
            };
        }
    }

    public struct RasterBrush {
        public AbstractTextureReference NozzleAtlas;
        public SamplerState NozzleSamplerState;

        private int _NozzleCountXMinusOne, _NozzleCountYMinusOne;
        public int NozzleCountX {
            get => _NozzleCountXMinusOne + 1;
            set => _NozzleCountXMinusOne = value - 1;
        }
        public int NozzleCountY {
            get => _NozzleCountYMinusOne + 1;
            set => _NozzleCountYMinusOne = value - 1;
        }

        public static readonly BrushDynamics DefaultScale = new BrushDynamics {
            Constant = 1,
        };
        public static readonly BrushDynamics DefaultBrushIndex = new BrushDynamics {
            Increment = 1,
        };
        public static readonly BrushDynamics DefaultHardness = new BrushDynamics {
            Constant = 1,
        };
        public static readonly BrushDynamics DefaultWidthFactor = new BrushDynamics {
            Constant = 1,
        };
        public static readonly BrushDynamics DefaultFlow = new BrushDynamics {
            Constant = 1,
        };
        public static readonly float DefaultSpacing = 0.33f;

        private bool _HasScale, _HasBrushIndex, _HasHardness, _HasWidthFactor, _HasFlow, _HasSpacing;
        private BrushDynamics _Scale, _BrushIndex, _Hardness, _WidthFactor, _Flow;
        private float _Spacing;

        public BrushDynamics AngleDegrees;
        public float SizePx;

        public BrushDynamics Scale {
            get => _HasScale ? _Scale : DefaultScale;
            set {
                _Scale = value;
                _HasScale = true;
            }
        }

        public BrushDynamics BrushIndex {
            get => _HasBrushIndex ? _BrushIndex : DefaultBrushIndex;
            set {
                _BrushIndex = value;
                _HasBrushIndex = true;
            }
        }

        public BrushDynamics Hardness {
            get => _HasHardness ? _Hardness : DefaultHardness;
            set {
                _Hardness = value;
                _HasHardness = true;
            }
        }

        public BrushDynamics WidthFactor {
            get => _HasWidthFactor ? _WidthFactor : DefaultWidthFactor;
            set {
                _WidthFactor = value;
                _HasWidthFactor = true;
            }
        }

        public BrushDynamics Flow {
            get => _HasFlow ? _Flow : DefaultFlow;
            set {
                _Flow = value;
                _HasFlow = true;
            }
        }

        public float Spacing {
            get => _HasSpacing ? _Spacing : DefaultSpacing;
            set {
                _Spacing = value;
                _HasSpacing = true;
            }
        }

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
                (NozzleSamplerState == rhs.NozzleSamplerState) &&
                (_NozzleCountXMinusOne == rhs._NozzleCountXMinusOne) &&
                (_NozzleCountYMinusOne == rhs._NozzleCountYMinusOne) &&
                (SizePx == rhs.SizePx) &&
                (Spacing == rhs.Spacing) &&
                (Scale == rhs.Scale) &&
                (AngleDegrees == rhs.AngleDegrees) &&
                (Flow == rhs.Flow) &&
                (BrushIndex == rhs.BrushIndex) &&
                (Hardness == rhs.Hardness) &&
                (WidthFactor == rhs.WidthFactor);
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
        /// Random seed for the stroke
        /// </summary>
        public float Seed;
        /// <summary>
        /// (in pixels, out pixels, start offset, end offset)
        /// </summary>
        public Vector4 TaperRanges;

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
        private static Texture2D CachedNoiseTexture;
        private Texture2D NoiseTexture;

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

        const int NoiseTextureSize = 256;

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
                var seed = new Vector4(0, 0, 1f / NoiseTextureSize, 0.33f / NoiseTextureSize);

                for (int i = 0, j = 0; i < count; i++, j+=4) {
                    ref var dc = ref _DrawCalls.Item(i);

                    seed.X = dc.Seed / NoiseTextureSize;
                    var vert = new RasterStrokeVertex {
                        PointsAB = new Vector4(dc.A.X, dc.A.Y, dc.B.X, dc.B.Y),
                        Seed = seed,
                        Taper = dc.TaperRanges,
                        ColorA = dc.ColorA4,
                        ColorB = dc.ColorB4,
                        WorldSpace = (short)(dc.WorldSpace ? 1 : 0)
                    };
                    vw.Write(vert);
                }

                NativeBatch.RecordPrimitives(count * CornerBufferPrimCount);
            }
        }

        private unsafe Texture2D GetNoiseTexture (RenderManager renderManager) {
            // FIXME
            var result = Volatile.Read(ref CachedNoiseTexture);
            if (result?.IsDisposed == false)
                return result;

            CachedNoiseTexture = null;
            lock (renderManager.CreateResourceLock)
                result = new Texture2D(renderManager.DeviceManager.Device, NoiseTextureSize, NoiseTextureSize, false, SurfaceFormat.Vector4);

            // FIXME: Do this on a worker thread?
            var rng = new CoreCLR.Xoshiro(null);
            int c = NoiseTextureSize * NoiseTextureSize * 4;
            var buffer = new float[c];
            for (int i = 0; i < c; i += 4) {
                buffer[i] = rng.NextSingle();
                buffer[i + 1] = rng.NextSingle();
                buffer[i + 2] = rng.NextSingle();
                buffer[i + 3] = rng.NextSingle();
            }

            lock (renderManager.UseResourceLock) {
                fixed (float * pData = buffer) {
                    result.SetDataPointerEXT(0, null, (IntPtr)pData, c * sizeof(float));
                }
            }

            Interlocked.CompareExchange(ref CachedNoiseTexture, result, null);
            var actualResult = Volatile.Read(ref CachedNoiseTexture);
            // FIXME
            if (actualResult != result)
                renderManager.DisposeResource(result);

            return actualResult;
        }

        public override void Issue (DeviceManager manager) {
            base.Issue(manager);

            var count = _DrawCalls.Count;
            if (count > 0) {
                // manager.Device.SetStringMarkerEXT(this.ToString());
                var device = manager.Device;

                // FIXME: Select technique based on this
                bool hasNoise = (Brush.Flow.NoiseFactor != 0) ||
                    (Brush.AngleDegrees.NoiseFactor != 0) ||
                    (Brush.BrushIndex.NoiseFactor != 0) ||
                    (Brush.Hardness.NoiseFactor != 0) ||
                    (Brush.Scale.NoiseFactor != 0) ||
                    (Brush.WidthFactor.NoiseFactor != 0);

                // HACK: Workaround for D3D11 debug layer shouting about no texture bound :(
                if (hasNoise || true)
                    NoiseTexture = GetNoiseTexture(Container.RenderManager);
                else
                    NoiseTexture = null;

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
                var atlas = Brush.NozzleAtlas.Instance;
                int nozzleBaseSize = atlas == null
                    ? 1 
                    : Math.Max(atlas.Width / Brush.NozzleCountX, atlas.Height / Brush.NozzleCountY);
                ep["UsesNoise"].SetValue(hasNoise);
                ep["NozzleParams"].SetValue(new Vector4(Brush.NozzleCountX, Brush.NozzleCountY, nozzleBaseSize, 0));
                ep["SizeDynamics"].SetValue(Brush.Scale.ToVector4());
                var angle = Brush.AngleDegrees.ToVector4();
                angle.Y /= 360f;
                ep["AngleDynamics"].SetValue(angle);
                ep["FlowDynamics"].SetValue(Brush.Flow.ToVector4());
                ep["BrushIndexDynamics"].SetValue(Brush.BrushIndex.ToVector4());
                ep["HardnessDynamics"].SetValue(Brush.Hardness.ToVector4());
                ep["WidthDynamics"].SetValue(Brush.WidthFactor.ToVector4());
                ep["Constants1"].SetValue(new Vector4(
                    Brush.Scale.Constant, Brush.AngleDegrees.Constant / 360f, Brush.Flow.Constant, Brush.BrushIndex.Constant
                ));
                ep["Constants2"].SetValue(new Vector4(
                    Brush.Hardness.Constant, Brush.WidthFactor.Constant, Brush.Spacing, Brush.SizePx
                ));
                ep["BlendInLinearSpace"].SetValue(BlendInLinearSpace);
                ep["OutputInLinearSpace"].SetValue(isSrgbRenderTarget);
                ep["Textured"].SetValue(atlas != null);

                manager.ApplyMaterial(Material, ref MaterialParameters);

                if (BlendState != null)
                    device.BlendState = BlendState;
                if (DepthStencilState != null)
                    device.DepthStencilState = DepthStencilState;
                if (RasterizerState != null)
                    device.RasterizerState = RasterizerState;

                // FIXME: why the hell
                // HACK: Ensure something is bound
                device.Textures[0] = atlas ?? NoiseTexture;
                device.Textures[1] = NoiseTexture;
                device.SamplerStates[0] = Brush.NozzleSamplerState ?? SamplerState.LinearWrap;
                device.SamplerStates[1] = SamplerState.PointWrap;

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
                device.Textures[1] = null;

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
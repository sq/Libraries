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
using Squared.Render.RasterShape;
using Squared.Util;

namespace Squared.Render.RasterStroke {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RasterStrokeVertex : IVertexType {
        public Vector4 PointsAB;
        public Vector4 ColorA, ColorB;
        public Vector4 Seed;
        public Vector4 Taper;
        public Vector4 Biases;
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
                new VertexElement( Marshal.OffsetOf(tThis, "Biases").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2 ),
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

    public enum RasterStrokeType : short {
        LineSegment = 0,
        Rectangle = 1,
        Polygon = 2
    }

    internal class StrokeShader {
        public Material Material;
        public EffectParameter BlendInLinearSpace,
            OutputInLinearSpace,
            UsesNoise,
            NozzleParams,
            Constants1,
            Constants2,
            SizeDynamics,
            AngleDynamics,
            FlowDynamics,
            BrushIndexDynamics,
            HardnessDynamics,
            ColorDynamics,
            ShadowColor,
            ShadowSettings;

        public StrokeShader (Material material) {
            Material = material;
            var p = material.Effect.Parameters;
            BlendInLinearSpace = p["BlendInLinearSpace"];
            OutputInLinearSpace = p["OutputInLinearSpace"];
            UsesNoise = p["UsesNoise"];
            NozzleParams = p["NozzleParams"];
            Constants1 = p["Constants1"];
            Constants2 = p["Constants2"];
            SizeDynamics = p["SizeDynamics"];
            AngleDynamics = p["AngleDynamics"];
            FlowDynamics = p["FlowDynamics"];
            BrushIndexDynamics = p["BrushIndexDynamics"];
            HardnessDynamics = p["HardnessDynamics"];
            ColorDynamics = p["ColorDynamics"];
            ShadowColor = p["ShadowColor"];
            ShadowSettings = p["ShadowSettings"];
        }
    }

    public struct BrushDynamics {
        public float Constant;
        public float TaperFactor;
        public float Increment;
        public float NoiseFactor;
        public float AngleFactor;
        public bool Wrap;

        public BrushDynamics (float constant, float taper = 0, float increment = 0, float noise = 0, float angle = 0, bool wrap = false) {
            Constant = constant;
            TaperFactor = taper;
            Increment = increment;
            NoiseFactor = noise;
            AngleFactor = angle;
            Wrap = wrap;
        }

        public override string ToString () {
            return $"<dynamics(({Constant} * taper({TaperFactor})) + step({Increment}) + noise({NoiseFactor}) + angle({AngleFactor}) wrap={Wrap})>";
        }

        public override int GetHashCode () {
            // FIXME
            return Constant.GetHashCode();
        }

        public bool Equals (BrushDynamics rhs) => Equals(ref rhs);

        public bool Equals (ref BrushDynamics rhs) {
            return (Constant == rhs.Constant) &&
                (TaperFactor == rhs.TaperFactor) &&
                (Increment == rhs.Increment) &&
                (NoiseFactor == rhs.NoiseFactor) &&
                (AngleFactor == rhs.AngleFactor) &&
                (Wrap == rhs.Wrap);
        }        

        public override bool Equals (object obj) {
            if (obj is BrushDynamics bd)
                return Equals(ref bd);
            else
                return false;
        }

        public static bool operator != (BrushDynamics lhs, BrushDynamics rhs) {
            return !lhs.Equals(rhs);
        }

        public static bool operator == (BrushDynamics lhs, BrushDynamics rhs) {
            return lhs.Equals(rhs);
        }

        internal Vector4 ToVector4 () {
            return new Vector4(TaperFactor, Increment, NoiseFactor, AngleFactor);
        }

        internal float UploadConstant => Constant * (Wrap ? -1 : 1);

        public static implicit operator BrushDynamics (float constant) {
            return new BrushDynamics {
                Constant = constant
            };
        }
    }

    public struct RasterBrush {
        public Texture2D NozzleAtlas;
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
        public static readonly BrushDynamics DefaultColor = new BrushDynamics {
            Increment = 1,
        };
        public static readonly BrushDynamics DefaultFlow = new BrushDynamics {
            Constant = 1,
        };

        public const float DefaultSpacing = 0.33f;

        private bool _HasScale, _HasBrushIndex, _HasHardness, _HasColor, _HasFlow, _HasSpacing;
        private BrushDynamics _Scale, _BrushIndex, _Hardness, _Color, _Flow;
        private float _Spacing;

        internal Vector4 _ShadowSettings;
        public Vector2 ShadowOffset {
            get => new Vector2(_ShadowSettings.X, _ShadowSettings.Y);
            set {
                _ShadowSettings.X = value.X;
                _ShadowSettings.Y = value.Y;
            }
        }
        public float ShadowHardness {
            get => _ShadowSettings.Z + 1;
            set => _ShadowSettings.Z = value - 1;
        }
        public float ShadowExpansion {
            get => _ShadowSettings.W;
            set => _ShadowSettings.W = value;
        }
        public pSRGBColor ShadowColor;

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

        public BrushDynamics Color {
            get => _HasColor ? _Color : DefaultColor;
            set {
                _Color = value;
                _HasColor = true;
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

        public RasterBrush (Texture2D atlas, float sizePx, float spacing = DefaultSpacing, int countX = 1, int countY = 1) : this() {
            NozzleAtlas = atlas;
            NozzleCountX = countX;
            NozzleCountY = countY;
            SizePx = sizePx;
            Spacing = spacing;
        }

        public override bool Equals (object obj) {
            if (obj is RasterBrush rb)
                return Equals(ref rb);
            else
                return false;
        }

        public override int GetHashCode () {
            return NozzleAtlas.GetHashCode();
        }

        public bool Equals (RasterBrush rhs) => Equals(ref rhs);

        public bool Equals (ref RasterBrush rhs) {
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
                (Color == rhs.Color) &&
                (_ShadowSettings == rhs._ShadowSettings) &&
                (ShadowColor == rhs.ShadowColor);
        }
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct RasterStrokeDrawCall {
        public RasterStrokeType Type;

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

        /// <summary>
        /// Bias values that apply to the dynamic constants for this stroke.
        /// (Size, Flow, Hardness, Color)
        /// </summary>
        public Vector4 Biases;

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
        // FIXME
        public RasterShapeColorSpace BlendIn;

        internal int Index;

        public int PolygonIndexOffset {
            set => A.X = value;
        }
        public int PolygonVertexCount {
            set => A.Y = value;
        }
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

        private class BatchManager : SubBatchManager<RasterStrokeBatch, RasterStrokeDrawCall, SubBatch> {
            public static readonly BatchManager Instance = new BatchManager();

            protected override bool KeyEquals (
                RasterStrokeBatch self, ref RasterStrokeDrawCall last, ref RasterStrokeDrawCall dc
            ) {
                // FIXME
                return (last.Type == dc.Type);
            }

            protected override void CreateBatch (RasterStrokeBatch self, ref RasterStrokeDrawCall drawCall, int offset, int count) {
                self._SubBatches.Add(new SubBatch {
                    InstanceOffset = offset,
                    InstanceCount = count,
                    Type = drawCall.Type
                });
            }
        }

        private struct SubBatch {
            public int InstanceOffset, InstanceCount;
            public RasterStrokeType Type;
        }

        private static bool HasGeneratedShadowWarning;
        private DenseList<SubBatch> _SubBatches;

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
        private PolygonBuffer _PolygonBuffer = null;

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
        public DitheringSettings? DitheringSettings;

        static RasterStrokeBatch () {
            AdjustPoolCapacities(1024, null, 512, 16);
            ConfigureClearBehavior(true);
        }

        public void Initialize (IBatchContainer container, int layer, DefaultMaterialSet materials) {
            // FIXME: Default material
            base.Initialize(container, layer, materials.RasterStrokeMaterials[(int)RasterStrokeType.LineSegment][0].Material, true);

            Materials = materials;

            BlendInLinearSpace = true;
            DepthStencilState = null;
            BlendState = null;
            RasterizerState = null;
            Brush = default;
            BatchManager.Instance.Clear(this, ref _SubBatches);

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
                BatchManager.Instance.Setup(this, ref _SubBatches, count);

                _DrawCalls.Sort(StrokeDrawCallSorter);

                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<RasterStrokeVertex>>();
                _CornerBuffer = Container.Frame.PrepareData.GetCornerBuffer(Container, CornerBufferRepeatCount);
                var swb = _BufferGenerator.Allocate(vertexCount, 1);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<RasterStrokeVertex>(swb.Vertices);
                var vw = vb.GetWriter(count, clear: false);
                var seed = new Vector4(0, 0, 1f / NoiseTextureSize, 0.33f / NoiseTextureSize);

                ref var firstDc = ref _DrawCalls.Item(0);
                // FIXME: If the first draw call is dead this is wrong
                BatchManager.Instance.Start(this, ref firstDc, out var state);
                int actualCount = 0;

                for (int i = 0, j = 0; i < count; i++) {
                    ref var dc = ref _DrawCalls.Item(i);
                    // HACK: Right now the shader doesn't handle this correctly and generates a little splat
                    if ((dc.TaperRanges.Z + dc.TaperRanges.W) >= 1.0f)
                        continue;

                    BatchManager.Instance.Step(this, ref dc, ref state, j++);
                    actualCount++;

                    seed.X = dc.Seed / NoiseTextureSize;
                    vw.NextVertex = new RasterStrokeVertex {
                        PointsAB = new Vector4(dc.A.X, dc.A.Y, dc.B.X, dc.B.Y),
                        Seed = seed,
                        Taper = dc.TaperRanges,
                        Biases = dc.Biases,
                        ColorA = dc.ColorA4,
                        ColorB = dc.ColorB4,
                        WorldSpace = (short)(dc.WorldSpace ? 1 : 0)
                    };
                }

                BatchManager.Instance.Finish(this, ref state, actualCount);
                NativeBatch.RecordPrimitives(actualCount * CornerBufferPrimCount);
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
            if (count <= 0) {
                _SoftwareBuffer = null;
                return;
            }

            // manager.Device.SetStringMarkerEXT(this.ToString());
            var device = manager.Device;

            // FIXME: Select technique based on this
            bool hasNoise = (Brush.Flow.NoiseFactor != 0) ||
                (Brush.AngleDegrees.NoiseFactor != 0) ||
                (Brush.BrushIndex.NoiseFactor != 0) ||
                (Brush.Hardness.NoiseFactor != 0) ||
                (Brush.Scale.NoiseFactor != 0) ||
                (Brush.Color.NoiseFactor != 0);

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

            var atlas = Brush.NozzleAtlas;
            int nozzleBaseSize = atlas == null
                ? 1 
                : Math.Max(atlas.Width / Brush.NozzleCountX, atlas.Height / Brush.NozzleCountY);

            Vector4 angle = Brush.AngleDegrees.ToVector4(),
                constants1 = new Vector4(
                    Brush.Scale.UploadConstant, Brush.AngleDegrees.UploadConstant / 360f, Brush.Flow.UploadConstant, Brush.BrushIndex.UploadConstant
                ),
                constants2 = new Vector4(
                    Brush.Hardness.UploadConstant, Brush.Color.UploadConstant, Brush.Spacing, Brush.SizePx
                ),
                nozzleParams = new Vector4(Brush.NozzleCountX, Brush.NozzleCountY, nozzleBaseSize, 0);
            angle.Y /= 360f;

            for (int i = 0; i < _SubBatches.Count; i++) {
                ref var sb = ref _SubBatches.Item(i);
                var type = (int)sb.Type;
                var idx = (atlas != null) ? 1 : 0;
                if (!Brush.ShadowColor.IsTransparent)
                    idx += 2;

                var material = Materials.RasterStrokeMaterials[type][idx];
                if (material == null) {
                    idx = idx = (atlas != null) ? 1 : 0;
                    material = Materials.RasterStrokeMaterials[type][idx];
                    if (material == null)
                        throw new Exception($"Failed to locate shader for stroke of type {sb.Type}");
                    else if (!HasGeneratedShadowWarning) {
                        HasGeneratedShadowWarning = true;
                        System.Diagnostics.Debug.WriteLine($"WARNING: No shadowed shader is available for stroke of type {sb.Type}");
                    }
                }

                var ds = DitheringSettings ?? Materials.DefaultDitheringSettings;
                Materials.uDithering.Set(material.Material, ref ds);

                material.UsesNoise.SetValue(hasNoise);
                material.NozzleParams.SetValue(nozzleParams);
                material.SizeDynamics.SetValue(Brush.Scale.ToVector4());
                material.AngleDynamics.SetValue(angle);
                material.FlowDynamics.SetValue(Brush.Flow.ToVector4());
                material.BrushIndexDynamics.SetValue(Brush.BrushIndex.ToVector4());
                material.HardnessDynamics.SetValue(Brush.Hardness.ToVector4());
                material.ColorDynamics.SetValue(Brush.Color.ToVector4());
                material.Constants1.SetValue(constants1);
                material.Constants2.SetValue(constants2);
                material.BlendInLinearSpace.SetValue(BlendInLinearSpace);
                material.OutputInLinearSpace.SetValue(isSrgbRenderTarget);
                // FIXME: BlendInLinearSpace
                material.ShadowColor?.SetValue(BlendInLinearSpace ? Brush.ShadowColor.ToPLinear() : Brush.ShadowColor.ToVector4());
                material.ShadowSettings?.SetValue(Brush.ShadowColor.IsTransparent ? Vector4.Zero : Brush._ShadowSettings);

                // HACK
                if (sb.Type == RasterStrokeType.Polygon) {
                    _PolygonBuffer.Flush(manager);
                    lock (_PolygonBuffer.Lock)
                        material.Material.Effect.Parameters["PolygonVertexBufferInvSize"]?.SetValue(
                            new Vector2(1.0f / _PolygonBuffer.TextureWidth, 1.0f / _PolygonBuffer.TextureHeight)
                        );
                }

                manager.ApplyMaterial(material.Material, ref MaterialParameters);

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

                if (sb.Type == RasterStrokeType.Polygon) {
                    lock (_PolygonBuffer.Lock) {
                        device.Textures[2] = _PolygonBuffer.Texture;
                        device.VertexTextures[2] = _PolygonBuffer.Texture;
                    }
                } else {
                    device.VertexTextures[2] = null;
                    device.Textures[2] = null;
                }

                scratchBindings[1] = new VertexBufferBinding(
                    vb, _SoftwareBuffer.HardwareVertexOffset + sb.InstanceOffset, 1
                );

                device.SetVertexBuffers(scratchBindings);

                device.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList, 
                    0, _CornerBuffer.HardwareVertexOffset, CornerBufferVertexCount, 
                    _CornerBuffer.HardwareIndexOffset, CornerBufferPrimCount, 
                    sb.InstanceCount
                );

                device.Textures[0] = null;
                device.Textures[1] = null;
                device.Textures[2] = null;
                device.VertexTextures[2] = null;
            }

            NativeBatch.RecordCommands(_SubBatches.Count);
            hwb.SetInactive();
            cornerHwb.SetInactive();

            device.SetVertexBuffer(null);
            _SoftwareBuffer = null;
        }

        new public void Add (in RasterStrokeDrawCall dc) {
            var result = dc;
            // FIXME
            result.Index = _DrawCalls.Count;
            _DrawCalls.Add(result);
        }

        public static RasterStrokeBatch New (
            IBatchContainer container, int layer, DefaultMaterialSet materials, ref RasterBrush brush,
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

        public void AddPolygonVertices (
            ArraySegment<RasterPolygonVertex> vertices, out int indexOffset, out int vertexCount,
            Matrix? vertexTransform = null, Func<RasterPolygonVertex, RasterPolygonVertex> vertexModifier = null
        ) {
            _PolygonBuffer = Container.Frame.PrepareData.GetPolygonBuffer(Container);
            _PolygonBuffer.AddVertices(vertices, out indexOffset, out vertexCount, false, vertexTransform, vertexModifier);
        }

        protected override void OnReleaseResources () {
            _SubBatches.Dispose();
            base.OnReleaseResources();
        }
    }
}
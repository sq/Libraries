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

namespace Squared.Render.RasterShape {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RasterShapeVertex : IVertexType {
        public Vector4 PointsAB, PointsCD;
        public Vector4 Parameters, Parameters2;
        public Vector4 TextureRegion;
        public Vector4 InnerColor, OuterColor, OutlineColor;
        public short   Type, WorldSpace;

        public static readonly VertexElement[] Elements;
        static readonly VertexDeclaration _VertexDeclaration;

        static RasterShapeVertex () {
            var tThis = typeof(RasterShapeVertex);

            Elements = new VertexElement[] {
                new VertexElement( Marshal.OffsetOf(tThis, "PointsAB").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "PointsCD").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Position, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Parameters").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Parameters2").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "TextureRegion").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 2 ),
                new VertexElement( Marshal.OffsetOf(tThis, "InnerColor").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Color, 0 ),
                new VertexElement( Marshal.OffsetOf(tThis, "OuterColor").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Color, 1 ),
                new VertexElement( Marshal.OffsetOf(tThis, "OutlineColor").ToInt32(),
                    VertexElementFormat.Vector4, VertexElementUsage.Color, 2 ),
                new VertexElement( Marshal.OffsetOf(tThis, "Type").ToInt32(),
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 1)
            };

            _VertexDeclaration = new VertexDeclaration(Elements);
        }

        public override string ToString () {
            return string.Format("{0} {1}", Type, PointsAB);
        }

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public enum RasterShapeType : short {
        Ellipse = 0,
        LineSegment = 1,
        Rectangle = 2,
        Triangle = 3,
        QuadraticBezier = 4,
        Arc = 5,
        Polygon = 6
    }

    public enum RasterFillMode : int {
        /// <summary>
        /// The default fill mode for the shape.
        /// </summary>
        Natural = 0,
        /// <summary>
        /// A linear fill across the shape's bounding box.
        /// </summary>
        Linear = 1,
        /// <summary>
        /// A linear fill enclosing the shape's bounding box.
        /// </summary>
        LinearEnclosing = 2,
        /// <summary>
        /// A linear fill enclosed by the shape's bounding box.
        /// </summary>
        LinearEnclosed = 3,
        /// <summary>
        /// A radial fill across the shape's bounding box.
        /// </summary>
        Radial = 4,
        /// <summary>
        /// A radial fill enclosing the shape's bounding box.
        /// </summary>
        RadialEnclosing = 5,
        /// <summary>
        /// A radial fill enclosed by the shape's bounding box.
        /// </summary>
        RadialEnclosed = 6,
        /// <summary>
        /// A fill that travels along the shape (only valid for lines and arcs).
        /// </summary>
        Along = 7,
        /// <summary>
        /// Solid fill with no gradient.
        /// </summary>
        None = 8,
        /// <summary>
        /// A linear gradient with a configurable angle.
        /// </summary>
        Angular = 512,
        /// <summary>
        /// A linear gradient that goes top-to-bottom.
        /// </summary>
        Vertical = Angular,
        /// <summary>
        /// A linear gradient that goes left-to-right.
        /// </summary>
        Horizontal = Angular + 90,
        /// <summary>
        /// A fill that extends outwards from the center and travels around the outside edge (like a pie chart)
        /// </summary>
        Conical = Angular + 720,
    }

    /// <summary>
    /// Controls the behavior of this point in the polygon
    /// </summary>
    public enum RasterVertexType : int {
        /// <summary>
        /// Creates a line between this vertex and the previous vertex
        /// </summary>
        Line = 0,
        /// <summary>
        /// Creates a cubic bezier between the previous vertex and this vertex, using
        ///  ControlPoint1 + ControlPoint2 as control points
        /// </summary>
        Bezier = 1,
    }

    public struct RasterFillSettings {
        public RasterFillMode Mode;
        public float Offset;
        private float SizeMinusOne;
        public float Size {
            get => SizeMinusOne + 1;
            set {
                SizeMinusOne = value - 1;
            }
        }
        internal float ModeF => Convenience.ImperativeRenderer.ConvertFillMode(Mode, Angle);
        public float Angle;
        public Vector2? GradientPower;

        public RasterFillSettings (
            RasterFillMode mode, float size = 1f, float offset = 0f,
            float angle = 0f, Vector2? gradientPower = null
        ) {
            Mode = mode;
            Offset = offset;
            SizeMinusOne = size - 1f;
            Angle = angle;
            GradientPower = gradientPower;
        }

        public static implicit operator RasterFillSettings (RasterFillMode mode) =>
            new RasterFillSettings {
                Mode = mode
            };
    }

    public enum RasterTextureCompositeMode : byte {
        Multiply = 0,
        Over = 1,
        Under = 2
    }

    public struct RasterTextureSettings {
        public SamplerState SamplerState;
        private int ShadowMode;
        internal Vector4 ModeAndSize;
        internal Vector4 Placement;

        public RasterTextureCompositeMode Mode {
            get {
                return (RasterTextureCompositeMode)ShadowMode;
            }
            set {
                ShadowMode = (int)value;
                ModeAndSize.X = (float)ShadowMode;
            }
        }

        public bool PreserveAspectRatio {
            get {
                return ModeAndSize.Y > 0.5f;
            }
            set {
                ModeAndSize.Y = value ? 1f : 0f;
            }
        }

        public Vector2 Scale {
            get {
                return new Vector2(ModeAndSize.Z + 1, ModeAndSize.W + 1);
            }
            set {
                ModeAndSize.Z = value.X - 1;
                ModeAndSize.W = value.Y - 1;
            }
        }

        public Vector2 Origin {
            get {
                return new Vector2(Placement.X, Placement.Y);
            }
            set {
                Placement.X = value.X;
                Placement.Y = value.Y;
            }
        }

        public Vector2 Position {
            get {
                return new Vector2(Placement.Z, Placement.W);
            }
            set {
                Placement.Z = value.X;
                Placement.W = value.Y;
            }
        }

        public override int GetHashCode () {
            // HACK: Vector4.GetHashCode is so slow that it's probably not worth using the placement value
            //  as part of the hashcode
            return (SamplerState?.GetHashCode() ?? 0) |
                ShadowMode.GetHashCode() /* |
                Placement.GetHashCode() */;
        }

        public bool Equals (RasterTextureSettings rhs) {
            return (SamplerState == rhs.SamplerState) && 
                (ModeAndSize == rhs.ModeAndSize) &&
                (Placement == rhs.Placement);
        }

        public override bool Equals (object obj) {
            if (obj is RasterTextureSettings)
                return Equals((RasterTextureSettings)obj);
            else
                return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RasterPolygonVertex {
        public Vector2 Position;
        public Vector2 ControlPoint1, ControlPoint2;
        public RasterVertexType Type;

        public static implicit operator RasterPolygonVertex (Vector2 position) =>
            new RasterPolygonVertex {
                Position = position,
                ControlPoint1 = default,
                ControlPoint2 = default,
                Type = RasterVertexType.Line
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RasterShapeDrawCall {
        public RasterShapeType Type;
        public bool WorldSpace;

        /// <summary>
        /// The top-left or first coordinate of the shape.
        /// </summary>
        public Vector2 A;
        /// <summary>
        /// The bottom-right or second coordinate of the shape.
        /// </summary>
        public Vector2 B;
        /// <summary>
        /// The third coordinate of the shape, or control values for a 1-2 coordinate shape.
        /// For lines, C.X controls whether the gradient is 'along' the line.
        /// For rectangles, C.X controls whether the gradient is radial.
        /// </summary>
        public Vector2 C;
        /// <summary>
        /// The radius of the shape. 
        /// This is in addition to any size implied by the coordinates (for shapes with volume)
        /// Most shapes only use .X
        /// </summary>
        public Vector2 Radius;

        /// <summary>
        /// The premultiplied sRGB color of the center of the shape (or the beginning for 'along' gradients)
        /// </summary>
        public Vector4 InnerColor4;
        /// <summary>
        /// The premultiplied sRGB color for the outside of the shape (or the end for 'along' gradients)
        /// </summary>
        public Vector4 OuterColor4;
        /// <summary>
        /// The premultiplied sRGB color of the shape's outline.
        /// </summary>
        public Vector4 OutlineColor4;

        public pSRGBColor InnerColor {
            get {
                return new pSRGBColor(InnerColor4);
            }
            set {
                InnerColor4 = value.ToVector4();
            }
        }

        public pSRGBColor OuterColor {
            get {
                return new pSRGBColor(OuterColor4);
            }
            set {
                OuterColor4 = value.ToVector4();
            }
        }

        public pSRGBColor OutlineColor {
            get {
                return new pSRGBColor(OutlineColor4);
            }
            set {
                OutlineColor4 = value.ToVector4();
            }
        }

        public int SortKey;
        /// <summary>
        /// If true, the outline has soft falloff instead of a sharp edge.
        /// </summary>
        public bool SoftOutline;
        /// <summary>
        /// The thickness of the shape's outline.
        /// </summary>
        public float OutlineSize;
        /// <summary>
        /// Applies gamma correction to the outline to make it appear softer or sharper.
        /// </summary>
        public float OutlineGammaMinusOne;
        /// <summary>
        /// If set, blending between inner/outer/outline colors occurs in linear space.
        /// </summary>
        public bool BlendInLinearSpace;
        /// <summary>
        /// If set, final output is in linear space instead of sRGB.
        /// </summary>
        public bool OutputInLinearSpace;
        /// <summary>
        /// The fill gradient weight is calculated as 1 - pow(1 - pow(w, FillGradientPowerMinusOne.x + 1), FillGradientPowerMinusOne.y + 1)
        /// Adjusting x and y away from 1 allows you to adjust the shape of the curve
        /// </summary>
        public Vector2 FillGradientPowerMinusOne;
        /// <summary>
        /// The fill mode to use for the interior, (+ an angle in degrees if the mode is Angular).
        /// </summary>
        public float FillMode;
        /// <summary>
        /// Offsets the gradient towards or away from the beginning.
        /// </summary>
        public float FillOffset;
        /// <summary>
        /// Sets the size of the gradient, with 1.0 filling the entire shape.
        /// </summary>
        public float FillSize;
        /// <summary>
        /// For angular gradients, set the angle of the gradient (in degrees).
        /// </summary>
        public float FillAngle;
        /// <summary>
        /// If above zero, the shape becomes annular (hollow) instead of solid, with a border this size in pixels.
        /// </summary>
        public float AnnularRadius;
        /// <summary>
        /// Specifies the region of the texture to apply to the shape.
        /// The top-left part of this region will be aligned with the top-left
        ///  corner of the shape's bounding box.
        /// </summary>
        public Bounds TextureBounds;
        public RasterTextureSettings TextureSettings;

        /// <summary>
        /// Configures the shadow for the raster shape, if any.
        /// </summary>
        public RasterShadowSettings Shadow;

        internal int IsSimple;
        internal int PackedFlags;
        internal int Index;
    }

    public struct RasterShadowSettings {
        /// <summary>
        /// Configures the position of the shadow relative to the shape.
        /// </summary>
        public Vector2 Offset;

        /// <summary>
        /// Configures the softness of the shadow. Larger values provide softer falloff and a larger shadow.
        /// </summary>
        public float Softness;

        /// <summary>
        /// Makes the shadow larger or smaller than the object it's shadowing.
        /// </summary>
        public float Expansion;

        private float FillSuppressionMinusOne;
        /// <summary>
        /// Configures how much of the shadow is visible behind the fill of the shape (if the fill is not opaque).
        /// A value of 1 fully suppresses the shadow within the shape's fill region.
        /// </summary>
        public float FillSuppression {
            get {
                return FillSuppressionMinusOne + 1;
            }
            set {
                FillSuppressionMinusOne = value - 1;
            }
        }

        /// <summary>
        /// The shadow color (premultiplied sRGB).
        /// </summary>
        internal pSRGBColor _Color;

        public pSRGBColor Color {
            get {
                return _Color;
            }
            set {
                _Color = value;
                IsEnabled = !_Color.IsTransparent ? 1 : 0;
            }
        }

        /// <summary>
        /// Shadow inside of the shape instead of outside
        /// </summary>
        public bool Inside;

        internal int IsEnabled;

        public bool Equals (ref RasterShadowSettings rhs) {
            return (IsEnabled == rhs.IsEnabled) &&
                (Offset == rhs.Offset) &&
                (Softness == rhs.Softness) &&
                (Expansion == rhs.Expansion) &&
                (FillSuppressionMinusOne == rhs.FillSuppressionMinusOne) &&
                (_Color == rhs._Color) &&
                (Inside == rhs.Inside);
        }

        public bool Equals (RasterShadowSettings rhs) {
            return Equals(ref rhs);
        }

        public override bool Equals (object obj) {
            if (obj is RasterShadowSettings)
                return Equals((RasterShadowSettings)obj);
            else
                return false;
        }

        // FIXME
        public override int GetHashCode () {
            return IsEnabled.GetHashCode() ^ Color.GetHashCode();
        }
    }

    public struct RasterShader {
        public Material Material;
        public EffectParameter BlendInLinearSpace,
            OutputInLinearSpace,
            RasterTexture,
            RampTexture,
            ShadowOptions,
            ShadowOptions2,
            ShadowColorLinear,
            TextureModeAndSize,
            TexturePlacement;

        public RasterShader (Material material) {
            Material = material;
            var p = material.Effect.Parameters;
            BlendInLinearSpace = p["BlendInLinearSpace"];
            OutputInLinearSpace = p["OutputInLinearSpace"];
            RasterTexture = p["RasterTexture"];
            RampTexture = p["RampTexture"];
            ShadowOptions = p["ShadowOptions"];
            ShadowOptions2 = p["ShadowOptions2"];
            ShadowColorLinear = p["ShadowColorLinear"];
            TextureModeAndSize = p["TextureModeAndSize"];
            TexturePlacement = p["TexturePlacement"];
        }
    }

    public class RasterShapeBatch : ListBatch<RasterShapeDrawCall> {
        private sealed class RasterShapeDrawCallSorter : IRefComparer<RasterShapeDrawCall>, IComparer<RasterShapeDrawCall> {
            public int Compare (ref RasterShapeDrawCall lhs, ref RasterShapeDrawCall rhs) {
                var result = lhs.SortKey - rhs.SortKey;
                if (result == 0)
                    result = lhs.PackedFlags - rhs.PackedFlags;
                if (result == 0)
                    result = lhs.TextureSettings.GetHashCode() - rhs.TextureSettings.GetHashCode();
                if (result == 0)
                    result = lhs.Index - rhs.Index;
                return result;
            }

            public int Compare (RasterShapeDrawCall lhs, RasterShapeDrawCall rhs) {
                return Compare(ref lhs, ref rhs);
            }
        }

        private struct SubBatch {
            public RasterShapeType Type;
            public bool BlendInLinearSpace, OutputInLinearSpace;
            public RasterShadowSettings Shadow;
            public bool Shadowed, Simple;
            public int InstanceOffset, InstanceCount;
            internal RasterTextureSettings TextureSettings;
        }

        private BufferGenerator<RasterShapeVertex> _BufferGenerator = null;
        private BufferGenerator<CornerVertex>.SoftwareBuffer _CornerBuffer = null;

        protected static ThreadLocal<VertexBufferBinding[]> _ScratchBindingArray = 
            new ThreadLocal<VertexBufferBinding[]>(() => new VertexBufferBinding[2]);

        internal ArrayPoolAllocator<RasterShapeVertex> VertexAllocator;
        internal ISoftwareBuffer _SoftwareBuffer;

        public DefaultMaterialSet Materials;
        public Texture2D Texture;
        public SamplerState SamplerState;
        public Texture2D RampTexture;

        public bool UseUbershader = false;

        private static readonly RasterShapeDrawCallSorter ShapeDrawCallSorter = new RasterShapeDrawCallSorter();

        private static ListPool<SubBatch> _SubListPool = new ListPool<SubBatch>(
            256, 4, 32, 128, 512
        );
        private DenseList<SubBatch> _SubBatches;

        const int MaxVertexCount = 65535;

        const bool HollowOptimization = true;

        const int CornerBufferRepeatCount = HollowOptimization ? 5 : 1;
        const int CornerBufferVertexCount = CornerBufferRepeatCount * 4;
        const int CornerBufferIndexCount = CornerBufferRepeatCount * 6;
        const int CornerBufferPrimCount = CornerBufferRepeatCount * 2;

        public DepthStencilState DepthStencilState;
        public BlendState BlendState;
        public RasterizerState RasterizerState;
        public RasterShadowSettings ShadowSettings;

        // FIXME FIXME HACK HACK GROSS FIXME
        private static object PolygonVertexLock = new object();
        private static bool PolygonVertexFlushRequired = true;
        private static int PolygonVertexWriteOffset = 0, PolygonVertexCount = 0;
        private static RasterPolygonVertex[] PolygonVertexBuffer;
        private static Texture2D PolygonVertexTexture;

        static RasterShapeBatch () {
            AdjustPoolCapacities(1024, null, 512, 16);
        }

        public void Initialize (IBatchContainer container, int layer, DefaultMaterialSet materials) {
            base.Initialize(container, layer, materials.RasterShapeUbershader, true);

            Materials = materials;

            DepthStencilState = null;
            BlendState = null;
            RasterizerState = null;

            Texture = null;

            if (VertexAllocator == null)
                VertexAllocator = container.RenderManager.GetArrayAllocator<RasterShapeVertex>();
        }

        new public static void AdjustPoolCapacities (
            int? smallItemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            ListBatch<RasterShapeDrawCall>.AdjustPoolCapacities(smallItemSizeLimit, largeItemSizeLimit, smallPoolCapacity, largePoolCapacity);
        }

        private static bool ShouldBeShadowed (ref RasterShadowSettings shadow) {
            return !shadow.Color.IsTransparent && (
                (shadow.Softness >= 0.1) || 
                (shadow.Expansion >= 0.1) ||
                (shadow.Offset.Length() > 0.1)
            );
        }

        protected override void Prepare (PrepareManager manager) {
            var count = _DrawCalls.Count;
            var vertexCount = count;
            if (count > 0) {
                _SubBatches.ListPoolOrAllocator = _SubListPool;
                _SubBatches.Clear();
                _SubBatches.EnsureCapacity(count, true);

                _DrawCalls.Sort(ShapeDrawCallSorter);

                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<RasterShapeVertex>>();
                _CornerBuffer = Container.Frame.PrepareData.GetCornerBuffer(Container, CornerBufferRepeatCount);
                var swb = _BufferGenerator.Allocate(vertexCount, 1);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<RasterShapeVertex>(swb.Vertices);
                var vw = vb.GetWriter(count, clear: false);

                RasterShapeDrawCall dc;
                _DrawCalls.GetItem(0, out dc);
                var lastType = dc.Type;
                var lastBlend = dc.BlendInLinearSpace;
                var lastOutput = dc.OutputInLinearSpace;
                var lastShadow = dc.Shadow;
                var lastOffset = 0;
                var lastIsSimple = dc.IsSimple;
                var lastTextureSettings = dc.TextureSettings;

                for (int i = 0, j = 0; i < count; i++, j+=4) {
                    if (i > 0)
                        _DrawCalls.GetItem(i, out dc);

                    if (
                        ((dc.Type != lastType) && (!UseUbershader || dc.Type == RasterShapeType.Polygon || lastType == RasterShapeType.Polygon)) ||
                        (dc.BlendInLinearSpace != lastBlend) ||
                        (dc.OutputInLinearSpace != lastOutput) ||
                        !dc.Shadow.Equals(ref lastShadow) ||
                        (dc.IsSimple != lastIsSimple) ||
                        !dc.TextureSettings.Equals(lastTextureSettings)
                    ) {
                        _SubBatches.Add(new SubBatch {
                            InstanceOffset = lastOffset,
                            InstanceCount = (i - lastOffset),
                            BlendInLinearSpace = lastBlend,
                            OutputInLinearSpace = lastOutput,
                            Type = lastType,
                            Shadow = lastShadow,
                            Shadowed = ShouldBeShadowed(ref lastShadow),
                            Simple = lastIsSimple != 0,
                            TextureSettings = lastTextureSettings
                        });
                        lastOffset = i;
                        lastType = dc.Type;
                        lastShadow = dc.Shadow;
                        lastIsSimple = dc.IsSimple;
                        lastTextureSettings = dc.TextureSettings;
                    }

                    var vert = new RasterShapeVertex {
                        PointsAB = new Vector4(dc.A.X, dc.A.Y, dc.B.X, dc.B.Y),
                        // FIXME: Fill this last space with a separate value?
                        PointsCD = new Vector4(dc.C.X, dc.C.Y, dc.Radius.X, dc.Radius.Y),
                        InnerColor = dc.InnerColor.ToVector4(),
                        OutlineColor = dc.OutlineColor.ToVector4(),
                        OuterColor = dc.OuterColor.ToVector4(),
                        Parameters = new Vector4(dc.OutlineSize * (dc.SoftOutline ? -1 : 1), dc.AnnularRadius, dc.FillMode, dc.OutlineGammaMinusOne),
                        Parameters2 = new Vector4(dc.FillGradientPowerMinusOne.X + 1, dc.FillGradientPowerMinusOne.Y + 1, dc.FillOffset, dc.FillSize),
                        TextureRegion = dc.TextureBounds.ToVector4(),
                        Type = (short)dc.Type,
                        WorldSpace = (short)(dc.WorldSpace ? 1 : 0)
                    };
                    vw.Write(vert);
                }

                _SubBatches.Add(new SubBatch {
                    InstanceOffset = lastOffset,
                    InstanceCount = (count - lastOffset),
                    BlendInLinearSpace = lastBlend,
                    OutputInLinearSpace = lastOutput,
                    Type = lastType,
                    Shadow = lastShadow,
                    Shadowed = ShouldBeShadowed(ref lastShadow),
                    Simple = lastIsSimple != 0,
                    TextureSettings = lastTextureSettings
                });

                NativeBatch.RecordPrimitives(count * CornerBufferPrimCount);
            }
        }

        private RasterShader PickBaseMaterial (RasterShapeType? type, bool shadowed, bool simple) {
            var key = new DefaultMaterialSet.RasterShaderKey {
                Type = type,
                Simple = simple && (Texture == null) && (RampTexture == null),
                Shadowed = shadowed,
                Textured = (Texture != null),
                HasRamp = (RampTexture != null)
            };

            RasterShader result;

            if (!Materials.RasterShapeMaterials.TryGetValue(key, out result)) {
                key.Simple = false;
                if (!Materials.RasterShapeMaterials.TryGetValue(key, out result)) {
                    key.Type = null;
                    if (!Materials.RasterShapeMaterials.TryGetValue(key, out result)) {
                        // FIXME
                        key.HasRamp = false;
                        if (!Materials.RasterShapeMaterials.TryGetValue(key, out result))
                            throw new Exception($"Shader not found for raster shape {type} (shadowed={shadowed}, textured={Texture != null}, simple={simple}, ramp={RampTexture != null})");
                    }
                }
            }

            return result;
        }

        private RasterShader PickMaterial (RasterShapeType? type, bool shadowed, bool simple) {
            var baseMaterial = PickBaseMaterial(type, shadowed, simple);
            return baseMaterial;
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

                foreach (var sb in _SubBatches) {
                    var rasterShader = (UseUbershader && sb.Type != RasterShapeType.Polygon) 
                        ? PickMaterial(null, sb.Shadowed, sb.Simple)
                        : PickMaterial(sb.Type, sb.Shadowed, sb.Simple);

                    // HACK
                    if (sb.Type == RasterShapeType.Polygon)
                        lock (PolygonVertexLock)
                            rasterShader.Material.Effect.Parameters["PolygonVertexBufferInvWidth"]?.SetValue(
                                1.0f / PolygonVertexBuffer.Length
                            );

                    rasterShader.BlendInLinearSpace.SetValue(sb.BlendInLinearSpace);
                    rasterShader.OutputInLinearSpace.SetValue(isSrgbRenderTarget || sb.OutputInLinearSpace);
                    rasterShader.RasterTexture?.SetValue(Texture);
                    rasterShader.RampTexture?.SetValue(RampTexture);

                    // HACK: If the shadow color is fully transparent, suppress the offset and softness.
                    // If we don't do this, the bounding box of the shapes will be pointlessly expanded.
                    var shadowColor = sb.BlendInLinearSpace ? sb.Shadow.Color.ToPLinear() : sb.Shadow.Color.ToVector4();
                    var shadowOffset = sb.Shadowed ? sb.Shadow.Offset : Vector2.Zero;
                    var shadowSoftness = sb.Shadowed ? sb.Shadow.Softness : 0;
                    var shadowExpansion = (sb.Shadowed ? sb.Shadow.Expansion : 0) * (sb.Shadow.Inside ? -1 : 1);
                    // Also suppress the shadow entirely if the parameters are such that it would basically be invisible
                    if (!sb.Shadowed) {
                        shadowOffset = Vector2.Zero;
                        shadowColor = Vector4.Zero;
                        shadowSoftness = 0;
                        shadowExpansion = 0;
                    }

                    rasterShader.ShadowOptions.SetValue(new Vector4(
                        shadowOffset.X, shadowOffset.Y,
                        shadowSoftness, sb.Shadow.FillSuppression
                    ));
                    rasterShader.ShadowOptions2.SetValue(new Vector2(
                        shadowExpansion, sb.Shadow.Inside ? 1 : 0
                    ));
                    rasterShader.ShadowColorLinear.SetValue(shadowColor);
                    rasterShader.TextureModeAndSize?.SetValue(sb.TextureSettings.ModeAndSize);
                    rasterShader.TexturePlacement?.SetValue(sb.TextureSettings.Placement);

                    manager.ApplyMaterial(rasterShader.Material, ref MaterialParameters);

                    if (BlendState != null)
                        device.BlendState = BlendState;
                    if (DepthStencilState != null)
                        device.DepthStencilState = DepthStencilState;
                    if (RasterizerState != null)
                        device.RasterizerState = RasterizerState;

                    // FIXME: why the hell
                    device.Textures[0] = Texture;
                    device.SamplerStates[0] = sb.TextureSettings.SamplerState ?? SamplerState ?? SamplerState.LinearWrap;
                    device.Textures[3] = RampTexture;

                    if (sb.Type == RasterShapeType.Polygon) {
                        lock (PolygonVertexLock) {
                            device.Textures[2] = PolygonVertexTexture;
                            device.VertexTextures[2] = PolygonVertexTexture;
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
                    rasterShader.RasterTexture?.SetValue((Texture2D)null);
                    rasterShader.RampTexture?.SetValue((Texture2D)null);
                }

                NativeBatch.RecordCommands(_SubBatches.Count);
                hwb.SetInactive();
                cornerHwb.SetInactive();

                device.SetVertexBuffer(null);
            }

            _SoftwareBuffer = null;
        }

        new public void Add (RasterShapeDrawCall dc) {
            Add(ref dc);
        }

        new public void Add (ref RasterShapeDrawCall dc) {
            // FIXME
            dc.Index = _DrawCalls.Count;
            dc.IsSimple = (dc.OuterColor4.FastEquals(ref dc.InnerColor4) || (dc.FillMode == (float)RasterFillMode.None)) ? 1 : 0;
            dc.PackedFlags = (
                (int)dc.Type | (dc.IsSimple << 16) | (dc.Shadow.IsEnabled << 17) | ((dc.BlendInLinearSpace ? 1 : 0) << 18) |
                ((dc.Shadow.Inside ? 1 : 0) << 19) | ((dc.OutputInLinearSpace ? 1 : 0) << 20) |
                ((dc.SoftOutline ? 1 : 0) << 21)
            );
            _DrawCalls.Add(ref dc);
        }

        public static RasterShapeBatch New (
            IBatchContainer container, int layer, DefaultMaterialSet materials, Texture2D texture = null, SamplerState desiredSamplerState = null,
            RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null, Texture2D rampTexture = null
        ) {
            if (container == null)
                throw new ArgumentNullException("container");
            if (materials == null)
                throw new ArgumentNullException("materials");

            var result = container.RenderManager.AllocateBatch<RasterShapeBatch>();
            result.Initialize(container, layer, materials);
            result.RasterizerState = rasterizerState;
            result.DepthStencilState = depthStencilState;
            result.BlendState = blendState;
            result.Texture = texture;
            result.SamplerState = desiredSamplerState;
            result.RampTexture = rampTexture;
            result.CaptureStack(0);
            return result;
        }

        protected override void OnReleaseResources () {
            _SubBatches.Dispose();
            base.OnReleaseResources();
        }

        internal static void ClearPolygonVertices () {
            lock (PolygonVertexLock) {
                PolygonVertexFlushRequired = false;
                PolygonVertexCount = 0;
                PolygonVertexWriteOffset = 0;
            }
        }

        internal void FlushPolygonVertices (DeviceManager dm) {
            // HACK
            lock (PolygonVertexLock) {
                // FIXME: Calculate tighter size
                var size = PolygonVertexBuffer.Length * ((2 * 3) + 1);

                if ((PolygonVertexTexture == null) || (PolygonVertexTexture.Width < size)) {
                    dm.DisposeResource(PolygonVertexTexture);
                    PolygonVertexTexture = new Texture2D(dm.Device, size, 1, false, SurfaceFormat.Single);
                    PolygonVertexFlushRequired = true;
                }

                if (!PolygonVertexFlushRequired)
                    return;

                var temp = new float[size];
                int j = 0;
                for (int i = 0; i < PolygonVertexCount; i++) {
                    var vert = PolygonVertexBuffer[i];
                    temp[j] = (float)(int)vert.Type;
                    temp[j + 1] = vert.Position.X;
                    temp[j + 2] = vert.Position.Y;
                    j += 3;
                    if (vert.Type == RasterVertexType.Bezier) {
                        temp[j + 0] = vert.ControlPoint1.X;
                        temp[j + 1] = vert.ControlPoint1.Y;
                        temp[j + 2] = vert.ControlPoint2.X;
                        temp[j + 3] = vert.ControlPoint2.Y;
                        j += 4;
                    }
                }
                PolygonVertexTexture.SetData(temp, 0, size);

                PolygonVertexFlushRequired = false;
            }
        }

        internal int AddPolygonVertices (ArraySegment<RasterPolygonVertex> vertices) {
            lock (PolygonVertexLock) {
                PolygonVertexFlushRequired = true;
                var result = PolygonVertexWriteOffset;
                var newCount = PolygonVertexCount + vertices.Count;
                if ((PolygonVertexBuffer == null) || (newCount > PolygonVertexBuffer.Length)) {
                    var newSize = ((newCount + 15) / 16) * 16;
                    if (PolygonVertexBuffer == null)
                        PolygonVertexBuffer = new RasterPolygonVertex[newSize];
                    else
                        Array.Resize(ref PolygonVertexBuffer, newSize);
                }
                PolygonVertexWriteOffset += vertices.Count;
                PolygonVertexCount = newCount;
                Array.Copy(vertices.Array, vertices.Offset, PolygonVertexBuffer, result, vertices.Count);
                return result;
            }
        }
    }
}
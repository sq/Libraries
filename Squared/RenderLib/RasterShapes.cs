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
        /// Creates a quadratic bezier between the previous vertex and this vertex, using
        ///  ControlPoint as the control point.
        /// NOTE: For closed polygons this uses an approximation
        /// </summary>
        Bezier = 1,
        /// <summary>
        /// Skips the connection between the previous vertex and this vertex, creating
        ///  a gap in the polygon
        /// </summary>
        Skip = 2,
    }

    public struct RasterFillSettings {
        public RasterFillMode Mode;
        public float Offset;
        internal Vector2 FillRangeBiased;
        /// <summary>
        /// Configures the size of the gradient. Shorthand for FillRange.Y.
        /// </summary>
        public float Size {
            get => (FillRangeBiased.Y - FillRangeBiased.X) + 1;
            set => FillRangeBiased.Y = FillRangeBiased.X + value - 1;
        }
        internal float ModeF => Convenience.ImperativeRenderer.ConvertFillMode(Mode, Angle);
        internal float GradientPowerMinusOne;
        /// <summary>
        /// For angular fills, the angle in degrees
        /// </summary>
        public float Angle;
        public bool Repeat;

        /// <summary>
        /// For repeated fills, FillRange.X specifies the padding around the gradient at both ends.
        /// For non-repeated fills, the gradient starts at FillRange.X and ends at FillRange.Y
        /// Both values are in a 0-1 range.
        /// </summary>
        public Vector2 FillRange {
            get => new Vector2(FillRangeBiased.X, FillRangeBiased.Y + 1);
            set => FillRangeBiased = new Vector2(value.X, value.Y - 1);
        }
        /// <summary>
        /// The exponent used to ease the gradient value on both ends. Higher values produce a steeper curve.
        /// </summary>
        public float GradientPower {
            get => GradientPowerMinusOne + 1;
            set => GradientPowerMinusOne = value - 1;
        }

        public RasterFillSettings (
            RasterFillMode mode, float size = 1f, float offset = 0f,
            float angle = 0f, float gradientPower = 1f, bool repeat = false
        ) {
            Mode = mode;
            Offset = offset;
            FillRangeBiased = new Vector2(0, size - 1f);
            Angle = angle;
            GradientPowerMinusOne = gradientPower - 1f;
            Repeat = repeat;
        }

        public RasterFillSettings (
            RasterFillMode mode, Vector2 fillRange, float offset = 0f,
            float angle = 0f, float gradientPower = 1f, bool repeat = false
        ) {
            Mode = mode;
            Offset = offset;
            FillRangeBiased = new Vector2(fillRange.X, fillRange.Y - 1f);
            Angle = angle;
            GradientPowerMinusOne = gradientPower - 1f;
            Repeat = repeat;
        }

        public static implicit operator RasterFillSettings (RasterFillMode mode) =>
            new RasterFillSettings {
                Mode = mode
            };
    }
    
    [Flags]
    public enum RasterTextureCompositeMode : int {
        Multiply = 0,
        Over = 1,
        Under = 2,

        ScreenSpace = 128,
        ScreenSpaceLocal = ScreenSpace | 64,
    }

    public struct RasterTextureSettings {
        public SamplerState SamplerState;        
        private int ShadowMode;
        internal Vector4 ModeAndScaleMinusOne;
        internal Vector4 Placement;
        internal Vector4 TextureOptions;

        public float Saturation {
            get => TextureOptions.X + 1;
            set => TextureOptions.X = value - 1;
        }

        public float Brightness {
            get => TextureOptions.Y + 1;
            set => TextureOptions.Y = value - 1;
        }

        public RasterTextureCompositeMode Mode {
            get {
                return (RasterTextureCompositeMode)ShadowMode;
            }
            set {
                ShadowMode = (int)value;
                ModeAndScaleMinusOne.X = (float)ShadowMode;
            }
        }

        public bool PreserveAspectRatio {
            get {
                return ModeAndScaleMinusOne.Y > 0.5f;
            }
            set {
                ModeAndScaleMinusOne.Y = value ? 1f : 0f;
            }
        }

        public Vector2 Scale {
            get {
                return new Vector2(ModeAndScaleMinusOne.Z + 1, ModeAndScaleMinusOne.W + 1);
            }
            set {
                ModeAndScaleMinusOne.Z = value.X - 1;
                ModeAndScaleMinusOne.W = value.Y - 1;
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
                (ModeAndScaleMinusOne == rhs.ModeAndScaleMinusOne) &&
                (Placement == rhs.Placement) &&
                (TextureOptions == rhs.TextureOptions);
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
        public Vector2 ControlPoint;
        public float LocalRadius;
        public RasterVertexType Type;

        public RasterPolygonVertex (Vector2 position, float localRadius = 0) {
            Type = RasterVertexType.Line;
            Position = position;
            ControlPoint = default;
            LocalRadius = localRadius;
        }

        public RasterPolygonVertex (Vector2 position, Vector2 controlPoint, float localRadius = 0) {
            Type = RasterVertexType.Bezier;
            Position = position;
            ControlPoint = controlPoint;
            LocalRadius = localRadius;
        }

        public static implicit operator RasterPolygonVertex (Vector2 position) =>
            new RasterPolygonVertex(position);

        public override string ToString () {
            if (Type == RasterVertexType.Line)
                return $"({Position.X}, {Position.Y}) radius={LocalRadius}";
            else
                return $"bezier (prev) ({ControlPoint}) ({Position.X}, {Position.Y}) radius={LocalRadius}";
        }
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
        public RasterFillSettings Fill;
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

        public bool Equals (in RasterShadowSettings rhs) {
            return (IsEnabled == rhs.IsEnabled) &&
                (Offset == rhs.Offset) &&
                (Softness == rhs.Softness) &&
                (Expansion == rhs.Expansion) &&
                (FillSuppressionMinusOne == rhs.FillSuppressionMinusOne) &&
                (_Color == rhs._Color) &&
                (Inside == rhs.Inside);
        }

        public override bool Equals (object obj) {
            if (obj is RasterShadowSettings rss)
                return Equals(rss);
            else
                return false;
        }

        // FIXME
        public override int GetHashCode () {
            return IsEnabled.GetHashCode() ^ Color.GetHashCode();
        }
    }

    internal struct RasterShader {
        public Material Material;
        public EffectParameter BlendInLinearSpace,
            OutputInLinearSpace,
            RasterTexture,
            RampTexture,
            RampUVOffset,
            ShadowOptions,
            ShadowOptions2,
            ShadowColorLinear,
            TextureModeAndSize,
            TexturePlacement,
            TextureTraits,
            TextureOptions;

        public RasterShader (Material material) {
            Material = material;
            var p = material.Effect.Parameters;
            BlendInLinearSpace = p["BlendInLinearSpace"];
            OutputInLinearSpace = p["OutputInLinearSpace"];
            RasterTexture = p["RasterTexture"];
            RampTexture = p["RampTexture"];
            RampUVOffset = p["RampUVOffset"];
            ShadowOptions = p["ShadowOptions"];
            ShadowOptions2 = p["ShadowOptions2"];
            ShadowColorLinear = p["ShadowColorLinear"];
            TextureModeAndSize = p["TextureModeAndSize"];
            TexturePlacement = p["TexturePlacement"];
            TextureTraits = p["TextureTraits"];
            TextureOptions = p["TextureOptions"];
        }
    }

    public class RasterShapeBatch : ListBatch<RasterShapeDrawCall> {
        private sealed class RasterShapeDrawCallSorter : IRefComparer<RasterShapeDrawCall>, IComparer<RasterShapeDrawCall> {
            public int Compare (ref RasterShapeDrawCall lhs, ref RasterShapeDrawCall rhs) {
                unchecked {
                    var result = lhs.SortKey - rhs.SortKey;
                    if (result == 0)
                        result = lhs.PackedFlags - rhs.PackedFlags;
                    if (result == 0)
                        result = lhs.TextureSettings.GetHashCode() - rhs.TextureSettings.GetHashCode();
                    if (result == 0)
                        result = lhs.Index - rhs.Index;
                    return result;
                }
            }

            public int Compare (RasterShapeDrawCall lhs, RasterShapeDrawCall rhs) {
                return Compare(ref lhs, ref rhs);
            }
        }

        private class BatchManager : SubBatchManager<RasterShapeBatch, RasterShapeDrawCall, SubBatch> {
            public static readonly BatchManager Instance = new BatchManager();

            protected override bool KeyEquals (
                RasterShapeBatch self, ref RasterShapeDrawCall last, ref RasterShapeDrawCall dc
            ) {
                return !(
                    (dc.Type != last.Type) &&
                    (!self.UseUbershader || dc.Type == RasterShapeType.Polygon || last.Type == RasterShapeType.Polygon) ||
                    (dc.BlendInLinearSpace != last.BlendInLinearSpace) ||
                    !dc.Shadow.Equals(in last.Shadow) ||
                    (dc.IsSimple != last.IsSimple) ||
                    !dc.TextureSettings.Equals(last.TextureSettings)
                );
            }

            protected override void CreateBatch (RasterShapeBatch self, ref RasterShapeDrawCall drawCall, int offset, int count) {
                self._SubBatches.Add(new SubBatch {
                    InstanceOffset = offset,
                    InstanceCount = count,
                    BlendInLinearSpace = drawCall.BlendInLinearSpace,
                    Type = drawCall.Type,
                    Shadow = drawCall.Shadow,
                    Shadowed = ShouldBeShadowed(in drawCall.Shadow),
                    Simple = drawCall.IsSimple != 0,
                    TextureSettings = drawCall.TextureSettings
                });
            }
        }

        private struct SubBatch {
            public int InstanceOffset, InstanceCount;
            public RasterShapeType Type;
            public bool BlendInLinearSpace, Shadowed, Simple;
            public RasterShadowSettings Shadow;
            internal RasterTextureSettings TextureSettings;
        }

        private DenseList<SubBatch> _SubBatches;

        private BufferGenerator<RasterShapeVertex> _BufferGenerator = null;
        private BufferGenerator<CornerVertex>.SoftwareBuffer _CornerBuffer = null;
        private PolygonBuffer _PolygonBuffer = null;

        protected static ThreadLocal<VertexBufferBinding[]> _ScratchBindingArray = 
            new ThreadLocal<VertexBufferBinding[]>(() => new VertexBufferBinding[2]);

        internal ArrayPoolAllocator<RasterShapeVertex> VertexAllocator;
        internal ISoftwareBuffer _SoftwareBuffer;

        public DefaultMaterialSet Materials;
        public Texture2D Texture;
        public Texture2D RampTexture;
        public Vector2 RampUVOffset;

        public bool UseUbershader = false;

        private static readonly RasterShapeDrawCallSorter ShapeDrawCallSorter = new RasterShapeDrawCallSorter();

        const int MaxVertexCount = 65535;

        const int CornerBufferRepeatCount = 5;
        const int CornerBufferVertexCount = CornerBufferRepeatCount * 4;
        const int CornerBufferIndexCount = CornerBufferRepeatCount * 6;
        const int CornerBufferPrimCount = CornerBufferRepeatCount * 2;

        public DepthStencilState DepthStencilState;
        public BlendState BlendState;
        public RasterizerState RasterizerState;
        public SamplerState SamplerState;
        public RasterShadowSettings ShadowSettings;

        static RasterShapeBatch () {
            AdjustPoolCapacities(1024, null, 512, 16);
        }

        public void Initialize (IBatchContainer container, int layer, DefaultMaterialSet materials) {
            base.Initialize(container, layer, materials.RasterShapeUbershader, true);

            Materials = materials;

            DepthStencilState = null;
            BlendState = null;
            RasterizerState = null;
            SamplerState = null;
            Texture = null;
            RampTexture = null;
            ShadowSettings = default;
            RampUVOffset = default;
            BatchManager.Instance.Clear(this, ref _SubBatches);

            if (VertexAllocator == null)
                VertexAllocator = container.RenderManager.GetArrayAllocator<RasterShapeVertex>();
        }

        new public static void AdjustPoolCapacities (
            int? smallItemSizeLimit, int? largeItemSizeLimit,
            int? smallPoolCapacity, int? largePoolCapacity
        ) {
            ListBatch<RasterShapeDrawCall>.AdjustPoolCapacities(smallItemSizeLimit, largeItemSizeLimit, smallPoolCapacity, largePoolCapacity);
        }

        private static bool ShouldBeShadowed (in RasterShadowSettings shadow) {
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
                BatchManager.Instance.Setup(this, ref _SubBatches, count);

                _DrawCalls.Sort(ShapeDrawCallSorter);

                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<RasterShapeVertex>>();
                _CornerBuffer = Container.Frame.PrepareData.GetCornerBuffer(Container, CornerBufferRepeatCount);
                var swb = _BufferGenerator.Allocate(vertexCount, 1);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<RasterShapeVertex>(swb.Vertices);
                var vw = vb.GetWriter(count, clear: false);

                ref var firstDc = ref _DrawCalls.Item(0);
                BatchManager.Instance.Start(this, ref firstDc, out var state);

                for (int i = 0, j = 0; i < count; i++, j+=4) {
                    ref var dc = ref _DrawCalls.Item(i);
                    BatchManager.Instance.Step(this, ref dc, ref state, i);

                    var fill = dc.Fill;
                    var gpower = fill.GradientPowerMinusOne + 1f;
                    if (fill.Repeat)
                        gpower = -gpower;
                    var vert = new RasterShapeVertex {
                        PointsAB = new Vector4(dc.A.X, dc.A.Y, dc.B.X, dc.B.Y),
                        // FIXME: Fill this last space with a separate value?
                        PointsCD = new Vector4(dc.C.X, dc.C.Y, dc.Radius.X, dc.Radius.Y),
                        InnerColor = dc.InnerColor4,
                        OutlineColor = dc.OutlineColor4,
                        OuterColor = dc.OuterColor4,
                        Parameters = new Vector4(dc.OutlineSize * (dc.SoftOutline ? -1 : 1), dc.AnnularRadius, fill.ModeF, dc.OutlineGammaMinusOne),
                        Parameters2 = new Vector4(gpower, fill.FillRange.X, fill.FillRange.Y, fill.Offset),
                        TextureRegion = dc.TextureBounds.ToVector4(),
                        Type = (short)dc.Type,
                        WorldSpace = (short)(dc.WorldSpace ? 1 : 0)
                    };
                    vw.Write(vert);
                }

                BatchManager.Instance.Finish(this, ref state, count);
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
            if (count <= 0) {
                _SoftwareBuffer = null;
                return;
            }
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
                if (sb.Type == RasterShapeType.Polygon) {
                    _PolygonBuffer.Flush(manager);
                    lock (_PolygonBuffer.Lock)
                        rasterShader.Material.Effect.Parameters["PolygonVertexBufferInvSize"]?.SetValue(
                            new Vector2(1.0f / _PolygonBuffer.TextureWidth, 1.0f / _PolygonBuffer.TextureHeight)
                        );
                }

                rasterShader.BlendInLinearSpace.SetValue(sb.BlendInLinearSpace);
                rasterShader.OutputInLinearSpace.SetValue(isSrgbRenderTarget);
                rasterShader.RasterTexture?.SetValue(Texture);
                rasterShader.RampTexture?.SetValue(RampTexture);
                rasterShader.RampUVOffset?.SetValue(RampUVOffset);

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

                var textureSamplerState = sb.TextureSettings.SamplerState ?? SamplerState ?? SamplerState.LinearWrap;

                rasterShader.ShadowOptions.SetValue(new Vector4(
                    shadowOffset.X, shadowOffset.Y,
                    shadowSoftness, sb.Shadow.FillSuppression
                ));
                rasterShader.ShadowOptions2.SetValue(new Vector2(
                    shadowExpansion, sb.Shadow.Inside ? 1 : 0
                ));
                rasterShader.ShadowColorLinear.SetValue(shadowColor);
                Vector4 mas = sb.TextureSettings.ModeAndScaleMinusOne, to = sb.TextureSettings.TextureOptions;
                mas.Z += 1;
                mas.W += 1;

                var tempSamplerState = textureSamplerState ?? SamplerState ?? SamplerState.LinearClamp;
                to.Z = tempSamplerState?.AddressU == TextureAddressMode.Clamp ? 1 : 0;
                to.W = tempSamplerState?.AddressV == TextureAddressMode.Clamp ? 1 : 0;

                if (Texture != null) {
                    if ((sb.TextureSettings.Mode & RasterTextureCompositeMode.ScreenSpace) == RasterTextureCompositeMode.ScreenSpace) {
                        mas.Z *= Texture.Width;
                        mas.W *= Texture.Height;
                    }
                    var traits = Evil.TextureUtils.GetTraits(Texture.Format);
                    rasterShader.TextureTraits?.SetValue(traits);
                }
                rasterShader.TextureModeAndSize?.SetValue(mas);
                rasterShader.TexturePlacement?.SetValue(sb.TextureSettings.Placement);
                rasterShader.TextureOptions?.SetValue(to);

                manager.ApplyMaterial(rasterShader.Material, ref MaterialParameters);

                if (BlendState != null)
                    device.BlendState = BlendState;
                if (DepthStencilState != null)
                    device.DepthStencilState = DepthStencilState;
                if (RasterizerState != null)
                    device.RasterizerState = RasterizerState;

                // FIXME: why the hell
                device.Textures[0] = Texture;
                device.SamplerStates[0] = textureSamplerState;
                device.Textures[3] = RampTexture;

                if (sb.Type == RasterShapeType.Polygon) {
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
                rasterShader.RasterTexture?.SetValue((Texture2D)null);
                rasterShader.RampTexture?.SetValue((Texture2D)null);
            }

            NativeBatch.RecordCommands(_SubBatches.Count);
            hwb.SetInactive();
            cornerHwb.SetInactive();

            device.SetVertexBuffer(null);
            _SoftwareBuffer = null;
        }

        new public void Add (in RasterShapeDrawCall dc) {
            var result = dc;
            // FIXME
            result.Index = _DrawCalls.Count;
            result.IsSimple = (result.OuterColor4.FastEquals(in result.InnerColor4) || (result.Fill.Mode == RasterFillMode.None)) ? 1 : 0;
            result.PackedFlags = (
                (int)result.Type | (result.IsSimple << 16) | (result.Shadow.IsEnabled << 17) | ((result.BlendInLinearSpace ? 1 : 0) << 18) |
                ((result.Shadow.Inside ? 1 : 0) << 19) | ((result.SoftOutline ? 1 : 0) << 20)
            );
            _DrawCalls.Add(ref result);
        }

        public static RasterShapeBatch New (
            IBatchContainer container, int layer, DefaultMaterialSet materials, Texture2D texture = null, SamplerState desiredSamplerState = null,
            RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null,
            Texture2D rampTexture = null, Vector2? rampUVOffset = null
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
            result.RampUVOffset = rampUVOffset ?? Vector2.Zero;
            result.CaptureStack(0);
            return result;
        }

        protected override void OnReleaseResources () {
            _SubBatches.Dispose();
            base.OnReleaseResources();
        }

        public void AddPolygonVertices (
            ArraySegment<RasterPolygonVertex> vertices, out int offset, out int count
        ) {
            _PolygonBuffer = Container.Coordinator.PolygonBuffer;
            _PolygonBuffer.AddVertices(vertices, out offset, out count);
        }
    }
}
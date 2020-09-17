using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public VertexDeclaration VertexDeclaration {
            get { return _VertexDeclaration; }
        }
    }

    public enum RasterShapeType : int {
        Ellipse = 0,
        LineSegment = 1,
        Rectangle = 2,
        Triangle = 3,
        QuadraticBezier = 4,
        Arc = 5
    }

    public struct pSRGBColor {
        public bool IsVector4;
        public Vector4 Vector4;
        public Color Color;

        public pSRGBColor (float r, float g, float b, float a) {
            IsVector4 = true;
            Vector4 = new Vector4(r * a, g * a, b * a, a);
            Color = default(Color);
        }

        public pSRGBColor (Color c) {
            IsVector4 = false;
            Vector4 = default(Vector4);
            Color = c;
        }

        public pSRGBColor (Vector4 v4, bool isPremultiplied = true) {
            IsVector4 = true;
            if (!isPremultiplied) {
                float a = v4.W;
                Vector4 = v4 * a;
                Vector4.W = a;
            } else {
                Vector4 = v4;
            }
            Color = default(Color);
        }

        public Vector4 ToVector4 () {
            if (IsVector4)
                return Vector4;
            else
                return new Vector4(Color.R / 255f, Color.G / 255f, Color.B / 255f, Color.A / 255f);
        }

        public Vector4 ToPLinear () {
            var v4 = ToVector4();
            float alpha = v4.W;
            if (alpha <= 0)
                return Vector4.Zero;

            // Unpremultiply
            v4 *= (1.0f / alpha);

            // Compute low/high linear pairs from the sRGB values
            var low = v4 / 12.92f;
            var preHigh = (v4 + new Vector4(0.055f)) / 1.055f;
            var high = new Vector3(
                (float)Math.Pow(preHigh.X, 2.4),
                (float)Math.Pow(preHigh.Y, 2.4),
                (float)Math.Pow(preHigh.Z, 2.4)
            );
            // Select low/high value based on threshold
            var result = new Vector4(
                v4.X <= 0.04045f ? low.X : high.X,
                v4.Y <= 0.04045f ? low.Y : high.Y,
                v4.Z <= 0.04045f ? low.Z : high.Z,
                1
            );

            result *= alpha;
            return result;
        }

        public static pSRGBColor operator * (pSRGBColor color, float alpha) {
            var result = color.ToVector4();
            result *= alpha;
            return new pSRGBColor(result, true);
        }

        public static implicit operator pSRGBColor (Vector4 v4) {
            return new pSRGBColor(v4);
        }

        public static implicit operator pSRGBColor (Color c) {
            return new pSRGBColor(c);
        }

        public bool Equals (pSRGBColor rhs) {
            var a = ToVector4();
            var b = rhs.ToVector4();
            return (a == b);
        }

        public override bool Equals (object obj) {
            if (obj is pSRGBColor)
                return Equals((pSRGBColor)obj);
            else if (obj is Vector4)
                return Equals((pSRGBColor)(Vector4)obj);
            else if (obj is Color)
                return Equals((pSRGBColor)(Color)obj);
            else
                return false;
        }

        public static bool operator == (pSRGBColor lhs, pSRGBColor rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (pSRGBColor lhs, pSRGBColor rhs) {
            return !lhs.Equals(rhs);
        }

        // FIXME
        public override int GetHashCode () {
            return 0;
        }
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

        /// <summary>
        /// Configures the shadow for the raster shape, if any.
        /// </summary>
        public RasterShadowSettings Shadow;

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
        public pSRGBColor Color;

        public bool Equals (ref RasterShadowSettings rhs) {
            return (Offset == rhs.Offset) &&
                (Softness == rhs.Softness) &&
                (FillSuppressionMinusOne == rhs.FillSuppressionMinusOne) &&
                (Color == rhs.Color);
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
            return 0;
        }
    }

    public class RasterShapeBatch : ListBatch<RasterShapeDrawCall> {
        private class RasterShapeTypeSorter : IRefComparer<RasterShapeDrawCall> {
            public int Compare (ref RasterShapeDrawCall lhs, ref RasterShapeDrawCall rhs) {
                var result = ((int)lhs.Type).CompareTo((int)(rhs.Type));
                if (result == 0)
                    result = lhs.BlendInLinearSpace.CompareTo(rhs.BlendInLinearSpace);
                if (result == 0)
                    result = lhs.Index.CompareTo(rhs.Index);
                return result;
            }
        }

        private struct SubBatch {
            public RasterShapeType Type;
            public bool BlendInLinearSpace;
            public RasterShadowSettings Shadow;
            public int InstanceOffset, InstanceCount;
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

        public bool UseUbershader = false;

        private readonly RasterShapeTypeSorter ShapeTypeSorter = new RasterShapeTypeSorter();

        private DenseList<SubBatch> _SubBatches = new DenseList<SubBatch>();

        private static ListPool<SubBatch> _SubListPool = new ListPool<SubBatch>(
            64, 4, 16, 64, 256
        );

        const int MaxVertexCount = 65535;

        public DepthStencilState DepthStencilState;
        public BlendState BlendState;
        public RasterizerState RasterizerState;
        public RasterShadowSettings ShadowSettings;

        public void Initialize (IBatchContainer container, int layer, DefaultMaterialSet materials) {
            base.Initialize(container, layer, materials.RasterShape, true);

            Materials = materials;

            _SubBatches.ListPool = _SubListPool;
            _SubBatches.Clear();

            DepthStencilState = null;
            BlendState = null;
            RasterizerState = null;

            Texture = null;

            if (VertexAllocator == null)
                VertexAllocator = container.RenderManager.GetArrayAllocator<RasterShapeVertex>();
        }

        protected override void Prepare (PrepareManager manager) {
            var count = _DrawCalls.Count;
            var vertexCount = count;
            if (count > 0) {
                if (!UseUbershader)
                    _DrawCalls.Sort(ShapeTypeSorter);

                _BufferGenerator = Container.RenderManager.GetBufferGenerator<BufferGenerator<RasterShapeVertex>>();
                _CornerBuffer = QuadUtils.CreateCornerBuffer(Container);
                var swb = _BufferGenerator.Allocate(vertexCount, 1);
                _SoftwareBuffer = swb;

                var vb = new Internal.VertexBuffer<RasterShapeVertex>(swb.Vertices);
                var vw = vb.GetWriter(count);

                var lastType = _DrawCalls[0].Type;
                var lastBlend = _DrawCalls[0].BlendInLinearSpace;
                var lastShadow = _DrawCalls[0].Shadow;
                var lastOffset = 0;

                for (int i = 0, j = 0; i < count; i++, j+=4) {
                    var dc = _DrawCalls[i];

                    if (
                        ((dc.Type != lastType) && !UseUbershader) || 
                        (dc.BlendInLinearSpace != lastBlend) ||
                        !dc.Shadow.Equals(ref lastShadow)
                    ) {
                        _SubBatches.Add(new SubBatch {
                            InstanceOffset = lastOffset,
                            InstanceCount = (i - lastOffset),
                            BlendInLinearSpace = lastBlend,
                            Type = lastType,
                            Shadow = lastShadow
                        });
                        lastOffset = i;
                        lastType = dc.Type;
                        lastShadow = dc.Shadow;
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
                    Type = lastType,
                    Shadow = lastShadow
                });

                NativeBatch.RecordPrimitives(count * 2);
            }
        }

        private Material PickBaseMaterial (RasterShapeType? type) {
            switch (type) {
                case RasterShapeType.Ellipse:
                    return (Texture != null) ? Materials.TexturedRasterEllipse : Materials.RasterEllipse;
                case RasterShapeType.Rectangle:
                    return (Texture != null) ? Materials.TexturedRasterRectangle : Materials.RasterRectangle;
                case RasterShapeType.LineSegment:
                    return (Texture != null) ? Materials.TexturedRasterLine : Materials.RasterLine;
                case RasterShapeType.Triangle:
                    return (Texture != null) ? Materials.TexturedRasterTriangle : Materials.RasterTriangle;
                default:
                    return (Texture != null) ? Materials.TexturedRasterShape : Materials.RasterShape;
            }
        }

        private Material PickMaterial (RasterShapeType? type) {
            var baseMaterial = PickBaseMaterial(type);
            return baseMaterial;
        }

        public override void Issue (DeviceManager manager) {
            var count = _DrawCalls.Count;
            if (count > 0) {
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

                foreach (var sb in _SubBatches) {
                    var material = UseUbershader ? PickMaterial(null) : PickMaterial(sb.Type);
                    manager.ApplyMaterial(material);

                    if (BlendState != null)
                        device.BlendState = BlendState;
                    if (DepthStencilState != null)
                        device.DepthStencilState = DepthStencilState;
                    if (RasterizerState != null)
                        device.RasterizerState = RasterizerState;

                    material.Effect.Parameters["BlendInLinearSpace"].SetValue(sb.BlendInLinearSpace);
                    material.Effect.Parameters["RasterTexture"]?.SetValue(Texture);

                    // HACK: If the shadow color is fully transparent, suppress the offset and softness.
                    // If we don't do this, the bounding box of the shapes will be pointlessly expanded.
                    var shadowColor = sb.BlendInLinearSpace ? sb.Shadow.Color.ToPLinear() : sb.Shadow.Color.ToVector4();
                    var shadowOffset = (shadowColor.W > 0) ? sb.Shadow.Offset : Vector2.Zero;
                    var shadowSoftness = (shadowColor.W > 0) ? sb.Shadow.Softness : 0;
                    // Also suppress the shadow entirely if the parameters are such that it would basically be invisible
                    if ((shadowOffset.LengthSquared() < 0.5) && (shadowSoftness < 0.1)) {
                        shadowOffset = Vector2.Zero;
                        shadowColor = Vector4.Zero;
                        shadowSoftness = 0;
                    }
                    material.Effect.Parameters["ShadowOptions"].SetValue(new Vector4(
                        shadowOffset.X, shadowOffset.Y,
                        shadowSoftness, sb.Shadow.FillSuppression
                    ));

                    material.Effect.Parameters["ShadowColorLinear"].SetValue(shadowColor);
                    material.Flush();

                    // FIXME: why the hell
                    device.Textures[0] = Texture;
                    device.SamplerStates[0] = SamplerState ?? SamplerState.LinearWrap;

                    scratchBindings[1] = new VertexBufferBinding(
                        vb, _SoftwareBuffer.HardwareVertexOffset + sb.InstanceOffset, 1
                    );

                    device.SetVertexBuffers(scratchBindings);

                    device.DrawInstancedPrimitives(
                        PrimitiveType.TriangleList, 
                        0, _CornerBuffer.HardwareVertexOffset, 4, 
                        _CornerBuffer.HardwareIndexOffset, 2, 
                        sb.InstanceCount
                    );

                    device.Textures[0] = null;
                    material.Effect.Parameters["RasterTexture"]?.SetValue((Texture2D)null);
                }

                NativeBatch.RecordCommands(_SubBatches.Count);
                hwb.SetInactive();
                cornerHwb.SetInactive();

                device.SetVertexBuffer(null);
            }

            _SoftwareBuffer = null;

            base.Issue(manager);
        }

        new public void Add (RasterShapeDrawCall dc) {
            dc.Index = _DrawCalls.Count;
            _DrawCalls.Add(ref dc);
        }

        new public void Add (ref RasterShapeDrawCall dc) {
            // FIXME
            dc.Index = _DrawCalls.Count;
            _DrawCalls.Add(ref dc);
        }

        public static RasterShapeBatch New (
            IBatchContainer container, int layer, DefaultMaterialSet materials, Texture2D texture = null, SamplerState desiredSamplerState = null,
            RasterizerState rasterizerState = null, DepthStencilState depthStencilState = null, BlendState blendState = null
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
            result.CaptureStack(0);
            return result;
        }

        protected override void OnReleaseResources () {
            _SubBatches.Dispose();
            base.OnReleaseResources();
        }
    }
}

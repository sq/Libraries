#if DEBUG
#define MEASURE_CACHE_HIT_COUNTS
#endif

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.RasterShape;
using Squared.Render.RasterStroke;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.DeclarativeSort;
using Squared.Util.Text;

namespace Squared.Render.Convenience {
    public static class RenderStates {
        /// <summary>
        /// Assumes premultiplied source and premultiplied destination.
        /// Approximates porter-duff Over and produces premultiplied output.
        /// </summary>
        public static readonly BlendState PorterDuffOver = new BlendState {
            Name = "PorterDuffOver",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            ColorSourceBlend = Blend.One,
        };

        /// <summary>
        /// Assumes non-premultiplied source and premultiplied destination.
        /// Approximates porter-duff Over and produces premultiplied output.
        /// </summary>
        public static readonly BlendState PorterDuffNonPremultipliedOver = new BlendState {
            Name = "PorterDuffNonPremultipliedOver",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            ColorSourceBlend = Blend.SourceAlpha,
        };

        public static readonly BlendState SubtractiveBlend = new BlendState {
            Name = "SubtractiveBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState SubtractiveBlendAllChannels = new BlendState {
            Name = "SubtractiveBlendAllChannels",
            AlphaBlendFunction = BlendFunction.ReverseSubtract,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState SubtractiveBlendNonPremultiplied = new BlendState {
            Name = "SubtractiveBlendNonPremultiplied",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState InverseSubtractiveBlend = new BlendState {
            Name = "InverseSubtractiveBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.InverseSourceColor
        };

        public static readonly BlendState AdditiveBlend = new BlendState {
            Name = "AdditiveBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState AdditiveBlendNonPremultiplied = new BlendState {
            Name = "AdditiveBlendNonPremultiplied",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };


        public static readonly BlendState ReplaceDestinationPremultiplied = new BlendState {
            Name = "ReplaceDestinationPremultiplied",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState ReplaceDestinationNonPremultiplied = new BlendState {
            Name = "ReplaceDestinationNonPremultiplied",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState ReplaceDestinationAlpha = new BlendState {
            Name = "ReplaceDestinationAlpha",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.Zero,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.Zero
        };

        public static readonly BlendState IncreaseDestinationAlpha = new BlendState {
            Name = "IncreaseDestinationAlpha",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.Zero
        };

        public static readonly BlendState RefineDestinationAlpha = new BlendState {
            Name = "RefineDestinationAlpha",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.SourceAlpha,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.Zero
        };

        public static readonly BlendState DestinationAlphaMask = new BlendState {
            Name = "DestinationAlphaMask",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.InverseDestinationAlpha,
            ColorSourceBlend = Blend.DestinationAlpha
        };

        public static readonly BlendState DestinationAlphaMaskAdditive = new BlendState {
            Name = "DestinationAlphaMaskAdditive",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.DestinationAlpha
        };

        public static readonly BlendState RasterShapeAlphaBlend = PorterDuffNonPremultipliedOver;

        public static readonly BlendState RasterShapeAdditiveBlend = new BlendState {
            Name = "RasterShapeAdditiveBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState RasterShapeSubtractiveBlend = new BlendState {
            Name = "RasterShapeSubtractiveBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };

        public static readonly BlendState RasterShapeMaxBlend = new BlendState {
            Name = "RasterShapeMaxBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Max,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.SourceAlpha
        };


        public static readonly BlendState MaxBlendValue = new BlendState {
            Name = "MaxBlendValue",
            AlphaBlendFunction = BlendFunction.Max,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Max,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState MinBlendValue = new BlendState {
            Name = "MinBlendValue",
            AlphaBlendFunction = BlendFunction.Min,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Min,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState MaxBlend = new BlendState {
            Name = "MaxBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Max,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState MinBlend = new BlendState {
            Name = "MinBlend",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One,
            ColorBlendFunction = BlendFunction.Min,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.One
        };

        public static readonly BlendState DrawNone = new BlendState {
            Name = "DrawNone",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.One,
            ColorSourceBlend = Blend.Zero,
            ColorWriteChannels = ColorWriteChannels.None
        };

        public static readonly BlendState MultiplyColor = new BlendState {
            Name = "MultiplyColor",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.SourceColor,
            ColorSourceBlend = Blend.Zero
        };

        public static readonly BlendState MultiplyColor2x = new BlendState {
            Name = "MultiplyColor2x",
            AlphaBlendFunction = BlendFunction.Add,
            AlphaDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.Zero,
            ColorBlendFunction = BlendFunction.Add,
            ColorDestinationBlend = Blend.SourceColor,
            ColorSourceBlend = Blend.DestinationColor
        };

        public static readonly RasterizerState ScissorOnly = new RasterizerState {
            Name = "ScissorOnly",
            CullMode = CullMode.None,
            ScissorTestEnable = true
        };

        public static readonly DepthStencilState StencilErase = new DepthStencilState {
            Name = "StencilErase",
            StencilEnable = true,
            StencilFunction = CompareFunction.Always,
            StencilPass = StencilOperation.Replace,
            ReferenceStencil = 0,
            DepthBufferEnable = false
        };

        public static readonly DepthStencilState StencilWrite = new DepthStencilState {
            Name = "StencilWrite",
            StencilEnable = true,
            StencilFunction = CompareFunction.Always,
            StencilPass = StencilOperation.Replace,
            ReferenceStencil = 1,
            DepthBufferEnable = false
        };

        public static readonly DepthStencilState StencilIntersection = new DepthStencilState {
            Name = "StencilIntersection",
            StencilEnable = true,
            StencilFunction = CompareFunction.Equal,
            StencilPass = StencilOperation.Keep,
            StencilFail = StencilOperation.Zero,
            ReferenceStencil = 1,
            DepthBufferEnable = false
        };

        public static readonly DepthStencilState StencilTest = new DepthStencilState {
            Name = "StencilTest",
            StencilEnable = true,
            StencilFunction = CompareFunction.NotEqual,
            StencilWriteMask = 0,
            StencilPass = StencilOperation.Keep,
            ReferenceStencil = 0,
            StencilFail = StencilOperation.Keep,
            DepthBufferEnable = false
        };

        public static readonly DepthStencilState OutlinedTextDepthStencil = new DepthStencilState {
            Name = "OutlinedTextDepthStencil",
            DepthBufferEnable = true,
            DepthBufferWriteEnable = true,
            // We want to allow pixels with the same depth to draw on top of each other, so that
            //  antialiasing looks correct - we just want to block pixels of lower depth (outline/shadow pixels)
            DepthBufferFunction = CompareFunction.GreaterEqual
        };

        /// <summary>
        /// Provides a sampler state appropriate for rendering text. The mip bias is adjusted to preserve sharpness.
        /// </summary>
        public static readonly SamplerState Text = new SamplerState {
            Name = "Text",
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            Filter = TextureFilter.Linear,
            MipMapLevelOfDetailBias = -0.65f
        };

        public static readonly SamplerState LinearMirror = new SamplerState {
            Name = "LinearMirror",
            AddressU = TextureAddressMode.Mirror,
            AddressV = TextureAddressMode.Mirror,
            AddressW = TextureAddressMode.Clamp,
            Filter = TextureFilter.Linear,
        };
    }

    public struct SamplerStateSet {
        public DenseList<SamplerState> FragmentStates,
            VertexStates;

        public SamplerState this [bool vertex, int index] {
            get {
                SamplerState result;
                if (vertex)
                    VertexStates.TryGetItem(index, out result);
                else
                    FragmentStates.TryGetItem(index, out result);
                return result;
            }
            set {
                ref DenseList<SamplerState> target = ref (vertex ? ref VertexStates : ref FragmentStates);
                while (target.Count <= index)
                    target.Add(null);
                target[index] = value;
            }
        }

        public void Apply (DeviceManager dm) {
            var dev = dm.Device;
            for (int i = 0; i < FragmentStates.Count; i++)
                if (FragmentStates[i] != null)
                    dev.SamplerStates[i] = FragmentStates[i];

            for (int i = 0; i < VertexStates.Count; i++)
                if (VertexStates[i] != null)
                    dev.VertexSamplerStates[i] = VertexStates[i];
        }

        private static bool AreEqual (ref DenseList<SamplerState> lhs, ref DenseList<SamplerState> rhs) {
            if (lhs.Count != rhs.Count)
                return false;
            for (int i = 0; i < lhs.Count; i++)
                if (lhs[i] != rhs[i])
                    return false;
            return true;
        }

        public bool Equals (ref SamplerStateSet rhs) {
            return AreEqual(ref FragmentStates, ref rhs.FragmentStates) &&
                AreEqual(ref VertexStates, ref rhs.VertexStates);
        }

        public override bool Equals (object obj) {
            if (obj is SamplerStateSet mss)
                return Equals(ref mss);
            else
                return false;
        }

        public override int GetHashCode () => 0;

        internal void Merge (ref SamplerStateSet samplers) {
            for (int i = 0; i < samplers.FragmentStates.Count; i++)
                this[false, i] = samplers.FragmentStates[i] ?? this[false, i];
            for (int i = 0; i < samplers.VertexStates.Count; i++)
                this[true, i] = samplers.VertexStates[i] ?? this[true, i];
        }
    }

    public struct PipelineStateSet {
        public BlendState BlendState;
        public DepthStencilState DepthStencilState;
        public RasterizerState RasterizerState;

        public void Apply (DeviceManager dm) {
            var dev = dm.Device;
            if (BlendState != null)
                dev.BlendState = BlendState;
            if (DepthStencilState != null)
                dev.DepthStencilState = DepthStencilState;
            if (RasterizerState != null)
                dev.RasterizerState = RasterizerState;
        }

        public bool Equals (ref PipelineStateSet rhs) {
            return (BlendState == rhs.BlendState) &&
                (DepthStencilState == rhs.DepthStencilState) &&
                (RasterizerState == rhs.RasterizerState);
        }

        public override bool Equals (object obj) {
            if (obj is PipelineStateSet mss)
                return Equals(ref mss);
            else
                return false;
        }

        public override int GetHashCode () => 0;

        internal void Merge (ref PipelineStateSet pipeline) {
            BlendState = pipeline.BlendState ?? BlendState;
            DepthStencilState = pipeline.DepthStencilState ?? DepthStencilState;
            RasterizerState = pipeline.RasterizerState ?? RasterizerState;
        }
    }

    public sealed class MaterialStateSet {
        public PipelineStateSet Pipeline;
        public SamplerStateSet Samplers;

        public void Apply (DeviceManager dm) {
            var dev = dm.Device;
            Pipeline.Apply(dm);
            Samplers.Apply(dm);
        }

        public bool Equals (MaterialStateSet rhs) {
            if (rhs == null) {
                // FIXME: return true if all values are default?                
                return false;
            } else {
                // FIXME
                return Pipeline.Equals(ref rhs.Pipeline) &&
                    Samplers.Equals(ref rhs.Samplers);
            }
        }

        public override bool Equals (object obj) {
            if (ReferenceEquals(this, obj))
                return true;

            if (obj is MaterialStateSet mss)
                return Equals(mss);
            else
                return false;
        }

        public BlendState BlendState => Pipeline.BlendState;
        public DepthStencilState DepthStencilState => Pipeline.DepthStencilState;
        public RasterizerState RasterizerState => Pipeline.RasterizerState;
        public SamplerState SamplerState1 => Samplers[false, 0];
        public SamplerState SamplerState2 => Samplers[false, 1];
        public SamplerState SamplerState3 => Samplers[false, 2];

        public override int GetHashCode () => 0;
    }

    public static class MaterialUtil {
        public static void ClearTexture (this EffectParameterCollection p, string parameterName) {
            if (p == null)
                return;
            var param = p[parameterName];
            if (param == null)
                return;
            param.SetValue((Texture2D)null);
        }

        public static void ClearTextures (this EffectParameterCollection p, params string[] parameterNames) {
            if (p == null)
                return;

            // Right now FNA's EffectParameterCollection just loops over all the items, so we might as well do that ourselves once
            foreach (var param in p) {
                foreach (var name in parameterNames) {
                    if (param.Name == name) {
                        param.SetValue((Texture2D)null);
                        break;
                    }
                }
            }
        }

        public static void ClearTextures (this MaterialEffectParameters p, params string[] parameterNames) {
            if (p == null)
                return;

            foreach (var name in parameterNames) {
                if (p.TryGetParameter(name, out var param)) {
                    param.SetValue((Texture2D)null);
                    break;
                }
            }
        }

        public static Action<DeviceManager> MakeDelegate (int index, SamplerState state) {
            return (dm) => { dm.Device.SamplerStates[index] = state; };
        }

        public static Action<DeviceManager> MakeDelegate (
            BlendState blendState = null,
            DepthStencilState depthStencilState = null, 
            RasterizerState rasterizerState = null,
            SamplerState samplerState1 = null,
            SamplerState samplerState2 = null,
            SamplerState samplerState3 = null
        ) {
            var mss = new MaterialStateSet {
                Pipeline = new PipelineStateSet {
                    RasterizerState = rasterizerState,
                    DepthStencilState = depthStencilState,
                    BlendState = blendState,
                },
                Samplers = new SamplerStateSet {
                    FragmentStates = new DenseList<SamplerState> { samplerState1, samplerState2, samplerState3 },
                },
            };
            return mss.Apply;
        }

        public static Material SetStates (
            this Material inner, 
            BlendState blendState = null,
            DepthStencilState depthStencilState = null, 
            RasterizerState rasterizerState = null, 
            SamplerState samplerState1 = null,
            SamplerState samplerState2 = null,
            SamplerState samplerState3 = null
        ) {
            var mss = new MaterialStateSet();
            var oldMss = inner.StateSet;

            var numBeginHandlers = (inner.BeginHandlers != null) ? inner.BeginHandlers.Length + 1 : 1;
            var handlers = new List<Action<DeviceManager>>(numBeginHandlers);
            if (inner.BeginHandlers != null)
            foreach (var bh in inner.BeginHandlers) {
                var bhs = bh.Target as MaterialStateSet;

                if (bhs != null) {
                    mss.Pipeline.Merge(ref bhs.Pipeline);
                    mss.Samplers.Merge(ref bhs.Samplers);
                } else {
                    handlers.Add(bh);
                }
            }

            mss.Pipeline.BlendState = blendState ?? mss.BlendState;
            mss.Pipeline.DepthStencilState = depthStencilState ?? mss.DepthStencilState;
            mss.Pipeline.RasterizerState = rasterizerState ?? mss.RasterizerState;
            mss.Samplers[false, 0] = samplerState1 ?? mss.SamplerState1;
            mss.Samplers[false, 1] = samplerState2 ?? mss.SamplerState2;
            mss.Samplers[false, 2] = samplerState3 ?? mss.SamplerState3;
            if (mss.Equals(oldMss))
                return inner;

            var result = new Material(
                inner, handlers.ToArray(), inner.EndHandlers
            ) {
                StateSet = mss,
            };
            return result;
        }
    }

    [Flags]
    public enum ImperativeRendererFlags : int { 
        WorldSpace                  = 0b1,
        UseZBuffer                  = 0b10,
        ZBufferOnlySorting          = 0b100,
        DepthPrePass                = 0b1000,
        AutoIncrementLayer          = 0b10000,
        AutoIncrementSortKey        = 0b100000,
        LowPriorityMaterialOrdering = 0b1000000,
        UseDiscard                  = 0b10000000,
        RasterSoftOutlines          = 0b100000000,
        RasterUseUbershader         = 0b1000000000,
        RasterBlendInLinearSpace    = 0b10000000000,
        RasterBlendInOkLabSpace     = 0b100000000000,
        DisableDithering            = 0b1000000000000,
    }

    internal static class ImperativeRendererUtil {
        public static readonly UnorderedList<MatrixBox> MatrixBoxes = new UnorderedList<MatrixBox>();
        public static ViewTransformModifier ChangeMatrixModifier = _ChangeMatrixModifier;

        private static void _ChangeMatrixModifier (ref ViewTransform vt, object userData) {
            var mb = (MatrixBox)userData;
            if (mb.Replace)
                vt.ModelView = mb.Matrix;
            else
                Matrix.Multiply(ref vt.ModelView, ref mb.Matrix, out vt.ModelView);

            lock (MatrixBoxes)
                if (MatrixBoxes.Count < 256)
                    MatrixBoxes.Add(mb);
        }
    }

    internal class MatrixBox {
        public Matrix Matrix;
        public bool Replace;
    }
}

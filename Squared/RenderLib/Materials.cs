using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Threading;
using Microsoft.Xna.Framework.Content;
using System.Reflection;
using Squared.Render.Convenience;
using Squared.Util;
using Squared.Render.Evil;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime;

namespace Squared.Render {
    public sealed class Material : IDisposable {
        public static bool LogPreloadTime = false;

        public sealed class PipelineHint {
            public static readonly PipelineHint Default = new PipelineHint {
                HasIndices = true,
                VertexFormats = new[] { typeof(CornerVertex) }
            };

            public bool HasIndices = true;
            public Type[] VertexFormats;
            public SurfaceFormat[] VertexTextureFormats;
        }

        internal readonly UniformBindingTable UniformBindings = new UniformBindingTable();

        public static readonly Material Null = new Material(null);

        // We have to retain this to prevent finalization
        private readonly Effect BaseEffect;

        public readonly Effect Effect;
        public          bool   OwnsEffect;

        public readonly Thread OwningThread;

        /// <summary>
        /// Default parameter values that will be applied each time you apply this material
        /// </summary>
        public MaterialParameterValues DefaultParameters;
        /// <summary>
        /// The default parameters from this other material will be applied first
        /// </summary>
        public Material InheritDefaultParametersFrom;

        public readonly MaterialEffectParameters Parameters;

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public PipelineHint HintPipeline = null;
        public Material DelegatedHintPipeline = null;

        private static int _NextMaterialID;
        public readonly int MaterialID;

        internal uint ActiveViewTransformId;
        internal int RenderTargetChangeIndex;

        internal List<EffectParameter> TextureParameters = new List<EffectParameter>();

        public string Name;

#if DEBUG
        public bool CaptureStackTraces = false;
        public StackTrace AllocationStackTrace { get; private set; }
#endif

        private bool _IsDisposed;

        private Material () {
            MaterialID = Interlocked.Increment(ref _NextMaterialID);
            
            _IsDisposed = false;
#if DEBUG
            if (CaptureStackTraces)
                AllocationStackTrace = new StackTrace(true);
#endif
        }

        public Material (
            Effect effect, string techniqueName = null, 
            Action<DeviceManager>[] beginHandlers = null,
            Action<DeviceManager>[] endHandlers = null,
            bool requiresClone = true
        ) : this() {
            if (techniqueName != null) {
                if (requiresClone) {
                    BaseEffect = effect;
                    Effect = effect.Clone();
                } else
                    Effect = effect;

                if (Effect.GraphicsDevice == null)
                    throw new NullReferenceException("Effect has no device");
                var technique = Effect.Techniques[techniqueName];
                
                if (technique != null)
                    Effect.CurrentTechnique = technique;
                else
                    throw new ArgumentException("No technique named " + techniqueName, "techniqueName");
            } else {
                Effect = effect;
            }

            Name = Effect?.CurrentTechnique?.Name;

            OwningThread = Thread.CurrentThread;

            BeginHandlers = beginHandlers;
            EndHandlers   = endHandlers;

            // FIXME: This should probably never be null.
            if (Effect != null) {
                Parameters = new MaterialEffectParameters(Effect);
                foreach (var p in Effect.Parameters) {
                    if (p.ParameterType == EffectParameterType.Texture2D)
                        TextureParameters.Add(p);
                }
            }
        }

        public Material WrapWithHandlers (
            Action<DeviceManager>[] additionalBeginHandlers = null,
            Action<DeviceManager>[] additionalEndHandlers = null
        ) {
            var newBeginHandlers = BeginHandlers;
            var newEndHandlers = EndHandlers;

            if (newBeginHandlers == null)
                newBeginHandlers = additionalBeginHandlers;
            else if (additionalBeginHandlers != null)
                newBeginHandlers = Enumerable.Concat(BeginHandlers, additionalBeginHandlers).ToArray();

            if (newEndHandlers == null)
                newEndHandlers = additionalEndHandlers;
            else if (additionalEndHandlers != null)
                newEndHandlers = Enumerable.Concat(additionalEndHandlers, EndHandlers).ToArray();

            var result = new Material(
                Effect, null,
                newBeginHandlers, newEndHandlers
            ) {
                DelegatedHintPipeline = this,
                InheritDefaultParametersFrom = this
            };
            return result;
        }

        public Material Clone () {
            var newEffect = Effect.Clone();
            if (newEffect.GraphicsDevice == null)
                throw new NullReferenceException("Effect has no device");
            newEffect.CurrentTechnique = newEffect.Techniques[Effect.CurrentTechnique.Name];

            var result = new Material(
                newEffect, null,
                BeginHandlers, EndHandlers
            ) {
                HintPipeline = HintPipeline,
                OwnsEffect = true,
            };
            DefaultParameters.CopyTo(ref result.DefaultParameters);
            return result;
        }

        private void ValidateParameters () {
            bool foundErrors = false;

            foreach (var ep in TextureParameters) {
                if (ep.GetValueTexture2D()?.IsDisposed != true)
                    continue;

                ep.SetValue((Texture2D)null);
                foundErrors = true;
            }

            if (foundErrors)
                Debug.WriteLine($"WARNING: A disposed texture was set on effect '{Effect.CurrentTechnique?.Name ?? Effect.Name}'.");
        }

        private void CheckDevice (DeviceManager deviceManager) {
            if (Effect == null)
                return;

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();
        }

        internal void Begin (DeviceManager deviceManager) {
            CheckDevice(deviceManager);
            Flush(deviceManager);

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);
        }

        internal void Begin (DeviceManager deviceManager, ref MaterialParameterValues parameters) {
            CheckDevice(deviceManager);
            Flush(deviceManager, ref parameters);

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);
        }

        private void Flush_Epilogue (DeviceManager deviceManager) {
            if (Effect != null) {
                UniformBinding.FlushEffect(Effect);

                var currentTechnique = Effect.CurrentTechnique;
                currentTechnique.Passes[0].Apply();
            }

            if (deviceManager.ActiveViewTransform != null)
                deviceManager.ActiveViewTransform.ActiveMaterial = this;
        }

        public void Flush (DeviceManager deviceManager) {
            deviceManager.ActiveViewTransform?.AutoApply(this);

            InheritDefaultParametersFrom?.DefaultParameters.Apply(this);
            DefaultParameters.Apply(this);

            ValidateParameters();

            Flush_Epilogue(deviceManager);
        }

        public void Flush (DeviceManager deviceManager, ref MaterialParameterValues parameters) {
            deviceManager.ActiveViewTransform?.AutoApply(this);

            // FIXME: Avoid double-set for cases where there is a default + override, since it's wasteful
            InheritDefaultParametersFrom?.DefaultParameters.Apply(this);
            DefaultParameters.Apply(this);
            parameters.Apply(this);

            ValidateParameters();

            Flush_Epilogue(deviceManager);
        }

        public void End (DeviceManager deviceManager) {
            CheckDevice(deviceManager);

            if (EndHandlers != null)
                foreach (var handler in EndHandlers)
                    handler(deviceManager);
        }

        public void Dispose () {
            if (_IsDisposed)
                return;

            if (OwnsEffect)
                Effect.Dispose();

            _IsDisposed = true;
        }

        public bool IsDisposed {
            get {
                return _IsDisposed;
            }
        }

        public override string ToString () {
            if (Effect == null) {
                return "NullEffect #" + MaterialID;
            } else {
                return string.Format(
                    "{3} #{0} ({1}: {2})", 
                    MaterialID, Effect.Name, Name ?? Effect.CurrentTechnique?.Name, 
                    ((BeginHandlers == null) && (EndHandlers == null))
                        ? "EffectMaterial"
                        : "DelegateEffectMaterial"
                );
            }
        }

        private PipelineHint GetPreloadHint () {
            if (Effect == null)
                return null;

            PipelineHint hint;
            if (DelegatedHintPipeline != null)
                hint = DelegatedHintPipeline.HintPipeline ?? HintPipeline;
            else
                hint = HintPipeline;

            return hint;
        }

        public bool Preload (RenderCoordinator coordinator, DeviceManager deviceManager, IndexBuffer tempIb) {
            var hint = GetPreloadHint();
            if (hint == null)
                return false;

            var bindings = new VertexBufferBinding[hint.VertexFormats.Length];
            var sw = Stopwatch.StartNew();

            for (int i = 0; i < bindings.Length; i++) {
                VertexBuffer tempVb;
                lock (coordinator.CreateResourceLock)
                    tempVb = new VertexBuffer(deviceManager.Device, hint.VertexFormats[i], hint.HasIndices ? 4 : 6, BufferUsage.WriteOnly);
                bindings[i] = new VertexBufferBinding(tempVb, 0, (i == 0) ? 0 : 1);
                coordinator.DisposeResource(tempVb);
            }
            if ((hint.VertexTextureFormats?.Length ?? 0) >= 1) {
                for (int i = 0; i < 4; i++) {
                    var vtf = i < hint.VertexTextureFormats.Length 
                        ? hint.VertexTextureFormats[i] 
                        : (SurfaceFormat)9999;

                    Texture2D tempTexture = null;
                    if (((int)vtf) < 32) {
                        lock (coordinator.CreateResourceLock) {
                            tempTexture = new Texture2D(deviceManager.Device, 1, 1, false, vtf);
                            coordinator.RegisterAutoAllocatedTextureResource(tempTexture);
                        }
                        deviceManager.Device.VertexSamplerStates[i] = SamplerState.PointClamp;
                    } else {
                        deviceManager.Device.VertexSamplerStates[i] = RenderManager.ResetSamplerState;
                    }

                    deviceManager.Device.VertexTextures[i] = tempTexture;
                    coordinator.DisposeResource(tempTexture);
                }
            } else {
                for (int i = 0; i < 4; i++) {
                    deviceManager.Device.VertexTextures[i] = null;
                    deviceManager.Device.VertexSamplerStates[i] = RenderManager.ResetSamplerState;
                }
            }

            deviceManager.Device.BlendState = RenderManager.ResetBlendState;

            lock (coordinator.UseResourceLock) {
                deviceManager.Device.Indices = hint.HasIndices ? tempIb : null;
                deviceManager.Device.SetVertexBuffers(bindings);
            }

            // FIXME: This currently generates a bunch of recompile warnings but might actually fix stalls?

            Begin(deviceManager);

            // FIXME: If we skip drawing we still spend like ~100ms precompiling shaders, but it doesn't seem to help

            if (hint.VertexFormats.Length > 1)
                deviceManager.Device.DrawInstancedPrimitives(
                    PrimitiveType.TriangleList, 0, 0, 4, 0, 2, 1
                );
            else if (hint.HasIndices)
                deviceManager.Device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, 4, 0, 2);
            else
                deviceManager.Device.DrawPrimitives(PrimitiveType.TriangleList, 0, 2);

            if ((sw.ElapsedMilliseconds > 10) || LogPreloadTime)
                Debug.WriteLine($"Preloading shader {Effect.CurrentTechnique.Name} took {sw.ElapsedMilliseconds}ms");

            return true;
        }

        public bool PreloadAsync (RenderCoordinator coordinator, DeviceManager dm, IndexBuffer tempIb, Action onComplete) {
            var hint = GetPreloadHint();
            if (hint == null)
                return false;

            coordinator.BeforeIssue(() => {
                lock (coordinator.UseResourceLock)
                    Preload(coordinator, dm, tempIb);
                if (onComplete != null)
                    onComplete();
            });

            return true;
        }
    }
    
    public class MaterialEffectParameters {
        internal readonly Effect Effect;
        internal readonly Dictionary<string, EffectParameter> Cache = 
            // Higher capacity for faster lookup
            new Dictionary<string, EffectParameter>(512, StringComparer.Ordinal);

        public readonly EffectParameter ScaleAndPosition, InputAndOutputZRanges;
        public readonly EffectParameter ProjectionMatrix, ModelViewMatrix;
        public readonly EffectParameter InverseProjection, InverseModelView;
        public readonly EffectParameter BitmapTextureSize, BitmapTexelSize;
        public readonly EffectParameter BitmapTextureSize2, BitmapTexelSize2;
        public readonly EffectParameter BitmapTraits, BitmapTraits2;
        public readonly EffectParameter ShadowColor, ShadowOffset, ShadowMipBias, ShadowedTopMipBias, LightmapUVOffset;
        public readonly EffectParameter Time, FrameIndex, DitherStrength;
        public readonly EffectParameter RenderTargetDimensions;
        public readonly EffectParameter Palette, PaletteSize;

        // Used by DefaultMaterialSet
        internal bool DitheringInitialized;
        internal UniformBinding<DitheringSettings> Dithering;

        internal readonly EffectParameter BitmapTexture, SecondTexture;

        public MaterialEffectParameters (Effect effect) {
            Effect = effect;
            var viewport = this["Viewport"];

            if (viewport != null) {
                ScaleAndPosition = viewport.StructureMembers["ScaleAndPosition"];
                InputAndOutputZRanges = viewport.StructureMembers["InputAndOutputZRanges"];
                ProjectionMatrix = viewport.StructureMembers["Projection"];
                ModelViewMatrix = viewport.StructureMembers["ModelView"];
            }

            BitmapTextureSize = this["BitmapTextureSize"];
            BitmapTexelSize = this["BitmapTexelSize"];
            BitmapTextureSize2 = this["BitmapTextureSize2"];
            BitmapTraits = this["BitmapTraits"];
            BitmapTraits2 = this["BitmapTraits2"];
            BitmapTexelSize2 = this["BitmapTexelSize2"];
            Time = this["Time"];
            FrameIndex = this["FrameIndex"];
            ShadowColor = this["GlobalShadowColor"];
            ShadowOffset = this["ShadowOffset"];
            ShadowMipBias = this["ShadowMipBias"];
            ShadowedTopMipBias = this["ShadowedTopMipBias"];
            LightmapUVOffset = this["LightmapUVOffset"];
            DitherStrength = this["DitherStrength"];
            RenderTargetDimensions = this["__RenderTargetDimensions__"];
            Palette = this["Palette"];
            PaletteSize = this["PaletteSize"];
            BitmapTexture = this["BitmapTexture"];
            SecondTexture = this["SecondTexture"];
            InverseModelView = this["InverseModelView"];
            InverseProjection = this["InverseProjection"];
        }

        public void SetPalette (Texture2D palette) {
            Palette?.SetValue(palette);
            PaletteSize?.SetValue(new Vector2(palette.Width, palette.Height));
        }

        public EffectParameter this[string name] {
            get {
                if (!Cache.TryGetValue(name, out EffectParameter result))
                    // NOTE: We use the passed-in name instead of result.Name because the passed-in one is more likely to be interned
                    Cache[name] = result = Effect.Parameters[name];

                return result;
            }
        }
    }
}
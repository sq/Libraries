﻿using System;
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
using System.Security.Cryptography.X509Certificates;

namespace Squared.Render {
    public enum MaterialHotReloadResult {
        NotConfigured,
        Failed,
        Unchanged,
        Reloaded
    }

    public sealed class Material : IDisposable {
        public static bool LogPreloadTime = false;

        internal enum SynthesizedParameterType {
            None,
            SizeInPixels,
            TexelSize,
            Traits
        }

        internal struct SynthesizedParameter {
            public EffectParameter Source, Target;
            public SynthesizedParameterType Type;
        }

        public sealed class PipelineHint {
            public static readonly PipelineHint Default = new PipelineHint {
                HasIndices = true,
                VertexFormats = new[] { typeof(CornerVertex) }
            };

            public bool HasIndices = true;
            public Type[] VertexFormats;
            public SurfaceFormat[] VertexTextureFormats;
        }

        internal int HotReloadVersion;
        internal readonly UniformBindingTable UniformBindings = new UniformBindingTable();

        public static readonly Material Null = new Material(null);

        internal          Material InheritEffectFrom;

        // Now internal to make hot reload safer
        internal          Effect OwnedEffect { get; private set; }
        internal          bool   DisposeEffect;

        public readonly   Thread OwningThread;

        /// <summary>
        /// Default parameter values that will be applied each time you apply this material
        /// </summary>
        public MaterialParameterValues DefaultParameters;
        /// <summary>
        /// The default parameters from this other material will be applied first
        /// </summary>
        public Material InheritDefaultParametersFrom;

        public MaterialEffectParameters Parameters { get; private set; }

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public PipelineHint HintPipeline = null;
        public Material DelegatedHintPipeline = null;

        private static int _NextMaterialID;
        public readonly int MaterialID;

        internal uint ActiveViewTransformId;
        internal int RenderTargetChangeIndex;
        internal bool HotReloadRequiresClone;
        internal MaterialStateSet StateSet;

        private DenseList<Effect> DiscardedEffects;

        internal List<EffectParameter> TextureParameters = new ();
        internal List<SynthesizedParameter> SynthesizedParameters = new ();

        public string Name;

#if DEBUG
        public bool CaptureStackTraces = false;
        public StackTrace AllocationStackTrace { get; private set; }
#endif

        private bool _IsDisposed;

        // If set, HotReload will call this to get a new effect instance
        public Func<Material, bool, Effect> GetEffectForReload;

        public string TechniqueName;

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
            Action<DeviceManager>[] endHandlers = null
        ) : this() {
            TechniqueName = techniqueName ?? effect?.CurrentTechnique?.Name;
            OwnedEffect = effect;
            HotReloadRequiresClone = false;

            OwningThread = Thread.CurrentThread;

            BeginHandlers = beginHandlers;
            EndHandlers   = endHandlers;

            InitializeForEffect(Effect);
        }

        public Material (
            Material inheritFrom, Action<DeviceManager>[] beginHandlers, Action<DeviceManager>[] endHandlers
        ) : this() {
            if (inheritFrom == null)
                throw new NullReferenceException();

            InheritEffectFrom = inheritFrom;
            TechniqueName = inheritFrom.TechniqueName;
            DelegatedHintPipeline = inheritFrom;
            Name = inheritFrom.Name;
            InheritDefaultParametersFrom = inheritFrom;
            OwningThread = Thread.CurrentThread;
            BeginHandlers = beginHandlers;
            EndHandlers = endHandlers;
            InitializeForEffect(Effect);
        }

        private void InitializeForEffect (Effect effect) {
            UniformBindings.Clear();
            Name = TechniqueName;
            TextureParameters.Clear();
            SynthesizedParameters.Clear();
            if (effect == null)
                return;
            if (Parameters == null)
                Parameters = new MaterialEffectParameters(effect);
            else
                Parameters.Initialize(effect);

            foreach (var p in effect.Parameters) {
                if (p.ParameterType == EffectParameterType.Texture2D)
                    TextureParameters.Add(p);

                foreach (var a in p.Annotations) {
                    var type = a.Name.ToLowerInvariant() switch {
                        "sizeinpixelsof" => SynthesizedParameterType.SizeInPixels,
                        "texelsizeof" => SynthesizedParameterType.TexelSize,
                        "traitsof" => SynthesizedParameterType.Traits,
                        _ => SynthesizedParameterType.None,
                    };
                    if (type == SynthesizedParameterType.None)
                        continue;
                    var sourceName = a.GetValueString();
                    var source = effect.Parameters[sourceName];
                    if (source == null)
                        throw new Exception($"Synthesized effect parameter {p.Name}'s source {sourceName} does not exist!");
                    SynthesizedParameters.Add(new SynthesizedParameter {
                        Source = source, Target = p,
                        Type = type
                    });
                }
            }
        }

        internal Effect Effect {
            get {
                if (OwnedEffect != null)
                    return OwnedEffect;
                if (InheritEffectFrom == null)
                    return null;

                if (HotReloadVersion < InheritEffectFrom.HotReloadVersion) {
                    InitializeForEffect(InheritEffectFrom.Effect);
                    HotReloadVersion = InheritEffectFrom.HotReloadVersion;
                }

                return InheritEffectFrom.Effect;
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

            return new Material(this, newBeginHandlers, newEndHandlers);
        }

        public Material Clone () {
            var newEffect = Effect.Clone();
            if (newEffect.GraphicsDevice == null)
                throw new NullReferenceException("Effect has no device");

            var result = new Material(
                newEffect, null,
                BeginHandlers, EndHandlers
            ) {
                TechniqueName = TechniqueName,
                HintPipeline = HintPipeline,
                DisposeEffect = true,
                GetEffectForReload = GetEffectForReload,
                HotReloadRequiresClone = true,
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
                Debug.WriteLine($"WARNING: A disposed texture was set on effect '{Name ?? ToString()}'.");
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

        private void Flush_Prologue (DeviceManager deviceManager) {
            if (Effect == null)
                return;

            if (Effect.IsDisposed)
                throw new ObjectDisposedException("Effect");

            EffectTechnique tech = !string.IsNullOrWhiteSpace(TechniqueName)
                ? Effect.Techniques[TechniqueName]
                : Effect.Techniques[0];
            Effect.CurrentTechnique = tech;
        }

        private void SynthesizeParameters () {
            foreach (var sp in SynthesizedParameters) {
                Vector4 value = default;
                var texture = sp.Source.GetValueTexture2D();
                if (texture != null)
                    value = sp.Type switch {
                        SynthesizedParameterType.SizeInPixels => new Vector4(texture.Width, texture.Height, 0, 0),
                        SynthesizedParameterType.TexelSize => new Vector4(1.0f / texture.Width, 1.0f / texture.Height, 0, 0),
                        SynthesizedParameterType.Traits => Evil.TextureUtils.GetTraits(texture.Format),
                        _ => Vector4.Zero,
                    };
                sp.Target.SetValue(value);
            }
        }

        private void Flush_Epilogue (DeviceManager deviceManager) {
            if (Effect != null) {
                SynthesizeParameters();

                UniformBinding.FlushEffect(Effect);

                Effect.CurrentTechnique.Passes[0].Apply();
            }

            if (deviceManager.ActiveViewTransform != null)
                deviceManager.ActiveViewTransform.ActiveMaterial = this;
        }

        public void Flush (DeviceManager deviceManager) {
            Flush_Prologue(deviceManager);

            deviceManager.ActiveViewTransform?.AutoApply(this);

            InheritDefaultParametersFrom?.DefaultParameters.Apply(this);
            DefaultParameters.Apply(this);

            ValidateParameters();

            Flush_Epilogue(deviceManager);
        }

        public void Flush (DeviceManager deviceManager, ref MaterialParameterValues parameters) {
            Flush_Prologue(deviceManager);

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

            if (DisposeEffect)
                OwnedEffect?.Dispose();

            foreach (var de in DiscardedEffects)
                de.Dispose();
            DiscardedEffects.Clear();

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

        public MaterialHotReloadResult TryHotReload (RenderCoordinator coordinator) {
            if (InheritEffectFrom != null)
                return MaterialHotReloadResult.NotConfigured;

            var gefr = GetEffectForReload ?? InheritDefaultParametersFrom?.GetEffectForReload;
            if (gefr == null)
                return MaterialHotReloadResult.NotConfigured;

            var newEffect = gefr(InheritDefaultParametersFrom ?? this, HotReloadRequiresClone);
            if (newEffect == null)
                return MaterialHotReloadResult.Failed;

            if (newEffect == Effect)
                return MaterialHotReloadResult.Unchanged;

            if (DisposeEffect)
                DiscardedEffects.Add(OwnedEffect);

            OwnedEffect = newEffect;
            InitializeForEffect(newEffect);
            return MaterialHotReloadResult.Reloaded;
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

        public void RecordUsedTextures (Action<Texture2D> markResourceAsInUseDelegate) {
            foreach (var p in TextureParameters) {
                var t = p.GetValueTexture2D();
                if (t == null)
                    continue;
                markResourceAsInUseDelegate(t);
            }
        }
    }
    
    public class MaterialEffectParameters {
        internal Effect Effect;
        internal readonly Dictionary<string, EffectParameter> Cache = 
            // Higher capacity for faster lookup
            new Dictionary<string, EffectParameter>(512, StringComparer.Ordinal);

        public EffectParameter ScaleAndPosition { get; private set; }
        public EffectParameter InputAndOutputZRanges { get; private set; }
        public EffectParameter ProjectionMatrix { get; private set; }
        public EffectParameter ModelViewMatrix { get; private set; }
        public EffectParameter InverseProjection { get; private set; }
        public EffectParameter InverseModelView { get; private set; }
        public EffectParameter ShadowColor { get; private set; }
        public EffectParameter ShadowOffset { get; private set; }
        public EffectParameter ShadowMipBias { get; private set; }
        public EffectParameter ShadowedTopMipBias { get; private set; }
        public EffectParameter LightmapUVOffset { get; private set; }
        public EffectParameter Time { get; private set; }
        public EffectParameter FrameIndex { get; private set; }
        public EffectParameter DitherStrength { get; private set; }
        public EffectParameter RenderTargetInfo { get; private set; }
        public EffectParameter Palette { get; private set; }
        public EffectParameter PaletteSize { get; private set; }

        // Used by DefaultMaterialSet
        internal bool DitheringInitialized;
        internal UniformBinding<DitheringSettings> Dithering;

        internal EffectParameter BitmapTexture;
        internal EffectParameter SecondTexture;

        /// <summary>
        /// Please don't use this
        /// </summary>
        public EffectParameterCollection AllParameters => Effect?.Parameters;

        public MaterialEffectParameters (Effect effect) {
            Initialize(effect);
        }

        // For hot reload
        internal void Initialize (Effect effect) {
            Effect = effect;
            var viewport = this["Viewport"];

            if (viewport != null) {
                ScaleAndPosition = viewport.StructureMembers["ScaleAndPosition"];
                InputAndOutputZRanges = viewport.StructureMembers["InputAndOutputZRanges"];
                ProjectionMatrix = viewport.StructureMembers["Projection"];
                ModelViewMatrix = viewport.StructureMembers["ModelView"];
            }

            Time = this["Time"];
            FrameIndex = this["FrameIndex"];
            ShadowColor = this["GlobalShadowColor"];
            ShadowOffset = this["ShadowOffset"];
            ShadowMipBias = this["ShadowMipBias"];
            ShadowedTopMipBias = this["ShadowedTopMipBias"];
            LightmapUVOffset = this["LightmapUVOffset"];
            DitherStrength = this["DitherStrength"];
            RenderTargetInfo = this["__RenderTargetInfo__"];
            Palette = this["Palette"];
            PaletteSize = this["PaletteSize"];
            BitmapTexture = this["BitmapTexture"];
            SecondTexture = this["SecondTexture"];
            InverseModelView = this["InverseModelView"];
            InverseProjection = this["InverseProjection"];

            Cache.Clear();
        }

        public void SetPalette (Texture2D palette) {
            Palette?.SetValue(palette);
            PaletteSize?.SetValue(new Vector2(palette.Width, palette.Height));
        }

        public bool TryGetParameter (string name, out EffectParameter result) {
            if (Effect == null) {
                result = null;
                return false;
            }

            if (!Cache.TryGetValue(name, out result))
                // NOTE: We use the passed-in name instead of result.Name because the passed-in one is more likely to be interned
                Cache[name] = result = Effect.Parameters[name];

            return result != null;
        }

        public EffectParameter this[string name] {
            get {
                TryGetParameter(name, out var result);
                return result;
            }
        }
    }
}
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

namespace Squared.Render {
    public sealed class Material : IDisposable {
        public class PipelineHint {
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
        public readonly bool   OwnsEffect;

        public readonly Thread OwningThread;

        public readonly DefaultMaterialSetEffectParameters Parameters;

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public PipelineHint HintPipeline = null;
        public Material DelegatedHintPipeline = null;

        private static int _NextMaterialID;
        public readonly int MaterialID;

        internal DefaultMaterialSet.ActiveViewTransformInfo ActiveViewTransform;
        internal uint ActiveViewTransformId;

        public string Name;

        private bool _IsDisposed;

        private Material () {
            MaterialID = Interlocked.Increment(ref _NextMaterialID);
            
            _IsDisposed = false;
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
                    throw new Exception();
                var technique = Effect.Techniques[techniqueName];
                
                if (technique != null)
                    Effect.CurrentTechnique = technique;
                else {
                    throw new ArgumentException("No technique named " + techniqueName, "techniqueName");
                }
            } else {
                Effect = effect;
            }

            Name = Effect?.CurrentTechnique?.Name;

            OwningThread = Thread.CurrentThread;

            // FIXME: This should probably never be null.
            if (Effect != null) {
                Parameters = new DefaultMaterialSetEffectParameters(Effect);
            }

            BeginHandlers = beginHandlers;
            EndHandlers   = endHandlers;
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
            );
            result.DelegatedHintPipeline = this;
            return result;
        }

        public Material Clone () {
            var newEffect = Effect.Clone();
            if (newEffect.GraphicsDevice == null)
                throw new Exception();
            newEffect.CurrentTechnique = newEffect.Techniques[Effect.CurrentTechnique.Name];

            var result = new Material(
                newEffect, null,
                BeginHandlers, EndHandlers
            ) { HintPipeline = HintPipeline };
            return result;
        }

        internal bool AutoApplyCurrentViewTransform () {
            if (ActiveViewTransform == null)
                return false;
            return ActiveViewTransform.AutoApply(this);
        }

        private void CheckDevice (DeviceManager deviceManager) {
            if (Effect == null)
                return;

            if (Effect.GraphicsDevice != deviceManager.Device)
                throw new InvalidOperationException();
        }

        // For debugging
        private void Begin_Internal (string shaderName, DeviceManager deviceManager) {
            CheckDevice(deviceManager);
            // FIXME: This never runs because ActiveViewTransform is always null
            if (ActiveViewTransform != null)
                ActiveViewTransform.ActiveMaterial = this;

            Flush();

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);

            if (AutoApplyCurrentViewTransform())
                Flush(false);
        }

        public void Begin (DeviceManager deviceManager) {
            Begin_Internal(Effect != null ? Effect.CurrentTechnique.Name : null, deviceManager);
        }

        // For debugging
        private void Flush_Internal (string shaderName, bool autoApplyViewTransform) {
            if (autoApplyViewTransform)
                AutoApplyCurrentViewTransform();

            if (Effect != null) {
                UniformBinding.FlushEffect(Effect);

                var currentTechnique = Effect.CurrentTechnique;
                currentTechnique.Passes[0].Apply();
            }
        }

        public void Flush (bool autoApplyViewTransform = true) {
            Flush_Internal(Effect != null ? Effect.CurrentTechnique.Name : null, autoApplyViewTransform);
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

        public void Preload (RenderCoordinator coordinator, DeviceManager deviceManager, IndexBuffer tempIb) {
            if (Effect == null)
                return;

            PipelineHint hint;
            if (DelegatedHintPipeline != null)
                hint = DelegatedHintPipeline.HintPipeline ?? HintPipeline;
            else
                hint = HintPipeline;

            if (hint == null)
                return;

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
                for (int i = 0; i < hint.VertexTextureFormats.Length; i++) {
                    Texture2D tempTexture;
                    lock (coordinator.CreateResourceLock)
                        tempTexture = new Texture2D(deviceManager.Device, 1, 1, false, hint.VertexTextureFormats[i]);
                    deviceManager.Device.VertexTextures[i] = tempTexture;
                    deviceManager.Device.VertexSamplerStates[i] = SamplerState.PointClamp;
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

            if (sw.ElapsedMilliseconds > 10)
                Debug.WriteLine($"Preloading shader {Effect.CurrentTechnique.Name} took {sw.ElapsedMilliseconds}ms");
        }
    }
}
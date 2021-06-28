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
        public          bool   OwnsEffect;

        public readonly Thread OwningThread;

        /// <summary>
        /// Default parameter values that will be applied each time you apply this material
        /// </summary>
        public MaterialParameterValues DefaultParameters;

        public readonly MaterialEffectParameters Parameters;

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public PipelineHint HintPipeline = null;
        public Material DelegatedHintPipeline = null;

        private static int _NextMaterialID;
        public readonly int MaterialID;

        internal uint ActiveViewTransformId;

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
                    throw new Exception();
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

            // FIXME: This should probably never be null.
            if (Effect != null) {
                Parameters = new MaterialEffectParameters(Effect);
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
            ) {
                DelegatedHintPipeline = this,
                DefaultParameters = DefaultParameters
            };
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
            ) {
                HintPipeline = HintPipeline,
                OwnsEffect = true,
                DefaultParameters = DefaultParameters
            };
            return result;
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
            Flush(deviceManager);

            if (BeginHandlers != null)
                foreach (var handler in BeginHandlers)
                    handler(deviceManager);
        }

        public void Begin (DeviceManager deviceManager) {
            Begin_Internal(Effect != null ? Effect.CurrentTechnique.Name : null, deviceManager);
        }

        // For debugging
        private void Flush_Internal (DeviceManager deviceManager, string shaderName) {
            deviceManager.ActiveViewTransform?.AutoApply(this);

            if (Effect != null) {
                UniformBinding.FlushEffect(Effect);

                var currentTechnique = Effect.CurrentTechnique;
                currentTechnique.Passes[0].Apply();

                if (deviceManager.ActiveViewTransform != null)
                    deviceManager.ActiveViewTransform.ActiveMaterial = this;
            }
        }

        public void Flush (DeviceManager deviceManager) {
            Flush_Internal(
                deviceManager, Effect != null ? Effect.CurrentTechnique.Name : null
            );
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
                    lock (coordinator.CreateResourceLock) {
                        tempTexture = new Texture2D(deviceManager.Device, 1, 1, false, hint.VertexTextureFormats[i]);
                        coordinator.AutoAllocatedTextureResources.Add(tempTexture);
                    }
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
    
    public class MaterialEffectParameters {
        internal readonly Effect Effect;
        internal readonly Dictionary<string, EffectParameter> Cache = 
            new Dictionary<string, EffectParameter>(StringComparer.Ordinal);

        public readonly EffectParameter ScaleAndPosition, InputAndOutputZRanges;
        public readonly EffectParameter ProjectionMatrix, ModelViewMatrix;
        public readonly EffectParameter BitmapTextureSize, HalfTexel;
        public readonly EffectParameter BitmapTextureSize2, HalfTexel2;
        public readonly EffectParameter ShadowColor, ShadowOffset, ShadowMipBias, ShadowedTopMipBias, LightmapUVOffset;
        public readonly EffectParameter Time, FrameIndex, DitherStrength;
        public readonly EffectParameter HalfPixelOffset;
        public readonly EffectParameter RenderTargetDimensions;
        public readonly EffectParameter Palette, PaletteSize;

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
            HalfTexel = this["HalfTexel"];
            BitmapTextureSize2 = this["BitmapTextureSize2"];
            HalfTexel2 = this["HalfTexel2"];
            Time = this["Time"];
            FrameIndex = this["FrameIndex"];
            ShadowColor = this["GlobalShadowColor"];
            ShadowOffset = this["ShadowOffset"];
            ShadowMipBias = this["ShadowMipBias"];
            ShadowedTopMipBias = this["ShadowedTopMipBias"];
            LightmapUVOffset = this["LightmapUVOffset"];
            DitherStrength = this["DitherStrength"];
            HalfPixelOffset = this["HalfPixelOffset"];
            RenderTargetDimensions = this["__RenderTargetDimensions__"];
            Palette = this["Palette"];
            PaletteSize = this["PaletteSize"];
        }

        public void SetPalette (Texture2D palette) {
            Palette?.SetValue(palette);
            PaletteSize?.SetValue(new Vector2(palette.Width, palette.Height));
        }

        public EffectParameter this[string name] {
            get {
                if (!Cache.TryGetValue(name, out EffectParameter result))
                    Cache[name] = result = Effect.Parameters[name];
                return result;
            }
        }
    }

    public struct MaterialParameterValues {
        private enum EntryValueType {
            Tex2D,
            Array,
            B,
            F,
            I,
            V2,
            V3,
            V4,
            M
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct EntryUnion {
            [FieldOffset(0)]
            public bool B;
            [FieldOffset(0)]
            public float F;
            [FieldOffset(0)]
            public int I;
            [FieldOffset(0)]
            public Vector2 V2;
            [FieldOffset(0)]
            public Vector3 V3;
            [FieldOffset(0)]
            public Vector4 V4;
            [FieldOffset(0)]
            public Matrix M;
        }

        private struct Entry {
            public string Name;
            public EntryValueType ValueType;
            public object ReferenceValue;
            public EntryUnion PrimitiveValue;
        }

        private DenseList<Entry> Entries;

        private int Find (string name) {
            for (int i = 0, c = Entries.Count; i < c; i++)
                if (Entries[i].Name == name)
                    return i;

            return -1;
        }

        public void Clear (string name) {
            var index = Find(name);
            if (index < 0)
                return;
            Entries.RemoveAt(index);
        }

        public void Set (string name, int value) {
            Clear(name);
            Entries.Add(new Entry {
                Name = name,
                ValueType = EntryValueType.I,
                PrimitiveValue = {
                    I = value
                }
            });
        }

        public void Set (string name, bool value) {
            Clear(name);
            Entries.Add(new Entry {
                Name = name,
                ValueType = EntryValueType.B,
                PrimitiveValue = {
                    B = value
                }
            });
        }

        public void Set (string name, float value) {
            Clear(name);
            Entries.Add(new Entry {
                Name = name,
                ValueType = EntryValueType.F,
                PrimitiveValue = {
                    F = value
                }
            });
        }

        public void Set (string name, Vector2 value) {
            Clear(name);
            Entries.Add(new Entry {
                Name = name,
                ValueType = EntryValueType.V2,
                PrimitiveValue = {
                    V2 = value
                }
            });
        }

        public void Set (string name, Vector3 value) {
            Clear(name);
            Entries.Add(new Entry {
                Name = name,
                ValueType = EntryValueType.V3,
                PrimitiveValue = {
                    V3 = value
                }
            });
        }

        public void Set (string name, Vector4 value) {
            Clear(name);
            Entries.Add(new Entry {
                Name = name,
                ValueType = EntryValueType.V4,
                PrimitiveValue = {
                    V4 = value
                }
            });
        }
    }
}
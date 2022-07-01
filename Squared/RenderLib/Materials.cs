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

        public readonly MaterialEffectParameters Parameters;

        public readonly Action<DeviceManager>[] BeginHandlers;
        public readonly Action<DeviceManager>[] EndHandlers;

        public PipelineHint HintPipeline = null;
        public Material DelegatedHintPipeline = null;

        private static int _NextMaterialID;
        public readonly int MaterialID;

        internal uint ActiveViewTransformId;

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

            // FIXME: This should probably never be null.
            if (Effect != null) {
                Parameters = new MaterialEffectParameters(Effect);
                foreach (var p in Effect.Parameters) {
                    if (p.ParameterType == EffectParameterType.Texture2D)
                        TextureParameters.Add(p);
                }
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
                throw new NullReferenceException("Effect has no device");
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

            DefaultParameters.Apply(this);

            ValidateParameters();

            Flush_Epilogue(deviceManager);
        }

        public void Flush (DeviceManager deviceManager, ref MaterialParameterValues parameters) {
            deviceManager.ActiveViewTransform?.AutoApply(this);

            // FIXME: Avoid double-set for cases where there is a default + override, since it's wasteful
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
        public readonly EffectParameter BitmapTraits, BitmapTraits2;
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
            BitmapTraits = this["BitmapTraits"];
            BitmapTraits2 = this["BitmapTraits2"];
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

    public struct MaterialParameterValues : IEnumerable<KeyValuePair<string, object>> {
        internal enum EntryValueType {
            None,
            Texture,
            Array,
            B,
            F,
            I,
            V2,
            V3,
            V4,
            Q
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct EntryUnion {
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
            public Quaternion Q;
        }

        internal struct Entry {
            public string Name;
            public EntryValueType ValueType;
            public object ReferenceValue;
            public EntryUnion PrimitiveValue;

            public static bool Equals (in Entry lhs, in Entry rhs) {
                if (lhs.ValueType != rhs.ValueType)
                    return false;

                if (lhs.ReferenceValue != rhs.ReferenceValue)
                    return false;

                switch (lhs.ValueType) {
                    case EntryValueType.B:
                        return lhs.PrimitiveValue.B == rhs.PrimitiveValue.B;
                    case EntryValueType.F:
                        return lhs.PrimitiveValue.F == rhs.PrimitiveValue.F;
                    case EntryValueType.I:
                        return lhs.PrimitiveValue.I == rhs.PrimitiveValue.I;
                    case EntryValueType.V2:
                        return lhs.PrimitiveValue.V2 == rhs.PrimitiveValue.V2;
                    case EntryValueType.V3:
                        return lhs.PrimitiveValue.V3 == rhs.PrimitiveValue.V3;
                    case EntryValueType.V4:
                        return lhs.PrimitiveValue.V4 == rhs.PrimitiveValue.V4;
                    case EntryValueType.Q:
                        return lhs.PrimitiveValue.Q == rhs.PrimitiveValue.Q;
                    default:
                        throw new ArgumentOutOfRangeException("lhs.ValueType");
                }
            }
        }

        private bool IsCleared;
        private DenseList<Entry> Entries;
        
        public int Count {
            [TargetedPatchingOptOut("")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return IsCleared ? 0 : Entries.Count;
            }
        }

        private int Find (string name) {
            if (IsCleared)
                return -1;

            for (int i = 0, c = Count; i < c; i++)
                if (Entries[i].Name == name)
                    return i;

            return -1;
        }

        public void Clear () {
            if (Entries.Count > 0)
                IsCleared = true;
            // Entries.Clear();
        }

        public void Clear (string name) {
            var index = Find(name);
            if (index < 0)
                return;
            Entries.RemoveAt(index);
        }

        public void AddRange (in MaterialParameterValues rhs) {
            for (int i = 0, c = rhs.Count; i < c; i++) {
                ref readonly var entry = ref rhs.Entries.ReadItem(i);
                Set(in entry);
            }
        }

        internal bool TryGet (string name, out Entry result) {
            var index = Find(name);
            if (index < 0) {
                result = default(Entry);
                return false;
            }
            Entries.GetItem(index, out result);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AutoClear () {
            if (!IsCleared)
                return;
            Entries.Clear();
            IsCleared = false;
        }

        private void Set (in Entry entry) {
            AutoClear();
            var index = Find(entry.Name);
            if (index < 0)
                Entries.Add(entry);
            else
                Entries[index] = entry;
        }

        public void Add (string name, int value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.I,
                PrimitiveValue = {
                    I = value
                }
            });
        }

        public void Add (string name, Color value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.V4,
                PrimitiveValue = {
                    V4 = value.ToVector4()
                }
            });
        }

        public void Add (string name, bool value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.B,
                PrimitiveValue = {
                    B = value
                }
            });
        }

        public void Add (string name, float value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.F,
                PrimitiveValue = {
                    F = value
                }
            });
        }

        public void Add (string name, Vector2 value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.V2,
                PrimitiveValue = {
                    V2 = value
                }
            });
        }

        public void Add (string name, Vector3 value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.V3,
                PrimitiveValue = {
                    V3 = value
                }
            });
        }

        public void Add (string name, Vector4 value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.V4,
                PrimitiveValue = {
                    V4 = value
                }
            });
        }

        public void Add (string name, Quaternion value) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.Q,
                PrimitiveValue = {
                    Q = value
                }
            });
        }

        public void Add (string name, Texture texture) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.Texture,
                ReferenceValue = texture
            });
        }

        public void Add (string name, Array array) {
            Set(new Entry {
                Name = name,
                ValueType = EntryValueType.Array,
                ReferenceValue = array
            });
        }

        public void Apply (Material material) {
            if (material.Effect == null)
                return;
            Apply(material.Effect, material.Parameters);
        }

        private void Apply (Effect effect, MaterialEffectParameters cache) {
            for (int i = 0, c = Count; i < c; i++) {
                ref var entry = ref Entries.Item(i);
                var p = cache[entry.Name];
                if (p == null)
                    continue;
                ApplyEntry(entry, p);
            }
        }

        private static void ApplyEntry (in Entry entry, EffectParameter p) {
            var r = entry.ReferenceValue;
            switch (entry.ValueType) {
                case EntryValueType.Texture:
                    p.SetValue((Texture)r);
                    break;
                case EntryValueType.Array:
                    if (r is float[] fa)
                        p.SetValue(fa);
                    else if (r is int[] ia)
                        p.SetValue(ia);
                    else if (r is bool[] ba)
                        p.SetValue(ba);
                    else if (r is Matrix[] ma)
                        p.SetValue(ma);
                    else if (r is Vector2[] v2a)
                        p.SetValue(v2a);
                    else if (r is Vector3[] v3a)
                        p.SetValue(v3a);
                    else if (r is Vector4[] v4a)
                        p.SetValue(v4a);
                    else if (r is Quaternion[] qa)
                        p.SetValue(qa);
                    else
                        throw new ArgumentException("Unsupported array parameter type");
                    break;
                case EntryValueType.B:
                    p.SetValue(entry.PrimitiveValue.B);
                    break;
                case EntryValueType.F:
                    p.SetValue(entry.PrimitiveValue.F);
                    break;
                case EntryValueType.I:
                    p.SetValue(entry.PrimitiveValue.I);
                    break;
                case EntryValueType.V2:
                    p.SetValue(entry.PrimitiveValue.V2);
                    break;
                case EntryValueType.V3:
                    p.SetValue(entry.PrimitiveValue.V3);
                    break;
                case EntryValueType.V4:
                    p.SetValue(entry.PrimitiveValue.V4);
                    break;
                case EntryValueType.Q:
                    p.SetValue(entry.PrimitiveValue.Q);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("entry.ValueType");
            }
        }

        public bool Equals (ref MaterialParameterValues pRhs) {
            var count = Count;
            if (count != pRhs.Count)
                return false;
            if (count == 0)
                return true;

            for (int i = 0; i < count; i++) {
                ref var lhs = ref Entries.Item(i);
                var j = pRhs.Find(lhs.Name);
                if (j < 0)
                    return false;
                pRhs.Entries.TryGetItem(j, out Entry rhs);
                if (!Entry.Equals(in lhs, in rhs))
                    return false;
            }

            return true;
        }

        public override int GetHashCode () {
            return Count;
        }

        public override bool Equals (object obj) {
            if (obj is MaterialParameterValues mpv)
                return Equals(ref mpv);
            else
                return false;
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator () {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            throw new NotImplementedException();
        }
    }
}
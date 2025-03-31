using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Util;

namespace Squared.Render {
    public interface IUniformBinding : IDisposable {
        Type   Type    { get; }
        string Name    { get; }
        Effect Effect  { get; }
        bool   IsDirty { get; }

        void Flush();
        void HandleDeviceReset();
    }

    public static class UniformBindingExtensions {
        public static UniformBinding<T> Cast<T> (this IUniformBinding iub)
            where T : unmanaged
        {
            return (UniformBinding<T>)iub;
        }
    }

    public unsafe partial class UniformBinding<T> : IUniformBinding 
        where T : unmanaged
    {
        public static void ValidateFieldType (string name, Type type) {
            if (type == typeof(Matrix))
                return;
            else if (type == typeof(Vector4))
                return;

            // FIXME: If we figure out how to match mojoshader layout this can be removed
            throw new InvalidUniformMemberException(name, type);
        }

        private Layout NativeLayout;
        private static Type tMatrix = typeof(Matrix);

        unsafe void NativeFlush () {
            if (!IsDirty)
                return;

            // This means the parameter was killed by the shader compiler or the struct has no members
            if (!NativeLayout.IsValid)
                return;

            fixed (T* pScratchT = &_Value) {
                var pScratch = (byte*)pScratchT;
                foreach (var m in NativeLayout.Members) {
                    var srcPtr = (pScratch + m.ManagedOffset);
                    var destPtr = m.NativePointer.ToPointer();

                    Buffer.MemoryCopy(srcPtr, destPtr, m.NativeSize, m.ManagedSize);

                    // Convert from HLSL row/column order to GLSL (funny that D3DX9 uses this order too...)
                    if (m.FieldType == tMatrix)
                        InPlaceTranspose((float*)destPtr);
                }
            }
        }

        private readonly object         Lock = new object();

        private T                       _Value;

        private delegate void CompatibilitySetter (in T value);
        private CompatibilitySetter CurrentCompatibilityBinding;

        public  bool   IsDirty    { get; private set; }
        public  bool   IsDisposed { get; private set; }
        public  Effect Effect     { get; private set; }
        public  string Name       { get; private set; }
        public  Type   Type       { get; private set; }

        /// <summary>
        /// Bind a single named uniform of the effect as type T.
        /// </summary>
        private UniformBinding (Effect effect, string uniformName) {
            Type = typeof(T);

            Effect = effect;
            Name = uniformName;

            IsDirty = true;

            UniformBinding.Register(effect, this);

            var parameter = effect.Parameters[uniformName];
            if (parameter == null)
                NativeLayout = default(Layout);
            else
                NativeLayout = new Layout(Type, parameter);
        }

        public static UniformBinding<T> TryCreate (Effect effect, string uniformName) {
            if (effect == null)
                return null;
            if (effect.Parameters[uniformName] == null)
                return null;

            return new UniformBinding<T>(effect, uniformName);
        }

        public ref T Value {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                IsDirty = true;
                return ref _Value;
            }
        }

        private void InPlaceTranspose (float* pMatrix) {
            float temp = pMatrix[4];
            pMatrix[4] = pMatrix[1];
            pMatrix[1] = temp;

            temp = pMatrix[8];
            pMatrix[8] = pMatrix[2];
            pMatrix[2] = temp;

            temp = pMatrix[12];
            pMatrix[12] = pMatrix[3];
            pMatrix[3] = temp;

            temp = pMatrix[9];
            pMatrix[9] = pMatrix[6];
            pMatrix[6] = temp;

            temp = pMatrix[13];
            pMatrix[13] = pMatrix[7];
            pMatrix[7] = temp;

            temp = pMatrix[14];
            pMatrix[14] = pMatrix[11];
            pMatrix[11] = temp;
        }

        // We use LCG to create a single throwaway delegate that is responsible for filling
        //  all the relevant effect parameters with the value(s) of each field of the struct.
        private void CreateCompatibilityBinding (out CompatibilitySetter result) {
            var t = typeof(T);
            var tp = typeof(EffectParameter);

            var uniformParameter = Effect.Parameters[Name];
            if (uniformParameter == null)
                throw new UniformBindingException("Shader has no uniform named '" + Name + "'");
            if (uniformParameter.ParameterClass != EffectParameterClass.Struct)
                throw new UniformBindingException("Shader uniform is not a struct");

            var pValue = Expression.Parameter(t.MakeByRefType(), "value");
            var body = new List<Expression>();

            foreach (var p in uniformParameter.StructureMembers) {
                var field = t.GetField(p.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) {
                    if (UniformBinding.IgnoreMissingUniforms)
                        continue;
                    else
                        throw new UniformBindingException(string.Format("No field named '{0}' found when binding type {1}", p.Name, t.Name));
                }

                var setMethod = tp.GetMethod("SetValue", new[] { field.FieldType });
                if (setMethod == null)
                    throw new UniformBindingException(string.Format("No setter for effect parameter type {0}", field.FieldType.Name));

                var vParameter = Expression.Constant(p, tp);

                var fieldValue = Expression.Field(pValue, field);
                body.Add(Expression.Call(vParameter, setMethod, fieldValue));
            }

            var bodyBlock = Expression.Block(body);

            var expr = Expression.Lambda<CompatibilitySetter>(bodyBlock, pValue);
            result = expr.Compile();
        }

        private void GetCurrentCompatibilityBinding (out CompatibilitySetter compatibilityBinding) {
            lock (Lock) {
                if (CurrentCompatibilityBinding == null) {
                    IsDirty = true;
                    CreateCompatibilityBinding(out CurrentCompatibilityBinding);
                }

                compatibilityBinding = CurrentCompatibilityBinding;
            }
        }

        private void CompatibilityFlush () {
            CompatibilitySetter setter;
            GetCurrentCompatibilityBinding(out setter);
            setter(in _Value);
        }

        public void Flush () {
            if (UniformBinding.ForceCompatibilityMode) {
                CompatibilityFlush();
            } else {
                NativeFlush();
            }

            IsDirty = false;
        }

        private static bool IsStructure (Type type) {
            return type.IsValueType && !type.IsPrimitive;
        }

        public void HandleDeviceReset () {
            lock (Lock) {
                ReleaseBindings();
                IsDirty = true;
            }
        }

        private void ReleaseBindings () {
            // TODO: Should we invalidate the compatibility binding here? I don't think that needs to happen
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            lock (Lock)
                ReleaseBindings();
        }
    }

    public static class UniformBinding {
        // Making a dictionary larger increases performance
        private const int BindingDictionaryCapacity = 1024;

        public static bool ForceCompatibilityMode = false;
        public static bool IgnoreMissingUniforms = true;

        private static readonly Dictionary<Effect, List<IUniformBinding>> BindingsByEffect =
            new Dictionary<Effect, List<IUniformBinding>>(BindingDictionaryCapacity, new ReferenceComparer<Effect>());

        public static void CollectGarbage () {
            var deadEffects = new List<Effect>();

            lock (BindingsByEffect) {
                foreach (var kvp in BindingsByEffect) {
                    if (kvp.Key.IsDisposed || kvp.Key.GraphicsDevice.IsDisposed)
                        deadEffects.Add(kvp.Key);
                }
            }

            lock (BindingsByEffect) {
                foreach (var effect in deadEffects) {
                    var list = BindingsByEffect[effect];
                    foreach (var binding in list)
                        binding.Dispose();
                    BindingsByEffect.Remove(effect);
                }
            }
        }

        public static void HandleDeviceReset () {
            lock (BindingsByEffect)
                foreach (var kvp in BindingsByEffect)
                    foreach (var b in kvp.Value)
                        b.HandleDeviceReset();
        }

        public static void FlushEffect (Effect effect) {
            lock (BindingsByEffect) {
                List<IUniformBinding> bindings;
                if (!BindingsByEffect.TryGetValue(effect, out bindings))
                    return;

                foreach (var binding in bindings)
                    binding.Flush();
            }
        }

        internal static void Register (Effect effect, IUniformBinding binding) {
            List<IUniformBinding> bindings;
            lock (BindingsByEffect) {
                if (!BindingsByEffect.TryGetValue(effect, out bindings))
                    BindingsByEffect[effect] = bindings = new List<IUniformBinding>();

                bindings.Add(binding);
            }
        }
    }

    internal struct UniformBindingKey {
        public sealed class EqualityComparer : IEqualityComparer<UniformBindingKey> {
            public bool Equals (UniformBindingKey x, UniformBindingKey y) {
                return x.Equals(y);
            }

            public int GetHashCode (UniformBindingKey obj) {
                return obj.HashCode;
            }
        }

        public            Effect Effect;
        public   readonly string UniformName;
        public   readonly Type   Type;
        internal readonly int    HashCode;

        public UniformBindingKey (Effect effect, string uniformName, Type type) {
            Effect = effect;
            UniformName = uniformName;
            Type = type;

            HashCode = Type.GetHashCode() ^ 
                (UniformName.GetHashCode() << 8);
        }

        public override int GetHashCode () {
            return HashCode;
        }

        public bool Equals (UniformBindingKey rhs) {
            return (Effect == rhs.Effect) &&
                (Type == rhs.Type) &&
                (UniformName == rhs.UniformName);
        }

        public override bool Equals (object obj) {
            if (obj is UniformBindingKey)
                return Equals((UniformBindingKey)obj);
            else
                return false;
        }
    }

    internal class UniformBindingTable {
        private volatile bool[]            HasValue = new bool[256];
        private volatile IUniformBinding[] BindingsByID = new IUniformBinding[256];

        public void Add (uint id, IUniformBinding binding) {
            var b = BindingsByID;
            var hv = HasValue;
            var bToUnlock = b;
            lock (bToUnlock) {
                if (id >= b.Length) {
                    var newLength = id + 12;
                    {
                        var newArray = new IUniformBinding[newLength];
                        Array.Copy(b, newArray, b.Length);
                        b = BindingsByID = newArray;
                    }
                    {
                        var newArray = new bool[newLength];
                        Array.Copy(HasValue, newArray, hv.Length);
                        hv = HasValue = newArray;
                    }
                }

                var existing = b[id];
                if ((existing != null) && (existing != binding))
                    throw new UniformBindingException("Binding table has two entries for the same ID");

                hv[id] = true;
                b[id] = binding;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue (uint id, out IUniformBinding result) {
            result = null;
            if (id < 0)
                return false;

            var bindings = BindingsByID;
            if (id >= bindings.Length)
                return false;

            var hasValue = HasValue;
            if (!hasValue[id])
                return false;

            result = bindings[id];

            return true;
        }

        internal void Clear () {
            Array.Clear(HasValue, 0, HasValue.Length);
            Array.Clear(BindingsByID, 0, BindingsByID.Length);
        }
    }

    public interface ITypedUniform {
        string Name { get; }
        uint ID { get; }
        void Initialize (Material m);
    }

    internal static class TypedUniform {
        internal readonly static Dictionary<UniformBindingKey, uint> KeyToIDCache = 
            new Dictionary<UniformBindingKey, uint>(new UniformBindingKey.EqualityComparer());
    }

    public sealed class TypedUniform<T> : IDisposable, ITypedUniform
        where T: unmanaged
    {
        internal readonly UniformBindingKey KeyTemplate;
        public readonly MaterialSetBase MaterialSet;
        public string Name { get; private set; }
        public uint ID { get; private set; }

        public TypedUniform (MaterialSetBase materials, string uniformName) {
            MaterialSet = materials;
            KeyTemplate = new UniformBindingKey(null, uniformName, typeof(T));
            Name = uniformName;

            uint id;
            lock (TypedUniform.KeyToIDCache) {
                if (!TypedUniform.KeyToIDCache.TryGetValue(KeyTemplate, out id)) {
                    id = (uint)TypedUniform.KeyToIDCache.Count;
                    TypedUniform.KeyToIDCache[KeyTemplate] = id;
                }
            }

            ID = id;

            MaterialSet.RegisterUniform(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySet (Material m, ref T value) {
            var ub = MaterialSet.GetUniformBinding(m, this);
            if (ub == null)
                return false;

            ub.Value = value;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set (Material m, ref T value) {
            if (!TrySet(m, ref value))
                throw new UniformBindingException("Failed to set uniform " + Name);
        }

        public void Initialize (Material material) {
            MaterialSet.GetUniformBinding(material, this);
        }

        public void Dispose () {
            MaterialSet.UnregisterUniform(this);
        }
    }

    public class UniformBindingException : Exception {
        public UniformBindingException (string message, Exception innerException = null)
            : base (message, innerException) {
        }
    }

    public class InvalidUniformMemberException : Exception {
        public string FieldName;
        public Type FieldType;

        public InvalidUniformMemberException (string fieldName, Type fieldType)
            : base(string.Format("Uniform members can only be Vector4 or Matrix. Blame OpenGL. Member {0} is of type {1}.", fieldName, fieldType.Name)) {
            FieldName = fieldName;
            FieldType = fieldType;
        }
    }
}

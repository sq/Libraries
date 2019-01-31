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
using System.Runtime.CompilerServices;

namespace Squared.Render {
    public interface IMaterialCollection {
        void ForEachMaterial<T> (Action<Material, T> action, T userData);
        void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData);
    }

    public delegate void RefMaterialAction<T> (Material material, ref T userData);

    public class MaterialList : List<Material>, IMaterialCollection, IDisposable {
        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            foreach (var material in this)
                action(material, userData);
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            foreach (var material in this)
                action(material, ref userData);
        }

        public void Dispose () {
            foreach (var material in this)
                material.Dispose();

            Clear();
        }
    }

    public class MaterialDictionary<TKey> : Dictionary<TKey, Material>, IDisposable, IMaterialCollection {
        public MaterialDictionary () 
            : base() {
        }

        public MaterialDictionary (IEqualityComparer<TKey> comparer)
            : base(comparer) {
        }

        public void Dispose () {
            foreach (var value in Values)
                value.Dispose();

            Clear();
        }

        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            foreach (var material in Values)
                action(material, userData);
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            foreach (var material in Values)
                action(material, ref userData);
        }
    }

    public abstract class MaterialSetBase : IDisposable {
        private   readonly object Lock = new object();
        protected readonly MaterialList ExtraMaterials = new MaterialList();

        public readonly Func<Material>[] AllMaterialFields;
        public readonly Func<IEnumerable<Material>>[] AllMaterialSequences;
        public readonly Func<IMaterialCollection>[] AllMaterialCollections;

        // Making a dictionary larger increases performance
        private const int BindingDictionaryCapacity = 4096;

        private readonly List<ITypedUniform> RegisteredUniforms = new List<ITypedUniform>();
        private readonly List<ITypedUniform> PendingUniformRegistrations = new List<ITypedUniform>();

        private readonly Dictionary<UniformBindingKey, IUniformBinding> UniformBindings = 
            new Dictionary<UniformBindingKey, IUniformBinding>(
                BindingDictionaryCapacity, new UniformBindingKey.EqualityComparer()
            );

        public readonly Thread OwningThread;

        public MaterialSetBase() 
            : base() {

            OwningThread = Thread.CurrentThread;
            BuildMaterialSets(out AllMaterialFields, out AllMaterialSequences, out AllMaterialCollections);
        }

        protected abstract void QueuePendingRegistrationHandler ();

        internal void InitializeTypedUniformsForMaterial (Material m, List<ITypedUniform> uniforms) {
            foreach (var tu in uniforms)
                tu.Initialize(m);
        }

        internal void PerformPendingRegistrations () {
            List<ITypedUniform> pur;
            lock (PendingUniformRegistrations) {
                if (PendingUniformRegistrations.Count == 0)
                    return;

                pur = PendingUniformRegistrations.ToList();
                PendingUniformRegistrations.Clear();
            }

            ForEachMaterial(InitializeTypedUniformsForMaterial, pur);
        }

        protected void BuildMaterialSets (
            out Func<Material>[] materialFields, 
            out Func<IEnumerable<Material>>[] materialSequences,
            out Func<IMaterialCollection>[] materialCollections 
        ) {
            var sequences = new List<Func<IEnumerable<Material>>>();
            var fields = new List<Func<Material>>();
            var collections = new List<Func<IMaterialCollection>>(); 

            var tMaterial = typeof(Material);
            var tMaterialDictionary = typeof(MaterialDictionary<>);

            var tMaterialCollection = typeof(IMaterialCollection);

            sequences.Add(() => this.ExtraMaterials);
            collections.Add(() => this.ExtraMaterials);

            foreach (var field in this.GetType().GetFields()) {
                var f = field;

                if (field.FieldType == tMaterial ||
                    tMaterial.IsAssignableFrom(field.FieldType) ||
                    field.FieldType.IsSubclassOf(tMaterial)
                ) {
                    fields.Add(
                        () => f.GetValue(this) as Material
                    );
                } else if (
                    field.FieldType.IsGenericType && 
                    field.FieldType.GetGenericTypeDefinition() == tMaterialDictionary
                ) {
                    var dictType = field.FieldType;
                    var valuesProperty = dictType.GetProperty("Values");

                    sequences.Add(
                        () => {
                            var dict = f.GetValue(this);
                            if (dict == null)
                                return null;

                            // Generics, bluhhhhh
                            var values = valuesProperty.GetValue(dict, null)
                                as IEnumerable<Material>;

                            return values;
                        }
                    );
                    collections.Add(() => (IMaterialCollection)f.GetValue(this));
                } else if (
                    tMaterialCollection.IsAssignableFrom(field.FieldType)
                ) {
                    collections.Add(() => (IMaterialCollection)f.GetValue(this));
                }
            }

            materialFields = fields.ToArray();
            materialSequences = sequences.ToArray();
            materialCollections = collections.ToArray();
        }

        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            lock (Lock) {
                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        action(material, userData);
                }

                foreach (var collection in AllMaterialCollections) {
                    var coll = collection();
                    if (coll == null)
                        continue;

                    coll.ForEachMaterial(action, userData);
                }
            }
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            lock (Lock) {
                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        action(material, ref userData);
                }

                foreach (var collection in AllMaterialCollections) {
                    var coll = collection();
                    if (coll == null)
                        continue;

                    coll.ForEachMaterial(action, ref userData);
                }
            }
        }

        public IEnumerable<Material> AllMaterials {
            get {
                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        yield return material;
                }

                foreach (var sequence in AllMaterialSequences) {
                    var seq = sequence();
                    if (seq == null)
                        continue;

                    foreach (var material in seq)
                        if (material != null)
                            yield return material;
                }
            }
        }

        private UniformBinding<T> GetUniformBindingSlow<T> (Material material, TypedUniform<T> uniform)
            where T: struct
        {
            var effect = material.Effect;
            if (effect == null)
                return null;

            IUniformBinding existing;
            var key = uniform.KeyTemplate;
            key.Effect = effect;

            lock (UniformBindings) {
                if (UniformBindings.TryGetValue(key, out existing))
                    return existing.Cast<T>();

                if (material.OwningThread != Thread.CurrentThread)
                    throw new UniformBindingException(
                        "Uniform bindings must be allocated on the thread that owns the material. An attempt was made to allocate a binding for '" + 
                        uniform.Name + "' on '" + material.Effect.CurrentTechnique.Name + "'"
                    );

                var result = UniformBinding<T>.TryCreate(effect, uniform.Name);

                UniformBindings.Add(key, result);
                material.UniformBindings.Add(uniform.ID, result);

                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal UniformBinding<T> GetUniformBinding<T> (Material material, TypedUniform<T> uniform)
            where T: struct 
        {
            IUniformBinding existing;
            if (material.UniformBindings.TryGetValue(uniform.ID, out existing))
                return existing.Cast<T>();

            var result = GetUniformBindingSlow<T>(material, uniform);
            material.UniformBindings.Add(uniform.ID, result);
            return result;
        }

        public void Add (Material extraMaterial) {
            List<ITypedUniform> ru;
            lock (RegisteredUniforms)
                ru = RegisteredUniforms.ToList();

            foreach (var u in ru)
                u.Initialize(extraMaterial);

            lock (Lock)
                ExtraMaterials.Add(extraMaterial);
        }

        public bool Remove (Material extraMaterial) {
            lock (Lock)
                return ExtraMaterials.Remove(extraMaterial);
        }

        public virtual void Dispose () {
            lock (UniformBindings) {
                foreach (var kvp in UniformBindings) {
                    if (kvp.Value != null)
                        kvp.Value.Dispose();
                }
                UniformBindings.Clear();
            }

            lock (Lock)
            foreach (var material in AllMaterials)
                material.Dispose();
        }

        public TypedUniform<T> NewTypedUniform<T> (string uniformName)
            where T : struct
        {
            return new TypedUniform<T>(this, uniformName);
        }

        internal void RegisterUniform (ITypedUniform uniform) {
            bool needQueue;
            lock (PendingUniformRegistrations) {
                needQueue = PendingUniformRegistrations.Count == 0;
                PendingUniformRegistrations.Add(uniform);
            }
            lock (RegisteredUniforms)
                RegisteredUniforms.Add(uniform);
            if (needQueue)
                QueuePendingRegistrationHandler();
        }

        internal void UnregisterUniform (ITypedUniform uniform) {
        }
    }
}
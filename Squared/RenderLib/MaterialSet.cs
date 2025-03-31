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
using System.Diagnostics;
using Squared.Threading;
using System.Runtime;

namespace Squared.Render {
    public interface IMaterialCollection {
        void ForEachMaterial<T> (Action<Material, T> action, T userData);
        void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData);
        void AddToSet (UnorderedList<Material> set);
        IEnumerable<Material> Materials { get; }
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

        public void AddToSet (UnorderedList<Material> set) {
            for (int i = 0, c = Count; i < c; i++)
                set.Add(this[i]);
        }

        public IEnumerable<Material> Materials => this;
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

        public void AddToSet (UnorderedList<Material> set) {
            foreach (var material in Values)
                set.Add(material);
        }

        public IEnumerable<Material> Materials => Values;
    }

    public abstract class MaterialSetBase : IDisposable {
        private   readonly object Lock = new object();
        protected readonly MaterialList ExtraMaterials = new MaterialList();

        public readonly Func<Material>[] AllMaterialFields;
        public readonly Func<IMaterialCollection>[] AllMaterialCollections;

        // Making a dictionary larger increases performance. This costs around 250kb of memory right now
        private const int BindingDictionaryCapacity = 4096;

        private readonly List<ITypedUniform> RegisteredUniforms = new List<ITypedUniform>();
        private readonly List<ITypedUniform> PendingUniformRegistrations = new List<ITypedUniform>();

        private readonly Dictionary<UniformBindingKey, IUniformBinding> UniformBindings = 
            new Dictionary<UniformBindingKey, IUniformBinding>(
                BindingDictionaryCapacity, new UniformBindingKey.EqualityComparer()
            );

        public readonly Thread OwningThread;

        public bool IsDisposed { get; private set; }

        protected UnorderedList<Material> MaterialCache = new UnorderedList<Material>(1024);
        // private HashSet<Material> MaterialCacheScratchSet = new HashSet<Material>(1024, new ReferenceComparer<Material>());

        public MaterialSetBase() 
            : base() {

            OwningThread = Thread.CurrentThread;
            BuildMaterialSets(out AllMaterialFields, out AllMaterialCollections);
        }

        protected abstract void QueuePendingRegistrationHandler ();

        protected void BuildMaterialCache () {
            lock (Lock) {
                MaterialCache.UnsafeFastClear();

                foreach (var field in AllMaterialFields) {
                    var material = field();
                    if (material != null)
                        MaterialCache.Add(material);
                }

                foreach (var coll in AllMaterialCollections)
                    coll()?.AddToSet(MaterialCache);

                /*
                var set = new HashSet<Material>(MaterialCache, ReferenceComparer<Material>.Instance);
                var extra = MaterialCache.Count - set.Count;
                ;
                */
            }
        }

        protected void ReleaseMaterialCache () {
            lock (Lock)
                MaterialCache.Clear();
        }

        internal void InitializeTypedUniformsForMaterial (Material m, List<ITypedUniform> uniforms) {
            foreach (var tu in uniforms)
                tu.Initialize(m);
        }

        internal void PerformPendingRegistrations (Frame frame) {
            List<ITypedUniform> pur;
            lock (PendingUniformRegistrations) {
                if (PendingUniformRegistrations.Count == 0)
                    return;

                // FIXME: use ToDenseList
                pur = PendingUniformRegistrations.ToList();
                PendingUniformRegistrations.Clear();
            }

            ForEachMaterial(InitializeTypedUniformsForMaterial, pur);
        }

        protected void BuildMaterialSets (
            out Func<Material>[] materialFields,
            out Func<IMaterialCollection>[] materialCollections 
        ) {
            var fields = new List<Func<Material>>();
            var collections = new List<Func<IMaterialCollection>>(); 

            var tMaterial = typeof(Material);
            var tMaterialDictionary = typeof(MaterialDictionary<>);
            var tMaterialCollection = typeof(IMaterialCollection);

            foreach (var field in GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                var f = field;

                if (field.FieldType == tMaterial ||
                    tMaterial.IsAssignableFrom(field.FieldType) ||
                    field.FieldType.IsSubclassOf(tMaterial)
                ) {
                    fields.Add(
                        () => 
                            (Material)f.GetValue(this)
                    );
                } else if (
                    field.FieldType.IsGenericType && 
                    field.FieldType.GetGenericTypeDefinition() == tMaterialDictionary
                ) {
                    var dictType = field.FieldType;
                    var valuesProperty = dictType.GetProperty("Values");
                    collections.Add(
                        () => 
                            (IMaterialCollection)f.GetValue(this)
                    );
                } else if (
                    tMaterialCollection.IsAssignableFrom(field.FieldType)
                ) {
                    collections.Add(
                        () => 
                            (IMaterialCollection)f.GetValue(this)
                    );
                } else {
                    ;
                }
            }

            materialFields = fields.ToArray();
            materialCollections = collections.ToArray();
        }

        public void ForEachMaterial<T> (Action<Material, T> action, T userData) {
            if (IsDisposed)
                return;

            lock (Lock) {
                if (MaterialCache.Count == 0)
                    BuildMaterialCache();

                foreach (var m in MaterialCache)
                    action(m, userData);
            }
        }

        public void ForEachMaterial<T> (RefMaterialAction<T> action, ref T userData) {
            if (IsDisposed)
                return;

            lock (Lock) {
                if (MaterialCache.Count == 0)
                    BuildMaterialCache();

                foreach (var m in MaterialCache)
                    action(m, ref userData);
            }
        }

        // Unsafe to use unless locked
        private IEnumerable<Material> AllMaterials {
            get {
                if (IsDisposed)
                    throw new ObjectDisposedException("MaterialSetBase");

                if (MaterialCache.Count == 0)
                    BuildMaterialCache();
                return MaterialCache;
            }
        }

        private UniformBinding<T> GetUniformBindingSlow<T> (Material material, TypedUniform<T> uniform)
            where T: unmanaged
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

                var result = UniformBinding<T>.TryCreate(effect, uniform.Name);

                UniformBindings.Add(key, result);
                material.UniformBindings.Add(uniform.ID, result);

                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal UniformBinding<T> GetUniformBinding<T> (Material material, TypedUniform<T> uniform)
            where T: unmanaged
        {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

            IUniformBinding existing;
            if (material.UniformBindings.TryGetValue(uniform.ID, out existing))
                return existing.Cast<T>();

            var result = GetUniformBindingSlow<T>(material, uniform);
            material.UniformBindings.Add(uniform.ID, result);
            return result;
        }

        public void Add (Material extraMaterial, bool registerInList = true) {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

            List<ITypedUniform> ru;
            lock (RegisteredUniforms)
                ru = RegisteredUniforms.ToList();

            foreach (var u in ru)
                u.Initialize(extraMaterial);

            lock (Lock) {
                if (registerInList)
                    ExtraMaterials.Add(extraMaterial);
                MaterialCache.Clear();
            }
        }

        public bool Remove (Material extraMaterial) {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

            lock (Lock)
                return ExtraMaterials.Remove(extraMaterial);
        }

        public virtual void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

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
            where T : unmanaged
        {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

            return new TypedUniform<T>(this, uniformName);
        }

        internal void RegisterUniform (ITypedUniform uniform) {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

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

        protected virtual IEnumerable<Material> GetShadersToPreload () {
            BuildMaterialCache();

            foreach (var m in AllMaterials) {
                if (m.HintPipeline == null && m.DelegatedHintPipeline?.HintPipeline == null)
                    continue;
                yield return m;
            }
        }

        public void PreloadShaders (RenderCoordinator coordinator) {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

            var sw = Stopwatch.StartNew();
            var dm = coordinator.Manager.DeviceManager;

            // HACK: Applying a shader does an on-demand compile
            // HACK: We should really only need 6 indices but drivers seem to want more sometimes
            var tempIb = new IndexBuffer(dm.Device, IndexElementSize.SixteenBits, 128, BufferUsage.WriteOnly);
            var count = 0;

            foreach (var m in GetShadersToPreload()) {
                lock (coordinator.UseResourceLock)
                    m.Preload(coordinator, dm, tempIb);
                count++;
            }

            coordinator.DisposeResource(tempIb);

            var elapsed = sw.Elapsed.TotalMilliseconds;
            Debug.WriteLine($"Shader preload took {elapsed:000.00}ms for {count} material(s)");
        }

        public Future<string> PreloadShadersAsync (RenderCoordinator coordinator, Task.TaskScheduler scheduler, Action<float> onProgress = null, int speed = 1) {
            if (IsDisposed)
                throw new ObjectDisposedException("MaterialSetBase");

            var dm = coordinator.Manager.DeviceManager;
            var materials = GetShadersToPreload().ToList();
            var tempIb = new IndexBuffer(dm.Device, IndexElementSize.SixteenBits, 128, BufferUsage.WriteOnly);
            var f = new Future<string>();
            var thunk = new Task.SchedulableGeneratorThunk(PreloadShadersAsyncImpl(coordinator, dm, tempIb, materials, onProgress, speed));
            scheduler.Start(f, thunk, Task.TaskExecutionPolicy.RunAsBackgroundTask);
            return f;
        }

        private IEnumerator<object> PreloadShadersAsyncImpl (RenderCoordinator coordinator, DeviceManager dm, IndexBuffer tempIb, List<Material> materials, Action<float> onProgress, int speed) {
            var sw = Stopwatch.StartNew();
            var wfns = new Task.WaitForNextStep();
            int remaining = 0, totalPreloaded = 0;
            Action decRemaining = () => {
                Interlocked.Decrement(ref remaining);
            };

            for (int i = 0; i < materials.Count; i++) {
                var m = materials[i];
                if (onProgress != null)
                    onProgress(i / (float)materials.Count);

                if (m.PreloadAsync(coordinator, dm, tempIb, decRemaining)) {
                    Interlocked.Increment(ref remaining);
                    totalPreloaded += 1;
                }
                
                if ((i % speed) == 0)
                    yield return wfns;
            }

            while (Volatile.Read(ref remaining) > 0)
                yield return wfns;

            onProgress(1f);

            coordinator.DisposeResource(tempIb);

            var elapsed = sw.Elapsed.TotalMilliseconds;
            var msg = $"Async shader preload took {elapsed:000.00}ms for {totalPreloaded} material(s)";
            Debug.WriteLine(msg);

            yield return new Task.Result(msg);
        }
    }
}
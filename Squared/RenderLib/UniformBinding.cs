using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        void Flush ();
    }

    public static class UniformBindingExtensions {
        public static UniformBinding<T> Cast<T> (this IUniformBinding iub)
            where T : struct
        {
            return (UniformBinding<T>)iub;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    unsafe delegate void* DGetParameterDesc (
        void* _this, void* hParameter, out D3DXPARAMETER_DESC pDesc
    );

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    unsafe delegate void* DGetParameter (
        void* _this, void* hEnclosingParameter, uint index
    );

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    unsafe delegate void* DGetParameterByName (
        void* _this, void* hEnclosingParameter, 
        [MarshalAs(UnmanagedType.LPStr), In]
        string name
    );

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    unsafe delegate int DSetRawValue (
        void* _this, void* hParameter, 
        [In] void* pData, 
        uint byteOffset, uint countBytes
    );

    public unsafe partial class UniformBinding<T> : IUniformBinding 
        where T : struct
    {
        public class ValueContainer {
            public T Current;
        }

        private static class KnownMethodSlots {
            public static uint GetParameterDesc;
            public static uint GetParameter;
            public static uint GetParameterByName;
            public static uint SetRawValue;

            static KnownMethodSlots () {
                var iface = typeof(ID3DXEffect);
                var firstSlot = Marshal.GetStartComSlot(iface);
                var lastSlot = Marshal.GetEndComSlot(iface);

                for (var i = firstSlot; i <= lastSlot; i++) {
                    ComMemberType mt = ComMemberType.Method;
                    var mi = Marshal.GetMethodInfoForComSlot(iface, i, ref mt);
                    var targetField = typeof(KnownMethodSlots).GetField(mi.Name);
                    if (targetField == null)
                        continue;
                    targetField.SetValue(null, (uint)i);
                }
            }
        }

        private struct NativeBinding {
            public bool         IsValid;
            public Fixup[]      Fixups;
            public uint         UploadSize;
            public DSetRawValue pSetRawValue;
            public void*        pUnboxedEffect;
            public void*        hParameter;
        }

        private class Storage : SafeBuffer {
            public Storage () 
                : base(true) 
            {
                // HACK: If this isn't big enough, you screwed up
                const int size = 1024 * 4;
                Initialize(size);
                SetHandle(Marshal.AllocHGlobal(size));
            }

            protected override bool ReleaseHandle () {
                Marshal.FreeHGlobal(DangerousGetHandle());
                return true;
            }
        }

        private readonly ValueContainer _ValueContainer = new ValueContainer();
        // The latest value is written into this buffer
        private readonly SafeBuffer     ScratchBuffer;
        // And then transferred and mutated in this buffer before being sent to D3D
        private readonly SafeBuffer     UploadBuffer;

        private NativeBinding           CurrentNativeBinding;

        public  bool   IsDirty    { get; private set; }
        public  bool   IsDisposed { get; private set; }
        public  Effect Effect     { get; private set; }
        public  string Name       { get; private set; }
        public  Type   Type       { get; private set; }

        #region Direct3D
#if !SDL2

        /// <summary>
        /// Bind a single named uniform of the effect as type T.
        /// </summary>
        private UniformBinding (Effect effect, string uniformName) {
            Type = typeof(T);

            Effect = effect;
            Name = uniformName;

            ScratchBuffer = new Storage();
            UploadBuffer = new Storage();
            IsDirty = true;

            UniformBinding.Register(effect, this);
        }

        public static UniformBinding<T> TryCreate (Effect effect, string uniformName) {
            if (effect == null)
                return null;
            if (effect.Parameters[uniformName] == null)
                return null;

            return new UniformBinding<T>(effect, uniformName);
        }

        private void CreateNativeBinding (out NativeBinding result) {
            var pUnboxedEffect = Effect.GetUnboxedID3DXEffect();
            result = default(NativeBinding);

            var pGetParameterByName = COMUtils.GetMethodFromVTable<DGetParameterByName>(
                pUnboxedEffect, KnownMethodSlots.GetParameterByName
            );

            var hParameter = pGetParameterByName(pUnboxedEffect, null, Name);
            if (hParameter == null)
                throw new Exception("Could not find d3dx parameter for uniform " + Name);

            var layout = new Layout(Type, pUnboxedEffect, hParameter);

            result = new NativeBinding {
                pUnboxedEffect = pUnboxedEffect,
                hParameter = hParameter,
                Fixups = layout.Fixups,
                UploadSize = layout.UploadSize
            };

            result.pSetRawValue = COMUtils.GetMethodFromVTable<DSetRawValue>(result.pUnboxedEffect, KnownMethodSlots.SetRawValue);

            result.IsValid = true;
        }

#endif
        #endregion

        #region SDL2
        #if SDL2
        #endif
        #endregion

        /// <summary>
        /// If you retain this you are a bad person and I'm ashamed of you! Don't do that!!!
        /// </summary>
        public ValueContainer Value {
            get {
                IsDirty = true;
                return _ValueContainer;
            }
        }

        private void GetCurrentNativeBinding (out NativeBinding nativeBinding) {
            if (!CurrentNativeBinding.IsValid)
                CreateNativeBinding(out CurrentNativeBinding);

            nativeBinding = CurrentNativeBinding;
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

        public void Flush () {
            NativeBinding nb;
            GetCurrentNativeBinding(out nb);

            if (!IsDirty && CurrentNativeBinding.IsValid)
                return;

            ScratchBuffer.Write<T>(0, _ValueContainer.Current);

            var pScratch = ScratchBuffer.DangerousGetHandle();
            var pUpload  = UploadBuffer.DangerousGetHandle();

            // Fix-up matrices because the in-memory order is transposed :|
            foreach (var fixup in nb.Fixups) {
                var pSource = (pScratch + fixup.FromOffset);
                var pDest = (pUpload + fixup.ToOffset);

                if (fixup.TransposeMatrix)
                    InPlaceTranspose((float*)pSource);

                Buffer.MemoryCopy(
                    pSource.ToPointer(),
                    pDest.ToPointer(),
                    fixup.DataSize, fixup.DataSize
                );
            }

#if SDL2
            throw new NotImplementedException("Write parameters buffer to GL uniform buffer");
#else
            // HACK: Bypass the COM wrapper and invoke directly from the vtable.
            var hr = nb.pSetRawValue(nb.pUnboxedEffect, nb.hParameter, pUpload.ToPointer(), 0, nb.UploadSize);
            Marshal.ThrowExceptionForHR(hr);
            // pEffect.SetRawValue(hParameter, pUpload.ToPointer(), 0, UploadSize);
#endif

            IsDirty = false;
        }

        private static bool IsStructure (Type type) {
            return type.IsValueType && !type.IsPrimitive;
        }

        public void ReleaseNativeBinding () {
#if SDL2
#else
#endif
            CurrentNativeBinding = default(NativeBinding);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            ScratchBuffer.Dispose();
            UploadBuffer.Dispose();

            ReleaseNativeBinding();
        }
    }

    public static class UniformBinding {
        // Making a dictionary larger increases performance
        private const int BindingDictionaryCapacity = 4096;

        private static readonly Dictionary<Effect, List<IUniformBinding>> Bindings =
            new Dictionary<Effect, List<IUniformBinding>>(new ReferenceComparer<Effect>());

        public static void FlushEffect (Effect effect) {
            lock (Bindings) {
                List<IUniformBinding> bindings;
                if (!Bindings.TryGetValue(effect, out bindings))
                    return;

                foreach (var binding in bindings)
                    binding.Flush();
            }
        }

        internal static void Register (Effect effect, IUniformBinding binding) {
            List<IUniformBinding> bindings;
            lock (Bindings) {
                if (!Bindings.TryGetValue(effect, out bindings))
                    Bindings[effect] = bindings = new List<IUniformBinding>();

                bindings.Add(binding);
            }
        }
    }
}

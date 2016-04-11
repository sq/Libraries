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

    public unsafe partial class UniformBinding<T> : IUniformBinding 
        where T : struct
    {
        public class Storage : SafeBuffer {
            public Storage () 
                : base(true) 
            {
                // HACK: If this isn't big enough, you screwed up
                const int size = 1024 * 16;
                Initialize(size);
                SetHandle(Marshal.AllocHGlobal(size));
            }

            protected override bool ReleaseHandle () {
                Marshal.FreeHGlobal(DangerousGetHandle());
                return true;
            }
        }

        private readonly ID3DXEffect pEffect;
        private readonly void*       hParameter;
        private readonly Fixup[]     Fixups;
        private readonly uint        UploadSize;

        // The latest value is written into this buffer
        private readonly SafeBuffer  ScratchBuffer;
        // And then transferred and mutated in this buffer before being sent to D3D
        private readonly SafeBuffer  UploadBuffer;

        public  bool   IsDirty    { get; private set; }
        public  bool   IsDisposed { get; private set; }
        public  Effect Effect     { get; private set; }
        public  string Name       { get; private set; }
        public  Type   Type       { get; private set; }        

        /// <summary>
        /// Bind a single named uniform of the effect as type T.
        /// </summary>
        private UniformBinding (Effect effect, ID3DXEffect pEffect, void* hParameter) {
            Type = typeof(T);

            Effect = effect;
            this.pEffect = pEffect;
            this.hParameter = hParameter;

            var layout = new Layout(Type, pEffect, hParameter);
            Fixups = layout.Fixups;
            UploadSize = layout.UploadSize;

            ScratchBuffer = new Storage();
            UploadBuffer = new Storage();
            IsDirty = false;

            UniformBinding.Register(effect, this);
        }

        public static UniformBinding<T> TryCreate (Effect effect, string uniformName) {
            var pEffect = effect.GetID3DXEffect();
            var hParameter = pEffect.GetParameterByName(null, uniformName);
            if (hParameter == null)
                return null;

            return new UniformBinding<T>(effect, pEffect, hParameter);
        }

        public void SetValue (T value) {
            ScratchBuffer.Write<T>(0, value);
            IsDirty = true;
        }

        public void SetValue (ref T value) {
            ScratchBuffer.Write<T>(0, value);
            IsDirty = true;
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
            if (!IsDirty)
                return;

            var pScratch = ScratchBuffer.DangerousGetHandle();
            var pUpload  = UploadBuffer.DangerousGetHandle();

            // Fix-up matrices because the in-memory order is transposed :|
            foreach (var fixup in Fixups) {
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

            pEffect.SetRawValue(hParameter, pUpload.ToPointer(), 0, UploadSize);

            IsDirty = false;
        }

        private static bool IsStructure (Type type) {
            return type.IsValueType && !type.IsPrimitive;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            ScratchBuffer.Dispose();
            UploadBuffer.Dispose();
            Marshal.ReleaseComObject(pEffect);
        }
    }

    public static class UniformBinding {
        private static readonly ReaderWriterLock Lock = new ReaderWriterLock();
        private static readonly Dictionary<Effect, List<IUniformBinding>> Bindings =
            new Dictionary<Effect, List<IUniformBinding>>(new ReferenceComparer<Effect>());

        public static void FlushEffect (Effect effect) {
            Lock.AcquireReaderLock(-1);

            try {
                List<IUniformBinding> bindings;
                if (!Bindings.TryGetValue(effect, out bindings))
                    return;

                foreach (var binding in bindings)
                    binding.Flush();
            } finally {
                Lock.ReleaseReaderLock();
            }
        }

        internal static void Register (Effect effect, IUniformBinding binding) {
            Lock.AcquireWriterLock(-1);

            try {
                List<IUniformBinding> bindings;
                if (!Bindings.TryGetValue(effect, out bindings)) {
                    Bindings[effect] = bindings = new List<IUniformBinding>();
                }
                bindings.Add(binding);
            } finally {
                Lock.ReleaseWriterLock();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;

namespace Squared.Render {
    public interface IUniformBinding : IDisposable {
        Type   Type   { get; }
        string Name   { get; }
        Effect Effect { get; }
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

        // The latest value is written into this buffer
        private readonly SafeBuffer  ScratchBuffer;
        // And then transferred and mutated in this buffer before being sent to D3D
        private readonly SafeBuffer  UploadBuffer;

        private bool   ValueIsDirty;
        
        public  bool   IsDisposed { get; private set; }
        public  Effect Effect     { get; private set; }
        public  string Name       { get; private set; }
        public  Type   Type       { get; private set; }        

        /// <summary>
        /// Bind a single named uniform of the effect as type T.
        /// </summary>
        public UniformBinding (Effect effect, string uniformName) {
            Type = typeof(T);

            Effect = effect;
            pEffect = effect.GetID3DXEffect();
            hParameter = pEffect.GetParameterByName(null, uniformName);

            var layout = new Layout(Type, pEffect, hParameter);
            Fixups = layout.Fixups;

            ScratchBuffer = new Storage();
            UploadBuffer = new Storage();
            ValueIsDirty = false;
        }

        public void SetValue (T value) {
            ScratchBuffer.Write<T>(0, value);
            ValueIsDirty = true;
        }

        public void SetValue (ref T value) {
            ScratchBuffer.Write<T>(0, value);
            ValueIsDirty = true;
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
            if (!ValueIsDirty)
                return;

            var pScratch = ScratchBuffer.DangerousGetHandle();
            var pUpload  = UploadBuffer.DangerousGetHandle();

            // Fix-up matrices because the in-memory order is transposed :|
            foreach (var fixup in Fixups) {
                Buffer.MemoryCopy(
                    (pScratch + fixup.SourceOffset).ToPointer(),
                    (pUpload + fixup.DestinationOffset).ToPointer(),
                    fixup.Count, fixup.Count
                );
            }

            // pEffect.SetRawValue(hParameter, pBuffer.ToPointer(), 0, (uint)TotalSize);

            ValueIsDirty = false;
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
}

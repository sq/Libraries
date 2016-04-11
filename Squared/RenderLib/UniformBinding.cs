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

    public unsafe class UniformBinding<T> : IUniformBinding 
        where T : struct
    {
        public class Storage : SafeBuffer {
            public Storage () 
                : base(true) 
            {
                Initialize<T>(1);
                SetHandle(Marshal.AllocHGlobal((int)ByteLength));
            }

            protected override bool ReleaseHandle () {
                Marshal.FreeHGlobal(DangerousGetHandle());
                return true;
            }
        }

        public enum BindingType {
            Blittable,
            // GPU format is column-major for some reason??
            RowMajorMatrix
        }

        public class BindingMember {
            public   readonly string      Name;
            public   readonly uint        Offset;
            public   readonly uint        Size;
            internal readonly void*       Handle;
            public   readonly BindingType Type;

            internal BindingMember (
                string name, uint offset, uint size, 
                void* handle, BindingType type
            ) {
                Name = name;
                Offset = offset;
                Size = size;
                Handle = handle;
                Type = type;
            }
        }

        private class BindingCheckState {
            public readonly ID3DXEffect Effect;
            public readonly List<string> Errors = new List<string>();
            public readonly List<BindingMember> Results;

            private readonly string CheckEffectName;
            private readonly string CheckUniformName;

            public uint NextUniformOffset = 0;

            public BindingCheckState (
                ID3DXEffect pEffect, List<BindingMember> results, 
                string effectName, string uniformName
            ) {
                Effect = pEffect;
                Results = results;
                CheckEffectName = effectName;
                CheckUniformName = uniformName;
            }

            public void Finish () {
                if (Errors.Count < 1)
                    return;

                var sb = new StringBuilder();
                sb.AppendFormat(
                    "Uniform binding failed for {0}{1}{2}",
                    CheckEffectName,
                    (CheckUniformName != null)
                        ? "." + CheckUniformName
                        : "",
                    Environment.NewLine
                );
                foreach (var error in Errors)
                    sb.AppendLine(error);

                Console.WriteLine(sb.ToString());
                throw new Exception(sb.ToString());
            }

            public void CheckMemberBindings (
                void* hEnclosingParameter, Type type
            ) {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                // HACK: Just brute-force scan for parameters until it fails.
                for (uint i = 0; i < 999; i++) {
                    var hParameter = Effect.GetParameter(hEnclosingParameter, i);
                    if (hParameter == null)
                        break;

                    D3DXPARAMETER_DESC parameterDesc;
                    Effect.GetParameterDesc(hParameter, out parameterDesc);
                    var parameterName = Marshal.PtrToStringAnsi(new IntPtr(parameterDesc.Name));

                    var field = type.GetField(parameterName, flags);
                    if (field == null)
                        field = type.GetField("_" + parameterName, flags);

                    uint fieldOffset =
                        (field == null)
                            ? 0
                            : (uint)Marshal.OffsetOf(type, field.Name).ToInt32();
                    Type fieldType =
                        (field == null)
                            ? null
                            : field.FieldType;

                    CheckMemberBinding(hParameter, ref parameterDesc, parameterName, fieldType, fieldOffset);
                }
            }

            private void ParameterError (ref D3DXPARAMETER_DESC parameterDesc, string parameterName, string message) {
                Errors.Add(string.Format(
                    "Error binding uniform {0} ({1} {2}): {3}",
                    parameterName, parameterDesc.Class, parameterDesc.Type,
                    message
                ));
            }

            private uint ComputeEffectiveSize (ref D3DXPARAMETER_DESC desc) {
                switch (desc.Class) {
                    // All scalars and vectors are rounded up to float4
                    case D3DXPARAMETER_CLASS.SCALAR:
                    case D3DXPARAMETER_CLASS.VECTOR:
                        return 16 * desc.Rows;
                    
                    // All matrices are float4x4 in column-major order, which should be fine since XNA doesn't
                    //  expose 3x3 matrices...
                    case D3DXPARAMETER_CLASS.MATRIX_ROWS:
                    case D3DXPARAMETER_CLASS.MATRIX_COLUMNS:
                        return desc.SizeBytes;

                    case D3DXPARAMETER_CLASS.STRUCT:
                        throw new InvalidOperationException("Don't compute the size of a struct from the descriptor");
                }

                // FIXME
                throw new NotImplementedException(desc.Class.ToString());
            }

            public void CheckMemberBinding (
                void* hParameter, ref D3DXPARAMETER_DESC parameterDesc, string parameterName, Type type, uint fieldOffset
            ) {
                var uniformOffset = NextUniformOffset;

                if (parameterDesc.Class == D3DXPARAMETER_CLASS.STRUCT) {
                    if (type == null) {
                        ParameterError(ref parameterDesc, parameterName, "No matching public field");
                        return;
                    } else if (uniformOffset != fieldOffset) {
                        ParameterError(ref parameterDesc, parameterName, "Uniform offset " + uniformOffset + " != field offset " + fieldOffset);
                        return;
                    }

                    // FIXME: Nested structs will have bad offsets
                    CheckMemberBindings(hParameter, type);
                    return;
                }

                var effectiveSize = ComputeEffectiveSize(ref parameterDesc);
                NextUniformOffset += effectiveSize;

                switch (parameterDesc.Class) {
                    case D3DXPARAMETER_CLASS.SCALAR:
                    case D3DXPARAMETER_CLASS.VECTOR:
                    case D3DXPARAMETER_CLASS.MATRIX_COLUMNS:
                    case D3DXPARAMETER_CLASS.MATRIX_ROWS:
                        if (type == null) {
                            ParameterError(ref parameterDesc, parameterName, "No matching public field");
                            return;
                        } else if (uniformOffset != fieldOffset) {
                            ParameterError(ref parameterDesc, parameterName, "Uniform offset " + uniformOffset + " != field offset " + fieldOffset);
                            return;
                        }

                        var managedSize = Marshal.SizeOf(type);
                        if (effectiveSize < managedSize) {
                            ParameterError(ref parameterDesc, parameterName, "Uniform size " + effectiveSize + " < field size " + managedSize);
                            return;
                        }

                        if (parameterDesc.Class == D3DXPARAMETER_CLASS.SCALAR) {
                            if (!type.IsPrimitive) {
                                ParameterError(ref parameterDesc, parameterName, "Expected scalar");
                                return;
                            }
                        }

                        Results.Add(new BindingMember(
                            parameterName, uniformOffset, effectiveSize, hParameter,
                            parameterDesc.Class == D3DXPARAMETER_CLASS.MATRIX_ROWS
                                ? BindingType.RowMajorMatrix
                                : BindingType.Blittable
                        ));

                        return;

                    case D3DXPARAMETER_CLASS.OBJECT:
                        ParameterError(ref parameterDesc, parameterName, "Texture and sampler uniforms not supported");
                        return;

                    default:
                        throw new NotImplementedException(parameterDesc.Class.ToString());
                }
            }
        }

        public  readonly List<BindingMember> Members = new List<BindingMember>();
        private readonly BindingMember[]     Fixups;

        private readonly ID3DXEffect        pEffect;
        private readonly void*              hParameter;
        private readonly D3DXPARAMETER_DESC ParameterDesc;
        private readonly int                TotalSize;
        private readonly SafeBuffer         Buffer;

        private T      LatestValue;
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
            pEffect.GetParameterDesc(hParameter, out ParameterDesc);

            var state = new BindingCheckState(pEffect, Members, effect.CurrentTechnique.Name, uniformName);

            if (ParameterDesc.StructMembers > 0) {
                if (!IsStructure(Type))
                    throw new InvalidOperationException("This uniform is a structure so it may only be bound to a structure.");

                state.CheckMemberBindings(hParameter, Type);
            } else {
                state.CheckMemberBinding(hParameter, ref ParameterDesc, uniformName, Type, 0);
            }

            state.Finish();

            Fixups = Members.Where(m => m.Type == BindingType.RowMajorMatrix).ToArray();
            TotalSize = Marshal.SizeOf(Type);
            Buffer = new Storage();
            ValueIsDirty = true;
        }

        public void SetValue (T value) {
            LatestValue = value;
            ValueIsDirty = true;
        }

        public void SetValue (ref T value) {
            LatestValue = value;
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

            Buffer.Write(0, LatestValue);
            var pBuffer = Buffer.DangerousGetHandle();

            // Fix-up matrices because the in-memory order is transposed :|
            foreach (var fixup in Fixups) {
                var pMatrix = pBuffer + (int)fixup.Offset;
                InPlaceTranspose((float*)pMatrix);
            }

            pEffect.SetRawValue(hParameter, pBuffer.ToPointer(), 0, (uint)TotalSize);

            ValueIsDirty = false;
        }

        private static bool IsStructure (Type type) {
            return type.IsValueType && !type.IsPrimitive;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Buffer.Dispose();
            Marshal.ReleaseComObject(pEffect);
        }
    }
}

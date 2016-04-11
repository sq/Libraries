using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;

namespace Squared.Render {
    public interface IUniformBinding : IDisposable {
        Type   Type   { get; }
        string Name   { get; }
        Effect Effect { get; }
    }

    public unsafe class UniformBinding<T> : IUniformBinding 
        where T : struct
    {
        public class BindingMember {
            public   readonly string Name;
            public   readonly uint   Offset;
            public   readonly uint   Size;
            internal readonly void*  Handle;

            internal BindingMember (string name, uint offset, uint size, void* handle) {
                Name = name;
                Offset = offset;
                Size = size;
                Handle = handle;
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
                // HACK: Just brute-force scan for parameters until it fails.
                for (uint i = 0; i < 999; i++) {
                    var hParameter = Effect.GetParameter(hEnclosingParameter, i);
                    if (hParameter == null)
                        break;

                    D3DXPARAMETER_DESC parameterDesc;
                    Effect.GetParameterDesc(hParameter, out parameterDesc);
                    var parameterName = Marshal.PtrToStringAnsi(new IntPtr(parameterDesc.Name));

                    var field = type.GetField(parameterName);
                    uint fieldOffset =
                        (field == null)
                            ? 0
                            : (uint)Marshal.OffsetOf(type, parameterName).ToInt32();
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

            public void CheckMemberBinding (
                void* hParameter, ref D3DXPARAMETER_DESC parameterDesc, string parameterName, Type type, uint fieldOffset
            ) {
                var uniformOffset = NextUniformOffset;
                NextUniformOffset += parameterDesc.SizeBytes;

                switch (parameterDesc.Class) {
                    case D3DXPARAMETER_CLASS.STRUCT:
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
                        if (managedSize != parameterDesc.SizeBytes) {
                            ParameterError(ref parameterDesc, parameterName, "Uniform size " + parameterDesc.SizeBytes + " != field size " + managedSize);
                            return;
                        }

                        if (parameterDesc.Class == D3DXPARAMETER_CLASS.STRUCT) {
                            CheckMemberBindings(hParameter, type);
                            return;

                        } else if (parameterDesc.Class == D3DXPARAMETER_CLASS.SCALAR) {
                            if (!type.IsPrimitive) {
                                ParameterError(ref parameterDesc, parameterName, "Expected scalar");
                                return;
                            }
                        }

                        Results.Add(new BindingMember(
                            parameterName, uniformOffset, parameterDesc.SizeBytes, hParameter
                        ));

                        return;

                    case D3DXPARAMETER_CLASS.OBJECT:
                        // HACK: We ignore samplers for now since their size is 0 and it's unclear
                        //  whether we can even set them correctly
                        if (
                            (parameterDesc.Type >= D3DXPARAMETER_TYPE.SAMPLER) &&
                            (parameterDesc.Type <= D3DXPARAMETER_TYPE.SAMPLERCUBE)
                        ) {
                            return;
                        }

                        if (type == null)
                            ParameterError(ref parameterDesc, parameterName, "No matching public field");

                        Results.Add(new BindingMember(
                            parameterName, uniformOffset, parameterDesc.SizeBytes, hParameter
                        ));

                        return;

                    default:
                        throw new NotImplementedException(parameterDesc.Class.ToString());
                }
            }
        }

        public  readonly List<BindingMember> Members = new List<BindingMember>();

        private readonly ID3DXEffect         pEffect;
        private readonly void*               hParameter;
        private readonly D3DXPARAMETER_DESC  ParameterDesc;

        private T      LatestValue;
        private bool   ValueIsDirty, HasStoredValue;
        
        public  bool   IsDisposed { get; private set; }
        public  Effect Effect     { get; private set; }
        public  string Name       { get; private set; }
        public  Type   Type       { get; private set; }

        /// <summary>
        /// Bind all uniforms of the effect as a single structure of type T.
        /// </summary>
        public UniformBinding (Effect effect) {
            Type = typeof(T);
            if (!IsStructure(Type))
                throw new InvalidOperationException(
                    "An effect's entire uniform collection can only be bound to a structure."
                );

            pEffect = effect.GetID3DXEffect();

            var state = new BindingCheckState(pEffect, Members, effect.CurrentTechnique.Name, null);
            state.CheckMemberBindings(null, Type);
            state.Finish();
        }

        /// <summary>
        /// Bind a single named uniform of the effect as type T.
        /// </summary>
        public UniformBinding (Effect effect, string uniformName) {
            Type = typeof(T);
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
        }

        private bool DoValuesMatch (ref T oldValue, ref T newValue) {
            return false;
        }

        public void SetValue (T value) {
            if (
                !HasStoredValue || 
                !DoValuesMatch(ref LatestValue, ref value)
            ) {
                LatestValue = value;
                ValueIsDirty = true;
                HasStoredValue = true;
            }
        }

        public void SetValue (ref T value) {
            if (
                !HasStoredValue || 
                !DoValuesMatch(ref LatestValue, ref value)
            ) {
                LatestValue = value;
                ValueIsDirty = true;
                HasStoredValue = true;
            }
        }

        public T? GetValue () {
            if (!HasStoredValue)
                return null;

            return LatestValue;
        }

        public void Flush () {
            if (!ValueIsDirty)
                return;

            // FIXME: Slow as hell
            var gch = GCHandle.Alloc(LatestValue, GCHandleType.Pinned);
            try {
                var pValue = gch.AddrOfPinnedObject();

                foreach (var member in Members) {
                    var pMember = pValue + (int)member.Offset;
                    pEffect.SetRawValue(member.Handle, pMember.ToPointer(), member.Offset, member.Size);
                }
            } finally {
                gch.Free();
            }

            ValueIsDirty = false;
        }

        private static bool IsStructure (Type type) {
            return type.IsValueType && !type.IsPrimitive;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Marshal.ReleaseComObject(pEffect);
        }
    }
}

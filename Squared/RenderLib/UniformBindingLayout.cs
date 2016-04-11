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
    public unsafe partial class UniformBinding<T> : IUniformBinding 
        where T : struct
    {
        public struct Fixup {
            public readonly int FromOffset;
            public readonly int ToOffset;
            public readonly int DataSize;
            public readonly int PaddingSize;
        }

        public class Layout {
            public readonly Fixup[] Fixups;

            public Layout (Type managedType, ID3DXEffect effect, void* hParameter) {
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
    }
}

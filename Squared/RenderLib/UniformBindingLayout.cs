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
            public readonly bool TransposeMatrix;

            public Fixup (
                int fromOffset, int toOffset, 
                int dataSize,   int paddingSize, 
                bool transposeMatrix
            ) {
                FromOffset  = fromOffset;
                ToOffset    = toOffset;
                DataSize    = dataSize;
                PaddingSize = paddingSize;
                TransposeMatrix = transposeMatrix;
            }
        }

        public class Layout {
            public readonly Fixup[] Fixups;
            public readonly uint UploadSize;

            public static FieldInfo FindField (Type type, string name) {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var result = type.GetField(name, flags);
                if (result == null)
                    result = type.GetField("_" + name, flags);
                return result;
            }


            #region Direct3D
#if !SDL2 && !FNA

            private readonly void* pUnboxedEffect;

            public Layout (Type type, void* pUnboxedEffect, void* hParameter) {
                this.pUnboxedEffect = pUnboxedEffect;

                var fixups = new List<Fixup>();
                uint uploadSize = 0;

                FixupMembers(fixups, type, hParameter, 0, ref uploadSize);

                Fixups = fixups.ToArray();
                UploadSize = uploadSize;
            }

            private void FixupMembers (List<Fixup> fixups, Type type, void* hParameter, int sourceOffset, ref uint uploadSize) {
                uploadSize = 0;

                D3DXPARAMETER_DESC desc;
                var pGetParameter = COMUtils.GetMethodFromVTable<DGetParameter>(pUnboxedEffect, KnownMethodSlots.GetParameter);
                var pGetParameterDesc = COMUtils.GetMethodFromVTable<DGetParameterDesc>(pUnboxedEffect, KnownMethodSlots.GetParameterDesc);

                var hParam = pGetParameterDesc(pUnboxedEffect, hParameter, out desc);
                var managedSize = Marshal.SizeOf(type);
                if (desc.SizeBytes != managedSize)
                    throw new UniformBindingException("Managed struct " + type.Name + " has a different size from shader uniform");

                for (uint i = 0; i < 999; i++) {
                    var hMember = pGetParameter(pUnboxedEffect, hParameter, i);
                    if (hMember == null)
                        break;

                    pGetParameterDesc(pUnboxedEffect, hMember, out desc);

                    FixupMember(fixups, hMember, type, 0, ref desc, ref uploadSize);
                }
            }

            private void FixupMember (
                List<Fixup> fixups, void* hMember, 
                Type type, int sourceOffset,
                ref D3DXPARAMETER_DESC desc,
                ref uint uploadSize
            ) {
                var offset = uploadSize;

                var name = Marshal.PtrToStringAnsi(new IntPtr(desc.Name));
                var field = FindField(type, name);
                if (field == null)
                    throw new UniformBindingException("No field found for " + name);

                sourceOffset += Marshal.OffsetOf(type, field.Name).ToInt32();
                // FIXME: Arrays
                var valueSize = Marshal.SizeOf(field.FieldType);
                if (valueSize != desc.SizeBytes)
                    throw new UniformBindingException("Field size mismatch");

                var endpoint = (offset + valueSize - 1);

                var startVector = offset / 16;
                var endVector = endpoint / 16;

                if (valueSize <= 16) {
                    if (startVector != endVector)
                        throw new UniformBindingException("Field " + name + " crosses a 16 byte alignment boundary");
                }

                if (field.FieldType == typeof(bool))
                    throw new UniformBindingException("Bool fields not supported, set bool parameters directly");

                switch (desc.Class) {
                    case D3DXPARAMETER_CLASS.MATRIX_COLUMNS:
                    case D3DXPARAMETER_CLASS.MATRIX_ROWS:
                        fixups.Add(new Fixup(
                            sourceOffset, (int)offset, valueSize, (int)valueSize, 
                            (desc.Class == D3DXPARAMETER_CLASS.MATRIX_ROWS)
                        ));
                        break;

                    case D3DXPARAMETER_CLASS.SCALAR:
                    case D3DXPARAMETER_CLASS.VECTOR:
                        fixups.Add(new Fixup(
                            sourceOffset, (int)offset, valueSize, (int)valueSize, 
                            false
                        ));
                        break;

                    case D3DXPARAMETER_CLASS.STRUCT:
                        FixupMembers(fixups, field.FieldType, hMember, sourceOffset, ref uploadSize);
                        break;

                    case D3DXPARAMETER_CLASS.OBJECT:
                        // FIXME: Texture2D?
                        valueSize = 4;
                        break;

                    default:
                        throw new UniformBindingException("Parameter type not implemented: " + desc.Class.ToString());
                }

                var paddedSize = ((valueSize + 15) / 16) * 16;
                uploadSize += (uint)paddedSize;
            }

#endif
            #endregion

            #region SDL2
            #if SDL2 || FNA

            public Layout (Type type, EffectParameter parameter) {
                // FIXME
            }

            #endif
            #endregion
        }
    }
}

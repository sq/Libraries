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
        where T : unmanaged
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
            public static FieldInfo FindField (Type type, string name) {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var result = type.GetField(name, flags);
                if (result == null)
                    result = type.GetField("_" + name, flags);
                return result;
            }

            public struct MemberMapping {
                public string Name;
                public EffectParameter Member;
                public FieldInfo Field;
                public Type FieldType;
                public int ManagedOffset;
                public IntPtr NativePointer;
                public uint ManagedSize, NativeSize;
            }

            FieldInfo fData, fDataSize;
            public readonly MemberMapping[] Members;

            public Layout (Type type, EffectParameter parameter) {
                fData = FindField(typeof(EffectParameter), "values");
                if (fData == null)
                    throw new Exception("No field named 'values' found inside EffectParameter");
                fDataSize = FindField(typeof(EffectParameter), "valuesSizeBytes");

                var mappings = new List<MemberMapping>();
                foreach (var member in parameter.StructureMembers) {
                    var field = FindField(type, member.Name);
                    if (field == null)
                        throw new Exception("No matching field found for structure member " + member.Name);

                    ValidateFieldType(field.Name, field.FieldType);

                    var m = new MemberMapping {
                        Name = member.Name,
                        Member = member,
                        Field = field,
                        FieldType = field.FieldType,
                        ManagedOffset = Marshal.OffsetOf(type, field.Name).ToInt32(),
                        ManagedSize = (uint)Marshal.SizeOf(field.FieldType),
                        NativePointer = (IntPtr)fData.GetValue(member),
                        NativeSize = (uint)fDataSize.GetValue(member)
                    };
                    mappings.Add(m);
                }

                Members = mappings.ToArray();
            }

            public bool IsValid {
                get {
                    return (Members != null) && (Members.Length > 0);
                }
            }
        }
    }
}

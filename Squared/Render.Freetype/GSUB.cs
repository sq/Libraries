using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using SharpFont;
using Squared.Util;
using LookupTypes = Squared.Render.Text.OpenType.GSUBLookupTypes;
using LigatureSubst = Squared.Render.Text.OpenType.GSUBLigatureSubst;
using GSUBLookupTable = Squared.Render.Text.OpenType.LookupTable<Squared.Render.Text.OpenType.GSUBLookupTypes>;

namespace Squared.Render.Text.OpenType {
    public unsafe class GSUBTable : IDisposable {
        public readonly FreeTypeFont Font;
        public readonly byte* Data;

        internal readonly GSUBHeader* Header;
        internal readonly int HeaderSize;

        internal LookupList* LookupList;
        public readonly GSUBLigatureLookup[] Lookups;

        public bool IsDisposed { get; private set; }


        internal GSUBTable (FreeTypeFont font, IntPtr data) {
            Font = font;
            Data = (byte*)data;
            Header = (GSUBHeader*)Data;
            HeaderSize = (Header->MajorVersion > 1) || (Header->MinorVersion >= 1)
                ? 12 : 10;
            LookupList = (LookupList*)(Data + Header->LookupListOffset);

            var temp = new List<GSUBLigatureLookup>(LookupList->LookupCount);
            for (int i = 0, c = LookupList->LookupCount; i < c; i++) {
                var offset = (&LookupList->FirstLookupOffset)[i];
                var table = (GSUBLookupTable*)(((byte*)LookupList) + offset);
                switch (table->LookupType) {
                    case LookupTypes.Ligature:
                        temp.Add(new GSUBLigatureLookup(table));
                        continue;
                    default:
                        continue;
                }
            }

            Lookups = temp.ToArray();
        }

        public void Dispose () {
            if (IsDisposed)
                return;
            IsDisposed = true;
            FT.FT_OpenType_Free(Font.Face.Handle, (IntPtr)Data);
        }

        internal bool HasAnyEntriesForGlyph (uint index) {
            foreach (var lookup in Lookups)
                if (lookup.HasAnyEntriesForGlyph(index))
                    return true;

            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GSUBHeader {
        public readonly FTUInt16 MajorVersion, MinorVersion;
        public readonly FTUInt16 ScriptListOffset, FeatureListOffset, LookupListOffset;
        [OptionalField]
        public readonly FTUInt16 FeatureVariationsOffset;
    }

    public unsafe abstract class GSUBLookup : Lookup<LookupTypes> {
        internal GSUBLookup (GSUBLookupTable *table)
            : base (table) {
        }

        internal abstract bool HasAnyEntriesForGlyph (uint glyphId);
    }

    public unsafe class GSUBLigatureLookup : GSUBLookup {
        private List<Subtable<LigatureSubst>> TempSubTables;
        public readonly Subtable<LigatureSubst>[] SubTables;

        internal GSUBLigatureLookup (GSUBLookupTable * table)
            : base(table) {
            TempSubTables = new List<Subtable<LigatureSubst>>(table->SubTableCount);
            DecodeSubtables();
            SubTables = TempSubTables.ToArray();
            TempSubTables = null;
        }

        internal unsafe override void DecodeSubtable (UInt16 format, Coverage coverage, FTUInt16 *subtable, FTUInt16* data) {
            var setCount = data[0].Value;
            if (setCount == 0)
                return;

            var sets = new LigatureSubst[setCount];
            for (uint i = 0; i < setCount; i++) {
                var offset = data[1 + i];
                var set = new LigatureSubst((FTUInt16*)(((byte*)subtable) + offset));
                sets[i] = set;
            }
            TempSubTables.Add(new Subtable<LigatureSubst>(coverage, sets));
        }

        internal override bool HasAnyEntriesForGlyph (uint glyphId) {
            var result = default(LigatureSubst);
            foreach (var subtable in SubTables)
                if (subtable.TryGetValue((int)glyphId, ref result))
                    return true;

            return false;
        }

        /*
        internal override bool TryGetValue (int glyphId, int nextGlyphId, ref GSUBValueRecord thisGlyph, ref GSUBValueRecord nextGlyph) {
            foreach (var subtable in SubTables)
                if (subtable.TryGetValue(glyphId, ref thisGlyph))
                    return true;

            return false;
        }
        */
    }

    public enum GSUBLookupTypes : UInt16 {
        Single = 1,
        Multiple = 2,
        Alternate = 3,
        Ligature = 4,
        Context = 5,
        ChainingContext = 6,
        ExtensionSubstitution = 7,
        ReverseChainingContextSingle = 8,
        Reserved = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GSUBLigatureSubst {
        public UInt16 LigatureCount;
        public GSUBLigature[] Ligatures;

        public unsafe GSUBLigatureSubst (FTUInt16* source)
            : this () {
            LigatureCount = source[0].Value;
            Ligatures = new GSUBLigature[LigatureCount];
            for (uint i = 0; i < LigatureCount; i++) {
                var offset = source[1 + i].Value;
                var table = (FTUInt16*)((byte*)source + offset);
                var ligature = new GSUBLigature {
                    LigatureGlyph = table[0].Value,
                    ComponentCount = table[1].Value,
                    ComponentGlyphIDs = new ushort[table[1].Value - 1],
                };
                for (uint j = 0; j < ligature.ComponentGlyphIDs.Length; j++)
                    ligature.ComponentGlyphIDs[j] = table[2 + j].Value;
                Ligatures[i] = ligature;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GSUBLigature {
        public UInt16 LigatureGlyph;
        public UInt16 ComponentCount;
        public UInt16[] ComponentGlyphIDs;
    }
}

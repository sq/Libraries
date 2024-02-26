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
using LookupTypes = Squared.Render.Text.OpenType.GPOSLookupTypes;
using ValueFormats = Squared.Render.Text.OpenType.GPOSValueFormats;
using ValueRecord = Squared.Render.Text.OpenType.GPOSValueRecord;
using PairValueRecord = Squared.Render.Text.OpenType.GPOSPairValueRecord;
using GPOSLookupTable = Squared.Render.Text.OpenType.LookupTable<Squared.Render.Text.OpenType.GPOSLookupTypes>;

namespace Squared.Render.Text.OpenType {
    public unsafe class GPOSTable : IDisposable {
        public readonly FreeTypeFont Font;
        public readonly byte* Data;

        internal readonly GPOSHeader* Header;
        internal readonly int HeaderSize;

        internal LookupList* LookupList;
        public readonly GPOSLookup[] Lookups;

        public bool IsDisposed { get; private set; }


        internal GPOSTable (FreeTypeFont font, IntPtr data) {
            Font = font;
            Data = (byte*)data;
            Header = (GPOSHeader*)Data;
            HeaderSize = (Header->MajorVersion > 1) || (Header->MinorVersion >= 1)
                ? 12 : 10;
            LookupList = (LookupList*)(Data + Header->LookupListOffset);

            var temp = new List<GPOSLookup>(LookupList->LookupCount);
            for (int i = 0, c = LookupList->LookupCount; i < c; i++) {
                var offset = (&LookupList->FirstLookupOffset)[i];
                var table = (GPOSLookupTable*)(((byte*)LookupList) + offset);
                switch (table->LookupType) {
                    case LookupTypes.Single: {
                        var lookup = new GPOSSingleLookup(table);
                        if (lookup.SubTables.Length > 0)
                            temp.Add(lookup);
                        continue;
                    }
                    case LookupTypes.Pair: {
                        var lookup = new GPOSPairLookup(table);
                        if (lookup.SubTables.Length > 0)
                            temp.Add(lookup);
                        continue;
                    }
                    default:
                        continue;
                }
            }

            Lookups = temp.ToArray();
        }

        internal bool HasAnyEntriesForGlyph (uint index) {
            foreach (var lookup in Lookups)
                if (lookup.HasAnyEntriesForGlyph(index))
                    return true;

            return false;
        }

        public void Dispose () {
            if (IsDisposed)
                return;
            IsDisposed = true;
            FT.FT_OpenType_Free(Font.Face.Handle, (IntPtr)Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GPOSHeader {
        public readonly FTUInt16 MajorVersion, MinorVersion;
        public readonly FTUInt16 ScriptListOffset, FeatureListOffset, LookupListOffset;
        [OptionalField]
        public readonly FTUInt16 FeatureVariationsOffset;
    }

    public unsafe abstract class GPOSLookup : Lookup<LookupTypes> {
        internal GPOSLookup (GPOSLookupTable *table)
            : base (table) {
        }

        internal abstract bool HasAnyEntriesForGlyph (uint index);
        internal abstract bool TryGetValue (int glyphId, int nextGlyphId, ref GPOSValueRecord thisGlyph, ref GPOSValueRecord nextGlyph);
    }

    public unsafe class GPOSSingleLookup : GPOSLookup {
        private List<Subtable<GPOSValueRecord>> TempSubTables;
        public readonly Subtable<GPOSValueRecord>[] SubTables;

        internal GPOSSingleLookup (GPOSLookupTable * table)
            : base(table) {
            TempSubTables = new List<Subtable<GPOSValueRecord>>(table->SubTableCount);
            DecodeSubtables();
            SubTables = TempSubTables.ToArray();
            TempSubTables = null;
        }

        internal unsafe override void DecodeSubtable (UInt16 format, Coverage coverage, FTUInt16 *subtable, FTUInt16* data) {
            GPOSValueRecord[] values;
            switch (format) {
                case 1: {
                    var vf = (ValueFormats)(*data++).Value;
                    var vr = new ValueRecord(ref data, vf);
                    values = new[] { vr };
                    break;
                }
                case 2: {
                    var vf = (ValueFormats)(*data++).Value;
                    var count = (*data++).Value;
                    values = new GPOSValueRecord[count];
                    for (int i = 0; i < count; i++)
                        values[i] = new ValueRecord(ref data, vf);
                    break;
                }
                default:
                    throw new NotImplementedException();
            }

            // HACK: Omit empty subtables, there's no point in keeping them
            if (values.Length > 0)
                TempSubTables.Add(new Subtable<GPOSValueRecord>(coverage, values));
        }

        internal override bool HasAnyEntriesForGlyph (uint glyphId) {
            var result = default(ValueRecord);
            foreach (var subtable in SubTables)
                if (subtable.TryGetValue((int)glyphId, ref result))
                    return true;

            return false;
        }

        internal override bool TryGetValue (int glyphId, int nextGlyphId, ref GPOSValueRecord thisGlyph, ref GPOSValueRecord nextGlyph) {
            foreach (var subtable in SubTables)
                if (subtable.TryGetValue(glyphId, ref thisGlyph))
                    return true;
                
            return false;
        }
    }

    public struct GPOSPairValueRecord {
        public UInt16 SecondGlyph;
        public GPOSValueRecord Value1, Value2;
    }

    public unsafe class GPOSPairLookup : GPOSLookup {
        private List<Subtable<GPOSPairValueRecord[]>> TempSubTables = new List<Subtable<GPOSPairValueRecord[]>>();
        public readonly Subtable<GPOSPairValueRecord[]>[] SubTables;

        internal GPOSPairLookup (GPOSLookupTable * table)
            : base(table) {
            DecodeSubtables();
            SubTables = TempSubTables.ToArray();
            TempSubTables = null;
        }

        internal unsafe override void DecodeSubtable (UInt16 format, Coverage coverage, FTUInt16 *subtable, FTUInt16* data) {
            PairValueRecord[][] values;
            switch (format) {
                case 1: {
                    ValueFormats vf1 = (ValueFormats)(*data++).Value,
                        vf2 = (ValueFormats)(*data++).Value;
                    var count = (*data++).Value;
                    values = new PairValueRecord[count][];
                    for (int i = 0; i < count; i++)
                        values[i] = ReadPairSetTable(subtable, *data++, vf1, vf2);
                    break;
                }
                case 2: {
                    // TODO: pair class adjustment. it's tremendously complex and I'm not sure it matters that much
                    ValueFormats vf1 = (ValueFormats)(*data++).Value,
                        vf2 = (ValueFormats)(*data++).Value;
                    FTUInt16 classDef1Offset = *data++,
                        classDef2Offset = *data++,
                        class1Count = *data++,
                        class2Count = *data++;
                    values = new PairValueRecord[0][];
                    break;
                }
                default:
                    throw new NotImplementedException();
            }

            // HACK: Omit empty subtables, there's no point in keeping them
            if (values.Length > 0)
                TempSubTables.Add(new Subtable<PairValueRecord[]>(coverage, values));
        }

        internal override bool HasAnyEntriesForGlyph (uint glyphId) {
            PairValueRecord[] result = null;
            foreach (var subtable in SubTables)
                if (subtable.TryGetValue((int)glyphId, ref result))
                    return true;

            return false;
        }

        internal override bool TryGetValue (int glyphId, int nextGlyphId, ref GPOSValueRecord thisGlyph, ref GPOSValueRecord nextGlyph) {
            PairValueRecord[] pairs = null;
            foreach (var subtable in SubTables) {
                if (subtable.TryGetValue(glyphId, ref pairs)) {
                    if (FindPair(pairs, nextGlyphId, ref thisGlyph, ref nextGlyph))
                        return true;
                }
            }

            return false;
        }

        private bool FindPair (PairValueRecord[] pairs, int nextGlyphId, ref GPOSValueRecord thisGlyph, ref GPOSValueRecord nextGlyph) {
            uint low = 0, high = (uint)(pairs.Length - 1);
            unchecked {
                while (low <= high) {
                    uint i = (high + low) >> 1;
                    ref var pair = ref pairs[i];
                    int c = nextGlyphId - pair.SecondGlyph;
                    if (c == 0) {
                        thisGlyph = pair.Value1;
                        nextGlyph = pair.Value2;
                        return true;
                    } else if (c > 0) {
                        low = i + 1;
                    } else if (i > 0) {
                        high = i - 1;
                    } else
                        break;
                }

                return false;
            }
        }

        private PairValueRecord[] ReadPairSetTable (FTUInt16* subtable, UInt16 offset, ValueFormats vf1, ValueFormats vf2) {
            var data = (FTUInt16*)(((byte*)subtable) + offset);
            var count = (*data++).Value;
            var result = new PairValueRecord[count];
            for (int i = 0; i < count; i++) {
                result[i] = new PairValueRecord {
                    SecondGlyph = *data++
                };
                result[i].Value1 = new ValueRecord(ref data, vf1);
                result[i].Value2 = new ValueRecord(ref data, vf2);
            }
            return result;
        }
    }

    public enum GPOSLookupTypes : UInt16 {
        Single = 1,
        Pair = 2,
        Cursive = 3,
        MarkToBase = 4,
        MarkToLigature = 5,
        MarkToMark = 6,
        Context = 7,
        ChainedContext = 8,
        Extension = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPOSValueRecord {
        public readonly ValueFormats Format;
        public readonly FTInt16 XPlacement, YPlacement, XAdvance, YAdvance;
        // public readonly FTUInt16 XPlaDeviceOffset, YPlaDeviceOffset, XAdvDeviceOffset, YAdvDeviceOffset;

        public unsafe GPOSValueRecord (ref FTUInt16* source, ValueFormats format) {
            this = default;

            Format = format;
            FTInt16* values = (FTInt16*)source;
            if ((format & ValueFormats.XPlacement) != default)
                XPlacement = *values++;
            if ((format & ValueFormats.YPlacement) != default)
                YPlacement = *values++;
            if ((format & ValueFormats.XAdvance) != default)
                XAdvance = *values++;
            if ((format & ValueFormats.YAdvance) != default)
                YAdvance = *values++;
            if ((format & ValueFormats.XPlacementDevice) != default)
                values++;
            if ((format & ValueFormats.YPlacementDevice) != default)
                values++;
            if ((format & ValueFormats.XAdvanceDevice) != default)
                values++;
            if ((format & ValueFormats.YAdvanceDevice) != default)
                values++;

            source = (FTUInt16*)values;
        }
    }

    [Flags]
    public enum GPOSValueFormats : UInt16 {
        XPlacement = 0x01,
        YPlacement = 0x02,
        XAdvance = 0x04,
        YAdvance = 0x08,
        XPlacementDevice = 0x10,
        YPlacementDevice = 0x20,
        XAdvanceDevice = 0x40,
        YAdvanceDevice = 0x80,
        Reserved = 0xFF00,
    }
}

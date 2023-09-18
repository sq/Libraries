using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using SharpFont;
using Squared.Util;

namespace Squared.Render.Text {
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
                var table = (LookupTable*)(((byte*)LookupList) + offset);
                switch (table->LookupType) {
                    case LookupTypes.Single:
                        temp.Add(new GPOSSingleLookup(table));
                        continue;
                    case LookupTypes.Pair:
                        temp.Add(new GPOSPairLookup(table));
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
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GPOSHeader {
        public readonly FTUInt16 MajorVersion, MinorVersion;
        public readonly FTUInt16 ScriptListOffset, FeatureListOffset, LookupListOffset;
        [OptionalField]
        public readonly FTUInt16 FeatureVariationsOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LookupList {
        public readonly FTUInt16 LookupCount;
        public FTUInt16 FirstLookupOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LookupTable {
        private readonly FTUInt16 _LookupType;
        private readonly FTUInt16 _LookupFlag;
        public LookupTypes LookupType => (LookupTypes)_LookupType.Value;
        public LookupFlags LookupFlag => (LookupFlags)_LookupFlag.Value;
        public readonly FTUInt16 SubTableCount;
        public FTUInt16 FirstSubTableOffset;
    }

    public unsafe abstract class GPOSLookup {
        internal readonly LookupTable* Table;
        public readonly LookupTypes Type;
        public readonly LookupFlags Flag;

        internal GPOSLookup (LookupTable * table) {
            Table = table;
            Type = table->LookupType;
            Flag = table->LookupFlag;
        }

        internal void DecodeSubtables () {
            for (int i = 0, c = Table->SubTableCount; i < c; i++) {
                var subTableOffset = (&Table->FirstSubTableOffset)[i];
                var ptr = (FTUInt16*)((byte*)Table + subTableOffset);
                var format = ptr[0];
                var coverageOffset = ptr[1];
                var coverage = ((byte*)ptr) + coverageOffset;
                DecodeSubtable(format, (Coverage*)coverage, ptr, ptr + 2);
            }
        }

        internal abstract void DecodeSubtable (UInt16 format, Coverage* coverage, FTUInt16* subtable, FTUInt16* data);
    }

    public unsafe class GPOSSubtable<TItem> {
        internal readonly Coverage* Coverage;
        public readonly TItem[] Values;

        internal GPOSSubtable (Coverage* coverage, TItem[] values) {
            Coverage = coverage;
            Values = values;
        }   

        public bool TryGetValue (uint glyphId, out TItem result) {
            if (
                !Text.Coverage.TryGetIndex(Coverage, glyphId, out var coverageIndex) ||
                (coverageIndex >= Values.Length)
            ) {
                result = default;
                return false;
            }

            result = Values[coverageIndex];
            return true;
        }
    }

    public unsafe class GPOSSingleLookup : GPOSLookup {
        public readonly List<GPOSSubtable<ValueRecord>> SubTables;

        internal GPOSSingleLookup (LookupTable * table)
            : base(table) {
            SubTables = new List<GPOSSubtable<ValueRecord>>(table->SubTableCount);
            DecodeSubtables();
        }

        internal unsafe override void DecodeSubtable (UInt16 format, Coverage* coverage, FTUInt16 *subtable, FTUInt16* data) {
            ValueRecord[] values;
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
                    values = new ValueRecord[count];
                    for (int i = 0; i < count; i++)
                        values[i] = new ValueRecord(ref data, vf);
                    break;
                }
                default:
                    throw new NotImplementedException();
            }

            SubTables.Add(new GPOSSubtable<ValueRecord>(coverage, values));
        }

        internal bool TryGetValue (uint glyphId, out ValueRecord result) {
            foreach (var subtable in SubTables)
                if (subtable.TryGetValue(glyphId, out result))
                    return true;

            result = default;
            return false;
        }
    }

    public struct PairValueRecord {
        public UInt16 SecondGlyph;
        public ValueRecord Value1, Value2;
    }

    public unsafe class GPOSPairLookup : GPOSLookup {
        public readonly List<GPOSSubtable<PairValueRecord[]>> SubTables;

        internal GPOSPairLookup (LookupTable * table)
            : base(table) {
            SubTables = new List<GPOSSubtable<PairValueRecord[]>>(table->SubTableCount);
            DecodeSubtables();
        }

        internal unsafe override void DecodeSubtable (UInt16 format, Coverage* coverage, FTUInt16 *subtable, FTUInt16* data) {
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

            SubTables.Add(new GPOSSubtable<PairValueRecord[]>(coverage, values));
        }

        internal bool TryGetValue (uint previousGlyphId, uint glyphId, out PairValueRecord result) {
            foreach (var subtable in SubTables) {
                if (subtable.TryGetValue(previousGlyphId, out var pairs)) {
                    // FIXME: Binary search
                    foreach (var pair in pairs) {
                        if (pair.SecondGlyph == glyphId) {
                            result = pair;
                            return true;
                        }
                    }
                }
            }

            result = default;
            return false;
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct RangeRecord {
        public readonly FTUInt16 StartGlyphId, EndGlyphId, StartCoverageIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct Coverage {
        private readonly FTUInt16 _CoverageFormat;
        public CoverageFormats Format => (CoverageFormats)_CoverageFormat.Value;
        public readonly FTUInt16 Count;
        public readonly FTUInt16 Data;

        public static bool TryGetIndex (Coverage *coverage, uint glyphIndex, out int result) {
            result = default;
            // FIXME
            switch (coverage->Format) {
                case CoverageFormats.Values: {
                    var glyphIds = &coverage->Data;
                    for (int i = 0, c = coverage->Count; i < c; i++) {
                        if (glyphIds[i] != glyphIndex)
                            continue;

                        result = i;
                        return true;
                    }
                    break;
                }
                case CoverageFormats.Ranges: {
                    var ranges = (RangeRecord *)&coverage->Data;
                    for (int i = 0, c = coverage->Count; i < c; i++) {
                        if (ranges[i].StartGlyphId > glyphIndex)
                            continue;
                        else if (ranges[i].EndGlyphId < glyphIndex)
                            continue;

                        result = ranges[i].StartCoverageIndex + i;
                        return true;
                    }
                    break;
                }
            }
            return false;
        }
    }

    internal enum CoverageFormats : UInt16 {
        Values = 1,
        Ranges = 2,
    }

    public enum LookupTypes : UInt16 {
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

    [Flags]
    public enum LookupFlags : UInt16 {
        RightToLeft = 0x01,
        IgnoreBaseGlyphs = 0x02,
        IgnoreLigatures = 0x04,
        IgnoreMarks = 0x08,
        UseMarkFilteringSet = 0x10,
        Reserved = 0xE0,
        MarkAttachmentTypeMask = 0xFF00,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ValueRecord {
        public readonly ValueFormats Format;
        public readonly FTInt16 XPlacement, YPlacement, XAdvance, YAdvance;
        // public readonly FTUInt16 XPlaDeviceOffset, YPlaDeviceOffset, XAdvDeviceOffset, YAdvDeviceOffset;

        public unsafe ValueRecord (ref FTUInt16* source, ValueFormats format) {
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
    public enum ValueFormats : UInt16 {
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FTUInt16 {
        public readonly UInt16 RawValue;

        public UInt16 Value {
            get {
                if (!BitConverter.IsLittleEndian)
                    return RawValue;
                else
                    return (UInt16)((RawValue & 0xFF) << 8 | (RawValue & 0xFF00) >> 8);
            }
        }

        public override string ToString () => Value.ToString();

        public override bool Equals (object obj) => Value.Equals(obj);
        public override int GetHashCode () => Value.GetHashCode();

        public static implicit operator UInt16 (FTUInt16 value) => value.Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FTInt16 {
        public readonly UInt16 RawValue;

        public unsafe Int16 Value {
            get {
                var temp = !BitConverter.IsLittleEndian
                    ? RawValue
                    : (UInt16)((RawValue & 0xFF) << 8 | (RawValue & 0xFF00) >> 8);
                return *((Int16*)&temp);
            }
        }

        public override string ToString () => Value.ToString();

        public override bool Equals (object obj) => Value.Equals(obj);
        public override int GetHashCode () => Value.GetHashCode();

        public static implicit operator Int16 (FTInt16 value) => value.Value;
    }
}

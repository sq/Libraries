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
                var coverageData = ((byte*)ptr) + coverageOffset;
                var decodedCoverage = new Coverage((FTUInt16*)coverageData);
                DecodeSubtable(format, decodedCoverage, ptr, ptr + 2);
            }
        }

        internal abstract void DecodeSubtable (UInt16 format, Coverage coverage, FTUInt16* subtable, FTUInt16* data);

        internal abstract bool TryGetValue (int glyphId, int nextGlyphId, out ValueRecord thisGlyph, out ValueRecord nextGlyph);
    }

    public unsafe class GPOSSubtable<TItem> {
        internal readonly Coverage Coverage;
        public readonly TItem[] Values;

        internal GPOSSubtable (Coverage coverage, TItem[] values) {
            Coverage = coverage;
            Values = values;
        }   

        public bool TryGetValue (int glyphId, out TItem result) {
            if (
                !Coverage.TryGetIndex(glyphId, out var coverageIndex) ||
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
        private List<GPOSSubtable<ValueRecord>> TempSubTables;
        public readonly GPOSSubtable<ValueRecord>[] SubTables;

        internal GPOSSingleLookup (LookupTable * table)
            : base(table) {
            TempSubTables = new List<GPOSSubtable<ValueRecord>>(table->SubTableCount);
            DecodeSubtables();
            SubTables = TempSubTables.ToArray();
            TempSubTables = null;
        }

        internal unsafe override void DecodeSubtable (UInt16 format, Coverage coverage, FTUInt16 *subtable, FTUInt16* data) {
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

            TempSubTables.Add(new GPOSSubtable<ValueRecord>(coverage, values));
        }

        internal override bool TryGetValue (int glyphId, int nextGlyphId, out ValueRecord thisGlyph, out ValueRecord nextGlyph) {
            nextGlyph = default;

            foreach (var subtable in SubTables)
                if (subtable.TryGetValue(glyphId, out thisGlyph))
                    return true;

            thisGlyph = default;
            return false;
        }
    }

    public struct PairValueRecord {
        public UInt16 SecondGlyph;
        public ValueRecord Value1, Value2;
    }

    public unsafe class GPOSPairLookup : GPOSLookup {
        private List<GPOSSubtable<PairValueRecord[]>> TempSubTables = new List<GPOSSubtable<PairValueRecord[]>>();
        public readonly GPOSSubtable<PairValueRecord[]>[] SubTables;

        internal GPOSPairLookup (LookupTable * table)
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

            TempSubTables.Add(new GPOSSubtable<PairValueRecord[]>(coverage, values));
        }

        internal override bool TryGetValue (int glyphId, int nextGlyphId, out ValueRecord thisGlyph, out ValueRecord nextGlyph) {
            foreach (var subtable in SubTables) {
                if (subtable.TryGetValue(glyphId, out var pairs)) {
                    // FIXME: Binary search
                    foreach (var pair in pairs) {
                        if (pair.SecondGlyph == nextGlyphId) {
                            thisGlyph = pair.Value1;
                            nextGlyph = pair.Value2;
                            return true;
                        }
                    }
                }
            }

            thisGlyph = nextGlyph = default;
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

    internal unsafe struct RangeRecord {
        public readonly UInt16 StartGlyphId, EndGlyphId, StartCoverageIndex;

        public RangeRecord (ref FTUInt16* ptr) {
            StartGlyphId = *ptr++;
            EndGlyphId = *ptr++;
            StartCoverageIndex = *ptr++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Check (int glyphId, out int result) {
            if ((StartGlyphId <= glyphId) && (EndGlyphId >= glyphId)) {
                result = glyphId - StartGlyphId + StartCoverageIndex;
                return true;
            }

            result = default;
            return false;
        }
    }

    internal unsafe class Coverage {
        private sealed class UshortComparer : IComparer<ushort> {
            public static readonly UshortComparer Instance = new UshortComparer();

            public int Compare (ushort x, ushort y) {
                unchecked {
                    return (int)x - (int)y;
                }
            }
        }

        public readonly CoverageFormats Format;
        public readonly int Count;
        public readonly UInt16[] Values;
        public readonly RangeRecord[] Ranges;

        public Coverage (FTUInt16 * ptr) {
            Format = (CoverageFormats)(*ptr++).Value;
            Count = (*ptr++).Value;
            switch (Format) {
                case CoverageFormats.Values: {
                    Values = new UInt16[Count];
                    for (int i = 0; i < Count; i++)
                        Values[i] = (*ptr++).Value;
                    Ranges = null;
                    return;
                }
                case CoverageFormats.Ranges: {
                    Ranges = new RangeRecord[Count];
                    for (int i = 0; i < Count; i++)
                        Ranges[i] = new RangeRecord(ref ptr);
                    Values = null;
                    return;
                }
                default:
                    throw new InvalidDataException($"Invalid coverage format {Format}");
            }
        }

        public bool TryGetIndex (int glyphId, out int result) {
            result = default;
            if (Count == 0)
                return false;

            // FIXME
            switch (Format) {
                case CoverageFormats.Values: {
                    result = Array.BinarySearch(Values, (ushort)glyphId, UshortComparer.Instance);
                    return (result >= 0);
                }
                case CoverageFormats.Ranges: {
                    var ranges = Ranges;

                    // First, binary search to locate any range that might contain this character
                    int count = Count, scanFrom = -1;
                    uint low = 0, high = (uint)(count - 1);
                    while (low <= high) {
                        uint i = (high + low) >> 1;
                        int c = glyphId - ranges[i].StartGlyphId;
                        if (c == 0) {
                            scanFrom = (int)i;
                            break;
                        } else if (c > 0) {
                            low = i + 1;
                        } else if (i > 0) {
                            high = i - 1;
                        } else {
                            break;
                        }
                    }
                    if (scanFrom < 0)
                        scanFrom = (int)Math.Min(low, high);
                    // The closest range we found is the one that will contain the character,
                    //  because ranges are required to be sorted by start id and non-overlapping
                    if (scanFrom < count)
                        return ranges[scanFrom].Check(glyphId, out result);
                    else
                        return false;
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator UInt16 (FTUInt16 value) => value.Value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FTInt16 {
        public readonly UInt16 RawValue;

        public unsafe Int16 Value {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Int16 (FTInt16 value) => value.Value;
    }
}

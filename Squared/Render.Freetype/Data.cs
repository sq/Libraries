using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Render.Text.OpenType {

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

    public unsafe struct RangeRecord {
        public readonly UInt16 StartGlyphId, EndGlyphId, StartCoverageIndex;

        public RangeRecord (ref FTUInt16* ptr) {
            StartGlyphId = *ptr++;
            EndGlyphId = *ptr++;
            StartCoverageIndex = *ptr++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Check (int glyphId, out int result) {
            if ((StartGlyphId <= glyphId) && (EndGlyphId >= glyphId)) {
                result = glyphId - StartGlyphId + StartCoverageIndex;
                return true;
            }

            result = default;
            return false;
        }
    }

    public unsafe class Coverage {
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
        public readonly UInt16 Min, Max;

        public Coverage (FTUInt16 * ptr) {
            Format = (CoverageFormats)(*ptr++).Value;
            Count = (*ptr++).Value;
            Max = 0;
            Min = UInt16.MaxValue;
            switch (Format) {
                case CoverageFormats.Values: {
                    Values = new UInt16[Count];
                    for (int i = 0; i < Count; i++) {
                        Values[i] = (*ptr++).Value;
                        Min = Math.Min(Min, Values[i]);
                        Max = Math.Max(Max, Values[i]);
                    }
                    Ranges = null;
                    return;
                }
                case CoverageFormats.Ranges: {
                    Ranges = new RangeRecord[Count];
                    for (int i = 0; i < Count; i++) {
                        Ranges[i] = new RangeRecord(ref ptr);
                        Min = Math.Min(Min, Ranges[i].StartGlyphId);
                        Max = Math.Max(Max, Ranges[i].EndGlyphId);
                    }
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

            if (Min > glyphId)
                return false;
            else if (Max < glyphId)
                return false;

            // FIXME
            switch (Format) {
                case CoverageFormats.Values: {
                    result = Array.BinarySearch(Values, (ushort)glyphId, UshortComparer.Instance);
                    return (result >= 0);
                }
                case CoverageFormats.Ranges: {
                    // First, binary search to locate any range that might contain this character
                    int count = Count, scanFrom = -1;
                    uint low = 0, high = (uint)(count - 1);
                    unchecked {
                        fixed (RangeRecord *ranges = Ranges) {
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
                }
            }
            return false;
        }
    }

    public enum CoverageFormats : UInt16 {
        Values = 1,
        Ranges = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LookupList {
        public readonly FTUInt16 LookupCount;
        public FTUInt16 FirstLookupOffset;
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
    internal struct LookupTable<TTypes>
        where TTypes : unmanaged
    {
        private readonly FTUInt16 _LookupType;
        private readonly FTUInt16 _LookupFlag;
        public unsafe TTypes LookupType {
            get {
                var temp = _LookupType.Value;
                return *(TTypes*)&temp;
            }
        }
        public LookupFlags LookupFlag => (LookupFlags)_LookupFlag.Value;
        public readonly FTUInt16 SubTableCount;
        public FTUInt16 FirstSubTableOffset;
    }

    public unsafe abstract class Lookup<TTypes>
        where TTypes : unmanaged
    {
        internal readonly LookupTable<TTypes>* Table;
        public readonly TTypes Type;
        public readonly LookupFlags Flag;

        internal Lookup (LookupTable<TTypes> * table) {
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
    }

    public unsafe class Subtable<TItem> {
        internal readonly Coverage Coverage;
        public readonly TItem[] Values;

        internal Subtable (Coverage coverage, TItem[] values) {
            Coverage = coverage;
            Values = values;
        }   

        public bool TryGetValue (int glyphId, ref TItem result) {
            if (
                !Coverage.TryGetIndex(glyphId, out var coverageIndex) ||
                (coverageIndex >= Values.Length)
            ) {
                return false;
            }

            result = Values[coverageIndex];
            return true;
        }
    }
}

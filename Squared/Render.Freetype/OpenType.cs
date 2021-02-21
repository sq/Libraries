using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Render.OpenType {
    [StructLayout(LayoutKind.Sequential)]
    public struct FontHeader {
        public UInt32 sfntVersion;
        public UInt16 numTables, searchRange, entrySelector, rangeShift;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TableRecord {
        public UInt32 tag; // C char[4]
        public UInt32 checksum, offset, length;
    }
}

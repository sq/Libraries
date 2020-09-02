using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI {
    public enum ContainerFlags : int {
        Row = 0x02,
        Column = 0x03,

        WrapNone = 0x00,
        WrapLeftToRight = 0x04,

        AlignStart = 0x08,
        AlignMiddle = 0x00,
        AlignEnd = 0x10,
        AlignJustify = 0x18,

        Free = 0x00,
        Flex = Row
    }

    public enum ChildFlags : int {
        Left = 0x020,
        Top = 0x040,
        Right = 0x080,
        Bottom = 0x100,
        FillRow = 0x0a0,
        FillColumn = 0x140,
        Center = 0x000,
        Fill = 0x1e0,
        Break = 0x200
    }

    public enum MiscFlags : int {
        BoxModelMask = 0x7,
        BoxMask = 0x1f,
        LayoutMask = 0x3e0,
        Inserted = 0x400,
        HFixed = 0x800,
        VFixed = 0x1000,
        FixedMask = HFixed | VFixed,
    }
}

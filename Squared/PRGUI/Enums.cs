using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI {
    [Flags]
    public enum ControlFlags : uint {
        Layout_Row = 0x02,
        Layout_Column = 0x03,
        Layout_Wrap_None = 0x00,
        Layout_Wrap_LeftToRight = 0x04,
        Layout_Align_Start = 0x08,
        Layout_Align_Middle = 0x00,
        Layout_Align_End = 0x10,
        Layout_Align_Justify = 0x18,
        Layout_Free = 0x00,
        Layout_Flex = Layout_Row,

        Child_Left = 0x020,
        Child_Top = 0x040,
        Child_Right = 0x080,
        Child_Bottom = 0x100,
        Child_Fill_Row = 0x0a0,
        Child_Fill_Column = 0x140,
        Child_Center = 0x000,
        Child_Fill = 0x1e0,
        Child_Break = 0x200,

        Inserted = 0x400,
        HFixed = 0x800,
        VFixed = 0x1000,
    }

    public static class ControlFlagMask {
        public const ControlFlags BoxModel = (ControlFlags)0x7,
            Box = (ControlFlags)0x1f,
            Layout = (ControlFlags)0x3e0,
            Fixed = ControlFlags.HFixed | ControlFlags.VFixed;
    }

    public static class ControlFlagExtensions {
        public static bool HasFlag (this ControlFlags flags, ControlFlags flag) {
            return (flags & flag) != 0;
        }
    }
}

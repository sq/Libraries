using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI.NewEngine.Enums {
    /// <summary>
    /// NOTE: While this is a flags enum most flags in a category are mutually exclusive
    /// </summary>
    [Flags]
    internal enum ContainerFlag : ushort {
        /// <summary>
        /// Arrange child elements left-to-right
        /// </summary>
        Layout_Row                 = 0b1,
        /// <summary>
        /// Arrange child elements top-to-bottom
        /// </summary>
        Layout_Column              = 0b10,
        /// <summary>
        /// Arrange child elements right-to-left/bottom-to-top
        /// </summary>
        Layout_Reverse             = 0b100,

        /// <summary>
        /// Align each run to the bottom/right of container
        /// </summary>
        Align_End                  = 0b1000,
        /// <summary>
        /// Align each run to the center of the container
        /// </summary>
        Align_Center               = 0b10000,
        /// <summary>
        /// Spread the child elements across each run by distributing whitespace
        /// </summary>
        Align_Justify              = 0b100000,

        /// <summary>
        /// If a child will not fit in available space, wrap it to a new run
        /// </summary>
        Arrange_Wrap               = 0b1000000,

        /// <summary>
        /// Child rects will be clipped to our content box
        /// </summary>
        Boxes_Clip                 = 0b10000000,
        /// <summary>
        /// Child boxes will not automatically grow beyond our content box even if they have huge
        ///  siblings, but they will not be clipped
        /// </summary>
        Boxes_Constrain_Growth     = 0b100000000,
        /// <summary>
        /// In grid mode, boxes will not have their height normalized
        /// </summary>
        Boxes_Grid_NoNormalization = 0b1000000000,

        /// <summary>
        /// Will expand automatically beyond default width to hold children
        /// </summary>
        Size_ExpandForContent_X = 0b100000000000,
        /// <summary>
        /// Will expand automatically beyond default height to hold children
        /// </summary>
        Size_ExpandForContent_Y = 0b1000000000000,
        /// <summary>
        /// Will expand automatically beyond default size to hold children
        /// </summary>
        Size_ExpandForContent   = Size_ExpandForContent_X | Size_ExpandForContent_Y,
        /// <summary>
        /// Will expand horizontally to hold children and will not shrink even if container lacks space
        /// </summary>
        Size_PreventCrush_X     = 0b10000000000000 | Size_ExpandForContent_X,
        /// <summary>
        /// Will expand vertically to hold children and will not shrink even if container lacks space
        /// </summary>
        Size_PreventCrush_Y     = 0b100000000000000 | Size_ExpandForContent_Y,
        /// <summary>
        /// Will expand to hold children and will not shrink even if container lacks space
        /// </summary>
        Size_PreventCrush       = Size_PreventCrush_X | Size_PreventCrush_Y,

        DEFAULT                 = Layout_Row | Arrange_Wrap | Boxes_Clip | Size_ExpandForContent,
    }

    /// <summary>
    /// NOTE: While this is a flags enum some flags are mutually exclusive
    /// </summary>
    [Flags]
    internal enum BoxFlag : ushort {
        /// <summary>
        /// Instead of expanding to fill horizontal space, the box will be aligned to the left
        /// </summary>
        Anchor_Left     = 0b1,
        /// <summary>
        /// Instead of expanding to fill horizontal space, the box will be aligned to the right
        /// </summary>
        Anchor_Right    = 0b10,
        /// <summary>
        /// Instead of expanding to fill vertical space, the box will be aligned to the top
        /// </summary>
        Anchor_Top      = 0b100,
        /// <summary>
        /// Instead of expanding to fill vertical space, the box will be aligned to the bottom
        /// </summary>
        Anchor_Bottom   = 0b1000,

        /// <summary>
        /// The box will expand to fill available horizontal space
        /// </summary>
        Fill_Row        = Anchor_Left | Anchor_Right,
        /// <summary>
        /// The box will expand to fill available vertical space
        /// </summary>
        Fill_Column     = Anchor_Top | Anchor_Bottom,
        /// <summary>
        /// The box will expand to fill all available space
        /// </summary>
        Fill            = Fill_Row | Fill_Column,

        /// <summary>
        /// Forces the box to occupy a new run
        /// </summary>
        Break            = 0b10000,
        /// <summary>
        /// The box will be laid out inside the entire container instead of occupying a run
        /// </summary>
        Stacked          = 0b100000,
        /// <summary>
        /// The box will be laid out inside the entire container and will also not influence the container's content size
        /// </summary>
        Floating         = 0b1000000 | Stacked,

        /// <summary>
        /// The box's margins will be allowed to overlap with its container's padding.
        /// </summary>
        CollapseMargins  = 0b10000000,

        /// <summary>
        /// The box will not be included in its parent's content size calculations (for spacers)
        /// </summary>
        NoMeasurement    = 0b100000000,

        /// <summary>
        /// If the box is being aligned (as a stacked/floated box) it will align to its parent's box,
        ///  instead of to the box of its parent's content
        /// </summary>
        AlignToParentBox = 0b1000000000,

        // No anchor = centered
        DEFAULT       = Fill_Row 
    }

    public enum ChildDirection : ushort {
        Row = ContainerFlag.Layout_Row,
        Column = ContainerFlag.Layout_Column,
        RTL = ContainerFlag.Layout_Row | ContainerFlag.Layout_Reverse,
        Upward = ContainerFlag.Layout_Column | ContainerFlag.Layout_Reverse,

        [Obsolete]
        MASK = Row | Column | ContainerFlag.Layout_Reverse,
    }

    public enum ChildAlignment : ushort {
        Start = 0,
        Center = ContainerFlag.Align_Center,
        End = ContainerFlag.Align_End,
        Justify = ContainerFlag.Align_Justify,

        [Obsolete]
        MASK = Start | Center | End | Justify,
    }

    [Flags]
    public enum ContainerFlags : ushort {
        /// <summary>
        /// If a child will not fit in available space, wrap it to a new run
        /// </summary>
        Wrap = ContainerFlag.Arrange_Wrap,

        /// <summary>
        /// Child rects will be clipped to our content box
        /// </summary>
        OverflowHidden = ContainerFlag.Boxes_Clip,
        /// <summary>
        /// Child boxes will not automatically grow beyond our content box even if they have large
        ///  siblings, but they will not be clipped unless OverflowHidden is set
        /// </summary>
        ConstrainGrowth = ContainerFlag.Boxes_Constrain_Growth,
        /// <summary>
        /// In grid mode, boxes will not have their height normalized
        /// </summary>
        GridNoNormalization = ContainerFlag.Boxes_Grid_NoNormalization,

        /// <summary>
        /// Will expand automatically beyond default width to hold children
        /// </summary>
        ExpandForContent_X = ContainerFlag.Size_ExpandForContent_X,
        /// <summary>
        /// Will expand automatically beyond default height to hold children
        /// </summary>
        ExpandForContent_Y = ContainerFlag.Size_ExpandForContent_Y,
        /// <summary>
        /// Will expand automatically beyond default size to hold children
        /// </summary>
        ExpandForContent = ExpandForContent_X | ExpandForContent_Y,

        /// <summary>
        /// Will expand horizontally to hold children and will not shrink even if container lacks space
        /// </summary>
        PreventCrush_X = ContainerFlag.Size_PreventCrush_X,
        /// <summary>
        /// Will expand vertically to hold children and will not shrink even if container lacks space
        /// </summary>
        PreventCrush_Y = ContainerFlag.Size_PreventCrush_Y,
        /// <summary>
        /// Will expand to hold children and will not shrink even if container lacks space
        /// </summary>
        PreventCrush = PreventCrush_X | PreventCrush_Y,

        [Obsolete]
        MASK = Wrap | OverflowHidden | ConstrainGrowth | ExpandForContent | PreventCrush | GridNoNormalization,
    }

    public enum BoxAnchorMode : ushort {
        Left = BoxFlag.Anchor_Left,
        Top = BoxFlag.Anchor_Top,
        Right = BoxFlag.Anchor_Right,
        Bottom = BoxFlag.Anchor_Bottom,
        FillRow = BoxFlag.Fill_Row,
        FillColumn = BoxFlag.Fill_Column,
        Fill = BoxFlag.Fill,
    }

    [Flags]
    public enum BoxFlags : ushort {
        Break = BoxFlag.Break,
        Stacked = BoxFlag.Stacked,
        Floating = BoxFlag.Floating,
        CollapseMargins = BoxFlag.CollapseMargins,

        MASK = Break | Stacked | Floating | CollapseMargins,
    }
}

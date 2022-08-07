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
        Layout_Row              = 0b1,
        /// <summary>
        /// Arrange child elements top-to-bottom
        /// </summary>
        Layout_Column           = 0b10,

        /// <summary>
        /// Align each run to the bottom/right of container
        /// </summary>
        Align_End               = 0b100,
        /// <summary>
        /// Align each run to the center of the container
        /// </summary>
        Align_Center            = 0b1000,
        /// <summary>
        /// Spread the child elements across each run by distributing whitespace
        /// </summary>
        Align_Justify           = 0b10000,

        /// <summary>
        /// If a child will not fit in available space, wrap it to a new run
        /// </summary>
        Arrange_Wrap            = 0b100000,

        /// <summary>
        /// Child rects will be clipped to our content box
        /// </summary>
        Boxes_Clip              = 0b1000000,
        /// <summary>
        /// Child boxes will not be constrained by us at all, for scrolling viewports
        /// If Clip is set, the children will have their outer rect clipped but not their content rect,
        /// and our content rect will also contain the full size of all our children
        /// </summary>
        Boxes_Overflow          = 0b10000000,

        /// <summary>
        /// Will expand automatically beyond default size to hold content
        /// </summary>
        Size_ExpandForContent_X = 0b100000000,
        /// <summary>
        /// Will expand automatically beyond default size to hold content
        /// </summary>
        Size_ExpandForContent_Y = 0b1000000000,
        Size_ExpandForContent   = Size_ExpandForContent_X | Size_ExpandForContent_Y,
        /// <summary>
        /// Will expand to hold content and will not shrink even if container lacks space
        /// </summary>
        Size_PreventCrush_X     = 0b10000000000 | Size_ExpandForContent_X,
        /// <summary>
        /// Will expand to hold content and will not shrink even if container lacks space
        /// </summary>
        Size_PreventCrush_Y     = 0b100000000000 | Size_ExpandForContent_Y,
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
        Anchor_Left   = 0b1,
        /// <summary>
        /// Instead of expanding to fill horizontal space, the box will be aligned to the right
        /// </summary>
        Anchor_Right  = 0b10,
        /// <summary>
        /// Instead of expanding to fill vertical space, the box will be aligned to the top
        /// </summary>
        Anchor_Top    = 0b100,
        /// <summary>
        /// Instead of expanding to fill vertical space, the box will be aligned to the bottom
        /// </summary>
        Anchor_Bottom = 0b1000,

        /// <summary>
        /// The box will expand to fill available horizontal space
        /// </summary>
        Fill_Row      = Anchor_Left | Anchor_Right,
        /// <summary>
        /// The box will expand to fill available vertical space
        /// </summary>
        Fill_Column   = Anchor_Top | Anchor_Bottom,
        /// <summary>
        /// The box will expand to fill all available space
        /// </summary>
        Fill          = Fill_Row | Fill_Column,

        /// <summary>
        /// Forces the box to occupy a new run
        /// </summary>
        Break         = 0b10000,
        /// <summary>
        /// The box will be laid out inside the entire container instead of occupying a run
        /// </summary>
        Stacked       = 0b100000,
        /// <summary>
        /// The box will be laid out inside the entire container and will also not influence the container's content size
        /// </summary>
        Floating      = 0b1000000 | Stacked,

        // No anchor = centered
        DEFAULT       = Fill_Row 
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.Flags {
    public struct AnchorFlags {
        public bool Left, Top, Right, Bottom;
        /// <summary>
        /// Equivalent to Fill.All
        /// </summary>
        public bool All;

        public static implicit operator ControlFlags (AnchorFlags af) {
            var none = default(ControlFlags);
            return
                (af.Left ? ControlFlags.Layout_Anchor_Left : none) |
                (af.Top ? ControlFlags.Layout_Anchor_Top : none) |
                (af.Right ? ControlFlags.Layout_Anchor_Right : none) |
                (af.Bottom ? ControlFlags.Layout_Anchor_Bottom : none) |
                (af.All ? ControlFlags.Layout_Fill : none);
        }
    }

    public struct FillFlags {
        public bool Row, Column;
        /// <summary>
        /// Equivalent to Fill.Row + Fill.Column
        /// </summary>
        public bool All;

        public static implicit operator FillFlags (bool all) {
            return new FillFlags {
                All = all
            };
        }

        public static implicit operator ControlFlags (FillFlags ff) {
            var none = default(ControlFlags);
            return
                (ff.Row ? ControlFlags.Layout_Fill_Row : none) |
                (ff.Column ? ControlFlags.Layout_Fill_Column : none) |
                (ff.All ? ControlFlags.Layout_Fill : none);
        }
    }

    public struct LayoutFlags {
        public Vector2 FloatingPosition;

        public AnchorFlags Anchor;
        /// <summary>
        /// If set, will override anchor
        /// </summary>
        public FillFlags Fill;
        /// <summary>
        /// If set inside a wrapped container, will force this control to wrap
        /// </summary>
        public bool ForceBreak;
        /// <summary>
        /// If set, this control will be laid out as if its parent has no other children
        /// </summary>
        public bool Floating;
        /// <summary>
        /// If set, this control does not contribute to its siblings' layout.
        /// </summary>
        public bool Stacked;

        public ControlFlags Mask =>
            (
                (ControlFlags)Anchor != default(ControlFlags)
                    ? ~ControlFlags.Layout_Fill
                    : ~default(ControlFlags)
            ) &
            (
                (ControlFlags)Fill != default(ControlFlags)
                    ? ~ControlFlags.Layout_Fill
                    : ~default(ControlFlags)
            );

        public static implicit operator ControlFlags? (LayoutFlags lf) {
            return (lf.Mask != ~default(ControlFlags))
                ? lf
                : (ControlFlags?)null;
        }

        public static implicit operator ControlFlags (LayoutFlags lf) {
            var none = default(ControlFlags);
            return
                (ControlFlags)lf.Anchor |
                (ControlFlags)lf.Fill |
                (lf.ForceBreak ? ControlFlags.Layout_ForceBreak : none) |
                (lf.Floating ? ControlFlags.Layout_Floating : none) |
                (lf.Stacked ? ControlFlags.Layout_Stacked : none);
        }
    }

    public enum ChildArrangement : uint {
        Row = ControlFlags.Container_Row,
        Column = ControlFlags.Container_Column,
    }

    public enum ChildAlignment : uint {
        Start = ControlFlags.Container_Align_Start,
        Middle = ControlFlags.Container_Align_Middle,
        End = ControlFlags.Container_Align_End,
        /// <summary>
        /// Not compatible with Wrap
        /// </summary>
        Justify = ControlFlags.Container_Align_Justify
    }

    public struct ContainerFlags {
        public ChildArrangement Arrangement;
        public ChildAlignment Alignment;
        /// <summary>
        /// Attempts to automatically wrap children to the next row/column if they don't fit
        /// This also enables ForceBreak
        /// </summary>
        public bool Wrap;
        /// <summary>
        /// Prevents children from being larger than this container
        /// </summary>
        public bool ConstrainSize;
        /// <summary>
        /// Prevents children from being expanded to fit this container
        /// </summary>
        public bool NoExpansion, NoExpansionX, NoExpansionY;
        /// <summary>
        /// Prevents children from being shrunk to fit this container
        /// </summary>
        public bool PreventCrush, PreventCrushX, PreventCrushY;

        public bool Start {
            get => (Alignment == ChildAlignment.Start);
            set => Alignment = value ? ChildAlignment.Start : ChildAlignment.End;
        }

        public bool End {
            get => (Alignment == ChildAlignment.End);
            set => Alignment = value ? ChildAlignment.End : ChildAlignment.Start;
        }

        public bool Middle {
            get => (Alignment == ChildAlignment.Middle);
            set => Alignment = value ? ChildAlignment.Middle : ChildAlignment.Start;
        }

        public bool Row {
            get => (Arrangement == ChildArrangement.Row);
            set => Arrangement = value ? ChildArrangement.Row : ChildArrangement.Column;
        }

        public bool Column {
            get => (Arrangement == ChildArrangement.Column);
            set => Arrangement = value ? ChildArrangement.Column : ChildArrangement.Row;
        }

        public ControlFlags Mask =>
            (ControlFlags)(uint)(
                Arrangement != default(ChildArrangement)
                    ? ~(ChildArrangement.Row | ChildArrangement.Column)
                    : ~default(ChildArrangement)
            ) &
            (ControlFlags)(uint)(
                Alignment != default(ChildAlignment)
                    ? ~(ChildAlignment.Start | ChildAlignment.Middle | ChildAlignment.End | ChildAlignment.Justify)
                    : ~default(ChildAlignment)
            );

        public static implicit operator ControlFlags? (ContainerFlags lf) {
            return (lf.Mask != ~default(ControlFlags))
                ? lf
                : (ControlFlags?)null;
        }

        public static implicit operator ControlFlags (ContainerFlags cf) {
            var none = default(ControlFlags);
            return
                (ControlFlags)(uint)cf.Arrangement |
                (ControlFlags)(uint)cf.Alignment |
                (cf.Wrap ? ControlFlags.Container_Wrap : none) |
                (cf.ConstrainSize ? ControlFlags.Container_Constrain_Size : none) |
                (cf.NoExpansionX ? ControlFlags.Container_No_Expansion_X : none) |
                (cf.NoExpansionY ? ControlFlags.Container_No_Expansion_Y : none) |
                (cf.NoExpansion ? ControlFlags.Container_No_Expansion : none) |
                (cf.PreventCrushX ? ControlFlags.Container_Prevent_Crush_X : none) |
                (cf.PreventCrushY ? ControlFlags.Container_Prevent_Crush_Y : none) |
                (cf.PreventCrush ? ControlFlags.Container_Prevent_Crush : none);
        }
    }
}

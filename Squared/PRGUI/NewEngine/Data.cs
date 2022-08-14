using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using ControlKey = Squared.PRGUI.Layout.ControlKey;
using ControlFlags = Squared.PRGUI.Layout.ControlFlags;
using LayoutDimensions = Squared.PRGUI.Layout.LayoutDimensions;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine.Enums;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if DEBUG
using System.Xml.Serialization;
using Unserialized = System.Xml.Serialization.XmlIgnoreAttribute;
#else
using Unserialized = Squared.PRGUI.NewEngine.DummyAttribute;
#endif

namespace Squared.PRGUI.NewEngine {
    internal class DummyAttribute : Attribute {
    }

    public static class DataExtensions {
        public static ref ControlDimension Size (this ref BoxRecord record, LayoutDimensions dimension) {
            if (dimension == LayoutDimensions.X)
                return ref record.Width;
            else
                return ref record.Height;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasFlag (this ContainerFlag flags, ContainerFlag flag) {
            var masked = (uint)(flags & flag);
            return masked == (int)flag;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool HasFlag (this BoxFlag flags, BoxFlag flag) {
            var masked = (uint)(flags & flag);
            return masked == (int)flag;
        }
    }

    internal struct ControlKeyDefaultInvalid {
        internal int IndexPlusOne;

        public ControlKey Key {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new ControlKey(IndexPlusOne - 1);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => IndexPlusOne = value.ID + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator int (ControlKeyDefaultInvalid key) {
            return key.IndexPlusOne - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ControlKeyDefaultInvalid (int index) {
            return new ControlKeyDefaultInvalid {
                IndexPlusOne = index + 1
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator ControlKeyDefaultInvalid (ControlKey value) {
            return new ControlKeyDefaultInvalid {
                IndexPlusOne = value.ID + 1
            };
        }

        public bool IsInvalid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (IndexPlusOne <= 0);
        }

        public override string ToString () {
            return IndexPlusOne <= 0
                ? "<invalid>"
                : (IndexPlusOne - 1).ToString();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ControlConfiguration {
        public static readonly ControlConfiguration Default = new ControlConfiguration {
            _ContainerFlags = ContainerFlag.DEFAULT,
            _BoxFlags = BoxFlag.DEFAULT,
        };

        [Unserialized]
        internal ContainerFlag _ContainerFlags;
        [Unserialized]
        internal BoxFlag _BoxFlags;

        public ControlConfiguration (ControlFlags value) {
            this = default;

            {
                bool aend = value.IsFlagged(ControlFlags.Container_Align_End),
                    astart = value.IsFlagged(ControlFlags.Container_Align_Start),
                    ajustify = value.IsFlagged(ControlFlags.Container_Align_Justify),
                    acenter = !aend && !astart && !ajustify;

                if ((aend && astart) || (aend && ajustify) || (astart && ajustify))
                    throw new ArgumentException("Invalid child alignment mode");

                if (aend)
                    _ContainerFlags |= ContainerFlag.Align_End;
                else if (acenter)
                    _ContainerFlags |= ContainerFlag.Align_Center;
                else if (ajustify)
                    _ContainerFlags |= ContainerFlag.Align_Justify;
                else if (astart)
                    ; // Default
            }

            {
                bool aleft = value.IsFlagged(ControlFlags.Layout_Anchor_Left),
                    atop = value.IsFlagged(ControlFlags.Layout_Anchor_Top),
                    aright = value.IsFlagged(ControlFlags.Layout_Anchor_Right),
                    abottom = value.IsFlagged(ControlFlags.Layout_Anchor_Bottom);
                if (aleft)
                    _BoxFlags |= BoxFlag.Anchor_Left;
                if (aright)
                    _BoxFlags |= BoxFlag.Anchor_Right;
                if (atop)
                    _BoxFlags |= BoxFlag.Anchor_Top;
                if (abottom)
                    _BoxFlags |= BoxFlag.Anchor_Bottom;
            }

            {
                // Break_Auto already sets this
                // if (value.IsFlagged(ControlFlags.Container_Break_Allow))
                if (value.IsFlagged(ControlFlags.Container_Break_Auto))
                    _ContainerFlags |= ContainerFlag.Arrange_Wrap;
                if (value.IsFlagged(ControlFlags.Container_Clip_Children))
                    _ContainerFlags |= ContainerFlag.Boxes_Clip;
                if (!value.IsFlagged(ControlFlags.Container_Constrain_Growth) &&
                    !value.IsFlagged(ControlFlags.Container_Constrain_Size))
                    _ContainerFlags |= ContainerFlag.Boxes_Overflow;
                if (!value.IsFlagged(ControlFlags.Container_No_Expansion_X))
                    _ContainerFlags |= ContainerFlag.Size_ExpandForContent_X;
                if (!value.IsFlagged(ControlFlags.Container_No_Expansion_Y))
                    _ContainerFlags |= ContainerFlag.Size_ExpandForContent_Y;
                if (value.IsFlagged(ControlFlags.Container_Prevent_Crush_X))
                    _ContainerFlags |= ContainerFlag.Size_PreventCrush_X;
                if (value.IsFlagged(ControlFlags.Container_Prevent_Crush_Y))
                    _ContainerFlags |= ContainerFlag.Size_PreventCrush_Y;
            }

            if (value.IsFlagged(ControlFlags.Container_Column))
                _ContainerFlags |= ContainerFlag.Layout_Column;

            if (value.IsFlagged(ControlFlags.Layout_Floating))
                _BoxFlags |= BoxFlag.Floating;
            if (value.IsFlagged(ControlFlags.Layout_Stacked))
                _BoxFlags |= BoxFlag.Stacked;
            if (value.IsFlagged(ControlFlags.Layout_ForceBreak))
                _BoxFlags |= BoxFlag.Break;
        }

        [Unserialized]
        public uint AllFlags {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ((uint)_ContainerFlags << 0) | ((uint)_BoxFlags << 16);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                _ContainerFlags = (ContainerFlag)(value & 0xFFFFu);
                _BoxFlags = (BoxFlag)((value >> 16) & 0xFFFFu);
            }
        }

        public ChildDirection ChildDirection {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ChildDirection)_ContainerFlags & ChildDirection.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _ContainerFlags = (ContainerFlag)
                (((ChildDirection)_ContainerFlags & ~ChildDirection.MASK) | value);
        }

        public ChildAlignment ChildAlign {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ChildAlignment)_ContainerFlags & ChildAlignment.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _ContainerFlags = (ContainerFlag)
                (((ChildAlignment)_ContainerFlags & ~ChildAlignment.MASK) | value);
        }

        public ContainerFlags ChildFlags {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (ContainerFlags)_ContainerFlags & ContainerFlags.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _ContainerFlags = (ContainerFlag)
                (((ContainerFlags)_ContainerFlags & ~ContainerFlags.MASK) | value);
        }

        public BoxAnchorMode Anchor {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BoxAnchorMode)_BoxFlags & BoxAnchorMode.Fill;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _BoxFlags = (BoxFlag)
                (((BoxAnchorMode)_BoxFlags & ~BoxAnchorMode.Fill) | value);
        }

        public BoxFlags Flags {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BoxFlags)_BoxFlags & BoxFlags.MASK;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _BoxFlags = (BoxFlag)
                (((BoxFlags)_BoxFlags & ~BoxFlags.MASK) | value);
        }

        // TODO: Consider making these public and add setters
        internal bool ForceBreak => (_BoxFlags & BoxFlag.Break) != default;
        internal bool ConstrainChildren => (_ContainerFlags & ContainerFlag.Boxes_Overflow) == default;
        internal bool IsVertical => (_ContainerFlags & ContainerFlag.Layout_Column) != default;
        internal bool IsStackedOrFloating => (_BoxFlags & BoxFlag.Stacked) != default;
        internal bool IsWrap => (_ContainerFlags & ContainerFlag.Arrange_Wrap) != default;
        internal bool FillRow => (_BoxFlags & BoxFlag.Fill_Row) == BoxFlag.Fill_Row;
        internal bool FillColumn => (_BoxFlags & BoxFlag.Fill_Column) == BoxFlag.Fill_Column;

        public bool Equals (ControlConfiguration rhs) =>
            (_BoxFlags == rhs._BoxFlags) &&
            (_ContainerFlags == rhs._ContainerFlags);

        public override bool Equals (object obj) {
            if (obj is ControlConfiguration cc)
                return Equals(cc);
            else
                return false;
        }

        public override string ToString () {
            return $"{_BoxFlags} {_ContainerFlags}";
        }

        internal void GetRunAlignmentF (out float xAlign, out float yAlign) {
            // FIXME: Default for secondary axis should be null instead of 0 i think? Or 0.5?
            switch (ChildAlign) {
                default:
                case ChildAlignment.Start:
                    xAlign = 0;
                    yAlign = 0;
                    return;
                case ChildAlignment.End:
                    xAlign = IsVertical ? 0 : 1;
                    yAlign = IsVertical ? 1 : 0;
                    return;
                case ChildAlignment.Center:
                    xAlign = IsVertical ? 0 : 0.5f;
                    yAlign = IsVertical ? 0.5f : 0;
                    return;
                case ChildAlignment.Justify:
                    throw new NotImplementedException();
                    return;
            }
        }
    }

    /// <summary>
    /// Represents a box being laid out by the layout engine
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoxRecord {
        // Managed by the layout engine
        // TODO: Use a custom dense backing store and no setters
        internal ControlKeyDefaultInvalid _Key, _Parent, _FirstChild, 
            _LastChild, _PreviousSibling, _NextSibling;

        public ControlKey Key {
            get => _Key.Key;
#if DEBUG
            set => _Key.Key = value;
#endif
        }
        public ControlKey Parent {
            get => _Parent.Key;
#if DEBUG
            set => _Parent.Key = value;
#endif
        }
        public ControlKey FirstChild {
            get => _FirstChild.Key;
#if DEBUG
            set => _FirstChild.Key = value;
#endif
        }
        public ControlKey LastChild {
            get => _LastChild.Key;
#if DEBUG
            set => _LastChild.Key = value;
#endif
        }
        public ControlKey PreviousSibling {
            get => _PreviousSibling.Key;
#if DEBUG
            set => _PreviousSibling.Key = value;
#endif
        }
        public ControlKey NextSibling {
            get => _NextSibling.Key;
#if DEBUG
            set => _NextSibling.Key = value;
#endif
        }

        public ControlConfiguration Config;

        public Margins Margins, Padding;
        public ControlDimension Width, Height;
        // TODO: Add a scroll offset value so that Control doesn't need a display offset anymore
        public Vector2 FloatingPosition;
        public Layout.LayoutTags Tag;

        internal ControlFlags _OldFlags;
        internal ControlFlags OldFlags {
            get => _OldFlags;
            set {
                _OldFlags = value;
                Config = new ControlConfiguration(value);
            }
        }

        // TODO: When controls are overflowing/being clipped, we want to clip the lowest priority
        //  controls first in order to spare the high priority ones. This mostly is relevant for
        //  toolbars and I haven't needed it yet
        // int Priority;

        public Vector2 FixedSize {
            set {
                Width = value.X;
                Height = value.Y;
            }
        }

        public bool IsValid => !Key.IsInvalid;

        public override string ToString () {
            return $"#{Key.ID} {Tag} {Config}";
        }
    }

    /// <summary>
    /// Represents the state of the layout engine for a given box
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BoxLayoutResult {
        public Layout.LayoutTags Tag;

        // TODO: Optimize this out
        internal int Version;
        /// <summary>
        /// The index of the first run contained by this control, if any.
        /// </summary>
        internal int FirstRunIndex;
#if DEBUG
        internal int Depth;
        internal bool Break;
#endif
        /// <summary>
        /// Must be true before wrapping can occur
        /// </summary>
        internal bool SizeSetByParent;
        // FIXME: Remove all these
        internal bool Pass1Ready, Pass1Processed, Pass2Processed, Pass2bProcessed;

        /// <summary>
        /// The display/layout rectangle of the control.
        /// </summary>
        public RectF Rect;
        /// <summary>
        /// The location of the control's content area or actual content.
        /// This will be larger than Rect in scenarios where the control was clipped by its parent,
        ///  in which case you want to constrain it to the parent rect. This will prevent
        ///  clipping from making margins move in too far.
        /// </summary>
        public RectF ContentRect;
        /// <summary>
        /// Records the size that this control's parent attempted to set before constraints took
        ///  effect. You can use this to influence control auto-size behavior.
        /// </summary>
        public Vector2 AvailableSpace;
        /// <summary>
        /// The control's position within the current run (X for row layout, Y for column layout)
        /// </summary>
        internal float PositionInRun;

        public override string ToString () {
#if DEBUG
            var padding = new string(' ', Depth * 2);
            var br = Break;
#else
            var padding = string.Empty;
            var br = false;
#endif
            return $"{padding}{Tag} size {Rect.Size} {(br ? "break" : "")}";
        }
    }

    /// <summary>
    /// Represents the state of the layout engine for a given run
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct LayoutRun {
        public ControlKeyDefaultInvalid First, Last;
        public int FlowCount, ExpandCountX, ExpandCountY, NextRunIndex;
        public float TotalWidth, TotalHeight, MaxOuterWidth, MaxOuterHeight;

        public override string ToString () {
            if (First.IsInvalid || Last.IsInvalid)
                return "<invalid>";

            var tail = (NextRunIndex < 0) ? " end" : " ...";
            return $"{First}..{Last} flow={FlowCount} expandX={ExpandCountX} expandY={ExpandCountY}{tail}";
        }
    }
}

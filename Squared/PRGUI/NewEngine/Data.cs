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
        internal bool FillRow => (_BoxFlags & BoxFlag.Fill_Row) == BoxFlag.Fill_Row;
        internal bool FillColumn => (_BoxFlags & BoxFlag.Fill_Column) == BoxFlag.Fill_Column;
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

        internal ControlFlags OldFlags;

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
            return $"#{Key.ID} {Tag} {OldFlags}";
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
        internal bool Pass1Complete;

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
        // TODO: Store the amount of space we were offered by our parent before constraints took
        //  effect. This will be useful for StaticText autosize/wrap implementations
        // public Vector2 MaximumSize;
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
        public ControlFlags? XAnchor, YAnchor;

        public override string ToString () {
            if (First.IsInvalid || Last.IsInvalid)
                return "<invalid>";

            var tail = (NextRunIndex < 0) ? " end" : " ...";
            return $"{First}..{Last} flow={FlowCount} expandX={ExpandCountX} expandY={ExpandCountY}{tail}";
        }

        public void GetAlignmentF (ControlFlags containerFlags, out float x, out float y) {
            x = y = 0;

            // FIXME: Not sure this is working

            if ((XAnchor ?? default) != default) {
                XAnchor.Value.GetAlignmentF(out x, out _);
            } else if (PRGUIExtensions.HasFlag(containerFlags, ControlFlags.Container_Row))
                x = containerFlags.GetContainerAlignmentF();

            if ((YAnchor ?? default) != default) {
                YAnchor.Value.GetAlignmentF(out x, out y);
            } else if (PRGUIExtensions.HasFlag(containerFlags, ControlFlags.Container_Column))
                y = containerFlags.GetContainerAlignmentF();
        }
    }
}

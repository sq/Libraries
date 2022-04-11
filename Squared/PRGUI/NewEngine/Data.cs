using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using ControlKey = Squared.PRGUI.Layout.ControlKey;

namespace Squared.PRGUI.NewEngine {
    /// <summary>
    /// Represents a box being laid out by the layout engine
    /// </summary>
    public struct ControlRecord {
        internal struct KeyDefaultInvalid {
            public int IndexPlusOne;

            public ControlKey Key => new ControlKey(IndexPlusOne - 1);

            public static implicit operator KeyDefaultInvalid (int index) {
                return new KeyDefaultInvalid {
                    IndexPlusOne = index + 1
                };
            }

            public static implicit operator KeyDefaultInvalid (ControlKey value) {
                return new KeyDefaultInvalid {
                    IndexPlusOne = value.ID + 1
                };
            }

            public bool IsInvalid => (IndexPlusOne <= 0);

            public override string ToString () {
                return (IndexPlusOne - 1).ToString();
            }
        }

        // Managed by the layout engine
        // TODO: Use a custom dense backing store and no setters
        internal KeyDefaultInvalid _Key, _Parent, _FirstChild, 
            _LastChild, _PreviousSibling, _NextSibling;
        public ControlKey Key => _Key.Key;
        public ControlKey Parent => _Parent.Key;
        public ControlKey FirstChild => _FirstChild.Key;
        public ControlKey LastChild => _LastChild.Key;
        public ControlKey PreviousSibling => _PreviousSibling.Key;
        public ControlKey NextSibling => _NextSibling.Key;

        public Layout.ControlFlags Flags;
        public Margins Margins, Padding;
        public ControlDimension Width, Height;
        public Vector2 FloatingPosition;
        public Layout.LayoutTags Tag;

        public Layout.ControlFlags LayoutFlags {
            get => Flags & Layout.ControlFlagMask.Layout;
            set {
                Flags = (Flags & ~Layout.ControlFlagMask.Layout) | value;
            }
        }

        public Layout.ControlFlags ContainerFlags {
            get => Flags & Layout.ControlFlagMask.Container;
            set {
                Flags = (Flags & ~Layout.ControlFlagMask.Container) | value;
            }
        }

        public Vector2 FixedSize {
            set {
                Width = value.X;
                Height = value.Y;
            }
        }

        public bool IsValid => !Key.IsInvalid;

        public override string ToString () {
            return $"#{Key.ID} {Tag}";
        }
    }

    public struct ControlLayoutResult {
        internal int Version;

        public RectF Rect, ContentRect;
        public Vector2 ContentSize;
        public Layout.LayoutTags Tag;
    }
}

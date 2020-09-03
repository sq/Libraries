using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.PRGUI {
    public partial class Context : IDisposable {
        public Context () {
            Initialize();
        }

        public void Update () {
            if (!Update(Root))
                InvalidState();
        }

        private unsafe bool Update (ControlKey key) {
            if (key.IsInvalid)
                return false;

            CalcSize(key, Dimensions.X);
            Arrange (key, Dimensions.X);
            CalcSize(key, Dimensions.Y);
            Arrange (key, Dimensions.Y);

            return true;
        }

        public ControlKey CreateItem () {
            var key = new ControlKey(Count + 1);
            var newData = new ControlLayout(key);
            var newBox = default(Bounds);

            Layout.Add(ref newData);
            Boxes.Add(ref newBox);

            return key;
        }

        private unsafe void Append (ControlLayout * pEarlier, ControlLayout * pLater) {
            pLater->NextSibling = pEarlier->NextSibling;
            pLater->Flags = pLater->Flags | ControlFlags.Inserted;
            pEarlier->NextSibling = pLater->Key;
        }

        private unsafe void ClearItemBreak (ControlLayout * data) {
            data->Flags = data->Flags & ~ControlFlags.Child_Break;
        }

        public unsafe ControlKey LastChild (ControlKey key) {
            if (key.IsInvalid)
                return ControlKey.Invalid;

            var parent = LayoutPtr(key);
            if (parent->FirstChild.IsInvalid)
                return ControlKey.Invalid;

            var child = LayoutPtr(parent->FirstChild);
            var result = parent->FirstChild;
            for (;;) {
                var next = child->NextSibling;
                if (next.IsInvalid)
                    break;
                result = next;
                child = LayoutPtr(next);
            }
            return result;
        }

        public unsafe void Append (ControlKey earlier, ControlKey later) {
            AssertNotRoot(later);
            AssertNotEqual(earlier, later);

            var pEarlier = LayoutPtr(earlier);
            var pLater = LayoutPtr(later);
            Append(pEarlier, pLater);
        }

        public unsafe void Insert (ControlKey parent, ControlKey child) {
            AssertNotRoot(child);
            AssertNotEqual(parent, child);

            var pParent = LayoutPtr(parent);
            var pChild = LayoutPtr(child);
            Assert(!pChild->Flags.HasFlag(ControlFlags.Inserted));

            if (pParent->FirstChild.IsInvalid) {
                pParent->FirstChild = child;
                pChild->Flags |= ControlFlags.Inserted;
            } else {
                var next = pParent->FirstChild;
                var pNext = LayoutPtr(next);
                for (;;) {
                    next = pNext->NextSibling;
                    if (next.IsInvalid)
                        break;
                    pNext = LayoutPtr(next);
                }
                Append(pNext, pChild);
            }
        }

        public unsafe void Push (ControlKey parent, ControlKey newChild) {
            AssertNotRoot(newChild);
            AssertNotEqual(parent, newChild);
            var pParent = LayoutPtr(parent);
            var oldChild = pParent->FirstChild;
            var pChild = LayoutPtr(newChild);
            Assert(!pChild->Flags.HasFlag(ControlFlags.Inserted));
            pParent->FirstChild = newChild;
            pChild->Flags |= ControlFlags.Inserted;
            pChild->NextSibling = oldChild;
        }

        public unsafe Vector2 GetSize (ControlKey key) {
            var pItem = LayoutPtr(key);
            return pItem->Size;
        }

        public unsafe void GetSizeXY (ControlKey key, out float x, out float y) {
            var pItem = LayoutPtr(key);
            x = pItem->Size.X;
            y = pItem->Size.Y;
        }

        public unsafe void SetSize (ControlKey key, Vector2 size) {
            var pItem = LayoutPtr(key);
            pItem->Size = size;

            var flags = pItem->Flags;
            if (size.X <= 0)
                flags &= ~ControlFlags.HFixed;
            else
                flags |= ControlFlags.HFixed;

            if (size.Y <= 0)
                flags &= ~ControlFlags.VFixed;
            else
                flags |= ControlFlags.VFixed;
            pItem->Flags = flags;
        }

        public void SetSizeXY (ControlKey key, float width, float height) {
            SetSize(key, new Vector2(width, height));
        }

        public unsafe void SetLayoutFlags (ControlKey key, ControlFlags flags) {
            AssertMasked(flags, ControlFlagMask.Layout, nameof(ControlFlagMask.Layout));
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlagMask.Layout) | flags;
        }

        public unsafe void SetBoxFlags (ControlKey key, ControlFlags flags) {
            AssertMasked(flags, ControlFlagMask.Box, nameof(ControlFlagMask.Box));
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlagMask.Box) | flags;
        }

        public unsafe void SetMargins (ControlKey key, Vector4 ltrb) {
            var pItem = LayoutPtr(key);
            pItem->Margins = ltrb;
        }

        public unsafe void SetMarginsLTRB (ControlKey key, float left, float top, float right, float bottom) {
            var pItem = LayoutPtr(key);
            pItem->Margins = new Vector4(
                left, top, right, bottom
            );
        }

        public unsafe Vector4 GetMargins (ControlKey key) {
            var pItem = LayoutPtr(key);
            return pItem->Margins;
        }

        private unsafe float CalcWrappedOverlaySize (ControlKey key, Dimensions dim) {
            throw new NotImplementedException();
        }

        private unsafe float CalcWrappedStackedSize (ControlKey key, Dimensions dim) {
            throw new NotImplementedException();
        }

        public unsafe float CalcSize (ControlKey key, Dimensions dim) {
            throw new NotImplementedException();
        }

        private unsafe void ArrangeStacked (ControlKey key, Dimensions dim, bool wrap) {
            throw new NotImplementedException();
        }

        private unsafe void ArrangeOverlay (ControlKey key, Dimensions dim) {
            throw new NotImplementedException();
        }

        private unsafe void ArrangeOverlaySqueezedRange (Dimensions dim, ControlKey startItem, ControlKey endItem, float offset, float space) {
            throw new NotImplementedException();
        }

        private unsafe void ArrangeWrappedOverlaySqueezed (ControlKey item, Dimensions dim) {
            throw new NotImplementedException();
        }

        public unsafe void Arrange (ControlKey item, Dimensions dim) {
            throw new NotImplementedException();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.PRGUI {
    public partial class LayoutContext : IDisposable {
        public LayoutContext () {
            Initialize();
        }

        private void InvalidState () {
            throw new Exception("Invalid internal state");
        }

        private void Assert (bool b, string message = null) {
            if (b)
                return;

            throw new Exception(
                message != null
                    ? $"Assertion failed: {message}"
                    : "Assertion failed"
                );
        }

        private void AssertNotRoot (ControlKey key) {
            if (key.IsInvalid)
                throw new Exception("Invalid key");
            else if (key == Root)
                throw new Exception("Key must not be the root");
        }

        private void AssertNotEqual (ControlKey lhs, ControlKey rhs) {
            if (lhs == rhs)
                throw new Exception("Keys must not be equal");
        }

        private void AssertMasked (ControlFlags flags, ControlFlags mask, string maskName) {
            if ((flags & mask) != flags)
                throw new Exception("Flags must be compatible with mask " + maskName);
        }

        public void Update () {
            if (!Update(Root))
                InvalidState();
        }

        private unsafe bool Update (ControlKey key) {
            if (key.IsInvalid)
                return false;

            var pItem = LayoutPtr(key);
            CalcSize(pItem, Dimensions.X);
            Arrange (pItem, Dimensions.X);
            CalcSize(pItem, Dimensions.Y);
            Arrange (pItem, Dimensions.Y);

            return true;
        }

        public ControlKey CreateItem () {
            var key = new ControlKey(Count);
            var newData = new ControlLayout(key);
            var newBox = default(RectF);

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
            data->Flags = data->Flags & ~ControlFlags.Layout_Break;
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
            Assert(!pChild->Flags.IsFlagged(ControlFlags.Inserted), "is not inserted");

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
            Assert(!pChild->Flags.IsFlagged(ControlFlags.Inserted));
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

        public void SetSizeXY (ControlKey key, float width = 0, float height = 0) {
            SetSize(key, new Vector2(width, height));
        }

        public unsafe void SetContainerFlags (ControlKey key, ControlFlags flags) {
            AssertMasked(flags, ControlFlagMask.Container, nameof(ControlFlagMask.Container));
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlagMask.Container) | flags;
        }

        public unsafe void SetLayoutFlags (ControlKey key, ControlFlags flags) {
            AssertMasked(flags, ControlFlagMask.Layout, nameof(ControlFlagMask.Layout));
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlagMask.Layout) | flags;
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

        private unsafe float CalcOverlaySize (ControlLayout * pItem, Dimensions dim) {
            float result = 0;
            var child = pItem->FirstChild;
            while (!child.IsInvalid) {
                var pChild = LayoutPtr(child);
                var rect = GetRect(child);
                // FIXME: Is this a bug?
                var childSize = rect[(uint)dim] + rect[(uint)dim + 2] + pChild->Margins.GetElement((uint)dim + 2);
                result = Math.Max(result, childSize);
                child = pChild->NextSibling;
            }
            return result;
        }

        private unsafe float CalcStackedSize (ControlLayout * pItem, Dimensions dim) {
            float result = 0;
            int idim = (int)dim, wdim = idim + 2;
            var child = pItem->FirstChild;
            while (!child.IsInvalid) {
                var pChild = LayoutPtr(child);
                var rect = GetRect(child);
                result += rect[idim] + rect[wdim] + pChild->Margins.GetElement(wdim);
                child = pChild->NextSibling;
            }
            return result;
        }

        private unsafe float CalcWrappedSizeImpl (ControlLayout * pItem, Dimensions dim, bool overlaid) {
            int idim = (int)dim, wdim = idim + 2;
            float needSize = 0, needSize2 = 0;
            var child = pItem->FirstChild;
            while (!child.IsInvalid) {
                var pChild = LayoutPtr(child);
                var rect = GetRect(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Break)) {
                    if (overlaid)
                        needSize2 += needSize;
                    else
                        needSize2 = Math.Max(needSize2, needSize);

                    needSize = 0;
                }

                var childSize = rect[idim] + rect[wdim] + pChild->Margins.GetElement(wdim);
                if (overlaid)
                    needSize = Math.Max(needSize, childSize);
                else
                    needSize += childSize;

                child = pChild->NextSibling;
            }

            if (overlaid)
                return needSize + needSize2;
            else
                return Math.Max(needSize, needSize2);
        }

        private unsafe float CalcWrappedOverlaidSize (ControlLayout * pItem, Dimensions dim) {
            return CalcWrappedSizeImpl(pItem, dim, true);
        }

        private unsafe float CalcWrappedStackedSize (ControlLayout * pItem, Dimensions dim) {
            return CalcWrappedSizeImpl(pItem, dim, false);
        }

        private unsafe void CalcSize (ControlLayout * pItem, Dimensions dim) {
            var child = pItem->FirstChild;
            while (!child.IsInvalid) {
                // NOTE: Potentially unbounded recursion
                var pChild = LayoutPtr(child);
                CalcSize(pChild, dim);
                child = pChild->NextSibling;
            }

            var pRect = BoxPtr(pItem->Key);
            var idim = (int)dim;

            // Start by setting size to top/left margin
            (*pRect)[idim] = pItem->Margins.GetElement(idim);

            if (pItem->Size.GetElement(idim) > 0) {
                (*pRect)[idim + 2] = pItem->Size.GetElement(idim);
                return;
            }

            float result = 0;
            switch (pItem->Flags & ControlFlagMask.BoxModel) {
                case ControlFlags.Container_Column | ControlFlags.Container_Wrap:
                    if (dim == Dimensions.Y)
                        result = CalcStackedSize(pItem, dim);
                    else
                        result = CalcOverlaySize(pItem, dim);
                    break;
                case ControlFlags.Container_Row | ControlFlags.Container_Wrap:
                    if (dim == Dimensions.X)
                        result = CalcWrappedStackedSize(pItem, dim);
                    else
                        result = CalcWrappedOverlaidSize(pItem, dim);
                    break;
                case ControlFlags.Container_Row:
                case ControlFlags.Container_Column:
                    if (((uint)pItem->Flags & 1) == (uint)dim)
                        result = CalcStackedSize(pItem, dim);
                    else
                        result = CalcOverlaySize(pItem, dim);
                    break;
                default:
                    result = CalcOverlaySize(pItem, dim);
                    break;
            }

            (*pRect)[idim] = result;
        }

        private unsafe void ArrangeStacked (ControlLayout * pItem, Dimensions dim, bool wrap) {
            var itemFlags = pItem->Flags;
            var rect = GetRect(pItem->Key);
            int idim = (int)dim, wdim = idim + 2;
            float space = rect[wdim], max_x2 = rect[idim] + space;

            var startChild = pItem->FirstChild;
            while (!startChild.IsInvalid) {
                float used = 0;
                uint count = 0, squeezedCount = 0, total = 0;
                bool hardBreak = false;

                ControlKey child = startChild, endChild = ControlKey.Invalid;
                while (!child.IsInvalid) {
                    var pChild = LayoutPtr(child);
                    var childFlags = pChild->Flags;
                    var flags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Layout) >> idim);
                    var fFlags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Fixed) >> idim);
                    var childMargins = pChild->Margins;
                    var childRect = GetRect(child);
                    var extend = used;

                    if (flags.IsFlagged(ControlFlags.Layout_Fill_Row)) {
                        ++count;
                        extend += childRect[idim] + childMargins.GetElement(wdim);
                    } else {
                        if (!fFlags.IsFlagged(ControlFlags.HFixed))
                            ++squeezedCount;
                        extend += childRect[idim] + childRect[wdim] + childMargins.GetElement(wdim);
                    }

                    if (
                        wrap &&
                        (total != 0) && (
                            (extend > space) ||
                            childFlags.IsFlagged(ControlFlags.Layout_Break)
                        )
                    ) {
                        endChild = child;
                        hardBreak = childFlags.IsFlagged(ControlFlags.Layout_Break);
                        pChild->Flags |= ControlFlags.Layout_Break;
                        break;
                    } else {
                        used = extend;
                        child = pChild->NextSibling;
                    }

                    ++total;
                }

                var extraSpace = space - used;
                float filler = 0, spacer = 0, extraMargin = 0, eater = 0;

                if (extraSpace > 0) {
                    if (count > 0)
                        filler = extraSpace / count;
                    else if (total > 0) {
                        switch (itemFlags & ControlFlags.Container_Align_Justify) {
                            case ControlFlags.Container_Align_Justify:
                                if (!wrap || (!endChild.IsInvalid && !hardBreak))
                                    spacer = extraSpace / (total - 1);
                                break;
                            case ControlFlags.Container_Align_Start:
                                break;
                            case ControlFlags.Container_Align_End:
                                extraMargin = extraSpace;
                                break;
                            default:
                                extraMargin = extraSpace / 2;
                                break;
                        }
                    }

                } else if (!wrap && (squeezedCount > 0)) {
                    eater = extraSpace / squeezedCount;
                }

                float x = rect[idim], x1 = 0;
                child = startChild;

                while (child != endChild) {
                    float ix0 = 0, ix1 = 0;

                    // FIXME: Duplication
                    var pChild = LayoutPtr(child);
                    var childFlags = pChild->Flags;
                    var flags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Layout) >> idim);
                    var fFlags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Fixed) >> idim);
                    var childMargins = pChild->Margins;
                    var childRect = GetRect(child);

                    x += childRect[idim] + extraMargin;
                    if (flags.IsFlagged(ControlFlags.Layout_Fill_Row))
                        x1 = x + filler;
                    else if (fFlags.IsFlagged(ControlFlags.HFixed))
                        x1 = x + childRect[wdim];
                    else
                        x1 = x + Math.Max(0f, childRect[wdim] + eater);

                    ix0 = x;
                    if (wrap)
                        ix1 = Math.Min(max_x2 - childMargins.GetElement(wdim), x1);
                    else
                        ix1 = x1;

                    childRect[idim] = ix0;
                    childRect[wdim] = ix1 - ix0;
                    SetRect(child, ref childRect);
                    x = x1 + childMargins.GetElement(wdim);
                    child = pChild->NextSibling;
                    extraMargin = spacer;
                }

                startChild = endChild;
            }
        }

        private unsafe void ArrangeOverlay (ControlLayout * pItem, Dimensions dim) {
            int idim = (int)dim, wdim = idim + 2;

            var rect = GetRect(pItem->Key);
            var offset = rect[idim];
            var space = rect[wdim];

            var child = pItem->FirstChild;
            while (!child.IsInvalid) {
                var pChild = LayoutPtr(child);
                var bFlags = (ControlFlags)((uint)(pItem->Flags & ControlFlagMask.Layout) >> idim);
                var childMargins = pChild->Margins;
                var childRect = GetRect(child);

                switch (bFlags & ControlFlags.Layout_Fill_Row) {
                    case ControlFlags.Layout_Center:
                        childRect[idim] += (space - childRect[wdim]) / 2 - childMargins.GetElement(wdim);
                        break;
                    case ControlFlags.Layout_Right:
                        childRect[idim] += space - childRect[wdim] - childMargins.GetElement(idim) - childMargins.GetElement(wdim);
                        break;
                    case ControlFlags.Layout_Fill_Row:
                        childRect[wdim] = Math.Max(0, space - childRect[idim] - childMargins.GetElement(wdim));
                        break;
                }

                childRect[idim] += offset;
                SetRect(child, ref childRect);
                child = pChild->NextSibling;
            }
        }

        private unsafe void ArrangeOverlaySqueezedRange (Dimensions dim, ControlKey startItem, ControlKey endItem, float offset, float space) {
            if (startItem == endItem)
                return;

            Assert(!startItem.IsInvalid);

            int idim = (int)dim, wdim = idim + 2;

            var item = startItem;
            while (item != endItem) {
                var pItem = LayoutPtr(item);
                var bFlags = (ControlFlags)((uint)(pItem->Flags & ControlFlagMask.Layout) >> idim);
                var margins = pItem->Margins;
                var rect = GetRect(item);
                var minSize = Math.Max(0, space - rect[idim] - margins.GetElement((uint)dim));

                switch (bFlags & ControlFlags.Layout_Fill_Row) {
                    case ControlFlags.Layout_Center:
                        rect[wdim] = Math.Min(rect[wdim], minSize);
                        rect[idim] += (space - rect[wdim]) / 2 - margins.GetElement(wdim);
                        break;
                    case ControlFlags.Layout_Right:
                        rect[wdim] = Math.Min(rect[wdim], minSize);
                        rect[idim] = space - rect[wdim] - margins.GetElement(wdim);
                        break;
                    case ControlFlags.Layout_Fill_Row:
                        rect[wdim] = minSize;
                        break;
                    default:
                        rect[wdim] = Math.Min(rect[wdim], minSize);
                        break;
                }

                rect[idim] += offset;
                SetRect(item, ref rect);
                item = pItem->NextSibling;
            }
        }

        private unsafe float ArrangeWrappedOverlaySqueezed (ControlLayout * pItem, Dimensions dim) {
            int idim = (int)dim, wdim = idim + 2;
            float offset = GetRect(pItem->Key)[idim], needSize = 0;
            var child = pItem->FirstChild;
            var startChild = child;

            while (!child.IsInvalid) {
                var pChild = LayoutPtr(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Break)) {
                    ArrangeOverlaySqueezedRange(dim, startChild, child, offset, needSize);
                    offset += needSize;
                    startChild = child;
                    needSize = 0;
                }

                var rect = GetRect(child);
                var childSize = rect[idim] + rect[wdim] + pChild->Margins.GetElement(wdim);
                needSize = Math.Max(needSize, childSize);
                child = pChild->NextSibling;
            }

            ArrangeOverlaySqueezedRange(dim, startChild, ControlKey.Invalid, offset, needSize);
            offset += needSize;
            return offset;
        }

        private unsafe void Arrange (ControlLayout * pItem, Dimensions dim) {
            var flags = pItem->Flags;
            var pRect = BoxPtr(pItem->Key);
            var idim = (int)dim;

            switch (flags & ControlFlagMask.BoxModel) {
                case ControlFlags.Container_Column | ControlFlags.Container_Wrap:
                    if (dim != Dimensions.X) {
                        ArrangeStacked(pItem, Dimensions.Y, true);
                        var offset = ArrangeWrappedOverlaySqueezed(pItem, Dimensions.X);
                        (*pRect)[0] = offset - (*pRect)[0];
                    }
                    break;
                case ControlFlags.Container_Row | ControlFlags.Container_Wrap:
                    if (dim == Dimensions.X)
                        ArrangeStacked(pItem, Dimensions.X, true);
                    else
                        ArrangeWrappedOverlaySqueezed(pItem, Dimensions.Y);
                    break;
                case ControlFlags.Container_Column:
                case ControlFlags.Container_Row:
                    if (((uint)flags & 1) == (uint)dim) {
                        ArrangeStacked(pItem, dim, false);
                    } else {
                        ArrangeOverlaySqueezedRange(
                            dim, pItem->FirstChild, ControlKey.Invalid,
                            (*pRect)[idim], (*pRect)[idim + 2]
                        );
                    }
                    break;
                default:
                    ArrangeOverlay(pItem, dim);
                    break;
            }

            var child = pItem->FirstChild;
            while (!child.IsInvalid) {
                // NOTE: Potentially unbounded recursion
                var pChild = LayoutPtr(child);
                Arrange(pChild, dim);
                child = pChild->NextSibling;
            }
        }
    }
}

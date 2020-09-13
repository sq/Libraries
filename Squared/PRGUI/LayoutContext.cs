using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.PRGUI.Layout {
    public partial class LayoutContext : IDisposable {
        public unsafe struct ChildrenEnumerator : IEnumerator<ControlKey> {
            public readonly LayoutContext Context;
            public readonly ControlKey Parent;
            private readonly ControlKey FirstChild;
            private int Version;
            private unsafe LayoutItem* pCurrent;

            public ChildrenEnumerator (LayoutContext context, ControlKey parent) {
                Context = context;
                Version = context.Version;
                Parent = parent;
                pCurrent = null;
                FirstChild = ControlKey.Invalid;
            }

            internal ChildrenEnumerator (LayoutContext context, LayoutItem *pParent) {
                Context = context;
                Version = context.Version;
                Parent = pParent->Key;
                pCurrent = null;
                FirstChild = pParent->FirstChild;
            }

            private void CheckVersion () {
                if (Version <= -1)
                    throw new ObjectDisposedException("enumerator");

                Context.Assert(Version == Context.Version, "Context was modified");
            }

            private void CheckValid (void * ptr) {
                CheckVersion();
                Context.Assert(ptr != null, "No current item");
            }

            public ControlKey Current {
                get {
                    CheckVersion();
                    CheckValid(pCurrent);
                    return pCurrent->Key;
                }
            }

            object IEnumerator.Current => Current;

            public void Dispose () {
                pCurrent = null;
                Version = -1;
            }

            public bool MoveNext () {
                CheckVersion();

                if (pCurrent == null) {
                    if (FirstChild.IsInvalid) {
                        var pParent = Context.LayoutPtr(Parent);
                        pCurrent = Context.LayoutPtr(pParent->FirstChild, true);
                    } else {
                        pCurrent = Context.LayoutPtr(FirstChild);
                    }
                } else {
                    pCurrent = Context.LayoutPtr(pCurrent->NextSibling, true);
                }

                return (pCurrent != null);
            }

            void IEnumerator.Reset () {
                CheckVersion();
                pCurrent = null;
            }
        }

        public struct ChildrenEnumerable : IEnumerable<ControlKey> {
            public readonly LayoutContext Context;
            public readonly ControlKey Parent;

            internal ChildrenEnumerable (LayoutContext context, ControlKey parent) {
                Context = context;
                Parent = parent;
            }

            public ChildrenEnumerator GetEnumerator () {
                return new ChildrenEnumerator(Context, Parent);
            }

            IEnumerator<ControlKey> IEnumerable<ControlKey>.GetEnumerator () {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return GetEnumerator();
            }
        }

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

        private unsafe ChildrenEnumerable Children (LayoutItem *pParent) {
            Assert(pParent != null);
            return new ChildrenEnumerable(this, pParent->Key);
        }

        public ChildrenEnumerable Children (ControlKey parent) {
            Assert(!parent.IsInvalid);
            return new ChildrenEnumerable(this, parent);
        }

        public unsafe bool Update (ControlKey key) {
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
            var newData = new LayoutItem(key);
            var newBox = default(RectF);

            Layout.Add(ref newData);
            Boxes.Add(ref newBox);

            return key;
        }

        private unsafe void InsertBefore (LayoutItem * pNewItem, LayoutItem * pLater) {
            AssertNotRoot(pNewItem->Key);

            var pPreviousSibling = LayoutPtr(pLater->PreviousSibling, true);

            if (pPreviousSibling != null)
                pNewItem->NextSibling = pPreviousSibling->NextSibling;
            else
                pNewItem->NextSibling = ControlKey.Invalid;

            pNewItem->Parent = pLater->Parent;
            pNewItem->PreviousSibling = pLater->PreviousSibling;

            if (pPreviousSibling != null)
                pPreviousSibling->NextSibling = pNewItem->Key;

            pLater->PreviousSibling = pNewItem->Key;
        }

        private unsafe void InsertAfter (LayoutItem * pEarlier, LayoutItem * pNewItem) {
            AssertNotRoot(pNewItem->Key);

            var pNextSibling = LayoutPtr(pEarlier->NextSibling, true);

            pNewItem->Parent = pEarlier->Parent;
            pNewItem->PreviousSibling = pEarlier->Key;
            pNewItem->NextSibling = pEarlier->NextSibling;

            pEarlier->NextSibling = pNewItem->Key;

            if (pNextSibling != null)
                pNextSibling->PreviousSibling = pNewItem->Key;
        }

        private unsafe void ClearItemBreak (LayoutItem * data) {
            data->Flags = data->Flags & ~ControlFlags.Layout_Break;
        }

        public unsafe ControlKey GetParent (ControlKey child) {
            if (child.IsInvalid)
                return ControlKey.Invalid;

            var ptr = LayoutPtr(child);
            return ptr->Parent;
        }

        public unsafe ControlKey GetFirstChild (ControlKey parent) {
            if (parent.IsInvalid)
                return ControlKey.Invalid;

            var ptr = LayoutPtr(parent);
            return ptr->FirstChild;
        }

        // TODO: Optimize this
        public ControlKey GetLastChild (ControlKey parent) {
            if (parent.IsInvalid)
                return ControlKey.Invalid;

            var lastChild = ControlKey.Invalid;
            foreach (var child in Children(parent))
                lastChild = child;

            return lastChild;
        }

        public unsafe void InsertBefore (ControlKey newSibling, ControlKey later) {
            AssertNotRoot(newSibling);
            Assert(!later.IsInvalid);
            Assert(!newSibling.IsInvalid);
            AssertNotEqual(later, newSibling);

            var pLater = LayoutPtr(later);
            var pNewSibling = LayoutPtr(newSibling);
            InsertBefore(pNewSibling, pLater);
        }

        public unsafe void InsertAfter (ControlKey earlier, ControlKey newSibling) {
            AssertNotRoot(newSibling);
            Assert(!earlier.IsInvalid);
            Assert(!newSibling.IsInvalid);
            AssertNotEqual(earlier, newSibling);

            var pEarlier = LayoutPtr(earlier);
            var pLater = LayoutPtr(newSibling);
            InsertAfter(pEarlier, pLater);
        }

        /// <summary>
        /// Alias for InsertAtEnd
        /// </summary>
        public void Append (ControlKey parent, ControlKey child) {
            InsertAtEnd(parent, child);
        }

        public unsafe void InsertAtEnd (ControlKey parent, ControlKey child) {
            AssertNotRoot(child);
            AssertNotEqual(parent, child);

            var pParent = LayoutPtr(parent);
            var pChild = LayoutPtr(child);

            Assert(pChild->Parent.IsInvalid, "is not inserted");

            if (pParent->FirstChild.IsInvalid) {
                pParent->FirstChild = child;
                pChild->Parent = parent;
            } else {
                var lastChild = GetLastChild(parent);
                var pLastChild = LayoutPtr(lastChild);
                InsertAfter(pLastChild, pChild);
            }
        }

        public unsafe void InsertAtStart (ControlKey parent, ControlKey newFirstChild) {
            AssertNotRoot(newFirstChild);
            AssertNotEqual(parent, newFirstChild);
            var pParent = LayoutPtr(parent);
            var oldChild = pParent->FirstChild;
            var pOldChild = LayoutPtr(oldChild);
            var pChild = LayoutPtr(newFirstChild);

            Assert(pChild->Parent.IsInvalid, "is not inserted");

            pChild->Parent = parent;
            pChild->PreviousSibling = ControlKey.Invalid;
            pChild->NextSibling = oldChild;

            pParent->FirstChild = newFirstChild;
            pOldChild->PreviousSibling = newFirstChild;
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
                flags &= ~ControlFlags.FixedWidth;
            else
                flags |= ControlFlags.FixedWidth;

            if (size.Y <= 0)
                flags &= ~ControlFlags.FixedHeight;
            else
                flags |= ControlFlags.FixedHeight;
            pItem->Flags = flags;
        }

        public void SetSizeXY (ControlKey key, float width = 0, float height = 0) {
            SetSize(key, new Vector2(width, height));
        }

        public unsafe void ClearItemBreak (ControlKey key) {
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlags.Layout_Break);
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

        private unsafe float CalcOverlaySize (LayoutItem * pItem, Dimensions dim) {
            float result = 0;
            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                var rect = GetRect(child);
                // FIXME: Is this a bug?
                var childSize = rect[(uint)dim] + rect[(uint)dim + 2] + pChild->Margins.GetElement((uint)dim + 2);
                result = Math.Max(result, childSize);
            }
            return result;
        }

        private unsafe float CalcStackedSize (LayoutItem * pItem, Dimensions dim) {
            float result = 0;
            int idim = (int)dim, wdim = idim + 2;
            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                var rect = GetRect(child);
                result += rect[idim] + rect[wdim] + pChild->Margins.GetElement(wdim);
            }
            return result;
        }

        private unsafe float CalcWrappedSizeImpl (LayoutItem * pItem, Dimensions dim, bool overlaid) {
            int idim = (int)dim, wdim = idim + 2;
            float needSize = 0, needSize2 = 0;
            foreach (var child in Children(pItem)) {
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
            }

            if (overlaid)
                return needSize + needSize2;
            else
                return Math.Max(needSize, needSize2);
        }

        private unsafe float CalcWrappedOverlaidSize (LayoutItem * pItem, Dimensions dim) {
            return CalcWrappedSizeImpl(pItem, dim, true);
        }

        private unsafe float CalcWrappedStackedSize (LayoutItem * pItem, Dimensions dim) {
            return CalcWrappedSizeImpl(pItem, dim, false);
        }

        private unsafe void CalcSize (LayoutItem * pItem, Dimensions dim) {
            foreach (var child in Children(pItem)) {
                // NOTE: Potentially unbounded recursion
                var pChild = LayoutPtr(child);
                CalcSize(pChild, dim);
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

        private unsafe void ArrangeStacked (LayoutItem * pItem, Dimensions dim, bool wrap) {
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
                        if (!fFlags.IsFlagged(ControlFlags.FixedWidth))
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
                    else if (fFlags.IsFlagged(ControlFlags.FixedWidth))
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

        private unsafe void ArrangeOverlay (LayoutItem * pItem, Dimensions dim) {
            int idim = (int)dim, wdim = idim + 2;

            var rect = GetRect(pItem->Key);
            var offset = rect[idim];
            var space = rect[wdim];

            foreach (var child in Children(pItem)) {
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

        private unsafe float ArrangeWrappedOverlaySqueezed (LayoutItem * pItem, Dimensions dim) {
            int idim = (int)dim, wdim = idim + 2;
            float offset = GetRect(pItem->Key)[idim], needSize = 0;

            var startChild = pItem->FirstChild;
            foreach (var child in Children(pItem)) {
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
            }

            ArrangeOverlaySqueezedRange(dim, startChild, ControlKey.Invalid, offset, needSize);
            offset += needSize;
            return offset;
        }

        private unsafe void Arrange (LayoutItem * pItem, Dimensions dim) {
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

            foreach (var child in Children(pItem)) {
                // NOTE: Potentially unbounded recursion
                var pChild = LayoutPtr(child);
                Arrange(pChild, dim);
            }
        }
    }
}

/*
 * Contents of this file derived from layout.h (https://github.com/randrew/layout), which was in turn
 *  derived from oui/blendish. This file is as such under the same license as those two single-file 
 *  libraries. License follows:
 * 
 * PRGUI.Layout
 * Copyright (c) 2020 Katelyn Gadd kg@luminance.org
 * 
 * Layout - Simple 2D stacking boxes calculations
 * Copyright (c) 2016 Andrew Richards randrew@gmail.com
 * 
 * Blendish - Blender 2.5 UI based theming functions for NanoVG
 * Copyright (c) 2014 Leonard Ritter leonard.ritter@duangle.com
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 * 
 */

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
            if (!UpdateSubtree(Root))
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

        internal unsafe bool UpdateSubtree (ControlKey key) {
            if (key.IsInvalid)
                return false;

            var pItem = LayoutPtr(key);
            // HACK: Ensure its position is updated even if we don't fully lay out all controls
            if (pItem->Flags.IsFlagged(ControlFlags.Layout_Floating)) {
                var pRect = RectPtr(key);
                pRect->Left = pItem->Margins.Left;
                pRect->Top = pItem->Margins.Top;
            }

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

            if (pNextSibling != null) {
                pNextSibling->PreviousSibling = pNewItem->Key;
            } else {
                var pParent = LayoutPtr(pEarlier->Parent);
                Assert(pParent->LastChild == pEarlier->Key);
                pParent->LastChild = pNewItem->Key;
            }
        }

        public unsafe void SetItemSyntheticBreak (ControlKey key, bool newState) {
            var pItem = LayoutPtr(key);
            pItem->Flags = pItem->Flags & ~ControlFlags.Internal_Break;
            if (newState)
                pItem->Flags |= ControlFlags.Internal_Break;
        }

        public unsafe void SetItemForceBreak (ControlKey key, bool newState) {
            var pItem = LayoutPtr(key);
            pItem->Flags = pItem->Flags & ~ControlFlags.Layout_ForceBreak;
            if (newState)
                pItem->Flags |= ControlFlags.Layout_ForceBreak;
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

        public unsafe ControlKey GetLastChild (ControlKey parent) {
            if (parent.IsInvalid)
                return ControlKey.Invalid;

            var ptr = LayoutPtr(parent);
            return ptr->LastChild;
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
                Assert(pParent->LastChild.IsInvalid);
                pParent->FirstChild = child;
                pParent->LastChild = child;
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
            var pChild = LayoutPtr(newFirstChild);

            Assert(pChild->Parent.IsInvalid, "is not inserted");

            pChild->Parent = parent;
            pChild->PreviousSibling = ControlKey.Invalid;
            pChild->NextSibling = oldChild;

            pParent->FirstChild = newFirstChild;
            if (pParent->LastChild.IsInvalid)
                pParent->LastChild = newFirstChild;

            if (!oldChild.IsInvalid) {
                var pOldChild = LayoutPtr(oldChild);
                pOldChild->PreviousSibling = newFirstChild;
            }
        }

        public unsafe Vector2 GetFixedSize (ControlKey key) {
            var pItem = LayoutPtr(key);
            return pItem->FixedSize;
        }

        public unsafe void SetFixedSize (ControlKey key, Vector2 size) {
            var pItem = LayoutPtr(key);
            pItem->FixedSize = size;

            var flags = pItem->Flags;
            if (size.X <= 0)
                flags &= ~ControlFlags.Internal_FixedWidth;
            else
                flags |= ControlFlags.Internal_FixedWidth;

            if (size.Y <= 0)
                flags &= ~ControlFlags.Internal_FixedHeight;
            else
                flags |= ControlFlags.Internal_FixedHeight;
            pItem->Flags = flags;
        }

        public void SetFixedSize (ControlKey key, float width = 0, float height = 0) {
            SetFixedSize(key, new Vector2(width, height));
        }

        public unsafe void SetSizeConstraints (ControlKey key, Vector2? minimumSize = null, Vector2? maximumSize = null) {
            var pItem = LayoutPtr(key);
            pItem->MinimumSize = minimumSize ?? LayoutItem.NoSize;
            pItem->MaximumSize = maximumSize ?? LayoutItem.NoSize;
        }

        public void SetSizeConstraints (ControlKey key, float? minimumWidth = null, float? minimumHeight = null, float? maximumWidth = null, float? maximumHeight = null) {
            SetSizeConstraints(key, new Vector2(minimumWidth ?? -1, minimumHeight ?? -1), new Vector2(maximumWidth ?? -1, maximumHeight ?? -1));
        }

        public unsafe void GetSizeConstraints (ControlKey key, out Vector2 minimumSize, out Vector2 maximumSize) {
            var pItem = LayoutPtr(key);
            minimumSize = pItem->MinimumSize;
            maximumSize = pItem->MaximumSize;
        }

        public unsafe bool TryMeasureContent (ControlKey container, out RectF result) {
            var pItem = LayoutPtr(container);
            float minX = 999999, minY = 999999,
                maxX = -999999, maxY = -999999;

            if (pItem->FirstChild.IsInvalid) {
                result = default(RectF);
                return false;
            }

            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                var childRect = GetRect(child);

                // HACK: The arrange algorithms will clip an element to its containing box, which
                //  hinders attempts to measure all of the content inside a container for scrolling
                if (pChild->Flags.IsFlagged(ControlFlags.Internal_FixedWidth))
                    childRect.Width = pChild->FixedSize.X;
                if (pChild->Flags.IsFlagged(ControlFlags.Internal_FixedHeight))
                    childRect.Height = pChild->FixedSize.Y;

                minX = Math.Min(minX, childRect.Left - pChild->Margins.Left);
                maxX = Math.Max(maxX, childRect.Left + childRect.Width + pChild->Margins.Right);
                minY = Math.Min(minY, childRect.Top - pChild->Margins.Top);
                maxY = Math.Max(maxY, childRect.Top + childRect.Height + pChild->Margins.Bottom);
            }

            result = new RectF(minX, minY, maxX - minX, maxY - minY);
            return true;
        }

        public unsafe ControlFlags GetContainerFlags (ControlKey key) {
            var pItem = LayoutPtr(key);
            return pItem->Flags & ControlFlagMask.Container;
        }

        public unsafe void SetContainerFlags (ControlKey key, ControlFlags flags) {
            AssertMasked(flags, ControlFlagMask.Container, nameof(ControlFlagMask.Container));
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlagMask.Container) | flags;
        }

        public unsafe ControlFlags GetLayoutFlags (ControlKey key) {
            var pItem = LayoutPtr(key);
            return pItem->Flags & ControlFlagMask.Layout;
        }

        public unsafe void SetLayoutFlags (ControlKey key, ControlFlags flags) {
            AssertMasked(flags, ControlFlagMask.Layout, nameof(ControlFlagMask.Layout));
            var pItem = LayoutPtr(key);
            pItem->Flags = (pItem->Flags & ~ControlFlagMask.Layout) | flags;
        }

        public unsafe void SetMargins (ControlKey key, Margins m) {
            var pItem = LayoutPtr(key);
            pItem->Margins = m;
        }

        public unsafe void SetPadding (ControlKey key, Margins m) {
            var pItem = LayoutPtr(key);
            pItem->Padding = m;
        }

        public unsafe Margins GetMargins (ControlKey key) {
            var pItem = LayoutPtr(key);
            return pItem->Margins;
        }

        private unsafe float CalcOverlaySize (LayoutItem * pItem, Dimensions dim) {
            float result = 0;
            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Floating))
                    continue;

                var rect = GetRect(child);
                // FIXME: Is this a bug?
                var childSize = rect[(uint)dim] + rect[(uint)dim + 2] + pChild->Margins[(uint)dim + 2];
                result = Math.Max(result, childSize);
            }
            return result;
        }

        private unsafe float CalcStackedSize (LayoutItem * pItem, Dimensions dim) {
            float result = 0;
            int idim = (int)dim, wdim = idim + 2;
            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Floating))
                    continue;

                var rect = GetRect(child);
                result += rect[idim] + rect[wdim] + pChild->Margins[wdim];
            }
            return result;
        }

        private unsafe float CalcWrappedSizeImpl (
            LayoutItem * pItem, Dimensions dim, bool overlaid, bool forcedBreakOnly
        ) {
            int idim = (int)dim, wdim = idim + 2;
            float needSize = 0, needSize2 = 0;
            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Floating))
                    continue;

                var rect = GetRect(child);

                if (
                    (!forcedBreakOnly && pChild->Flags.IsBreak()) ||
                    pChild->Flags.IsFlagged(ControlFlags.Layout_ForceBreak)
                ) {
                    if (overlaid)
                        needSize2 += needSize;
                    else
                        needSize2 = Math.Max(needSize2, needSize);

                    needSize = 0;
                }

                var childSize = rect[idim] + rect[wdim] + pChild->Margins[wdim];
                if (overlaid)
                    needSize = Math.Max(needSize, childSize);
                else
                    needSize += childSize;
            }

            float result;
            if (overlaid)
                result = needSize + needSize2;
            else
                result = Math.Max(needSize, needSize2);

            // FIXME: Is this actually necessary?
            result = Constrain(result, pItem->MinimumSize.GetElement(idim), pItem->MaximumSize.GetElement(idim));
            return result;
        }

        private unsafe float CalcWrappedOverlaidSize (LayoutItem * pItem, Dimensions dim) {
            return CalcWrappedSizeImpl(pItem, dim, true, false);
        }

        private unsafe float CalcWrappedStackedSize (LayoutItem * pItem, Dimensions dim) {
            return CalcWrappedSizeImpl(pItem, dim, false, false);
        }

        private float Constrain (float value, float maybeMin, float maybeMax) {
            if (maybeMin >= 0)
                value = Math.Max(value, maybeMin);
            if (maybeMax >= 0)
                value = Math.Min(value, maybeMax);
            return value;
        }

        private unsafe float Constrain (float value, LayoutItem * pItem, int dimension) {
            return Constrain(value, pItem->MinimumSize.GetElement(dimension), pItem->MaximumSize.GetElement(dimension));
        }

        private unsafe void CalcSize (LayoutItem * pItem, Dimensions dim) {
            foreach (var child in Children(pItem)) {
                // NOTE: Potentially unbounded recursion
                var pChild = LayoutPtr(child);
                CalcSize(pChild, dim);
            }

            var pRect = RectPtr(pItem->Key);
            var idim = (int)dim;

            // Start by setting size to top/left margin
            (*pRect)[idim] = pItem->Margins[idim];

            if (pItem->FixedSize.GetElement(idim) > 0) {
                (*pRect)[idim + 2] = Constrain(pItem->FixedSize.GetElement(idim), pItem, idim);
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
                        // result = CalcWrappedSizeImpl(pItem, dim, false, true);
                    else
                        result = CalcOverlaySize(pItem, dim);
                        // result = CalcWrappedSizeImpl(pItem, dim, true, true);
                    break;
                default:
                    result = CalcOverlaySize(pItem, dim);
                    break;
            }

            result += pItem->Padding[idim] + pItem->Padding[2 + idim];

            (*pRect)[2 + idim] = Constrain(result, pItem, idim);
        }

        private unsafe void ArrangeStacked (LayoutItem * pItem, Dimensions dim, bool wrap) {
            var itemFlags = pItem->Flags;
            var rect = GetContentRect(pItem->Key);
            int idim = (int)dim, wdim = idim + 2;
            float space = rect[wdim], max_x2 = rect[idim] + space;

            var startChild = pItem->FirstChild;
            while (!startChild.IsInvalid) {
                float used;
                uint fillerCount, squeezedCount, total;
                bool hardBreak;
                ControlKey child, endChild;

                BuildStackedRow(
                    wrap, idim, wdim, space, startChild,
                    out used, out fillerCount, out squeezedCount, out total,
                    out hardBreak, out child, out endChild
                );

                var extraSpace = space - used;
                float filler = 0, spacer = 0, extraMargin = 0, eater = 0;

                if (extraSpace > 0) {
                    if (fillerCount > 0)
                        filler = extraSpace / fillerCount;
                    else if (total > 0) {
                        switch (itemFlags & ControlFlags.Container_Align_Justify) {
                            case ControlFlags.Container_Align_Justify:
                                // justify when not wrapping or not in last line, or not manually breaking
                                if (!wrap || (!endChild.IsInvalid && !hardBreak))
                                    spacer = extraSpace / (total - 1);
                                else
                                    ;
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
                    } else
                        ;

                    // oui.h
                    // } else if (!wrap && (extraSpace < 0)) {
                    // layout.h
                } else if (!wrap && (squeezedCount > 0)) {
                    eater = extraSpace / squeezedCount;
                }

                float x = rect[idim];
                child = startChild;
                ArrangeStackedRow(
                    wrap, idim, wdim, max_x2, 
                    pItem, ref child, endChild,
                    fillerCount, squeezedCount, total,
                    filler, spacer, 
                    extraMargin, eater, x
                );

                startChild = endChild;
            }
        }

        private unsafe void ArrangeStackedRow (
            bool wrap, int idim, int wdim, float max_x2,
            LayoutItem* pParent, ref ControlKey child, ControlKey endChild, 
            uint fillerCount, uint squeezedCount, uint total,
            float filler, float spacer, float extraMargin, 
            float eater, float x
        ) {
            int constrainedCount = 0;
            float extraFromConstraints = 0, originalExtraMargin = extraMargin, originalX = x;

            var startChild = child;

            // Perform initial size calculation for items, and then arrange items and calculate final sizes
            for (int pass = 0; pass < 2; pass++) {
                child = startChild;
                extraMargin = originalExtraMargin;
                x = originalX;

                while (child != endChild) {
                    float ix0 = 0, ix1 = 0;

                    // FIXME: Duplication
                    var pChild = LayoutPtr(child);
                    var childFlags = pChild->Flags;
                    if (childFlags.IsFlagged(ControlFlags.Layout_Floating)) {
                        // FIXME: Set position?
                        child = pChild->NextSibling;
                        continue;
                    }

                    var flags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Layout) >> idim);
                    var fFlags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Fixed) >> idim);
                    var childMargins = pChild->Margins;
                    var childRect = GetRect(child);
                    var isFixedSize = fFlags.IsFlagged(ControlFlags.Internal_FixedWidth);

                    x += childRect[idim] + extraMargin;

                    float computedSize;
                    if (flags.IsFlagged(ControlFlags.Layout_Fill_Row))
                        computedSize = filler;
                    else if (isFixedSize)
                        computedSize = childRect[wdim];
                    else
                        computedSize = Math.Max(0f, childRect[wdim] + eater);

                    if ((pass == 1) && (fillerCount > constrainedCount) && (constrainedCount > 0) && !isFixedSize)
                        computedSize += extraFromConstraints / (fillerCount - constrainedCount);

                    float constrainedSize = Constrain(computedSize, pChild, idim);
                    if (pass == 0) {
                        float constraintDelta = (computedSize - constrainedSize);
                        // FIXME: Epsilon too big?
                        if (Math.Abs(constraintDelta) >= 0.1) {
                            extraFromConstraints += constraintDelta;
                            constrainedCount++;
                        }
                    }

                    ix0 = x;
                    if (wrap)
                        ix1 = Math.Min(max_x2 - childMargins[wdim], x + constrainedSize);
                    else
                        ix1 = x + constrainedSize;

                    if (pass == 1) {
                        // FIXME: Is this correct?
                        if (pParent->Flags.IsFlagged(ControlFlags.Container_Constrain_Size)) {
                            var parentRect = GetRect(pParent->Key);
                            float parentExtent = parentRect[idim] + parentRect[wdim];
                            ix1 = Constrain(ix1, -1, parentExtent);
                        }
                        childRect[idim] = ix0;
                        childRect[wdim] = ix1 - ix0;
                        SetRect(child, ref childRect);
                        CheckConstraints(child, idim);
                    }

                    x = x + constrainedSize + childMargins[wdim];
                    child = pChild->NextSibling;
                    extraMargin = spacer;
                }
            }
        }

        private unsafe void CheckConstraints (ControlKey control, int dimension) {
            var pItem = LayoutPtr(control);
            var rect = GetRect(control);
            var wdim = dimension + 2;

            var min = pItem->MinimumSize.GetElement(dimension);
            var max = pItem->MaximumSize.GetElement(dimension);
            // FIXME
            if (min >= max)
                return;

            if (
                ((min >= 0) && (rect[wdim] < min)) ||
                ((max >= 0) && (rect[wdim] > max))
            )
                System.Diagnostics.Debugger.Break();
        }

        private unsafe void BuildStackedRow (
            bool wrap, int idim, int wdim, float space, ControlKey startChild, 
            out float used, out uint fillerCount, out uint squeezedCount, out uint total, 
            out bool hardBreak, out ControlKey child, out ControlKey endChild
        ) {
            used = 0;
            fillerCount = squeezedCount = total = 0;
            hardBreak = false;

            // first pass: count items that need to be expanded, and the space that is used
            child = startChild;
            endChild = ControlKey.Invalid;
            while (!child.IsInvalid) {
                var pChild = LayoutPtr(child);
                var childFlags = pChild->Flags;
                if (childFlags.IsFlagged(ControlFlags.Layout_Floating)) {
                    child = pChild->NextSibling;
                    continue;
                }

                var flags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Layout) >> idim);
                var fFlags = (ControlFlags)((uint)(childFlags & ControlFlagMask.Fixed) >> idim);
                var childMargins = pChild->Margins;
                var childRect = GetRect(child);
                var extend = used;

                if (
                    childFlags.IsFlagged(ControlFlags.Layout_ForceBreak) &&
                    (child != startChild)
                ) {
                    endChild = child;
                    break;
                } else if (flags.IsFlagged(ControlFlags.Layout_Fill_Row)) {
                    ++fillerCount;
                    extend += childRect[idim] + childMargins[wdim];
                } else {
                    if (!fFlags.IsFlagged(ControlFlags.Internal_FixedWidth))
                        ++squeezedCount;
                    extend += childRect[idim] + childRect[wdim] + childMargins[wdim];
                }

                if (
                    wrap &&
                    (total != 0) && (
                        (extend > space) ||
                        childFlags.IsBreak()
                    )
                ) {
                    endChild = child;
                    hardBreak = childFlags.IsBreak();
                    pChild->Flags |= ControlFlags.Internal_Break;
                    break;
                } else {
                    used = extend;
                    child = pChild->NextSibling;
                }

                ++total;
            }
        }

        private unsafe void ArrangeOverlay (LayoutItem * pItem, Dimensions dim) {
            int idim = (int)dim, wdim = idim + 2;

            var rect = GetContentRect(pItem->Key);
            var offset = rect[idim];
            var space = rect[wdim];

            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Floating))
                    continue;

                var bFlags = (ControlFlags)((uint)(pItem->Flags & ControlFlagMask.Layout) >> idim);
                var childMargins = pChild->Margins;
                var childRect = GetRect(child);

                switch (bFlags & ControlFlags.Layout_Fill_Row) {
                    case 0: // ControlFlags.Layout_Center:
                        childRect[idim] += (space - childRect[wdim]) / 2 - childMargins[wdim];
                        break;
                    case ControlFlags.Layout_Anchor_Right:
                        childRect[idim] += space - childRect[wdim] - childMargins[idim] - childMargins[wdim];
                        break;
                    case ControlFlags.Layout_Fill_Row:
                        var fillValue = Constrain(Math.Max(0, space - childRect[idim] - childMargins[wdim]), pChild, idim);
                        childRect[wdim] = fillValue;
                        break;
                }

                childRect[idim] += offset;
                SetRect(child, ref childRect);
                CheckConstraints(child, idim);
            }
        }

        private unsafe void ArrangeOverlaySqueezedRange (LayoutItem *pParent, Dimensions dim, ControlKey startItem, ControlKey endItem, float offset, float space) {
            if (startItem == endItem)
                return;

            Assert(!startItem.IsInvalid);

            var parentRect = GetContentRect(pParent->Key);

            int idim = (int)dim, wdim = idim + 2;

            var item = startItem;
            while (item != endItem) {
                var pItem = LayoutPtr(item);
                if (pItem->Flags.IsFlagged(ControlFlags.Layout_Floating)) {
                    item = pItem->NextSibling;
                    continue;
                }

                var bFlags = (ControlFlags)((uint)(pItem->Flags & ControlFlagMask.Layout) >> idim);
                var margins = pItem->Margins;
                var rect = GetRect(item);
                var minSize = Math.Max(0, space - rect[idim] - margins[wdim]);

                switch (bFlags & ControlFlags.Layout_Fill_Row) {
                    case 0: // ControlFlags.Layout_Center:
                        rect[wdim] = Math.Min(rect[wdim], minSize);
                        rect[idim] += (space - rect[wdim]) / 2 - margins[wdim];
                        break;
                    case ControlFlags.Layout_Anchor_Right:
                        rect[wdim] = Math.Min(rect[wdim], minSize);
                        rect[idim] = space - rect[wdim] - margins[wdim];
                        break;
                    case ControlFlags.Layout_Fill_Row:
                        rect[wdim] = minSize;
                        break;
                    default:
                        rect[wdim] = Math.Min(rect[wdim], minSize);
                        break;
                }

                rect[idim] += offset;
                var unconstrained = rect[wdim];
                // FIXME: Redistribute remaining space?
                rect[wdim] = Constrain(unconstrained, pItem->MinimumSize.GetElement(idim), pItem->MaximumSize.GetElement(idim));

                float extent = rect[idim] + rect[wdim];

                if (pParent->Flags.IsFlagged(ControlFlags.Container_Constrain_Size)) {
                    // rect[idim] = Constrain(rect[idim], parentRect[idim], parentRect[wdim]);
                    float parentExtent = parentRect[idim] + parentRect[wdim];
                    extent = Constrain(extent, -1, parentExtent);
                    rect[wdim] = extent - rect[idim];
                }

                SetRect(item, ref rect);
                CheckConstraints(item, idim);
                item = pItem->NextSibling;
            }
        }

        private unsafe float ArrangeWrappedOverlaySqueezed (LayoutItem * pItem, Dimensions dim) {
            int idim = (int)dim, wdim = idim + 2;
            float offset = GetContentRect(pItem->Key)[idim], needSize = 0;

            var startChild = pItem->FirstChild;
            foreach (var child in Children(pItem)) {
                var pChild = LayoutPtr(child);
                if (pChild->Flags.IsFlagged(ControlFlags.Layout_Floating))
                    continue;

                if (
                    pChild->Flags.IsBreak()
                ) {
                    ArrangeOverlaySqueezedRange(pItem, dim, startChild, child, offset, needSize);
                    offset += needSize;
                    startChild = child;
                    needSize = 0;
                }

                var rect = GetRect(child);
                var childSize = rect[idim] + rect[wdim] + pChild->Margins[wdim];
                needSize = Math.Max(needSize, childSize);
            }

            ArrangeOverlaySqueezedRange(pItem, dim, startChild, ControlKey.Invalid, offset, needSize);
            offset += needSize;
            return offset;
        }

        private unsafe void Arrange (LayoutItem * pItem, Dimensions dim) {
            var flags = pItem->Flags;
            var pRect = RectPtr(pItem->Key);
            var contentRect = GetContentRect(pItem->Key);
            var idim = (int)dim;

            switch (flags & ControlFlagMask.BoxModel) {
                case ControlFlags.Container_Column | ControlFlags.Container_Wrap:
                    if (dim != Dimensions.X) {
                        ArrangeStacked(pItem, Dimensions.Y, true);
                        var offset = ArrangeWrappedOverlaySqueezed(pItem, Dimensions.X);
                        // FIXME: Content rect?
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
                            pItem, dim, pItem->FirstChild, ControlKey.Invalid,
                            contentRect[idim], contentRect[idim + 2]
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

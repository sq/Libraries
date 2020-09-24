using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {

    public class EditableText : Control {
        // FIXME
        public static readonly bool Multiline = false;

        public const float SelectionHorizontalScrollPadding = 32;
        public const float RightScrollMargin = 64;
        public const float AutoscrollClickTimeout = 0.25f;

        public Vector2 ScrollOffset;

        protected DynamicStringLayout DynamicLayout = new DynamicStringLayout();
        protected StringBuilder Builder = new StringBuilder();
        protected Margins CachedPadding;

        private float DisableAutoscrollUntil;

        private Vector2? ClickStartVirtualPosition = null;

        private int CurrentScrollBias = 1;

        private Pair<int> _Selection;
        public Pair<int> Selection {
            get => _Selection;
            set {
                if (_Selection == value)
                    return;
                value.First = Arithmetic.Clamp(value.First, 0, Builder.Length);
                value.Second = Arithmetic.Clamp(value.Second, value.First, Builder.Length);

                if (value.First == value.Second) {
                    if ((value.First < Builder.Length) && char.IsLowSurrogate(Builder[value.First])) {
                        value.First--;
                        // FIXME: Bump this forward?
                        value.Second--;
                    }
                } else {
                    // Expand selection outward if it rests in the middle of a surrogate pair
                    if ((value.First > 0) && char.IsLowSurrogate(Builder[value.First]))
                        value.First--;
                    /*
                    if ((value.Second < (Builder.Length - 1)) && char.IsHighSurrogate(Builder[value.Second]))
                        value.Second++;
                    */
                }

                if (_Selection == value)
                    return;
                _Selection = value;
                Console.WriteLine("New selection is {0}", value);
                Invalidate();
            }
        }

        protected override bool ShouldClipContent => true;

        public EditableText ()
            : base () {
            DynamicLayout.Text = Builder;
            AcceptsCapture = true;
            AcceptsFocus = true;
            AcceptsTextInput = true;
        }

        public string Text {
            get {
                return Builder.ToString();
            }
            set {
                // FIXME: Optimize the 'value hasn't changed' case
                Builder.Clear();
                Builder.Append(value);
                Invalidate();
            }
        }

        public void Invalidate () {
            DynamicLayout.ResetMarkersAndHitTests();
            UpdateLayoutSettings();
            DynamicLayout.Invalidate();
        }

        private void UpdateLayoutSettings () {
            DynamicLayout.LineLimit = Multiline ? int.MaxValue : 1;
            MarkSelection();
        }

        protected StringLayout UpdateLayout (
            UIOperationContext context, DecorationSettings settings, IDecorator decorations, out Material material
        ) {
            // HACK: Avoid accumulating too many extra hit tests from previous mouse positions
            // This will invalidate the layout periodically as the mouse moves, but whatever
            if (DynamicLayout.HitTests.Count > 8)
                DynamicLayout.ResetMarkersAndHitTests();

            UpdateLayoutSettings();

            Color? color = null;
            decorations.GetTextSettings(context, settings.State, out material, out IGlyphSource font, ref color);
            CachedPadding = ComputePadding(context, decorations);

            DynamicLayout.GlyphSource = font;
            DynamicLayout.Color = color ?? Color.White;

            return DynamicLayout.Get();
        }

        protected override IDecorator GetDefaultDecorations (UIOperationContext context) {
            return context.DecorationProvider?.EditableText;
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            if (DynamicLayout.GlyphSource == null)
                return;

            var lineHeight = DynamicLayout.GlyphSource.LineSpacing;
            var contentMinimumHeight = lineHeight * (Multiline ? 2 : 1) + CachedPadding.Y; // FIXME: Include padding
            minimumHeight = Math.Max(minimumHeight ?? 0, contentMinimumHeight);
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent) {
            // HACK: Populate various fields that we will use to compute minimum size
            UpdateLayout(context, new DecorationSettings(), context.DecorationProvider.EditableText, out Material temp);
            return base.OnGenerateLayoutTree(context, parent);
        }

        protected LayoutMarker? MarkSelection () {
            return DynamicLayout.Mark(Selection.First, Math.Max(Selection.Second - 1, Selection.First));
        }

        private LayoutHitTest? ImmediateHitTest (Vector2 position) {
            var result = DynamicLayout.HitTest(position);
            if (result.HasValue)
                return result;

            DynamicLayout.Get();
            return DynamicLayout.HitTest(position);
        }

        private int? MapVirtualPositionToCharacterIndex (Vector2 position, bool? leanOverride) {
            var result = ImmediateHitTest(position);
            if (position.X < 0)
                return 0;
            else if (position.X > DynamicLayout.Get().Size.X)
                return Builder.Length;

            if (result.HasValue) {
                var rv = result.Value;
                var lean = leanOverride ?? rv.LeaningRight;
                var newIndex =
                    rv.FirstCharacterIndex.HasValue
                        ? (
                            lean
                                ? rv.LastCharacterIndex.Value + 1
                                : rv.FirstCharacterIndex.Value
                        )
                        : Builder.Length;
                return newIndex;
            }

            return null;
        }

        private bool OnMouseEvent (string name, MouseEventArgs args) {
            var position = new Vector2(
                Arithmetic.Clamp(args.LocalPosition.X, 0, args.ContentBox.Width - 1),
                Arithmetic.Clamp(args.LocalPosition.Y, 0, args.ContentBox.Height - 1)
            );

            var virtualPosition = position + ScrollOffset;

            if (name == UIContext.Events.MouseDown) {
                DisableAutoscrollUntil = (float)Time.Seconds + AutoscrollClickTimeout;

                ClickStartVirtualPosition = virtualPosition;
                var currentCharacter = MapVirtualPositionToCharacterIndex(virtualPosition, null);
                // If we're double-clicking inside the selection don't update it yet
                if (currentCharacter.HasValue && !args.DoubleClicking)
                    Selection = new Pair<int>(currentCharacter.Value, currentCharacter.Value);
                return true;
            } else if (
                (name == UIContext.Events.MouseDrag) ||
                (name == UIContext.Events.MouseUp)
            ) {
                // FIXME: Ideally we would just clamp the mouse coordinates into our rectangle instead of rejecting
                //  coordinates outside our rect. Maybe UIContext should do this?
                if (ClickStartVirtualPosition.HasValue) {
                    // If the user is drag-selecting multiple characters, we want to expand the selection
                    //  to cover all the character hitboxes touched by the mouse drag instead of just picking
                    //  the character(s) the positions were leaning towards. For clicks that just place the
                    //  caret on one side of a character, we honor the leaning value
                    var csvp = ClickStartVirtualPosition.Value;
                    var deltaBigEnough = Math.Abs(virtualPosition.X - csvp.X) >= 4;
                    bool? leanA = deltaBigEnough ? (virtualPosition.X < csvp.X) : (bool?)null,
                        leanB = deltaBigEnough ? (virtualPosition.X >= csvp.X) : (bool?)null;
                    // FIXME: This -1 shouldn't be needed
                    var a = MapVirtualPositionToCharacterIndex(csvp, leanA) ?? -1;
                    var b = MapVirtualPositionToCharacterIndex(virtualPosition, leanB) ?? -1;
                    
                    Selection = new Pair<int>(Math.Min(a, b), Math.Max(a, b));
                }

                if (name != UIContext.Events.MouseUp)
                    DisableAutoscrollUntil = (float)Time.Seconds + AutoscrollClickTimeout;

                return true;
            } else
                return false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (name == UIContext.Events.Click)
                return OnClick(Convert.ToInt32(args));
            else if (name == UIContext.Events.KeyPress)
                return OnKeyPress((KeyEventArgs)(object)args);
            return false;
        }

        protected bool OnKeyPress (KeyEventArgs evt) {
            Console.WriteLine("{0:X4} '{1}' {2}", (int)(evt.Char ?? '\0'), new String(evt.Char ?? '\0', 1), evt.Key);

            DisableAutoscrollUntil = 0;

            if (evt.Char.HasValue) {
                if (Selection.Second != Selection.First)
                    Builder.Remove(Selection.First, Selection.Second - Selection.First);
                Builder.Insert(Selection.First, evt.Char);
                Selection = new Pair<int>(Selection.First + 1, Selection.First + 1);
                Invalidate();
            } else if (evt.Key.HasValue) {
                switch (evt.Key.Value) {
                    case Keys.Back:
                        if (Selection.Second != Selection.First) {
                            Builder.Remove(Selection.First, Selection.Second - Selection.First);
                            Selection = new Pair<int>(Selection.First, Selection.First);
                        } else if (Selection.First > 0) {
                            int pos = Selection.First - 1, count = 1;
                            if (char.IsLowSurrogate(Builder[pos])) {
                                pos--; count++;
                            }
                            Builder.Remove(pos, count);
                            Selection = new Pair<int>(Selection.First - count, Selection.First - count);
                        }
                        Invalidate();
                        break;

                    case Keys.Left:
                    case Keys.Right:
                        HandleSelectionShift(evt.Key == Keys.Left ? -1 : 1, evt.Modifiers.Shift);
                        break;

                    case Keys.Home:
                    case Keys.End:
                        HandleSelectionShift(evt.Key == Keys.Home ? -99999 : 99999, evt.Modifiers.Shift);
                        break;
                }
            }

            return true;
        }

        private void HandleSelectionShift (int delta, bool grow) {
            if (delta < 0) {
                CurrentScrollBias = -1;
                Selection = new Pair<int>(
                    Selection.First + delta, 
                    grow ? Selection.Second : Selection.First + delta
                );
            } else {
                CurrentScrollBias = 1;
                var newOffset = Selection.Second + delta;
                if ((newOffset < Builder.Length) && char.IsLowSurrogate(Builder[newOffset]))
                    newOffset++;
                Selection = new Pair<int>(
                    grow ? Selection.First : newOffset, 
                    newOffset
                );
            }
        }

        protected bool OnClick (int clickCount) {
            // FIXME: Select current word, then entire textbox on triple click
            if (clickCount == 3) {
                DisableAutoscrollUntil = 0;
                Selection = new Pair<int>(0, Builder.Length);
                return true;
            } else if (clickCount == 2) {
                if (!ClickStartVirtualPosition.HasValue)
                    return false;

                var centerIndex = MapVirtualPositionToCharacterIndex(ClickStartVirtualPosition.Value, null);
                if (!centerIndex.HasValue)
                    return false;

                var boundary = Unicode.FindWordBoundary(Builder, centerIndex.Value);
                DisableAutoscrollUntil = 0;
                Selection = boundary;
                return true;
            }

            return false;
        }

        private void ColorizeSelection (
            ArraySegment<BitmapDrawCall> drawCalls, LayoutMarker? selection,
            UIOperationContext context, ControlStates state, IBaseDecorator selectionDecorator
        ) {
            Color? selectedColor = DynamicLayout.Color;
            selectionDecorator.GetTextSettings(context, state, out Material temp, out IGlyphSource temp2, ref selectedColor);
            var noColorizing = (selection == null) || 
                (selection.Value.Bounds == null) || 
                (_Selection.First == _Selection.Second) ||
                (selection.Value.GlyphCount == 0);
            for (int i = 0; i < drawCalls.Count; i++) {
                var color = noColorizing || ((i < selection.Value.FirstDrawCallIndex) || (i > selection.Value.LastDrawCallIndex))
                    ? DynamicLayout.Color
                    : Color.Black;
                drawCalls.Array[i + drawCalls.Offset].MultiplyColor = color;
            }
        }

        private Bounds? GetBoundsForSelection (LayoutMarker? selection) {
            if (selection == null)
                return null;
            else if (selection.Value.Bounds == null)
                return null;

            var sel = selection.Value.Bounds ?? default(Bounds);
            // If there's no text or something else bad happened, synthesize a selection rect
            if (sel.Size.Length() < 1)
                sel.BottomRight = sel.TopLeft + new Vector2(0, DynamicLayout.GlyphSource.LineSpacing);

            var hasRange = _Selection.First != _Selection.Second;

            // FIXME: Multiline
            if (!hasRange) {
                if (_Selection.First >= Builder.Length)
                    sel.TopLeft.X = sel.BottomRight.X;
                else
                    sel.BottomRight.X = sel.TopLeft.X;
            }

            return sel;
        }

        void UpdateScrollOffset (RectF contentBox, Bounds? selectionBounds, StringLayout layout) {
            var scrollOffset = ScrollOffset;
            float maxScrollValue = Math.Max(layout.Size.X - contentBox.Width + RightScrollMargin, 0);
            var viewportBox = contentBox;
            viewportBox.Position = scrollOffset;

            if (selectionBounds.HasValue) {
                var selBounds = selectionBounds.Value;
                var overflowX = Math.Max(selBounds.BottomRight.X + SelectionHorizontalScrollPadding - viewportBox.Extent.X, 0);
                var underflowX = Math.Max(viewportBox.Left - selBounds.TopLeft.X + SelectionHorizontalScrollPadding, 0);

                if (overflowX > 0) {
                    if (underflowX <= 0) {
                        scrollOffset.X += overflowX;
                    } else {
                        // FIXME: Do something sensible given the selection is too big for the viewport, like pick an appropriate edge to focus on
                    }
                } else if (underflowX > 0) {
                    scrollOffset.X -= underflowX;
                }
            }

            scrollOffset.X = Arithmetic.Clamp(scrollOffset.X, 0, maxScrollValue);
            scrollOffset.Y = 0;

            ScrollOffset = scrollOffset;
        }

        protected override void OnRasterize (UIOperationContext context, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, settings, decorations);

            MarkSelection();

            var selectionDecorator = context.DecorationProvider.Selection;
            var layout = UpdateLayout(context, settings, decorations, out Material textMaterial);
            var selection = MarkSelection();
            var selBounds = GetBoundsForSelection(selection);

            bool isAutoscrollTimeLocked = (context.AnimationTime < DisableAutoscrollUntil);
            bool isMouseInBounds = context.UIContext.MouseOver == this;
            if ((!isAutoscrollTimeLocked && !context.MouseButtonHeld) || !isMouseInBounds)
                UpdateScrollOffset(settings.ContentBox, selBounds, layout);

            var textOffset = (settings.ContentBox.Position - ScrollOffset).Floor();

            if (context.Pass != RasterizePasses.Content)
                return;

            if (selBounds.HasValue && 
                (
                    settings.State.HasFlag(ControlStates.Focused) || 
                    (_Selection.First != _Selection.Second)
                )
            ) {
                var selBox = (RectF)selBounds.Value;
                selBox.Position += textOffset;
                var selSettings = new DecorationSettings {
                    BackgroundColor = settings.BackgroundColor,
                    State = settings.State,
                    Box = selBox,
                    ContentBox = selBox
                };
                selectionDecorator.Rasterize(context, selSettings);
                context.Renderer.Layer += 1;
            }

            ColorizeSelection(layout.DrawCalls, selection, context, settings.State, selectionDecorator);

            context.Renderer.DrawMultiple(
                layout.DrawCalls, offset: textOffset,
                material: textMaterial, samplerState: RenderStates.Text
            );
        }
    }
}

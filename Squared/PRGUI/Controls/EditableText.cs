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

        public bool StripNewlines = true;

        public const float SelectionHorizontalScrollPadding = 40;
        public const float MinRightScrollMargin = 16, MaxRightScrollMargin = 64;
        public const float AutoscrollClickTimeout = 0.25f;
        public const float ScrollTurboThreshold = 420f, ScrollFastThreshold = 96f;
        public const float ScrollLimitPerFrameSlow = 6f, ScrollLimitPerFrameFast = 32f;

        public Vector2 ScrollOffset;

        protected DynamicStringLayout DynamicLayout = new DynamicStringLayout();
        protected StringBuilder Builder = new StringBuilder();
        protected Margins CachedPadding;

        private float DisableAutoscrollUntil;
        private int CurrentScrollBias = 1;
        private bool NextScrollInstant = true;

        private Vector2 LastLocalCursorPosition;

        private Vector2? ClickStartVirtualPosition = null;
        private Pair<int> _Selection;

        protected override bool ShouldClipContent => true;

        public EditableText ()
            : base () {
            DynamicLayout.Text = Builder;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            AcceptsTextInput = true;
        }

        public Pair<int> Selection {
            get => _Selection;
            set {
                SetSelection(value, 0);
                NextScrollInstant = true;
            }
        }

        public string SelectedText {
            get {
                if (Selection.First == Selection.Second)
                    return "";

                return Builder.ToString(Selection.First, Selection.Second - Selection.First);
            }
            set {
                var newRange = ReplaceRange(Selection, FilterInput(value));
                SetSelection(new Pair<int>(newRange.Second, newRange.Second), 1);
                NextScrollInstant = true;
            }
        }

        public string Text {
            get {
                return Builder.ToString();
            }
            set {
                // FIXME: Optimize the 'value hasn't changed' case
                Builder.Clear();
                Builder.Append(FilterInput(value));
                NextScrollInstant = true;
                ValueChanged();
            }
        }

        private void ValueChanged () {
            Invalidate();
            FireEvent(UIEvents.ValueChanged);
        }

        private string FilterInput (string input) {
            if (!Multiline) {
                var idx = input.IndexOfAny(new[] { '\r', '\n' });
                if (idx >= 0)
                    return input.Replace("\r", "").Replace("\n", " ");
            }

            return input;
        }

        private void SetSelection (Pair<int> value, int scrollBias) {
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

            CurrentScrollBias = scrollBias;
            _Selection = value;
            // Console.WriteLine("New selection is {0} biased {1}", value, scrollBias > 0 ? "right" : "left");
            Invalidate();
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

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // HACK: Populate various fields that we will use to compute minimum size
            UpdateLayout(context, new DecorationSettings(), context.DecorationProvider.EditableText, out Material temp);
            return base.OnGenerateLayoutTree(context, parent, existingKey);
        }

        protected LayoutMarker? MarkSelection () {
            // FIXME: Insertion mode highlight?
            var a = Selection.First;
            var b = Math.Max(Selection.Second - 1, a);
            return DynamicLayout.Mark(a, b);
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
            if (position.X < 0) {
                return 0;
            } else if (position.X > DynamicLayout.Get().Size.X) {
                return Builder.Length;
            }

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
                args.LocalPosition.X,
                Arithmetic.Clamp(args.LocalPosition.Y, 0, args.ContentBox.Height - 1)
            );

            var virtualPosition = position + ScrollOffset;

            if (name == UIEvents.MouseDown) {
                DisableAutoscrollUntil = (float)Time.Seconds + AutoscrollClickTimeout;

                ClickStartVirtualPosition = virtualPosition;
                var currentCharacter = MapVirtualPositionToCharacterIndex(virtualPosition, null);
                // If we're double-clicking inside the selection don't update it yet. FIXME: Bias
                if (currentCharacter.HasValue && !args.DoubleClicking)
                    SetSelection(new Pair<int>(currentCharacter.Value, currentCharacter.Value), 0);
                return true;
            } else if (
                (name == UIEvents.MouseDrag) ||
                (name == UIEvents.MouseUp)
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
                    bool? leanA = null, // deltaBigEnough ? (virtualPosition.X > csvp.X) : (bool?)null,
                        leanB = deltaBigEnough ? (virtualPosition.X > csvp.X) : (bool?)null;
                    // FIXME: This -1 shouldn't be needed
                    // Console.WriteLine("leanA={0}, leanB={1}", leanA, leanB);
                    var a = MapVirtualPositionToCharacterIndex(csvp, leanA) ?? -1;
                    var b = MapVirtualPositionToCharacterIndex(virtualPosition, leanB) ?? -1;

                    // FIXME: bias
                    int selectionBias = virtualPosition.X > csvp.X ? 1 : -1;
                    SetSelection(new Pair<int>(Math.Min(a, b), Math.Max(a, b)), selectionBias);
                }

                if (name != UIEvents.MouseUp)
                    DisableAutoscrollUntil = (float)Time.Seconds + AutoscrollClickTimeout;

                return true;
            } else
                return false;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (name == UIEvents.Click)
                return OnClick(Convert.ToInt32(args));
            else if (name == UIEvents.KeyPress)
                return OnKeyPress((KeyEventArgs)(object)args);
            return false;
        }

        private void MoveCaret (int characterIndex, int scrollBias) {
            SetSelection(new Pair<int>(characterIndex, characterIndex), scrollBias);
        }

        private void RemoveRange (Pair<int> range, bool fireEvent) {
            if (range.First >= range.Second)
                return;

            Builder.Remove(range.First, range.Second - range.First);
            if (fireEvent)
                ValueChanged();
        }

        private Pair<int> Insert (int offset, char newText) {
            Builder.Insert(offset, newText);
            ValueChanged();
            return new Pair<int>(offset, offset + 1);
        }

        private Pair<int> Insert (int offset, string newText) {
            Builder.Insert(offset, newText);
            ValueChanged();
            return new Pair<int>(offset, offset + newText.Length);
        }

        private Pair<int> ReplaceRange (Pair<int> range, char newText) {
            RemoveRange(range, false);
            return Insert(range.First, newText);
        }

        private Pair<int> ReplaceRange (Pair<int> range, string newText) {
            RemoveRange(range, false);
            return Insert(range.First, newText);
        }

        protected bool OnKeyPress (KeyEventArgs evt) {
            Console.WriteLine("{0:X4} '{1}' {2}", (int)(evt.Char ?? '\0'), new String(evt.Char ?? '\0', 1), evt.Key);

            DisableAutoscrollUntil = 0;

            if (evt.Char.HasValue) {
                if (evt.Modifiers.Control || evt.Modifiers.Alt)
                    return HandleHotKey(evt);

                if (!evt.Context.TextInsertionMode) {
                    if (Selection.Second == Selection.First)
                        SetSelection(new Pair<int>(Selection.First, Selection.First + 1), 1);
                }

                ReplaceRange(Selection, evt.Char.Value);
                MoveCaret(Selection.First + 1, 1);
                return true;
            } else if (evt.Key.HasValue) {
                switch (evt.Key.Value) {
                    case Keys.Delete:
                    case Keys.Back:
                        if (Selection.Second != Selection.First) {
                            RemoveRange(Selection, true);
                            MoveCaret(Selection.First, 1);
                        } else {
                            int pos = Selection.First, count = 1;
                            if (evt.Key.Value == Keys.Back)
                                pos -= 1;

                            if (pos < 0)
                                return true;
                            else if (pos >= Builder.Length)
                                return true;

                            if (char.IsLowSurrogate(Builder[pos])) {
                                if (evt.Key.Value == Keys.Back)
                                    pos--;
                                count++;
                            }

                            Builder.Remove(pos, count);
                            if (evt.Key.Value == Keys.Back)
                                MoveCaret(Selection.First - count, -1);

                            ValueChanged();
                        }
                        return true;

                    case Keys.Up:
                    case Keys.Down:
                        if (evt.Modifiers.Control)
                            HandleSelectionShift(evt.Key == Keys.Home ? -99999 : 99999, grow: evt.Modifiers.Shift, byWord: false);
                        else
                            ;// FIXME: Multiline
                        break;

                    case Keys.Left:
                    case Keys.Right:
                        HandleSelectionShift(evt.Key == Keys.Left ? -1 : 1, grow: evt.Modifiers.Shift, byWord: evt.Modifiers.Control);
                        return true;

                    case Keys.Home:
                    case Keys.End:
                        if (evt.Modifiers.Control)
                            ; // FIXME: Multiline
                        HandleSelectionShift(evt.Key == Keys.Home ? -99999 : 99999, grow: evt.Modifiers.Shift, byWord: false);
                        return true;

                    case Keys.Insert:
                        evt.Context.TextInsertionMode = !evt.Context.TextInsertionMode;
                        return true;

                    default:
                        if (evt.Modifiers.Control || evt.Modifiers.Alt)
                            return HandleHotKey(evt);

                        return false;
                }
            }

            return false;
        }

        private bool HandleHotKey (KeyEventArgs evt) {
            string keyString = (evt.Char.HasValue) ? new string(evt.Char.Value, 1) : evt.Key.ToString();
            keyString = keyString.ToLowerInvariant();

            switch (keyString) {
                case "a":
                    SetSelection(new Pair<int>(0, int.MaxValue), 1);
                    return true;
                case "c":
                case "x":
                    SDL2.SDL.SDL_SetClipboardText(SelectedText);
                    if (keyString == "x")
                        SelectedText = "";
                    return true;
                case "v":
                    SelectedText = SDL2.SDL.SDL_GetClipboardText();
                    return true;
                default:
                    Console.WriteLine(keyString);
                    break;
            }

            return false;
        }

        private int FindNextWordInDirection (int startingCharacter, int direction) {
            int searchPosition = startingCharacter;
            var boundary = new Pair<int>(0, Builder.Length);

            for (int i = 0; i < 3; i++) {
                boundary = Unicode.FindWordBoundary(Builder, searchFromCharacterIndex: searchPosition);
                var ch = Builder[boundary.First];

                if ((boundary.First == startingCharacter) || char.IsWhiteSpace(ch)) {
                    if (direction < 0) {
                        searchPosition = boundary.First - 1;
                    } else {
                        searchPosition = boundary.Second + 1;
                    }
                    // Console.WriteLine($"left==start || whitespace -> {searchPosition}");
                    continue;
                }

                if (direction > 0) {
                    if (boundary.First < startingCharacter) {
                        searchPosition = boundary.Second + 1;
                        // Console.WriteLine($"left<start -> {searchPosition}");
                        continue;
                    }
                }

                return boundary.First;
            }

            // FIXME: If the text has leading whitespace we won't ever jump to the beginning
            return (direction > 0) ? boundary.Second : boundary.First;
        }

        private void HandleSelectionShift (int delta, bool grow, bool byWord) {
            int anchor, extent;
            if (grow) {
                anchor = (CurrentScrollBias < 0) ? Selection.Second : Selection.First;
                extent = (CurrentScrollBias < 0) ? Selection.First : Selection.Second;
            } else {
                anchor = (delta < 0) ? Selection.First : Selection.Second;
                extent = anchor;
            }

            if (byWord) {
                int s = extent;
                extent = FindNextWordInDirection(extent, Math.Sign(delta));
                // Console.WriteLine($"FindNextWordInDirection({s}, {delta}) == {extent}");
            } else {
                extent += delta;
                if ((extent < Builder.Length) && (extent > 0) && char.IsLowSurrogate(Builder[extent]))
                    extent += delta;
            }

            int newBias = Math.Sign(extent - anchor);

            if (!grow) {
                // Pivoting from shift-arrow to arrow should reset the selection instead of moving the caret
                if (Selection.Second != Selection.First) {
                    extent = anchor;
                } else
                    anchor = extent;
            }

            SetSelection(
                new Pair<int>(Math.Min(anchor, extent), Math.Max(anchor, extent)),
                newBias
            );
        }

        protected bool OnClick (int clickCount) {
            // FIXME: Select current word, then entire textbox on triple click
            if (clickCount == 3) {
                DisableAutoscrollUntil = 0;
                SetSelection(new Pair<int>(0, Builder.Length), 1);
                return true;
            } else if (clickCount == 2) {
                if (!ClickStartVirtualPosition.HasValue)
                    return false;

                var centerIndex = MapVirtualPositionToCharacterIndex(ClickStartVirtualPosition.Value, null);
                if (!centerIndex.HasValue)
                    return false;

                var boundary = Unicode.FindWordBoundary(Builder, searchFromCharacterIndex: centerIndex.Value);
                DisableAutoscrollUntil = 0;
                // FIXME: Pick scroll bias based on which side of the word center we clicked
                SetSelection(boundary, 1);
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
            float maxScrollValue = Math.Max(
                layout.Size.X - contentBox.Width + 
                    (
                        layout.Size.X > (contentBox.Width - MinRightScrollMargin)
                            ? MaxRightScrollMargin
                            : MinRightScrollMargin
                    ), 0
            );
            var viewportBox = contentBox;
            viewportBox.Position = scrollOffset;
            var squashedViewportBox = viewportBox;
            squashedViewportBox.Left += SelectionHorizontalScrollPadding;
            squashedViewportBox.Width -= SelectionHorizontalScrollPadding * 2;
            if (squashedViewportBox.Width < 0)
                squashedViewportBox.Width = 0;

            if (selectionBounds.HasValue) {
                var selBounds = selectionBounds.Value;
                var overflowX = selBounds.BottomRight.X - squashedViewportBox.Extent.X;
                var underflowX = squashedViewportBox.Left - selBounds.TopLeft.X;

                // HACK: Suppress vibration for big selections, and bias the auto-fit to the edge that last changed
                if (selBounds.Size.X >= squashedViewportBox.Width) {
                    if (CurrentScrollBias >= 0)
                        underflowX = 0;
                    else
                        overflowX = 0;
                }

                var distance = Math.Max(overflowX, underflowX);
                float scrollLimit = (NextScrollInstant || distance >= ScrollFastThreshold)
                    ? (
                        (NextScrollInstant || distance >= ScrollTurboThreshold)
                        ? 99999
                        : ScrollLimitPerFrameFast
                    )
                    : ScrollLimitPerFrameSlow;

                if (overflowX > 0) {
                    if (underflowX <= 0) {
                        scrollOffset.X += Math.Min(overflowX, scrollLimit);
                        NextScrollInstant = false;
                    } else {
                        // FIXME: Do something sensible given the selection is too big for the viewport, like pick an appropriate edge to focus on
                    }
                } else if (underflowX > 0) {
                    scrollOffset.X -= Math.Min(underflowX, scrollLimit);
                    NextScrollInstant = false;
                }
            }

            scrollOffset.X = Arithmetic.Clamp(scrollOffset.X, 0, maxScrollValue);
            scrollOffset.Y = 0;

            ScrollOffset = scrollOffset;
        }

        internal Vector2 GetCursorPosition () {
            return LastLocalCursorPosition;
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
                    settings.State.IsFlagged(ControlStates.Focused) || 
                    (_Selection.First != _Selection.Second)
                )
            ) {
                var selBox = (RectF)selBounds.Value;
                selBox.Position += textOffset;

                LastLocalCursorPosition = selBox.Position - settings.Box.Position;

                if ((_Selection.First == _Selection.Second) && (context.UIContext.TextInsertionMode == false))
                    selBox.Width = 4;

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

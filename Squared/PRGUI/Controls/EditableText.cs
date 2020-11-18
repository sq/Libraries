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
    public class EditableText : Control, IScrollableControl, Accessibility.IReadingTarget {
        public const int ControlMinimumWidth = 300;

        public static readonly Menu ContextMenu = new Menu {
            Children = {
                new StaticText { Text = "Cut" },
                new StaticText { Text = "Copy" },
                new StaticText { Text = "Paste" },
                new StaticText { Text = "Select All" }
            }
        };

        // FIXME
        public const bool OptimizedClipping = true;
        public const bool Multiline = false;

        public bool StripNewlines = true;

        public const float SelectionHorizontalScrollPadding = 40;
        public const float MinRightScrollMargin = 16, MaxRightScrollMargin = 64;
        public const float AutoscrollClickTimeout = 0.25f;
        public const float ScrollTurboThreshold = 420f, ScrollFastThreshold = 96f;
        public const float ScrollLimitPerFrameSlow = 5.5f, ScrollLimitPerFrameFast = 32f;

        public HorizontalAlignment HorizontalAlignment = HorizontalAlignment.Left;

        public string Description;

        /// <summary>
        /// Pre-processes any new text being inserted
        /// </summary>
        public Func<string, string> StringFilter = null;
        /// <summary>
        /// Pre-processes any new characters being inserted. Return null to block insertion.
        /// </summary>
        public Func<char, char?> CharacterFilter = null;

        public Vector2 ScrollOffset { get; set; }
        protected Vector2 MinScrollOffset;
        protected Vector2? MaxScrollOffset;

        bool IScrollableControl.AllowDragToScroll => false;
        Vector2? IScrollableControl.MinScrollOffset => MinScrollOffset;
        Vector2? IScrollableControl.MaxScrollOffset => MaxScrollOffset;

        protected DynamicStringLayout DescriptionLayout = new DynamicStringLayout();
        protected DynamicStringLayout DynamicLayout = new DynamicStringLayout();
        protected StringBuilder Builder = new StringBuilder();
        protected Margins CachedPadding;

        private float DisableAutoscrollUntil;
        private int CurrentScrollBias = 1;
        private bool NextScrollInstant = true;

        protected Vector2 LastLocalCursorPosition;

        protected Vector2? ClickStartVirtualPosition = null;
        private Pair<int> _Selection;

        protected override bool ShouldClipContent {
            get {
                // HACK: If our content is smaller than our content box, disable clipping
                if (OptimizedClipping && (DynamicLayout.GlyphSource != null)) {
                    var contentBox = GetRect(Context.Layout, contentRect: true);
                    UpdateLayoutSettings();
                    var layout = DynamicLayout.Get();
                    // Extra padding for things like shadows/outlines on selection decorators
                    const float safeMargin = 4;
                    if (
                        (layout.UnconstrainedSize.X < (contentBox.Size.X - safeMargin)) &&
                        (layout.UnconstrainedSize.Y <= contentBox.Size.Y)
                    )
                        return false;
                    else
                        return true;
                }

                return true;
            }
        }

        protected Vector2 AlignmentOffset = Vector2.Zero;

        protected bool ClampVirtualPositionToTextbox = true;

        private bool IsChangingValue;

        public EditableText ()
            : base () {
            DynamicLayout.Text = Builder;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            AcceptsTextInput = true;
        }

        private bool _IntegerOnly, _DoubleOnly;

        public bool IntegerOnly {
            get => _IntegerOnly;
            set {
                if (value) {
                    _DoubleOnly = false;
                    CharacterFilter = (ch) =>
                        (char.IsNumber(ch) || (ch == '-'))
                            ? ch
                            : (char?)null;
                    StringFilter = (str) =>
                        int.TryParse(str, out int temp)
                            ? str
                            : null;
                } else if (_IntegerOnly) {
                    CharacterFilter = null;
                    StringFilter = null;
                }
                _IntegerOnly = value;
            }
        }

        public bool DoubleOnly {
            get => _DoubleOnly;
            set {
                if (value) {
                    _IntegerOnly = false;
                    CharacterFilter = (ch) =>
                        (char.IsNumber(ch) || (ch == '-') || (ch == '.') || (ch == 'e') || (ch == 'E'))
                            ? ch
                            : (char?)null;
                    StringFilter = (str) =>
                        double.TryParse(str, out double temp)
                            ? str
                            : null;
                } else if (_DoubleOnly) {
                    CharacterFilter = null;
                    StringFilter = null;
                }
                _DoubleOnly = value;
            }
        }

        public Pair<int> Selection {
            get => _Selection;
            set {
                SetSelection(value, 0);
                NextScrollInstant = true;
            }
        }

        protected Pair<int> ExpandedSelection {
            get {
                var result = _Selection;
                if (Builder.Length > 0) {
                    if ((result.Second > 0) && (result.Second < Builder.Length) && char.IsHighSurrogate(Builder[result.Second - 1]))
                        result.Second++;
                    if ((result.First < Builder.Length) && char.IsLowSurrogate(Builder[result.First]))
                        result.First--;

                    if (result.First < 0)
                        result.First = 0;
                    if (result.Second > Builder.Length)
                        result.Second = Builder.Length;
                }
                return result;
            }
        }

        public string SelectedText {
            get {
                if (Selection.First == Selection.Second)
                    return "";

                var sel = ExpandedSelection;
                var result = Builder.ToString(sel.First, sel.Second - sel.First);
                if (char.IsHighSurrogate(result[result.Length - 1]))
                    throw new Exception();
                return result;
            }
            set {
                var newRange = ReplaceRange(ExpandedSelection, FilterInput(value));
                SetSelection(new Pair<int>(newRange.Second, newRange.Second), 1);
                NextScrollInstant = true;
            }
        }

        protected void SetText (string newValue, bool bypassFilter) {
            // FIXME: Optimize the 'value hasn't changed' case
            if (!bypassFilter)
                newValue = FilterInput(newValue);
            Builder.Clear();
            Builder.Append(newValue);
            NextScrollInstant = true;
            NotifyValueChanged();
        }

        public string Text {
            get {
                return Builder.ToString();
            }
            set {
                SetText(value, false);
            }
        }

        protected void NotifyValueChanged () {
            if (IsChangingValue)
                return;

            SetSelection(_Selection, 0);

            IsChangingValue = true;
            try {
                Invalidate();
                OnValueChanged();
            } finally {
                IsChangingValue = false;
            }
        }

        protected virtual void OnValueChanged () {
            FireEvent(UIEvents.ValueChanged);
        }

        protected string FilterInput (string input) {
            if (StringFilter != null)
                input = StringFilter(input);

            if (!Multiline && (input != null)) {
                var idx = input.IndexOfAny(new[] { '\r', '\n' });
                if (idx >= 0)
                    return input.Replace("\r", "").Replace("\n", " ");
            }

            return input;
        }

        protected char? FilterInput (char input) {
            char? result = input;

            if (CharacterFilter != null)
                result = CharacterFilter(input);

            if ((result != null) && char.IsControl(result.Value))
                result = null;

            return result;
        }

        protected void SetSelection (Pair<int> value, int scrollBias) {
            value.First = Arithmetic.Clamp(value.First, 0, Builder.Length);
            value.Second = Arithmetic.Clamp(value.Second, value.First, Builder.Length);
            if (_Selection == value)
                return;

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
                /* FIXME: Why is this disabled?
                if ((value.Second < (Builder.Length - 1)) && char.IsHighSurrogate(Builder[value.Second]))
                    value.Second++;
                */
            }

            // Clamp again in case surrogate handling produced invalid outputs
            value.First = Arithmetic.Clamp(value.First, 0, Builder.Length);
            value.Second = Arithmetic.Clamp(value.Second, value.First, Builder.Length);

            if (_Selection == value)
                return;

            CurrentScrollBias = scrollBias;
            _Selection = value;
            // Console.WriteLine("New selection is {0} biased {1}", value, scrollBias > 0 ? "right" : "left");
            Invalidate();
            FireEvent(UIEvents.SelectionChanged, _Selection);
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

            pSRGBColor? color = null;
            decorations.GetTextSettings(context, settings.State, out material, out IGlyphSource font, ref color);
            CachedPadding = ComputePadding(context, decorations);

            if (font != null)
                DynamicLayout.GlyphSource = font;
            DynamicLayout.DefaultColor = color?.ToColor() ?? Color.White;

            return DynamicLayout.Get();
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.EditableText;
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
            if (DynamicLayout.GlyphSource == null)
                return;

            var lineHeight = DynamicLayout.GlyphSource.LineSpacing;
            var contentMinimumHeight = lineHeight * (Multiline ? 2 : 1) + CachedPadding.Y; // FIXME: Include padding
            minimumWidth = minimumWidth ?? ControlMinimumWidth;
            minimumHeight = Math.Max(minimumHeight ?? 0, contentMinimumHeight);
        }

        protected override ControlKey OnGenerateLayoutTree (UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // HACK: Populate various fields that we will use to compute minimum size
            UpdateLayout(context, new DecorationSettings(), context.DecorationProvider.EditableText, out Material temp);
            return base.OnGenerateLayoutTree(context, parent, existingKey);
        }

        protected LayoutMarker? MarkSelection () {
            // FIXME: Insertion mode highlight?
            var esel = ExpandedSelection;
            var a = esel.First;
            var b = Math.Max(esel.Second - 1, a);
            return DynamicLayout.Mark(a, b);
        }

        private LayoutHitTest? ImmediateHitTest (Vector2 virtualPosition) {
            var result = DynamicLayout.HitTest(virtualPosition);
            if (result.HasValue)
                return result;

            DynamicLayout.Get();
            return DynamicLayout.HitTest(virtualPosition);
        }

        /// <param name="virtualPosition">Local position ignoring scroll offset.</param>
        public int? CharacterIndexFromVirtualPosition (Vector2 virtualPosition, bool? leanOverride = null, bool forceClamp = false) {
            virtualPosition -= AlignmentOffset;
            var result = ImmediateHitTest(virtualPosition);
            if (virtualPosition.X < 0) {
                if (ClampVirtualPositionToTextbox || forceClamp)
                    return 0;
                else
                    return null;
            } else if (virtualPosition.X > DynamicLayout.Get().Size.X) {
                if (ClampVirtualPositionToTextbox || forceClamp)
                    return Builder.Length;
                else
                    return null;
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

        protected virtual bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.Click)
                return OnClick(args.SequentialClickCount);

            var position = new Vector2(
                args.LocalPosition.X,
                Arithmetic.Saturate(args.LocalPosition.Y, args.ContentBox.Height - 1)
            );

            var virtualPosition = position + ScrollOffset;
            var esel = ExpandedSelection;

            if (name == UIEvents.MouseDown) {
                DisableAutoscrollUntil = Context.Now + AutoscrollClickTimeout;

                ClickStartVirtualPosition = virtualPosition;
                var newCharacterIndex = CharacterIndexFromVirtualPosition(virtualPosition, null);

                // If we're double-clicking inside the selection don't update it yet. FIXME: Bias
                if (
                    newCharacterIndex.HasValue && 
                    !args.DoubleClicking &&
                    (
                        (args.Buttons == MouseButtons.Left) ||
                        (esel.First > newCharacterIndex.Value) ||
                        (esel.Second < newCharacterIndex.Value)
                    )
                ) {
                    SetSelection(new Pair<int>(newCharacterIndex.Value, newCharacterIndex.Value), 0);
                }
                return true;
            } else if (
                (name == UIEvents.MouseMove) ||
                (name == UIEvents.MouseUp)
            ) {
                if (args.PreviousButtons == MouseButtons.Left) {
                    // FIXME: Ideally we would just clamp the mouse coordinates into our rectangle instead of rejecting
                    //  coordinates outside our rect. Maybe UIContext should do this?
                    if (ClickStartVirtualPosition.HasValue) {
                        // If the user is drag-selecting multiple characters, we want to expand the selection
                        //  to cover all the character hitboxes touched by the mouse drag instead of just picking
                        //  the character(s) the positions were leaning towards. For clicks that just place the
                        //  caret on one side of a character, we honor the leaning value
                        var csvp = ClickStartVirtualPosition.Value;
                        var deltaBigEnough = Math.Abs(virtualPosition.X - csvp.X) >= 4;
                        var forceClamp = deltaBigEnough;
                        bool? leanA = null, // deltaBigEnough ? (virtualPosition.X > csvp.X) : (bool?)null,
                            leanB = deltaBigEnough ? (virtualPosition.X > csvp.X) : (bool?)null;
                        // FIXME: This -1 shouldn't be needed
                        // Console.WriteLine("leanA={0}, leanB={1}", leanA, leanB);
                        var _a = CharacterIndexFromVirtualPosition(csvp, leanA, forceClamp);
                        var _b = CharacterIndexFromVirtualPosition(virtualPosition, leanB, forceClamp);

                        if (_a.HasValue || ClampVirtualPositionToTextbox) {
                            var a = _a ?? -1;
                            var b = _b ?? -1;
                            // FIXME: bias
                            int selectionBias = virtualPosition.X > csvp.X ? 1 : -1;
                            SetSelection(new Pair<int>(Math.Min(a, b), Math.Max(a, b)), selectionBias);
                        }
                    }

                    if (name != UIEvents.MouseUp)
                        DisableAutoscrollUntil = Context.Now + AutoscrollClickTimeout;
                }

                // Right mouse button was released, show context menu
                if (
                    args.PreviousButtons.HasFlag(MouseButtons.Right) &&
                    !args.Buttons.HasFlag(MouseButtons.Right)
                ) {
                    if (ClampVirtualPositionToTextbox || CharacterIndexFromVirtualPosition(virtualPosition, null).HasValue)
                        ShowContextMenu(true);
                }

                return true;
            } else
                return false;
        }

        private void ShowContextMenu (bool forMouseEvent) {
            ContextMenu.Child<StaticText>(st => st.Text == "Cut").Enabled =
                ContextMenu.Child<StaticText>(st => st.Text == "Copy").Enabled =
                    Selection.First != Selection.Second;

            try {
                ContextMenu.Child<StaticText>(st => st.Text == "Paste").Enabled =
                    !string.IsNullOrEmpty(SDL2.SDL.SDL_GetClipboardText());
            } catch {
            }

            ContextMenu.Child<StaticText>(st => st.Text == "Select All").Enabled =
                Text.Length > 0;

            var menuResult = forMouseEvent
                    ? ContextMenu.Show(Context)
                    : ContextMenu.Show(Context, this);

            menuResult.RegisterOnComplete((_) => {
                if (menuResult.Result == null)
                    return;

                var item = menuResult.Result as StaticText;
                switch (item?.Text.ToString()) {
                    case "Cut":
                        CutSelection();
                        return;
                    case "Copy":
                        CopySelection();
                        return;
                    case "Paste":
                        Paste();
                        return;
                    case "Select All":
                        SelectAll();
                        return;
                }
            });
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs)
                return OnMouseEvent(name, (MouseEventArgs)(object)args);
            else if (name == UIEvents.KeyPress)
                return OnKeyPress((KeyEventArgs)(object)args);
            return false;
        }

        private void MoveCaret (int characterIndex, int scrollBias) {
            SetSelection(new Pair<int>(characterIndex, characterIndex), scrollBias);
        }

        protected void RemoveRange (Pair<int> range, bool fireEvent) {
            if (range.First >= range.Second)
                return;

            if (char.IsLowSurrogate(Builder[range.First]))
                range.First--;
            else if (char.IsHighSurrogate(Builder[range.Second - 1]))
                range.Second++;
            Builder.Remove(range.First, range.Second - range.First);
            if (fireEvent)
                NotifyValueChanged();
        }

        protected Pair<int> Insert (int offset, char newText) {
            var filtered = FilterInput(newText);
            if (!filtered.HasValue)
                return default(Pair<int>);

            Builder.Insert(offset, newText);
            NotifyValueChanged();
            return new Pair<int>(offset, offset + 1);
        }

        protected Pair<int> Insert (int offset, string newText) {
            var filtered = FilterInput(newText);
            if (filtered == null)
                return default(Pair<int>);

            Builder.Insert(offset, newText);
            NotifyValueChanged();
            return new Pair<int>(offset, offset + newText.Length);
        }

        protected Pair<int> ReplaceRange (Pair<int> range, char newText) {
            RemoveRange(range, false);
            return Insert(range.First, newText);
        }

        protected Pair<int> ReplaceRange (Pair<int> range, string newText) {
            RemoveRange(range, false);
            return Insert(range.First, newText);
        }

        protected virtual bool OnKeyPress (KeyEventArgs evt) {
            // Console.WriteLine("{0:X4} '{1}' {2}", (int)(evt.Char ?? '\0'), new String(evt.Char ?? '\0', 1), evt.Key);

            DisableAutoscrollUntil = 0;

            if (evt.Char.HasValue) {
                if (evt.Modifiers.Control || evt.Modifiers.Alt)
                    return HandleHotKey(evt);

                if (!evt.Context.TextInsertionMode) {
                    if (Selection.Second == Selection.First)
                        SetSelection(new Pair<int>(Selection.First, Selection.First + 1), 1);
                }

                ReplaceRange(ExpandedSelection, evt.Char.Value);
                MoveCaret(Selection.First + 1, 1);
                return true;
            } else if (evt.Key.HasValue) {
                switch (evt.Key.Value) {
                    case Keys.Apps:
                        ShowContextMenu(false);
                        return true;
                    case Keys.Delete:
                    case Keys.Back:
                        // FIXME: Ctrl-delete and Ctrl-backspace should eat entire words
                        if (Selection.Second != Selection.First) {
                            RemoveRange(ExpandedSelection, true);
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

                            NotifyValueChanged();
                        }
                        return true;

                    case Keys.Up:
                    case Keys.Down:
                        if (evt.Modifiers.Control)
                            AdjustSelection(evt.Key == Keys.Home ? -99999 : 99999, grow: evt.Modifiers.Shift, byWord: false);
                        else
                            ;// FIXME: Multiline
                        break;

                    case Keys.Left:
                    case Keys.Right:
                        AdjustSelection(evt.Key == Keys.Left ? -1 : 1, grow: evt.Modifiers.Shift, byWord: evt.Modifiers.Control);
                        return true;

                    case Keys.Home:
                    case Keys.End:
                        if (evt.Modifiers.Control)
                            ; // FIXME: Multiline
                        AdjustSelection(evt.Key == Keys.Home ? -99999 : 99999, grow: evt.Modifiers.Shift, byWord: false);
                        return true;

                    case Keys.Insert:
                        evt.Context.TextInsertionMode = !evt.Context.TextInsertionMode;
                        return true;
                    
                    // If we don't consume this event, the context will generate a click for it
                    case Keys.Space:
                        return true;

                    default:
                        if ((evt.Modifiers.Control || evt.Modifiers.Alt) && !UIContext.ModifierKeys.Contains(evt.Key.Value))
                            return HandleHotKey(evt);

                        return false;
                }
            }

            return false;
        }

        public void SelectNone () {
            SetSelection(new Pair<int>(int.MaxValue, int.MaxValue), 1);
        }

        public void SelectAll () {
            SetSelection(new Pair<int>(0, int.MaxValue), 1);
        }

        public void Paste () {
            try {
                SelectedText = SDL2.SDL.SDL_GetClipboardText();
            } catch {
            }
        }

        public void CopySelection () {
            SDL2.SDL.SDL_SetClipboardText(SelectedText);
        }

        public void CutSelection () {
            SDL2.SDL.SDL_SetClipboardText(SelectedText);
            SelectedText = "";
        }

        protected virtual bool HandleHotKey (KeyEventArgs evt) {
            string keyString = (evt.Char.HasValue) ? new string(evt.Char.Value, 1) : evt.Key.ToString();
            keyString = keyString.ToLowerInvariant();

            switch (keyString) {
                case "a":
                    SelectAll();
                    return true;
                case "c":
                case "x":
                    if (keyString == "x")
                        CutSelection();
                    else
                        CopySelection();
                    return true;
                case "v":
                    Paste();
                    return true;
                default:
                    Console.WriteLine($"Unhandled hotkey: {keyString}");
                    break;
            }

            return false;
        }

        private int FindNextWordInDirection (int startingCharacter, int direction) {
            int searchPosition = startingCharacter;
            var boundary = new Pair<int>(0, Builder.Length);

            for (int i = 0; i < 3; i++) {
                boundary = Unicode.FindWordBoundary(Builder, searchFromCharacterIndex: searchPosition);
                if (boundary.First < 0)
                    return 0;

                if ((i == 0) && (direction > 0) && (boundary.Second < startingCharacter))
                    return startingCharacter;

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
                } else if (boundary.First > startingCharacter) {
                    continue;
                } else {
                    return boundary.First;
                }
            }

            if (direction > 0) {
                // If we're stuck at the beginning of the very last word, jump to the end
                if (boundary.First <= startingCharacter)
                    return Builder.Length;
                else
                    return boundary.First;
            } else if (startingCharacter <= boundary.First) {
                // If we started at the first character of the first word, we want to jump to the beginning (whitespace)
                return 0;
            } else
                return boundary.First;
        }

        public void AdjustSelection (int delta, bool grow, bool byWord) {
            int anchor, extent;
            var esel = ExpandedSelection;
            if (grow) {
                anchor = (CurrentScrollBias < 0) ? esel.Second : esel.First;
                extent = (CurrentScrollBias < 0) ? esel.First : esel.Second;
            } else {
                anchor = (delta < 0) ? esel.First : esel.Second;
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

        protected virtual bool OnClick (int clickCount) {
            // FIXME: Select current word, then entire textbox on triple click
            if (clickCount == 3) {
                DisableAutoscrollUntil = 0;
                SetSelection(new Pair<int>(0, Builder.Length), 1);
                return true;
            } else if (clickCount == 2) {
                if (!ClickStartVirtualPosition.HasValue)
                    return false;

                var centerIndex = CharacterIndexFromVirtualPosition(ClickStartVirtualPosition.Value, null);
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
            pSRGBColor? selectedColor = DynamicLayout.Color;
            selectionDecorator.GetTextSettings(context, state, out Material temp, out IGlyphSource temp2, ref selectedColor);
            var selectedColorC = (selectedColor ?? Color.Black).ToColor();
            var nonSelectedColorC = (DynamicLayout.Color ?? Color.White);
            var noColorizing = (selection == null) || 
                (selection.Value.Bounds == null) || 
                (Selection.First == Selection.Second) ||
                (selection.Value.GlyphCount == 0);
            for (int i = 0; i < drawCalls.Count; i++) {
                var color = noColorizing || ((i < selection.Value.FirstDrawCallIndex) || (i > selection.Value.LastDrawCallIndex))
                    ? nonSelectedColorC
                    : selectedColorC;
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

            var hasRange = Selection.First != Selection.Second;

            // FIXME: Multiline
            if (!hasRange) {
                if (Selection.First >= Builder.Length)
                    sel.TopLeft.X = sel.BottomRight.X;
                else
                    sel.BottomRight.X = sel.TopLeft.X;
            }

            return sel;
        }

        void UpdateScrollOffset (RectF contentBox, Bounds? selectionBounds, StringLayout layout) {
            var scrollOffset = ScrollOffset;
            var isTooWide = layout.Size.X > (contentBox.Width - MinRightScrollMargin);
            float edgeScrollMargin = isTooWide
                ? MaxRightScrollMargin
                : MinRightScrollMargin;
            float minScrollValue = 0;
            float maxScrollValue = Math.Max(
                layout.Size.X - contentBox.Width + 
                    ((HorizontalAlignment == HorizontalAlignment.Left) ? edgeScrollMargin : 0), 
                minScrollValue
            );
            MinScrollOffset = new Vector2(minScrollValue, 0);
            MaxScrollOffset = new Vector2(maxScrollValue, 0);
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
                float scrollLimit = (
                    (NextScrollInstant || distance >= ScrollFastThreshold) && 
                    // If the user is adjusting the selection with the mouse we want to scroll slow no matter what
                    //  so that the selection doesn't get completely out of control
                    Context.MouseCaptured != this
                )
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

            scrollOffset.X = Arithmetic.Clamp(scrollOffset.X, minScrollValue, maxScrollValue);
            scrollOffset.Y = 0;

            ScrollOffset = scrollOffset;
        }

        internal Vector2 GetCursorPosition () {
            return LastLocalCursorPosition;
        }

        private void RasterizeDescription (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator myDecorations, float textExtentX) {
            var decorator = context.DecorationProvider.Description;
            if (decorator == null)
                return;

            var color = default(pSRGBColor?);
            decorator.GetTextSettings(context, settings.State, out Material material, out IGlyphSource font, ref color);
            if (material == null)
                return;
            if (font == null)
                return;
            if (color == null)
                return;

            DescriptionLayout.GlyphSource = font;
            DescriptionLayout.Text = Description;

            var descriptionLayout = DescriptionLayout.Get();
            var width = descriptionLayout.Size.X;
            var totalSize = width + textExtentX;

            float x;
            if (HorizontalAlignment != HorizontalAlignment.Left) {
                x = settings.ContentBox.Left;
            } else {
                x = settings.ContentBox.Extent.X - decorator.Margins.Right - width;
            }

            if (totalSize >= settings.ContentBox.Width)
                color *= 0.4f;

            var textCorner = new Vector2(x, settings.ContentBox.Top).Floor();
            renderer.DrawMultiple(
                descriptionLayout.DrawCalls, textCorner, 
                multiplyColor: color.Value.ToColor(), material: material
            );
            renderer.Layer += 1;
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            MarkSelection();

            var selectionDecorator = context.DecorationProvider.Selection;
            var layout = UpdateLayout(context, settings, decorations, out Material textMaterial);

            AlignmentOffset = Vector2.Zero;
            if (
                (HorizontalAlignment != HorizontalAlignment.Left) &&
                (layout.Size.X < settings.ContentBox.Width)
            ) {
                AlignmentOffset = new Vector2(
                    (settings.ContentBox.Width - layout.Size.X) * (HorizontalAlignment == HorizontalAlignment.Center ? 0.5f : 1.0f), 0
                );
            }

            var selection = MarkSelection();
            var selBounds = GetBoundsForSelection(selection);

            bool isAutoscrollTimeLocked = (context.Now < DisableAutoscrollUntil);
            bool isMouseInBounds = context.UIContext.MouseOver == this;
            if ((!isAutoscrollTimeLocked && !context.MouseButtonHeld) || !isMouseInBounds)
                UpdateScrollOffset(settings.ContentBox, selBounds, layout);

            var textOffset = (settings.ContentBox.Position - ScrollOffset + AlignmentOffset).Floor();

            // Draw in the Below pass for better batching
            if (context.Pass == RasterizePasses.Below) {
                if (Description != null)
                    RasterizeDescription(context, ref renderer, settings, decorations, layout.Size.X);
            }

            if (context.Pass != RasterizePasses.Content)
                return;

            if (selBounds.HasValue && 
                (
                    settings.State.IsFlagged(ControlStates.Focused) || 
                    (Selection.First != Selection.Second)
                )
            ) {
                var selBox = (RectF)selBounds.Value;
                selBox.Position += textOffset;

                LastLocalCursorPosition = selBox.Position - settings.Box.Position;

                if ((Selection.First == Selection.Second) && (context.UIContext.TextInsertionMode == false))
                    selBox.Width = 4;

                var selSettings = new DecorationSettings {
                    BackgroundColor = settings.BackgroundColor,
                    State = settings.State,
                    Box = selBox,
                    ContentBox = selBox
                };

                selectionDecorator.Rasterize(context, ref renderer, selSettings);
                renderer.Layer += 1;
            }

            ColorizeSelection(layout.DrawCalls, selection, context, settings.State, selectionDecorator);

            renderer.DrawMultiple(
                layout.DrawCalls, offset: textOffset,
                material: textMaterial, samplerState: RenderStates.Text
            );
        }

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                if (Description != null)
                    return $"Edit \"{Description}\". {Text}";
                else
                    return $"Edit. {Text}";
            }
        }

        Vector2 IScrollableControl.ScrollOffset => throw new NotImplementedException();

        void Accessibility.IReadingTarget.FormatValueInto (StringBuilder sb) {
            sb.Append(Text);
        }

        bool IScrollableControl.TrySetScrollOffset (Vector2 value, bool forUser) {
            ScrollOffset = value;
            return true;
        }
    }
}

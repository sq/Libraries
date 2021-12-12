using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class EditableText : Control, IScrollableControl, Accessibility.IReadingTarget, 
        IValueControl<string>, IAcceleratorSource, ISelectionBearer, IHasDescription
    {
        internal struct HistoryEntry {
            public ArraySegment<char> Text;
            public Pair<int> Selection;
        }

        internal class HistoryBuffer {
            public int Capacity = 100;

            private readonly List<HistoryEntry> Entries = new List<HistoryEntry>();

            public int Count => Entries.Count;

            public void Clear () {
                Entries.Clear();
            }

            private ArraySegment<char> Allocate (int length) {
                return new ArraySegment<char>(new char[length]);
            }

            public void PushFrom (StringBuilder source, Pair<int> selection) {
                var buffer = Allocate(source.Length);
                source.CopyTo(0, buffer.Array, buffer.Offset, source.Length);
                Entries.Add(new HistoryEntry {
                    Text = buffer,
                    Selection = selection
                });
                while (Entries.Count > Capacity)
                    Entries.RemoveAt(0);
            }

            public bool TryPopInto (StringBuilder destination, ref Pair<int> selection) {
                if (Entries.Count <= 0)
                    return false;

                var item = Entries[Entries.Count - 1];
                Entries.RemoveAt(Entries.Count - 1);
                destination.Clear();
                destination.Append(item.Text.Array, item.Text.Offset, item.Text.Count);
                selection = item.Selection;
                return true;
            }
        }

        public const int ControlMinimumWidth = 250;
        public bool DisableMinimumSize = false;

        public static readonly Menu ContextMenu = new Menu {
            Children = {
                new StaticText { Text = "Undo" },
                new StaticText { Text = "Cut" },
                new StaticText { Text = "Copy" },
                new StaticText { Text = "Paste" },
                new StaticText { Text = "Delete" },
                new StaticText { Text = "Select All" }
            }
        };

        // FIXME
        public const bool OptimizedClipping = true;
        public const bool Multiline = false;

        public bool AllowCopy = true;
        public bool AllowScroll = true;

        public bool StripNewlines = true;

        public const bool ClearHistoryOnFocusLoss = true;
        public bool SelectNoneOnFocusLoss = false, SelectAllOnFocus = false;

        public const float SelectionHorizontalScrollPadding = 40;
        public const float MinRightScrollMargin = 16, MaxRightScrollMargin = 64;
        public const float AutoscrollClickTimeout = 0.25f;
        public const float ScrollTurboThreshold = 420f, ScrollFastThreshold = 96f;
        public const float ScrollLimitPerFrameSlow = 5.5f, ScrollLimitPerFrameFast = 32f;

        public HorizontalAlignment HorizontalAlignment = HorizontalAlignment.Left;

        public string Description { get; set; }

        /// <summary>
        /// Pre-processes any new text being inserted
        /// </summary>
        public Func<string, string> StringFilter = null;
        /// <summary>
        /// Pre-processes any new characters being inserted. Return null to block insertion.
        /// </summary>
        public Func<char, char?> CharacterFilter = null;

        public Vector2 ScrollOffset { get; set; }
        protected bool ScrollOffsetSetByUser;
        protected Vector2 MinScrollOffset;
        protected Vector2? MaxScrollOffset;

        protected DynamicStringLayout DescriptionLayout = new DynamicStringLayout {
            HideOverflow = true,
            AlignToPixels = StaticTextBase.DefaultGlyphPixelAlignment
        };
        protected DynamicStringLayout DynamicLayout = new DynamicStringLayout {
            AlignToPixels = StaticTextBase.DefaultGlyphPixelAlignment
        };
        protected StringBuilder Builder = new StringBuilder();
        protected Margins CachedPadding;

        private float DisableAutoscrollUntil;
        private int CurrentScrollBias = 1;
        private bool NextScrollInstant = true;

        protected Vector2 LastLocalCursorPosition;

        protected Vector2? ClickStartVirtualPosition = null;
        private Pair<int> _Selection;
        private RectF? LastSelectionRect;

        protected Vector2 AlignmentOffset = Vector2.Zero;

        protected bool ClampVirtualPositionToTextbox = true;

        private bool IsChangingValue;

        private HistoryBuffer UndoBuffer = new HistoryBuffer(),
            RedoBuffer = new HistoryBuffer();

        public EditableText ()
            : base () {
            DynamicLayout.Text = Builder;
            AcceptsMouseInput = true;
            AcceptsFocus = true;
            AcceptsTextInput = true;
        }

        public bool Password {
            set {
                DynamicLayout.ReplacementCharacter = value ? '*' : (char?)null;
                AllowCopy = !value;
            }
            get => !AllowCopy && (DynamicLayout.ReplacementCharacter == '*');
        }

        protected override bool ShouldClipContent {
            get {
                // HACK: If our content is smaller than our content box, disable clipping
                if (OptimizedClipping && (DynamicLayout.GlyphSource != null)) {
                    var contentBox = GetRect(contentRect: true);
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
                    throw new Exception("Unpaired surrogate");
                return result;
            }
            set {
                var newRange = ReplaceRange(ExpandedSelection, FilterInput(value).ToString());
                SetSelection(new Pair<int>(newRange.Second, newRange.Second), 1);
                NextScrollInstant = true;
            }
        }

        private void PushUndoEntry () {
            RedoBuffer.Clear();
            PushHistoryEntry(UndoBuffer);
        }

        private void PushHistoryEntry (HistoryBuffer buffer) {
            buffer.PushFrom(Builder, _Selection);
        }

        public bool TryUndo () {
            Context.Log("Attempt Undo");

            if (UndoBuffer.Count <= 0)
                return false;

            PushHistoryEntry(RedoBuffer);

            if (UndoBuffer.TryPopInto(Builder, ref _Selection)) {
                Context.Log("Undo OK");
                NotifyValueChanged(true);
                return true;
            }

            return false;
        }

        public bool TryRedo () {
            Context.Log("Attempt Redo");

            if (RedoBuffer.Count <= 0)
                return false;

            PushHistoryEntry(UndoBuffer);

            if (RedoBuffer.TryPopInto(Builder, ref _Selection)) {
                Context.Log("Redo OK");
                NotifyValueChanged(true);
                return true;
            }

            return false;
        }

        public void ResetHistory () {
            UndoBuffer.Clear();
            RedoBuffer.Clear();
        }

        internal void SetText (AbstractString newValue, bool bypassFilter) {
            // FIXME: Optimize the 'value hasn't changed' case
            if (newValue.TextEquals(Builder, StringComparison.Ordinal))
                return;
            if (!bypassFilter) {
                newValue = FilterInput(newValue);
                if (newValue.TextEquals(Builder, StringComparison.Ordinal))
                    return;
            }
            ResetHistory();
            Builder.Clear();
            newValue.CopyTo(Builder);
            NextScrollInstant = true;
            NotifyValueChanged(false);
        }

        public void GetText (StringBuilder output) {
            Builder.CopyTo(output);
        }

        public ArraySegment<char> GetText (char[] output, int outputOffset = 0, int? outputLength = null) {
            var length = outputLength ?? Builder.Length;
            if (length > Builder.Length)
                length = Builder.Length;
            else if (length < 0)
                length = 0;
            Builder.CopyTo(0, output, outputOffset, length);
            return new ArraySegment<char>(output, outputOffset, length);
        }

        public string Text {
            get {
                return Builder.ToString();
            }
            set {
                SetText(value, false);
            }
        }

        protected void NotifyValueChanged (bool fromUserInput) {
            Invalidate();

            if (IsChangingValue)
                return;

            SetSelection(_Selection, 0);

            IsChangingValue = true;
            try {
                OnValueChanged(fromUserInput);
            } finally {
                IsChangingValue = false;
            }
        }

        protected virtual void OnValueChanged (bool fromUserInput) {
            FireEvent(UIEvents.ValueChanged, fromUserInput);
            if (fromUserInput)
                FireEvent(UIEvents.ValueChangedByUser, fromUserInput);
        }

        protected AbstractString FilterInput (AbstractString input) {
            if (StringFilter != null)
                input = StringFilter(input.ToString());

            if (!Multiline && (input != null)) {
                for (int i = 0, l = input.Length; i < l; i++) {
                    var ch = input[i];
                    // FIXME: Optimize this
                    if ((ch == '\r') || (ch == '\n'))
                        return input.ToString().Replace("\r", "").Replace("\n", " ");
                }
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
            ScrollOffsetSetByUser = false;
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
            ref UIOperationContext context, DecorationSettings settings, IDecorator decorations, out Material material
        ) {
            // HACK: Avoid accumulating too many extra hit tests from previous mouse positions
            // This will invalidate the layout periodically as the mouse moves, but whatever
            if (DynamicLayout.HitTests.Count > 8)
                DynamicLayout.ResetMarkersAndHitTests();

            UpdateLayoutSettings();

            Color? color = null;
            var font = decorations.GlyphSource;
            decorations.GetTextSettings(ref context, settings.State, out material, ref color);
            ComputeEffectiveSpacing(ref context, decorations, out CachedPadding, out Margins computedMargins);

            if (font != null)
                DynamicLayout.GlyphSource = font;
            DynamicLayout.DefaultColor = color ?? Color.White;

            return DynamicLayout.Get();
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.EditableText;
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            if (DynamicLayout.GlyphSource == null)
                return;

            ComputeEffectiveScaleRatios(context.DecorationProvider, out Vector2 paddingScale, out Vector2 marginScale, out Vector2 effectiveSizeScale);
            var lineHeight = DynamicLayout.GlyphSource.LineSpacing;
            var contentMinimumHeight = lineHeight * (Multiline ? 2 : 1) + CachedPadding.Y; // FIXME: Why is the padding value too big?
            if (!DisableMinimumSize)
                width.Minimum = width.Minimum ?? (ControlMinimumWidth * Context.Decorations.SizeScaleRatio.X);

            height.Minimum = Math.Max(height.Minimum ?? 0, contentMinimumHeight);
        }

        protected override ControlKey OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            // HACK: Populate various fields that we will use to compute minimum size
            UpdateLayout(ref context, new DecorationSettings(), context.DecorationProvider.EditableText, out Material temp);
            return base.OnGenerateLayoutTree(ref context, parent, existingKey);
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
            ContextMenu.Child<StaticText>(st => st.Text == "Undo").Enabled =
                UndoBuffer.Count > 0;

            ContextMenu.Child<StaticText>(st => st.Text == "Cut").Enabled =
                ContextMenu.Child<StaticText>(st => st.Text == "Copy").Enabled =
                (Selection.First != Selection.Second) && AllowCopy;

            ContextMenu.Child<StaticText>(st => st.Text == "Delete").Enabled =
                Selection.First != Selection.Second;

            try {
                ContextMenu.Child<StaticText>(st => st.Text == "Paste").Enabled =
                    !string.IsNullOrEmpty(SDL2.SDL.SDL_GetClipboardText());
            } catch {
            }

            ContextMenu.Child<StaticText>(st => st.Text == "Select All").Enabled =
                Text.Length > 0;

            Future<Control> menuResult;
            if (!forMouseEvent) {
                if (LastSelectionRect.HasValue) {
                    var myRect = GetRect();
                    LastSelectionRect.Value.Intersection(ref myRect, out RectF intersected);
                    menuResult = ContextMenu.Show(Context, intersected);
                } else
                    menuResult = ContextMenu.Show(Context, this);
            } else
                menuResult = ContextMenu.Show(Context);

            menuResult.RegisterOnComplete((_) => {
                if (menuResult.Result == null)
                    return;

                var item = menuResult.Result as StaticText;
                switch (item?.Text.ToString()) {
                    case "Undo":
                        TryUndo();
                        return;
                    case "Redo":
                        TryRedo();
                        return;
                    case "Cut":
                        CutSelection();
                        return;
                    case "Copy":
                        CopySelection();
                        return;
                    case "Delete":
                        Erase(true);
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
            if (args is MouseEventArgs ma)
                return OnMouseEvent(name, ma);
            else if (name == UIEvents.KeyPress)
                return OnKeyPress((KeyEventArgs)(object)args);
            else if (name == UIEvents.LostFocus) {
                if (args is Menu)
                    return false;

                if (ClearHistoryOnFocusLoss)
                    ResetHistory();
                if (SelectNoneOnFocusLoss)
                    SelectNone();
            } else if (name == UIEvents.GotFocus) {
                if (SelectAllOnFocus)
                    SelectAll();
            }
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

            PushUndoEntry();
            Builder.Remove(range.First, range.Second - range.First);
            if (fireEvent)
                NotifyValueChanged(true); // FIXME
        }

        protected Pair<int> Insert (int offset, char newText) {
            var filtered = FilterInput(newText);
            if (!filtered.HasValue)
                return default(Pair<int>);

            PushUndoEntry();
            Builder.Insert(offset, newText);
            NotifyValueChanged(true);
            return new Pair<int>(offset, offset + 1);
        }

        protected Pair<int> Insert (int offset, string newText) {
            var filtered = FilterInput(newText);
            if (filtered == null)
                return default(Pair<int>);

            if (filtered.Length > 0)
                PushUndoEntry();

            Builder.Insert(offset, filtered);
            NotifyValueChanged(true);
            return new Pair<int>(offset, offset + filtered.Length);
        }

        protected Pair<int> ReplaceRange (Pair<int> range, char newText) {
            RemoveRange(range, false);
            return Insert(range.First, newText);
        }

        protected Pair<int> ReplaceRange (Pair<int> range, string newText) {
            RemoveRange(range, false);
            return Insert(range.First, newText);
        }

        private void Erase (bool forward) {
            // FIXME: Ctrl-delete and Ctrl-backspace should eat entire words
            if (Selection.Second != Selection.First) {
                RemoveRange(ExpandedSelection, true);
                MoveCaret(Selection.First, 1);
            } else {
                int pos = Selection.First, count = 1;
                if (!forward)
                    pos -= 1;

                if (pos < 0)
                    return;
                else if (pos >= Builder.Length)
                    return;

                if (char.IsLowSurrogate(Builder[pos])) {
                    if (!forward)
                        pos--;
                    count++;
                }

                PushUndoEntry();
                Builder.Remove(pos, count);
                if (!forward)
                    MoveCaret(Selection.First - count, -1);

                NotifyValueChanged(true);
            }
        }

        protected virtual bool OnKeyPress (KeyEventArgs evt) {
            // Console.WriteLine("{0:X4} '{1}' {2}", (int)(evt.Char ?? '\0'), new String(evt.Char ?? '\0', 1), evt.Key);

            DisableAutoscrollUntil = 0;

            // If a dpad is used to move the cursor while the activate button is held, treat that
            //  as equivalent to a drag-select since it's sensible
            var shiftMode = evt.Modifiers.Shift || Context.CurrentInputState.ActivateKeyHeld;

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
                        Erase(evt.Key.Value == Keys.Delete);
                        return true;

                    case Keys.Up:
                    case Keys.Down:
                        if (evt.Modifiers.Control)
                            AdjustSelection(evt.Key == Keys.Home ? -99999 : 99999, grow: shiftMode, byWord: false);
                        else
                            ;// FIXME: Multiline
                        break;

                    case Keys.Left:
                    case Keys.Right:
                        AdjustSelection(evt.Key == Keys.Left ? -1 : 1, grow: shiftMode, byWord: evt.Modifiers.Control);
                        return true;

                    case Keys.Home:
                    case Keys.End:
                        if (evt.Modifiers.Control)
                            ; // FIXME: Multiline
                        AdjustSelection(evt.Key == Keys.Home ? -99999 : 99999, grow: shiftMode, byWord: false);
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
            if (!AllowCopy)
                return;
            SDL2.SDL.SDL_SetClipboardText(SelectedText);
        }

        public void CutSelection () {
            if (!AllowCopy)
                return;
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
                case "y":
                    TryRedo();
                    return true;
                case "z":
                    if (evt.Modifiers.Shift)
                        TryRedo();
                    else
                        TryUndo();
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
            UIOperationContext context, ControlStates state, IMetricsProvider selectionDecorator
        ) {
            Color? selectedColor = DynamicLayout.Color;
            selectionDecorator.GetTextSettings(ref context, state, out Material temp, ref selectedColor);
            var selectedColorC = context.UIContext.ConvertColor(selectedColor ?? Color.Black);
            var nonSelectedColorC = context.UIContext.ConvertColor(DynamicLayout.Color ?? Color.White);
            var noColorizing = (selection == null) || 
                (selection.Value.Bounds.Count < 1) || 
                (Selection.First == Selection.Second) ||
                (selection.Value.GlyphCount == 0);
            for (int i = 0; i < drawCalls.Count; i++) {
                var isSelected = !(noColorizing || ((i < selection.Value.FirstDrawCallIndex) || (i > selection.Value.LastDrawCallIndex)));
                var color = isSelected
                    ? selectedColorC
                    : nonSelectedColorC;
                drawCalls.Array[i + drawCalls.Offset].MultiplyColor = color;
                // HACK: Suppress shadow
                if (isSelected)
                    drawCalls.Array[i + drawCalls.Offset].UserData = new Vector4(0, 0, 0, 1 / 256f);
            }
        }

        private Bounds? GetBoundsForSelection (LayoutMarker? selection) {
            if (selection == null)
                return null;
            else if (selection.Value.Bounds.Count < 1)
                return null;

            var sel = selection.Value.UnionBounds;
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
            if (!AllowScroll) {
                ScrollOffset = Vector2.Zero;
                MaxScrollOffset = Vector2.Zero;
                return;
            }

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

            if (selectionBounds.HasValue && !ScrollOffsetSetByUser) {
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

        private void RasterizeDescription (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, float textExtentX) {
            var decorator = context.DecorationProvider.Description;
            if (decorator == null)
                return;

            var color = default(Color?);
            var font = decorator.GlyphSource;
            decorator.GetTextSettings(ref context, settings.State, out Material material, ref color);
            if (material == null)
                return;
            if (font == null)
                return;
            if (color == null)
                return;

            DescriptionLayout.GlyphSource = font;
            DescriptionLayout.SetText(Description, true, true);

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
                color *= 0.33f;

            float yAlignment = (settings.ContentBox.Height - descriptionLayout.UnconstrainedSize.Y) / 2f;
            var textCorner = new Vector2(x, settings.ContentBox.Top + yAlignment).Floor();
            renderer.DrawMultiple(
                descriptionLayout.DrawCalls, textCorner, 
                multiplyColor: color.Value, material: material
            );
            renderer.Layer += 1;
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref renderer, settings, decorations);

            MarkSelection();

            var selectionDecorator = context.DecorationProvider.Selection;
            var layout = UpdateLayout(ref context, settings, decorations, out Material textMaterial);

            AlignmentOffset = Vector2.Zero;
            if (
                (HorizontalAlignment != HorizontalAlignment.Left) &&
                (layout.Size.X < settings.ContentBox.Width)
            ) {
                AlignmentOffset = new Vector2(
                    (settings.ContentBox.Width - layout.Size.X) * (HorizontalAlignment == HorizontalAlignment.Center ? 0.5f : 1.0f), 0
                );
            }

            AlignmentOffset.Y += (settings.ContentBox.Height - layout.UnconstrainedSize.Y) / 2f;

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
                    RasterizeDescription(ref context, ref renderer, settings, layout.Size.X);
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
                    TextColor = settings.TextColor,
                    State = settings.State,
                    Box = selBox,
                    ContentBox = selBox
                };

                selectionDecorator.Rasterize(ref context, ref renderer, selSettings);
                renderer.Layer += 1;

                // HACK to ensure that we don't provide a null rect for the cursor if there's no selection
                if (selBox.Width <= 0) {
                    selBox.Left -= 0.5f;
                    selBox.Width = 1f;
                }
                LastSelectionRect = selBox;
            } else if (Selection.First == Selection.Second) {
                // FIXME: Why do we need to do this extra check? Certain values will cause us to null out here when we shouldn't
                LastSelectionRect = null;
            }

            ColorizeSelection(layout.DrawCalls, selection, context, settings.State, selectionDecorator);

            renderer.DrawMultiple(
                layout.DrawCalls, offset: textOffset,
                material: textMaterial, samplerState: RenderStates.Text
            );

            if (StaticTextBase.VisualizeLayout) {
                settings.ContentBox.SnapAndInset(out Vector2 ca, out Vector2 cb);
                renderer.RasterizeRectangle(ca, cb, 0f, 1f, Color.Transparent, Color.Transparent, outlineColor: Color.Blue, layer: 1);
                ca += AlignmentOffset;
                cb.Y += AlignmentOffset.Y;
                var h = layout.UnconstrainedSize.Y;
                var la = new Vector2(ca.X, ca.Y + h);
                var lb = new Vector2(cb.X, ca.Y + h);
                renderer.RasterizeLineSegment(la, lb, 1f, Color.Green, layer: 2);
            }
        }

        string IValueControl<string>.Value {
            get => Text;
            set => Text = value;
        }

        IEnumerable<AcceleratorInfo> IAcceleratorSource.Accelerators {
            get {
                var sb = new StringBuilder();
                if (Builder.Length > 0)
                    sb.AppendLine("Ctrl+A Select All");
                if ((Selection.First != Selection.Second) && AllowCopy) {
                    sb.AppendLine("Ctrl+X Cut");
                    sb.AppendLine("Ctrl+C Copy");
                }
                sb.AppendLine("Ctrl+V Paste");
                yield return new AcceleratorInfo(this, sb.ToString());
            }
        }

        bool ISelectionBearer.HasSelection => _Selection.First < _Selection.Second;
        RectF? ISelectionBearer.SelectionRect => LastSelectionRect;
        Control ISelectionBearer.SelectedControl => null;

        AbstractString Accessibility.IReadingTarget.Text {
            get {
                var text = Password ? "Masked password" : Text;
                if (Description != null)
                    return $"Edit \"{Description}\". {text}";
                else
                    return $"Edit. {text}";
            }
        }

        void IReadingTarget.FormatValueInto (StringBuilder sb) {
            // FIXME: Should value reading be disabled when the value is masked?
            if (Password)
                ;
            else
                sb.Append(Text);
        }

        Vector2 IScrollableControl.ScrollOffset => ScrollOffset;
        bool IScrollableControl.Scrollable {
            get => true;
            set {
            }
        }

        bool IScrollableControl.AllowDragToScroll => false;
        Vector2? IScrollableControl.MinScrollOffset => MinScrollOffset;
        Vector2? IScrollableControl.MaxScrollOffset => MaxScrollOffset;

        bool IScrollableControl.TrySetScrollOffset (Vector2 value, bool forUser) {
            if (forUser)
                ScrollOffsetSetByUser = true;
            ScrollOffset = value;
            return true;
        }

        public override string ToString () {
            var label = DebugLabel ?? Description;
            if (label != null)
                return $"{GetType().Name} #{GetHashCode():X8} '{label}'";
            else
                return $"{GetType().Name} #{GetHashCode():X8}";
        }
    }
}

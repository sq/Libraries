﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render.TextLayout2;
using Squared.Task;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Hash;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public sealed class DynamicStringLayout : IStringLayoutListener, IRichTextStateTracker {
        public struct MeasurementSettings {
            public float DesiredWidth;
            public float? LineBreakAtX,
                StopAtY;
            public int? CharacterLimit, 
                LineLimit,
                LineBreakLimit;

            public MeasurementSettings (DynamicStringLayout copyFrom) {
                DesiredWidth = copyFrom.DesiredWidth;
                LineBreakAtX = copyFrom.LineBreakAtX;
                StopAtY = copyFrom.StopAtY;
                CharacterLimit = copyFrom.CharacterLimit;
                LineLimit = copyFrom.LineLimit;
                LineBreakLimit = copyFrom.LineBreakLimit;
            }
        }

        private class Satellite {
            public DenseList<AbstractTextureReference> UsedTextures;
            public DenseList<AsyncRichImage> Dependencies;
            public Vector2? HitTest;
            public LayoutHitTest HitTestResult;
            public Pair<int>? MarkedRange;
            public LayoutMarker MarkedRangeResult;
            public DenseList<LayoutMarker> RichMarkers;
            public DenseList<Bounds> Boxes;

            public UnorderedList<LayoutMarker> LayoutEngineMarkerStorage;
        }

        [Flags]
        private enum InternalFlags : ushort {
            HasCachedStringLayout          = 0b1,
            WordWrap                       = 0b10,
            CharacterWrap                  = 0b100,
            ReverseOrder                   = 0b1000,
            MeasureOnly                    = 0b10000,
            RichText                       = 0b100000,
            HideOverflow                   = 0b1000000,
            RecordUsedTextures             = 0b10000000,
            ExpandHorizontallyWhenAligning = 0b100000000,
            SplitAtWrapCharactersOnly      = 0b1000000000,
            IncludeTrailingWhitespace      = 0b10000000000,
            AwaitingDependencies           = 0b100000000000,
            DisableMarkers                 = 0b1000000000000
        }

        private ArraySegment<BitmapDrawCall> _Buffer; 
        private StringLayout _CachedStringLayout;
        private int _CachedGlyphVersion = -1, _TextVersion = -1;
        private InternalFlags _Flags;

        private RichTextConfiguration _RichTextConfiguration;
        private string _StyleName;
        private Func<IGlyphSource> _GlyphSourceProvider;
        private IGlyphSource _GlyphSource;
        private AbstractString _Text, _TruncatedIndicator;
        private Vector2 _Position;
        private Color _DefaultColor;
        private Color? _Color;
        private float _Scale;
        private float _Spacing;
        private float _AdditionalLineSpacing;
        private float _SortKey;
        private float _DesiredWidth;
        private float? _MaxExpansionPerSpace;
        private float _MinRubyScale;
        private int _CharacterSkipCount;
        private int _CharacterLimit;
        private float _XOffsetOfFirstLine;
        private float _XOffsetOfNewLine;
        private float? _LineBreakAtX;
        private float? _StopAtY;
        private float _WrapIndentation;
        private float _ExtraLineBreakSpacing;
        private GlyphPixelAlignment _AlignToPixels = GlyphPixelAlignment.Default;
        private char _WrapCharacter;
        private byte _Alignment;
        private int _LineLimit;
        private int _LineBreakLimit;
        private int _TabSize;
        private char? _ReplacementCharacter;
        private char? _TerminatorCharacter;
        private uint[] _WordWrapCharacters;
        private DenseList<uint> _WordWrapCharacterTable;
        private Vector4 _CharacterUserData;
        private Vector4? _ImageUserData;
        private IStringLayoutListener _Listener;
        private IRichTextStateTracker _StateTracker;
        private object _RichTextUserData;

        private Satellite _Satellite;

        public int TextVersion => _TextVersion;

        public DynamicStringLayout (SpriteFont font, string text = "") {
            _GlyphSource = new SpriteFontGlyphSource(font);
            _Text = text;
            Reset();
        }

        public DynamicStringLayout (IGlyphSource font = null, string text = "") {
            _GlyphSource = font;
            _Text = text;
            Reset();
        }

        private bool GetFlag (InternalFlags flag) {
            return (_Flags & flag) == flag;
        }

        private bool? GetFlag (InternalFlags isSetFlag, InternalFlags valueFlag) {
            if ((_Flags & isSetFlag) != isSetFlag)
                return null;
            else
                return (_Flags & valueFlag) == valueFlag;
        }

        private void SetFlag (InternalFlags flag, bool state) {
            if (state)
                _Flags |= flag;
            else
                _Flags &= ~flag;
        }

        private bool ChangeFlag (InternalFlags flag, bool newState) {
            if (GetFlag(flag) == newState)
                return false;

            SetFlag(flag, newState);
            return true;
        }

        public void Reset () {
            RichTextConfiguration = default;
            _StyleName = null;
            TruncatedIndicator = default;
            Position = Vector2.Zero;
            DefaultColor = Microsoft.Xna.Framework.Color.White;
            Color = null;
            Scale = 1;
            Spacing = 1;
            SortKey = 0;
            CharacterSkipCount = 0;
            CharacterLimit = int.MaxValue;
            XOffsetOfFirstLine = 0;
            XOffsetOfNewLine = 0;
            DesiredWidth = 0;
            LineBreakAtX = null;
            StopAtY = null;
            WordWrap = false;
            CharacterWrap = true;
            WrapIndentation = 0f;
            AdditionalLineSpacing = 0f;
            ExtraLineBreakSpacing = 0f;
            AlignToPixels = GlyphPixelAlignment.Default;
            WrapCharacter = '\0';
            Alignment = HorizontalAlignment.Left;
            ReverseOrder = false;
            LineLimit = int.MaxValue;
            LineBreakLimit = int.MaxValue;
            TabSize = 4;
            // FIXME: Setting this to 1 by default because the way scaling works looks kind of screwed up ATM
            MinRubyScale = 1.0f;
            MeasureOnly = false;
            RichText = false;
            HideOverflow = false;
            RecordUsedTextures = false;
            ExpandHorizontallyWhenAligning = true;
            ReplacementCharacter = default;
            WordWrapCharacters = default;
            DisableMarkers = false;
            SetFlag(InternalFlags.AwaitingDependencies, false);
            _Satellite?.Dependencies.Clear();
            _Satellite?.RichMarkers.Clear();
            _Satellite?.Boxes.Clear();
            RichTextUserData = null;
            // FIXME: Clear listener?
        }

        public void ResetMarkersAndHitTests () {
            if (_Satellite?.MarkedRange != null) {
                _Satellite.MarkedRange = null;
                Invalidate();
            }
            if (_Satellite?.HitTest != null) {
                _Satellite.HitTest = null;
                Invalidate();
            }
        }

        private Satellite AutoAllocateSatellite () {
            if (_Satellite == null)
                _Satellite = new Satellite();
            return _Satellite;
        }

        public LayoutMarker MarkRange (int characterIndex) => MarkRange(characterIndex, characterIndex);

        public LayoutMarker MarkRange (int firstCharacterIndex, int lastCharacterIndex) {
            if (DisableMarkers)
                return default;

            var key = new Pair<int>(Math.Min(firstCharacterIndex, lastCharacterIndex), Math.Max(firstCharacterIndex, lastCharacterIndex));
            var satellite = AutoAllocateSatellite();
            if (satellite.MarkedRange == key)
                return satellite.MarkedRangeResult;

            satellite.MarkedRange = key;
            satellite.MarkedRangeResult = default;
            Invalidate();
            return satellite.MarkedRangeResult;
        }

        /// <summary>
        /// Specifies additional codepoints that will be treated as valid word separators for word wrap
        /// </summary>
        public uint[] WordWrapCharacters {
            get {
                return _WordWrapCharacters;
            }
            set {
                if (value == _WordWrapCharacters)
                    return;

                if (value != null) {
                    _WordWrapCharacterTable.Clear();
                    _WordWrapCharacterTable.AddRange(value);
                    _WordWrapCharacterTable.SortNonRef(Comparer<uint>.Default);
                    Invalidate();
                } else if (_WordWrapCharacterTable.Count != 0) {
                    _WordWrapCharacterTable.Clear();
                    Invalidate();
                }
                _WordWrapCharacters = value;
            }
        }

        private static readonly Dictionary<Pair<int>, LayoutMarker> EmptyMarkers = new Dictionary<Pair<int>, LayoutMarker>();

        public Vector2? HitTestLocation {
            get => _Satellite?.HitTest;
            set {
                if (value == null) {
                    if (_Satellite == null)
                        return;
                } else
                    AutoAllocateSatellite();

                if (_Satellite.HitTest == value)
                    return;
                _Satellite.HitTest = value;
                _Satellite.HitTestResult = default;
                Invalidate();
            }
        }
        public LayoutHitTest HitTestResult => _Satellite?.HitTestResult ?? default;
        public DenseList<LayoutMarker> RichMarkers => _Satellite?.RichMarkers ?? default;
        public DenseList<AbstractTextureReference> UsedTextures => _Satellite?.UsedTextures ?? default;
        public DenseList<Bounds> Boxes => _Satellite?.Boxes ?? default;
        public DenseList<AsyncRichImage> Dependencies => _Satellite?.Dependencies ?? default;

        private void InvalidatingNullableAssignment<T> (ref T? destination, T? newValue)
            where T : struct, IEquatable<T> {
            if (
                (destination.HasValue != newValue.HasValue) || 
                (destination.HasValue && !destination.Value.Equals(newValue.Value))
            ) {
                destination = newValue;
                Invalidate();
            }
        }

        private bool InvalidatingFlagAssignment (InternalFlags flag, bool newValue) {
            if (ChangeFlag(flag, newValue)) {
                Invalidate();
                return true;
            }
            return false;
        }

        private bool InvalidatingValueAssignment<T> (ref T destination, T newValue) 
            where T : struct, IEquatable<T>
        {
            if (!destination.Equals(newValue)) {
                destination = newValue;
                Invalidate();
                return true;
            }
            return false;
        }

        private void InvalidatingReferenceAssignment<T> (ref T destination, T newValue)
            where T : class
        {
            if (destination != newValue) {
                destination = newValue;
                Invalidate();
            }
        }   

        public ArraySegment<BitmapDrawCall> Buffer {
            get {
                return _Buffer;
            }
            set {
                _Buffer = value;
            }
        }

        public void EnsureBufferCapacity (int size) {
            size = ((Math.Max(size, 0) + 7) / 8) * 8;
            if ((_Buffer.Array == null) || (_Buffer.Count < size))
                _Buffer = new ArraySegment<BitmapDrawCall>(new BitmapDrawCall[size]);
        }

        /// <summary>
        /// Update the text stored in this layout.
        /// </summary>
        /// <param name="newText">The new string</param>
        /// <param name="compareText">
        /// If true, the new and existing strings will have their content 
        /// compared (if possible) to skip invalidating the layout.
        /// </param>
        /// <returns>true if the text was updated and the layout was invalidated.</returns>
        public bool SetText (AbstractString newText, bool compareText = true) {
            uint? newHash = null;
            if (
                compareText && 
                _Text.IsImmutable && 
                newText.IsImmutable &&
                (_Text.Length == newText.Length)
            ) {
                if (_Text.TextEquals(newText, StringComparison.Ordinal))
                    return false;
            }

            _Text = newText;
            unchecked {
                _TextVersion++;
            }
            Invalidate();
            return true;
        }

        public AbstractString Text {
            get {
                return _Text;
            }
            set {
                SetText(value, true);
            }
        }

        public AbstractString TruncatedIndicator {
            get {
                return _TruncatedIndicator;
            }
            set {
                InvalidatingValueAssignment(ref _TruncatedIndicator, value);
            }
        }

        public object RichTextUserData {
            get => _RichTextUserData;
            set => InvalidatingReferenceAssignment(ref _RichTextUserData, value);
        }

        public IStringLayoutListener Listener {
            get => _Listener;
            set {
                InvalidatingReferenceAssignment(ref _Listener, value);
                InvalidatingReferenceAssignment(ref _StateTracker, value as IRichTextStateTracker);
            }
        }

        public SpriteFont Font {
            get {
                if (_GlyphSource is SpriteFontGlyphSource)
                    return ((SpriteFontGlyphSource)_GlyphSource).Font;
                else
                    return null;
            }
            set {
                if (value == null)
                    throw new ArgumentNullException("value");

                _GlyphSourceProvider = null;
                InvalidatingReferenceAssignment(
                    ref _GlyphSource, 
                    new SpriteFontGlyphSource(value)
                );
            }
        }

        public Func<IGlyphSource> GlyphSourceProvider {
            get => _GlyphSourceProvider;
            set {
                if (value == _GlyphSourceProvider)
                    return;

                if (value != null) {
                    _GlyphSourceProvider = value;
                    _GlyphSource = value();
                } else {
                    _GlyphSourceProvider = null;
                }
            }
        }

        public IGlyphSource GlyphSource {
            get {
                if (
                    (_GlyphSourceProvider != null) &&
                    ((_GlyphSource == null) || (_GlyphSource.IsDisposed))
                ) {
                    Invalidate();
                    return _GlyphSource = _GlyphSourceProvider();
                } else
                    return _GlyphSource;
            }
            set {
                if (value == null)
                    throw new ArgumentNullException("value");
                _GlyphSourceProvider = null;
                InvalidatingReferenceAssignment(ref _GlyphSource, value);
            }
        }

        public Vector2 Position {
            get {
                return _Position;
            }
            set {
                InvalidatingValueAssignment(ref _Position, value);
            }
        }

        public Color DefaultColor {
            get {
                return _DefaultColor;
            }
            set {
                InvalidatingValueAssignment(ref _DefaultColor, value);
            }
        }

        public Color? Color {
            get {
                return _Color;
            }
            set {
                InvalidatingNullableAssignment(ref _Color, value);
            }
        }

        public float Scale {
            get {
                return _Scale;
            }
            set {
                InvalidatingValueAssignment(ref _Scale, value);
            }
        }

        public float Spacing {
            get {
                return _Spacing;
            }
            set {
                InvalidatingValueAssignment(ref _Spacing, value);
            }
        }

        public float AdditionalLineSpacing {
            get {
                return _AdditionalLineSpacing;
            }
            set {
                InvalidatingValueAssignment(ref _AdditionalLineSpacing, value);
            }
        }

        public float SortKey {
            get {
                return _SortKey;
            }
            set {
                InvalidatingValueAssignment(ref _SortKey, value);
            }
        }

        /// <summary>
        /// Skips one or more characters before beginning layout
        /// </summary>
        public int CharacterSkipCount {
            get {
                return _CharacterSkipCount;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterSkipCount, value);
            }
        }

        /// <summary>
        /// After this many characters are laid out (including non-printable characters), any further text will be hidden
        /// </summary>
        public int CharacterLimit {
            get {
                return _CharacterLimit;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterLimit, value);
            }
        }

        /// <summary>
        /// The first line in the layout will start at this X offset
        /// </summary>
        public float XOffsetOfFirstLine {
            get {
                return _XOffsetOfFirstLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfFirstLine, value);
            }
        }

        /// <summary>
        /// Each new line (after a line break, not wrapping) will start at this X offset
        /// </summary>
        public float XOffsetOfNewLine {
            get {
                return _XOffsetOfNewLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfNewLine, value);
            }
        }

        /// <summary>
        /// Any characters with an X offset exceeding this value will either be wrapped or hidden
        /// </summary>
        public float? LineBreakAtX {
            get {
                return _LineBreakAtX;
            }
            set {
                InvalidatingNullableAssignment(ref _LineBreakAtX, value);
            }
        }

        /// <summary>
        /// When using alignment modes this sets the desired width for the laid out string, and if
        ///  the string is less wide than the desired value it will be expanded. If the string is
        ///  wider, it will not be compressed - use LineBreakAtX for that.
        /// </summary>
        public float DesiredWidth {
            get {
                return _DesiredWidth;
            }
            set {
                InvalidatingValueAssignment(ref _DesiredWidth, value);
            }
        }

        /// <summary>
        /// When using justify alignment modes, the amount of whitespace added to each gap
        ///  in a line of text will not exceed this amount.
        /// </summary>
        public float? MaxExpansionPerSpace {
            get {
                return _MaxExpansionPerSpace;
            }
            set {
                InvalidatingNullableAssignment(ref _MaxExpansionPerSpace, value);
            }
        }

        /// <summary>
        /// When ruby text is larger than its anchor, the layout engine will automatically
        ///  attempt to shrink it to fit. This property sets a minimum scale for the text.
        /// </summary>
        public float MinRubyScale {
            get {
                return _MinRubyScale;
            }
            set {
                InvalidatingValueAssignment(ref _MinRubyScale, value);
            }
        }

        /// <summary>
        /// Any characters with a Y offset exceeding this value will be hidden
        /// </summary>
        public float? StopAtY {
            get {
                return _StopAtY;
            }
            set {
                InvalidatingNullableAssignment(ref _StopAtY, value);
            }
        }

        /// <summary>
        /// Tab characters (\t) will have a width of this many spaces
        /// </summary>
        public int TabSize {
            get {
                return _TabSize;
            }
            set {
                InvalidatingValueAssignment(ref _TabSize, value);
            }
        }

        /// <summary>
        /// After this many lines (after wrapping) are laid out, any further text will be hidden
        /// </summary>
        public int LineLimit {
            get {
                return _LineLimit;
            }
            set {
                InvalidatingValueAssignment(ref _LineLimit, value);
            }
        }

        /// <summary>
        /// After this many line breaks (i.e. \n) are encountered, any further text will be hidden
        /// </summary>
        public int LineBreakLimit {
            get {
                return _LineBreakLimit;
            }
            set {
                InvalidatingValueAssignment(ref _LineBreakLimit, value);
            }
        }

        public RichTextConfiguration RichTextConfiguration {
            get {
                return _RichTextConfiguration;
            }
            set {
                InvalidatingReferenceAssignment(ref _RichTextConfiguration, value);
            }
        }

        public bool RichText {
            get => GetFlag(InternalFlags.RichText);
            set => InvalidatingFlagAssignment(InternalFlags.RichText, value);
        }

        public string StyleName {
            get {
                return _StyleName;
            }
            set {
                InvalidatingReferenceAssignment(ref _StyleName, value);
            }
        }

        /// <summary>
        /// Any characters outside of the layout region will be hidden but still participate in layout/measurement
        /// </summary>
        public bool HideOverflow {
            get => GetFlag(InternalFlags.HideOverflow);
            set => InvalidatingFlagAssignment(InternalFlags.HideOverflow, value);
        }

        /// <summary>
        /// Attempt to wrap words to the next line when they extend past the wrap boundary
        /// </summary>
        public bool WordWrap {
            get => GetFlag(InternalFlags.WordWrap);
            set => InvalidatingFlagAssignment(InternalFlags.WordWrap, value);
        }

        /// <summary>
        /// Attempt to wrap characters to the next line when they extend past the wrap boundary
        /// </summary>
        public bool CharacterWrap {
            get => GetFlag(InternalFlags.CharacterWrap);
            set => InvalidatingFlagAssignment(InternalFlags.CharacterWrap, value);
        }

        /// <summary>
        /// Does not generate a table of draw calls, only measures the text
        /// </summary>
        public bool MeasureOnly {
            get => GetFlag(InternalFlags.MeasureOnly);
            set {
                // If we weren't in measurement-only mode, transitioning into it doesn't
                //  need to invalidate anything since all we'd do is throw away data
                if (!GetFlag(InternalFlags.MeasureOnly))
                    InvalidatingFlagAssignment(InternalFlags.MeasureOnly, value);
                else
                    SetFlag(InternalFlags.MeasureOnly, value);
            }
        }

        /// <summary>
        /// Does not generate the markers table even if there are marked strings
        /// </summary>
        public bool DisableMarkers {
            get => GetFlag(InternalFlags.DisableMarkers);
            set {
                if (value)
                    InvalidatingFlagAssignment(InternalFlags.DisableMarkers, value);
                else
                    SetFlag(InternalFlags.DisableMarkers, value);
            }
        }

        /// <summary>
        /// Aligns the entire layout (not single lines) horizontally after layout is complete
        /// </summary>
        public HorizontalAlignment Alignment {
            get {
                return (HorizontalAlignment)_Alignment;
            }
            set {
                InvalidatingValueAssignment(ref _Alignment, (byte)value);
            }
        }

        public char? WrapCharacter {
            get {
                return (_WrapCharacter == '\0') ? null : (char?)_WrapCharacter;
            }
            set {
                if (value.HasValue)
                    InvalidatingValueAssignment(ref _WrapCharacter, value.Value);
                else
                    InvalidatingValueAssignment(ref _WrapCharacter, '\0');
            }
        }

        /// <summary>
        /// When wrapping shifts text to a new line, it will have this X offset
        /// NOTE: Only valid if WordWrap is also true
        /// </summary>
        public float WrapIndentation {
            get {
                return _WrapIndentation;
            }
            set {
                InvalidatingValueAssignment(ref _WrapIndentation, value);
            }
        }

        /// <summary>
        /// Inserts additional space between lines after a line break
        /// </summary>
        public float ExtraLineBreakSpacing {
            get {
                return _ExtraLineBreakSpacing;
            }
            set {
                InvalidatingValueAssignment(ref _ExtraLineBreakSpacing, value);
            }
        }

        public GlyphPixelAlignment AlignToPixels {
            get {
                return _AlignToPixels;
            }
            set {
                InvalidatingValueAssignment(ref _AlignToPixels, value);
            }
        }

        public bool ReverseOrder {
            get => GetFlag(InternalFlags.ReverseOrder);
            set => InvalidatingFlagAssignment(InternalFlags.ReverseOrder, value);
        }

        /// <summary>
        /// If set, all characters in the string will be replaced by this character. Useful for censoring.
        /// </summary>
        public char? ReplacementCharacter {
            get {
                return _ReplacementCharacter;
            }
            set {
                InvalidatingNullableAssignment(ref _ReplacementCharacter, value);
            }
        }

        /// <summary>
        /// If set, layout will stop the first time this character is encountered.
        /// You can use this to produce truncated "previews" of larger blocks of text using non-printable markers.
        /// </summary>
        public char? TerminatorCharacter {
            get {
                return _TerminatorCharacter;
            }
            set {
                InvalidatingNullableAssignment(ref _TerminatorCharacter, value);
            }
        }

        /// <summary>
        /// If set, every texture used in the layout will be recorded so you can use it for tracking and lifetime management.
        /// </summary>
        public bool RecordUsedTextures {
            get => GetFlag(InternalFlags.RecordUsedTextures);
            set => InvalidatingFlagAssignment(InternalFlags.RecordUsedTextures, value);
        }

        /// <summary>
        /// When horizontally aligning text, the layout will automatically be expanded to reach its line break point
        /// </summary>
        public bool ExpandHorizontallyWhenAligning {
            get => GetFlag(InternalFlags.ExpandHorizontallyWhenAligning);
            set => InvalidatingFlagAssignment(InternalFlags.ExpandHorizontallyWhenAligning, value);
        }

        /// <summary>
        /// When justifying or wrapping text, only characters in the WordWrapCharacters list will be treated as split/wrap points
        /// </summary>
        public bool SplitAtWrapCharactersOnly {
            get => GetFlag(InternalFlags.SplitAtWrapCharactersOnly);
            set => InvalidatingFlagAssignment(InternalFlags.SplitAtWrapCharactersOnly, value);
        }

        /// <summary>
        /// Trailing whitespace will be included in the layout
        /// </summary>
        public bool IncludeTrailingWhitespace {
            get => GetFlag(InternalFlags.IncludeTrailingWhitespace);
            set => InvalidatingFlagAssignment(InternalFlags.IncludeTrailingWhitespace, value);
        }

        /// <summary>
        /// If set, text draw calls will have their UserData value set to this
        /// </summary>
        public Vector4 UserData {
            get {
                return _CharacterUserData;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterUserData, value);
            }
        }

        /// <summary>
        /// If set, image draw calls will have their UserData value set to this
        /// </summary>
        public Vector4? ImageUserData {
            get {
                return _ImageUserData;
            }
            set {
                InvalidatingNullableAssignment(ref _ImageUserData, value);
            }
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (
                    GetFlag(InternalFlags.HasCachedStringLayout) && 
                    (_CachedGlyphVersion >= _GlyphSource.Version) && 
                    !_GlyphSource.IsDisposed
                );
            }
        }

        public void Invalidate () {
            // Hey, you're the boss
            SetFlag(InternalFlags.HasCachedStringLayout, false);
            _CachedStringLayout = default;
            _Satellite?.UsedTextures.Clear();
            _Satellite?.RichMarkers.Clear();
        }

        /// <summary>
        /// Constructs a layout engine based on this configuration
        /// </summary>
        public void MakeLayoutEngine (out StringLayoutEngine result, MeasurementSettings? measureOnly = null) {
            if (_GlyphSource == null)
                throw new ArgumentNullException("GlyphSource");

            _Satellite?.UsedTextures.Clear();

            result = new StringLayoutEngine {
                position = _Position,
                overrideColor = _Color,
                defaultColor = _DefaultColor,
                scale = _Scale,
                spacing = _Spacing,
                additionalLineSpacing = _AdditionalLineSpacing,
                sortKey = _SortKey,
                desiredWidth = measureOnly.HasValue ? measureOnly.Value.DesiredWidth : _DesiredWidth,
                maxExpansionPerSpace = _MaxExpansionPerSpace,
                characterSkipCount = _CharacterSkipCount,
                characterLimit = measureOnly.HasValue ? measureOnly.Value.CharacterLimit : _CharacterLimit,
                xOffsetOfFirstLine = _XOffsetOfFirstLine,
                xOffsetOfWrappedLine = _WrapIndentation,
                xOffsetOfNewLine = _XOffsetOfNewLine,
                extraLineBreakSpacing = _ExtraLineBreakSpacing,
                lineBreakAtX = measureOnly.HasValue ? measureOnly.Value.LineBreakAtX : _LineBreakAtX,
                stopAtY =    _StopAtY,
                characterWrap = CharacterWrap,
                wordWrap = WordWrap,
                hideOverflow = HideOverflow,
                alignment = (HorizontalAlignment)_Alignment,
                reverseOrder = ReverseOrder,
                lineLimit = measureOnly.HasValue ? measureOnly.Value.LineLimit : _LineLimit,
                lineBreakLimit = measureOnly.HasValue ? measureOnly.Value.LineBreakLimit : _LineBreakLimit,
                measureOnly = measureOnly.HasValue || MeasureOnly,
                disableMarkers = DisableMarkers,
                replacementCodepoint = _ReplacementCharacter,
                recordUsedTextures = RecordUsedTextures,
                usedTextures = _Satellite?.UsedTextures ?? default,
                expandHorizontallyWhenAligning = ExpandHorizontallyWhenAligning,
                splitAtWrapCharactersOnly = SplitAtWrapCharactersOnly,
                includeTrailingWhitespace = IncludeTrailingWhitespace,
                userData = _CharacterUserData,
                imageUserData = _ImageUserData ?? _CharacterUserData,
                clearUserData = _CharacterUserData != default,
                WordWrapCharacters = _WordWrapCharacterTable,
            };

            result.Markers.UseExistingStorage(_Satellite?.LayoutEngineMarkerStorage, false);

            result.SetBuffer(_Buffer, true);

            if (_Satellite?.MarkedRange != null)
                result.Markers.Add(new LayoutMarker(_Satellite.MarkedRange.Value.First, _Satellite.MarkedRange.Value.Second));

            if (_Satellite?.HitTest != null)
                result.HitTests.Add(new LayoutHitTest { Position = _Satellite.HitTest.Value });
        }

        public void MakeLayoutEngine2 (out StringLayoutEngine2 result, MeasurementSettings? measureOnly = null, bool performingTextOutput = false) {
            if (!performingTextOutput && (_GlyphSource == null))
                throw new ArgumentNullException("GlyphSource");

            result = new StringLayoutEngine2 {
                Position = _Position,
                OverrideColor = _Color.HasValue,
                MultiplyColor = _Color ?? _DefaultColor,
                Scale = new Vector2(_Scale),
                Spacing = new Vector2(_Spacing),
                AdditionalLineSpacing = _AdditionalLineSpacing,
                SortKey = _SortKey,
                DesiredWidth = measureOnly.HasValue ? measureOnly.Value.DesiredWidth : _DesiredWidth,
                MaxExpansionPerSpace = _MaxExpansionPerSpace ?? float.MaxValue,
                CharacterSkipCount = _CharacterSkipCount,
                CharacterLimit = (measureOnly.HasValue ? measureOnly.Value.CharacterLimit : _CharacterLimit) ?? int.MaxValue,
                InitialIndentation = _XOffsetOfFirstLine,
                WrapIndentation = _WrapIndentation,
                BreakIndentation = _XOffsetOfNewLine,
                ExtraBreakSpacing = _ExtraLineBreakSpacing,
                MaximumWidth = (measureOnly.HasValue ? measureOnly.Value.LineBreakAtX : _LineBreakAtX) ?? float.MaxValue,
                MaximumHeight = _StopAtY ?? float.MaxValue,
                CharacterWrap = CharacterWrap,
                WordWrap = WordWrap,
                HideOverflow = HideOverflow,
                DefaultAlignment = (HorizontalAlignment)_Alignment,
                LineLimit = (measureOnly.HasValue ? measureOnly.Value.LineLimit : _LineLimit) ?? int.MaxValue,
                BreakLimit = (measureOnly.HasValue ? measureOnly.Value.LineBreakLimit : _LineBreakLimit) ?? int.MaxValue,
                TabSize = _TabSize,
                MeasureOnly = measureOnly.HasValue || MeasureOnly,
                MaskCodepoint = (uint)(_ReplacementCharacter ?? '\0'),
                SplitAtWrapCharactersOnly = SplitAtWrapCharactersOnly,
                IncludeTrailingWhitespace = IncludeTrailingWhitespace,
                WrapCharacters = _WordWrapCharacterTable,
                Listener = measureOnly.HasValue ? null : this,
                Listener2 = Listener as IStringLayoutListener2,
                MarkedRange = _Satellite?.MarkedRange,
                HitTestLocation = _Satellite?.HitTest,
                CharacterUserData = _CharacterUserData,
                ImageUserData = _ImageUserData ?? _CharacterUserData,
                // FIXME
                MinRubyScale = MinRubyScale,
                UserData = _RichTextUserData
            };
        }

        /// <summary>
        /// Copies all of source's configuration
        /// </summary>
        public void Copy (DynamicStringLayout source) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            this.Alignment = source.Alignment;
            this.AlignToPixels = source.AlignToPixels;
            this.CharacterLimit = source.CharacterLimit;
            this.CharacterSkipCount = source.CharacterSkipCount;
            this.CharacterWrap = source.CharacterWrap;
            this.Color = source.Color;
            this.GlyphSource = source.GlyphSource;
            this.LineBreakAtX = source.LineBreakAtX;
            this.StopAtY = source.StopAtY;
            this.LineLimit = source.LineLimit;
            this.LineBreakLimit = source.LineBreakLimit;
            this.Position = source.Position;
            this.ReverseOrder = source.ReverseOrder;
            this.Scale = source.Scale;
            this.Spacing = source.Spacing;
            this.SortKey = source.SortKey;
            this.SetText(source.Text, true);
            this.TruncatedIndicator = source.TruncatedIndicator;
            this.WordWrap = source.WordWrap;
            this.WrapCharacter = source.WrapCharacter;
            this.WrapIndentation = source.WrapIndentation;
            this.ExtraLineBreakSpacing = source.ExtraLineBreakSpacing;
            this.XOffsetOfFirstLine = source.XOffsetOfFirstLine;
            this.XOffsetOfNewLine = source.XOffsetOfNewLine;
            this.RichText = source.RichText;
            this.RichTextConfiguration = source.RichTextConfiguration;
            this.ReplacementCharacter = source.ReplacementCharacter;
            this.HideOverflow = source.HideOverflow;
            this.WordWrapCharacters = source.WordWrapCharacters;
            this.RecordUsedTextures = source.RecordUsedTextures;
            this.ExpandHorizontallyWhenAligning = source.ExpandHorizontallyWhenAligning;
            this.SplitAtWrapCharactersOnly = source.SplitAtWrapCharactersOnly;
            this.IncludeTrailingWhitespace = source.IncludeTrailingWhitespace;
            this.DesiredWidth = source.DesiredWidth;
            this.MaxExpansionPerSpace = source.MaxExpansionPerSpace;
            this.DisableMarkers = source.DisableMarkers;
            this.AdditionalLineSpacing = source.AdditionalLineSpacing;
            SetFlag(InternalFlags.AwaitingDependencies, false);
        }

        public bool IsAwaitingDependencies => GetFlag(InternalFlags.AwaitingDependencies);

        public void FormatAsText (StringBuilder output) {
            if (!RichText) {
                Text.CopyTo(output);
                return;
            }

            // HACK: Perform a measurement-only layout in a special mode that appends text to a stringbuilder
            MakeLayoutEngine2(out var le2, default(MeasurementSettings), true);
            le2.Initialize();
            le2.TextOutput = output;
            var rls = new RichTextLayoutState(_RichTextConfiguration, ref le2, null, this);
            rls.UserData = RichTextUserData ?? rls.UserData;
            if (_RichTextConfiguration != null)
                rls.Tags.AddRange(ref _RichTextConfiguration.Tags);
            _RichTextConfiguration.Append(ref le2, ref rls, _Text, _StyleName);
        }

        public string FormatAsText () {
            var sb = new StringBuilder();
            FormatAsText(sb);
            return sb.ToString();
        }

        public bool Get (out StringLayout result, MeasurementSettings? measureOnly = null) {
            if (GetFlag(InternalFlags.HasCachedStringLayout) && (_GlyphSource != null) &&
                ((_CachedGlyphVersion < _GlyphSource.Version) || _GlyphSource.IsDisposed)
            )
                Invalidate();

            if (GetFlag(InternalFlags.AwaitingDependencies)) {
                bool stillWaiting = false;
                if (_Satellite != null) {
                    foreach (var d in _Satellite.Dependencies) {
                        if (!d.HasValue && !d.Dead) {
                            stillWaiting = true;
                            break;
                        }
                    }
                }

                if (!stillWaiting) {
                    Invalidate();
                }
            }

            // FIXME: Detect whether the measurement settings exactly match the cached layout,
            //  and if they do, return the cached layout
            if (!GetFlag(InternalFlags.HasCachedStringLayout) || measureOnly.HasValue) {
                var glyphSource = GlyphSource;
                if (glyphSource == null) {
                    if (!measureOnly.HasValue)
                        _CachedStringLayout = result = default;
                    else
                        result = default;
                    return false;
                }

                if (_Text.IsNull) {
                    if (!measureOnly.HasValue) {
                        _CachedStringLayout = result = default;
                        _CachedGlyphVersion = glyphSource.Version;  
                    } else {
                        result = default;
                    }
                    return false;
                }

                int length = _Text.Length;

                int capacity = length + StringLayoutEngine.DefaultBufferPadding;

                if (!measureOnly.HasValue && !MeasureOnly) {
                    if (_Buffer.Array != null) {
                        Array.Clear(_Buffer.Array, _Buffer.Offset, _Buffer.Count);
                    }
                }

                var rls = default(RichTextLayoutState);
                MakeLayoutEngine2(out var le2, measureOnly);
                if (!measureOnly.HasValue) {
                    _Satellite?.RichMarkers.Clear();
                    _Satellite?.Boxes.Clear();
                }

                // We need this for dependency tracking to work right for prgui hypertext
                _Satellite?.Dependencies.Clear();

                try {
                    le2.EnsureCapacity(Math.Max((uint)Text.Length, 16), Math.Max((uint)Text.Length / 7, 4));
                    le2.Initialize();
                    if (RichText) {
                        rls = new RichTextLayoutState(_RichTextConfiguration, ref le2, glyphSource, this);
                        rls.UserData = RichTextUserData ?? rls.UserData;
                        if (_RichTextConfiguration.ImageUserData.HasValue)
                            le2.ImageUserData = _RichTextConfiguration.ImageUserData.Value;
                        rls.Tags.AddRange(ref _RichTextConfiguration.Tags);
                        _RichTextConfiguration.Append(ref le2, ref rls, _Text, _StyleName);
                        var dependencies = _Satellite?.Dependencies ?? default;
                        if (dependencies.Count > 0) {
                            SetFlag(InternalFlags.AwaitingDependencies, false);

                            for (int i = 0, c = dependencies.Count; i < c; i++) {
                                var d = dependencies[i];
                                if (!d.HasValue && !d.Dead)
                                    SetFlag(InternalFlags.AwaitingDependencies, true);
                                AutoAllocateSatellite().Dependencies.Add(d);
                            }
                        } else
                            SetFlag(InternalFlags.AwaitingDependencies, false);

                        // FIXME
                        if (le2.IsTruncated && !TruncatedIndicator.IsNull) {
                            le2.Desuppress();
                            _RichTextConfiguration.Append(ref le2, ref rls, TruncatedIndicator, _StyleName);
                        }
                    } else {
                        SetFlag(InternalFlags.AwaitingDependencies, false);
                        // le.AppendText(glyphSource, _Text);
                        le2.AppendText(glyphSource, _Text);
                        if (le2.IsTruncated && !TruncatedIndicator.IsNull) {
                            le2.Desuppress();
                            le2.AppendText(glyphSource, TruncatedIndicator);
                        }
                    }

                    if (!measureOnly.HasValue) {
                        _CachedGlyphVersion = glyphSource.Version;
                        EnsureBufferCapacity((int)le2.DrawCallCount);
                        le2.Finish(_Buffer, out _CachedStringLayout);

                        if (_Satellite?.MarkedRange != null) {
                            _Satellite.MarkedRangeResult.FirstCharacterIndex = _Satellite.MarkedRange.Value.First;
                            _Satellite.MarkedRangeResult.LastCharacterIndex = _Satellite.MarkedRange.Value.Second;
                            _Satellite.MarkedRangeResult.Bounds.Clear();
                            if (le2.MarkedRangeSpanIndex != uint.MaxValue) {
                                ref var span = ref le2.GetSpan(le2.MarkedRangeSpanIndex);
                                le2.TryGetSpanBoundingBoxes(le2.MarkedRangeSpanIndex, ref _Satellite.MarkedRangeResult.Bounds);
                                unchecked {
                                    _Satellite.MarkedRangeResult.FirstDrawCallIndex = (int)span.FirstDrawCall;
                                    _Satellite.MarkedRangeResult.LastDrawCallIndex = (int)(span.FirstDrawCall + span.DrawCallCount - 1);
                                    _Satellite.MarkedRangeResult.GlyphCount = (ushort)span.DrawCallCount;
                                }
                            } else {
                                _Satellite.MarkedRangeResult.FirstDrawCallIndex =
                                    _Satellite.MarkedRangeResult.LastDrawCallIndex = null;
                                _Satellite.MarkedRangeResult.GlyphCount = 0;
                            }
                        }

                        if (_Satellite?.HitTest != null)
                            _Satellite.HitTestResult = le2.HitTestResult;

                        // FIXME
                        SetFlag(InternalFlags.HasCachedStringLayout, true);
                    } else {
                        le2.Finish(default, out result);
                        return true;
                    }
                } finally {
                    /*
                    if (!measureOnly.HasValue) {
                        if (le.Markers.HasList)
                            AutoAllocateSatellite().LayoutEngineMarkerStorage = le.Markers.GetStorage(false);
                    }
                    */

                    rls.Dispose();
                    le2.Dispose();
                }
            }

            result = _CachedStringLayout;
            return true;
        }

        /// <summary>
        /// If the current state is invalid, computes a current layout. Otherwise, returns the cached layout.
        /// </summary>
        public StringLayout Get () {
            Get(out StringLayout result);
            return result;
        }

        void IStringLayoutListener.Initializing (ref StringLayoutEngine2 engine) {
            _Listener?.Initializing(ref engine);
            if (!engine.MeasureOnly)
                _Satellite?.RichMarkers.Clear();
        }

        void IStringLayoutListener.Error (ref StringLayoutEngine2 engine, string message) {
            _Listener?.Error(ref engine, message);
        }

        void IStringLayoutListener.RecordTexture (ref StringLayoutEngine2 engine, AbstractTextureReference texture) {
            if (RecordUsedTextures) {
                var satellite = AutoAllocateSatellite();
                if (satellite.UsedTextures.IndexOf(texture, AbstractTextureReference.Comparer.Instance) < 0)
                    satellite.UsedTextures.Add(texture);
            }

            _Listener?.RecordTexture(ref engine, texture);
        }

        void IStringLayoutListener.Finishing (ref StringLayoutEngine2 engine) {
            _Listener?.Finishing(ref engine);
        }

        void IStringLayoutListener.Finished (ref StringLayoutEngine2 engine, ref StringLayout result) {
            _Listener?.Finished(ref engine, ref result);

            _Satellite?.Boxes.Clear();

            if (engine.BoxCount > 0) {
                var satellite = AutoAllocateSatellite();
                satellite.Boxes.EnsureCapacity((int)engine.BoxCount);
                for (uint i = 0, c = engine.BoxCount; i < c; i++) {
                    if (engine.TryGetBox(i, out var box, out _))
                        satellite.Boxes.Add(ref box);
                }
            }

            if (_Satellite == null)
                return;

            for (int i = 0, c = _Satellite.RichMarkers.Count; i < c; i++) {
                ref var rm = ref _Satellite.RichMarkers.Item(i);
                engine.TryGetSpanBoundingBoxes(rm.SpanIndex, ref rm.Bounds);
                ref var span = ref engine.GetSpan(rm.SpanIndex);
                rm.FirstDrawCallIndex = (int)span.FirstDrawCall;
                rm.LastDrawCallIndex = (int)span.DrawCallCount + (int)span.FirstDrawCall - 1;
            }
        }

        void IRichTextStateTracker.MarkString (RichTextConfiguration config, ref StringLayoutEngine2 engine, AbstractString originalText, AbstractString text, AbstractString id, uint spanIndex) {
            _StateTracker?.MarkString(config, ref engine, originalText, text, id, spanIndex);

            if (DisableMarkers)
                return;

            if (engine.MeasureOnly)
                return;

            ref var span = ref engine.GetSpan(spanIndex);
            var rm = new LayoutMarker {
                OriginalText = originalText,
                ActualText = text,
                ID = id,
                SpanIndex = spanIndex,
                // Not available yet
                /*
                FirstDrawCallIndex = (int)span.FirstDrawCall,
                LastDrawCallIndex = (int)span.DrawCallCount + (int)span.FirstDrawCall - 1,
                */
            };

            AutoAllocateSatellite().RichMarkers.Add(rm);
        }

        void IRichTextStateTracker.ReferencedImage (RichTextConfiguration config, ref AsyncRichImage image) {
            AutoAllocateSatellite().Dependencies.Add(ref image);

            _StateTracker?.ReferencedImage(config, ref image);
        }

        RichCommandResult IRichTextStateTracker.TryProcessCommand (AbstractString value, ref RichTextLayoutState state, ref StringLayoutEngine2 layoutEngine) {
            if (_StateTracker != null)
                return _StateTracker.TryProcessCommand(value, ref state, ref layoutEngine);
            else
                return RichCommandResult.NotHandled;
        }

        RichStyleResult IRichTextStateTracker.TryApplyStyleProperty (ref RichTextLayoutState state, ref StringLayoutEngine2 layoutEngine, RichProperty rule) {
            if (_StateTracker != null)
                return _StateTracker.TryApplyStyleProperty(ref state, ref layoutEngine, rule);
            else
                return RichStyleResult.NotHandled;
        }

        void IRichTextStateTracker.ResetStyle (ref RichTextLayoutState state, ref StringLayoutEngine2 layoutEngine) {
            _StateTracker?.ResetStyle(ref state, ref layoutEngine);
        }
    }

    public class FallbackGlyphSource : IGlyphSource, IDisposable, IEnumerable<IGlyphSource>, IGlyphSourceChangeListener {
        public float? OverrideLineSpacing;
        public bool IsDisposed { get; private set; }
        public bool MaxLineSpacing = true;
        public bool OwnsSources = true;
        public int Version { get; private set; }
        public GlyphPixelAlignment? DefaultAlignment { get; set; }
        private readonly IGlyphSource[] Sources = null;

        object IGlyphSource.UniqueKey => this;

        public FallbackGlyphSource (bool ownsSources, params IGlyphSource[] sources) {
            OwnsSources = ownsSources;
            Sources = sources;
            var weakSelf = new WeakReference<IGlyphSourceChangeListener>(this);
            foreach (var s in Sources)
                s.RegisterForChangeNotification(weakSelf);
        }

        public FallbackGlyphSource (params IGlyphSource[] sources) {
            Sources = sources;
            var weakSelf = new WeakReference<IGlyphSourceChangeListener>(this);
            foreach (var s in Sources)
                s.RegisterForChangeNotification(weakSelf);
        }

        public SpriteFont SpriteFont {
            get {
                return null;
            }
        }

        public bool GetGlyph (uint ch, out Glyph result) {
            foreach (var item in Sources) {
                if (item.GetGlyph(ch, out result))
                    return true;
            }

            result = default(Glyph);
            return false;
        }

        public uint GetGlyphIndex (uint codepoint) {
            foreach (var item in Sources) {
                var result = item.GetGlyphIndex(codepoint);
                if (result > 0)
                    return result;
            }

            return 0;
        }

        void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) {
            throw new InvalidOperationException("Nesting FallbackGlyphSources is not supported");
        }

        public float DPIScaleFactor {
            get {
                return Sources[0].DPIScaleFactor;
            }
        }

        public float LineSpacing {
            get {
                if (OverrideLineSpacing.HasValue)
                    return OverrideLineSpacing.Value;

                if (!MaxLineSpacing)
                    return Sources[0].LineSpacing;

                var ls = 0f;
                foreach (var s in Sources)
                    ls = Math.Max(ls, s.LineSpacing);
                return ls;
            }
        }
        
        public void Dispose () {
            IsDisposed = true;
            if (OwnsSources) {
                foreach (var item in Sources)
                    if (item is IDisposable id)
                        id.Dispose();
            }
        }

        public IEnumerator<IGlyphSource> GetEnumerator () {
            return ((IEnumerable<IGlyphSource>)Sources).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<IGlyphSource>)Sources).GetEnumerator();
        }

        void IGlyphSourceChangeListener.NotifyChanged (IGlyphSource source) {
            Version++;
        }
    }

    public interface IGlyphSourceChangeListener {
        void NotifyChanged (IGlyphSource source);
    }

    public interface IGlyphSource {
        uint GetGlyphIndex (uint codepoint);
        bool GetGlyph (uint codepoint, out Glyph result);
        float LineSpacing { get; }
        float DPIScaleFactor { get; }
        bool IsDisposed { get; }
        int Version { get; }
        object UniqueKey { get; }
        void RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener);
    }

    public struct KerningData {
        public float XOffset, YOffset, LeftSideBearing, RightSideBearing;

        public bool Equals (KerningData rhs) =>
            (XOffset == rhs.XOffset) &&
            (YOffset == rhs.YOffset) &&
            (LeftSideBearing == rhs.LeftSideBearing) &&
            (RightSideBearing == rhs.RightSideBearing);

        public override bool Equals (object obj) {
            if (obj is KerningData kd)
                return Equals(kd);
            else
                return false;
        }

        public override int GetHashCode () => 0;
    }

    public interface IKerningProvider {
        bool TryGetKerning (uint glyphId, uint nextGlyphId, ref KerningData thisGlyph, ref KerningData nextGlyph);
    }

    public interface ILigatureProvider {
        /// <summary>
        /// Attempts to replace the provided glyph with its ligature by scanning forward in the input string.
        /// </summary>
        /// <param name="glyph">The first glyph</param>
        /// <param name="text">The input string</param>
        /// <param name="startOffset">The decoding offset inside the input string</param>
        /// <returns>The number of additional characters (not codepoints) that were consumed by the ligature, if any</returns>
        int TryGetLigature (ref Glyph glyph, AbstractString text, int startOffset);
    }

    public static class TextUtils {
        public struct FontFields {
            public Texture2D Texture;
            public List<Rectangle> GlyphRectangles;
            public List<Rectangle> CropRectangles;
            public List<char> Characters;
            public List<Vector3> Kerning;
        }
        internal static readonly FieldInfo textureValue, glyphData, croppingData, kerning, characterMap;

        static TextUtils () {
            var tSpriteFont = typeof(SpriteFont);
            textureValue = GetPrivateField(tSpriteFont, "textureValue");
            glyphData = GetPrivateField(tSpriteFont, "glyphData");
            croppingData = GetPrivateField(tSpriteFont, "croppingData");
            kerning = GetPrivateField(tSpriteFont, "kerning");
            characterMap = GetPrivateField(tSpriteFont, "characterMap");
        }

        private static FieldInfo GetPrivateField (Type type, string fieldName) {
            return type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        /// <summary>
        /// Prefetches all glyphs in a range so they will be available immediately in the future.
        /// </summary>
        /// <returns>The number of glyphs that were successfully fetched</returns>
        public static int PrefetchGlyphs (this IGlyphSource source, char firstChar, char lastChar) {
            return source.PrefetchGlyphs((uint)firstChar, (uint)lastChar);
        }

        /// <summary>
        /// Prefetches all glyphs in a range so they will be available immediately in the future.
        /// </summary>
        /// <returns>The number of glyphs that were successfully fetched</returns>
        public static int PrefetchGlyphs (this IGlyphSource source, uint firstChar, uint lastChar) {
            if (lastChar < firstChar)
                throw new ArgumentOutOfRangeException();

            int result = 0;
            Glyph temp;
            for (uint ch = firstChar; ch <= lastChar; ch++) {
                if (source.GetGlyph(ch, out temp))
                    result++;
            }
            return result;
        }

        public static bool GetGlyph<TSource> (this TSource source, char ch, out Glyph result)
            where TSource : IGlyphSource
        {
            return source.GetGlyph((uint)ch, out result);
        }

        public static bool GetPrivateFields (this SpriteFont font, out FontFields result) {
            if (textureValue == null) {
                result = default(FontFields);
                return false;
            }

            result = new FontFields {
                Texture = (Texture2D)(textureValue).GetValue(font),
                GlyphRectangles = (List<Rectangle>)glyphData.GetValue(font),
                CropRectangles = (List<Rectangle>)croppingData.GetValue(font),
                Characters = (List<char>)characterMap.GetValue(font),
                Kerning = (List<Vector3>)kerning.GetValue(font)
            };
            return true;
        }
    }

    public class AtlasGlyphSource : IGlyphSource, IDisposable, IEnumerable<AtlasGlyph> {
        public class Size : IGlyphSource {
            public readonly AtlasGlyphSource Source;
            public float Scale;

            private float? _LineSpacing;
            public float LineSpacing {
                get => _LineSpacing * Scale ?? Source.LineSpacing * Scale;
                set => _LineSpacing = value;
            }
            public float DPIScaleFactor => Source.DPIScaleFactor;
            public GlyphPixelAlignment? DefaultAlignment => Source.DefaultAlignment;
            public bool IsDisposed => Source.IsDisposed;
            public int Version => Source.Version;
            public object UniqueKey => Source.UniqueKey;

            public Size (AtlasGlyphSource source, float scale) {
                Source = source;
                Scale = scale;
            }

            public bool GetGlyph (uint ch, out Glyph result) {
                return Source.GetGlyph(ch, Scale, LineSpacing, out result);
            }

            public uint GetGlyphIndex (uint codepoint) => Source.GetGlyphIndex(codepoint);

            void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) =>
                ((IGlyphSource)Source).RegisterForChangeNotification(listener);
        }

        public readonly bool OwnsAtlas;
        public readonly Atlases.Atlas Atlas;

        public float LineSpacing { get; set; }
        public float CharacterSpacing { get; set; }
        public float DPIScaleFactor { get; set; }
        public float? Baseline { get; set; }
        public GlyphPixelAlignment? DefaultAlignment { get; set; }

        public bool IsDisposed { get; private set; }
        public int Version { get; set; }
        public object UniqueKey { get; set; }

        private bool NeedIncrementVersion = true;
        private readonly Dictionary<uint, AtlasGlyph> Registry = 
            new Dictionary<uint, AtlasGlyph>(UintComparer.Instance);
        private DenseList<WeakReference<IGlyphSourceChangeListener>> ChangeListeners;

        void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) {
            if (!ChangeListeners.Contains(listener))
                ChangeListeners.Add(listener);
        }

        public AtlasGlyphSource (Atlases.Atlas atlas, bool ownsAtlas) {
            Atlas = atlas;
            OwnsAtlas = ownsAtlas;
            LineSpacing = atlas.CellHeight;
            DPIScaleFactor = 1.0f;
        }

        public uint GetGlyphIndex (uint codepoint) {
            if (!Registry.TryGetValue(codepoint, out var glyph))
                return 0;

            // HACK
            var result = (uint)(glyph.Index ?? (glyph.X + (Atlas.WidthInCells * glyph.Y)) + 1);
            return result;
        }

        public bool GetGlyph (uint ch, out Glyph result) => GetGlyph(ch, 1.0f, null, out result);

        public bool GetGlyph (uint ch, float scale, float? lineSpacing, out Glyph result) {
            NeedIncrementVersion = true;
            if (IsDisposed)
                throw new ObjectDisposedException("AtlasGlyphSource");

            if (!Registry.TryGetValue(ch, out var glyph)) {
                result = default;
                return false;
            }

            var cell = glyph.Index.HasValue
                ? Atlas[glyph.Index.Value]
                : Atlas[glyph.X, glyph.Y];

            var unbiasedScale = scale;
            scale *= glyph.Scale;

            result = new Glyph {
                GlyphIndex = (uint)(glyph.Index ?? 0),
                Texture = Atlas.Texture,
                BoundsInTexture = cell.Bounds,
                Character = glyph.Character,
                Width = Atlas.CellWidth * scale,
                XOffset = glyph.XOffset * scale,
                YOffset = glyph.YOffset * scale,
                LeftSideBearing = glyph.LeftMargin * scale,
                RightSideBearing = (glyph.RightMargin + CharacterSpacing) * scale,
                LineSpacing = lineSpacing ?? (LineSpacing * scale),
                // HACK: Use unbiasedScale here because glyph.Scale is used to make the glyph bigger, we don't want to mess up the current baseline
                Baseline = (Baseline ?? Atlas.CellHeight) * unbiasedScale,
                RenderScale = scale
            };
            return true;
        }

        public void Add (AtlasGlyph glyph) {
            if (IsDisposed)
                throw new ObjectDisposedException("AtlasGlyphSource");

            if (glyph.Index >= Atlas.Count)
                throw new ArgumentOutOfRangeException("glyph.Index");
            else if (glyph.X >= Atlas.WidthInCells)
                throw new ArgumentOutOfRangeException("glyph.X");
            else if (glyph.Y >= Atlas.HeightInCells)
                throw new ArgumentOutOfRangeException("glyph.Y");

            Registry.Add(glyph.Character, glyph);
            if (NeedIncrementVersion) {
                NeedIncrementVersion = false;
                Version++;
                foreach (var listener in ChangeListeners)
                    if (listener.TryGetTarget(out var t))
                        t.NotifyChanged(this);
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;
            IsDisposed = true;
            if (OwnsAtlas)
                Atlas.Texture.Dispose();
        }

        public Dictionary<uint, AtlasGlyph>.ValueCollection.Enumerator GetEnumerator => Registry.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => Registry.Values.GetEnumerator();
        IEnumerator<AtlasGlyph> IEnumerable<AtlasGlyph>.GetEnumerator () => Registry.Values.GetEnumerator();
    }

    public struct AtlasGlyph {
        public readonly uint Character;
        public int? Index;
        public int X, Y;
        public string Name;
        public float XOffset, YOffset, RightMargin, LeftMargin;
        public float Scale {
            get => ScaleMinusOne + 1f;
            set => ScaleMinusOne = value - 1f;
        }
        private float ScaleMinusOne;

        public AtlasGlyph (uint character, int index, string name = null) : this() {
            Character = character;
            Index = index;
            Name = name;
        }

        public AtlasGlyph (uint character, int x, int y, string name = null) : this() {
            Character = character;
            X = x;
            Y = y;
            Name = name;
        }
    }

    public struct SpriteFontGlyphSource : IGlyphSource {
        public readonly SpriteFont Font;
        public readonly Texture2D Texture;
        public GlyphPixelAlignment? DefaultAlignment => GlyphPixelAlignment.None;

        public readonly TextUtils.FontFields Fields;
        public readonly int DefaultCharacterIndex;

        object IGlyphSource.UniqueKey => Font;

        public bool IsDisposed => (Texture?.IsDisposed == true);

        // Forward some SpriteFont methods and properties to make it easier to drop-in replace
        
        public float Spacing {
            get {
                return Font.Spacing;
            }
        }

        public float LineSpacing {
            get {
                return Font.LineSpacing;
            }
        }

        int IGlyphSource.Version {
            get {
                return 1;
            }
        }

        public Vector2 MeasureString (string text) {
            return Font.MeasureString(text);
        }

        public Vector2 MeasureString (StringBuilder text) {
            return Font.MeasureString(text);
        }

        void IGlyphSource.RegisterForChangeNotification (WeakReference<IGlyphSourceChangeListener> listener) {
        }

        public float DPIScaleFactor {
            get {
                return 1.0f;
            }
        }

        private void MakeGlyphForCharacter (uint ch, int characterIndex, out Glyph glyph) {
            var kerning = Fields.Kerning[characterIndex];
            var cropping = Fields.CropRectangles[characterIndex];
            var rect = Fields.GlyphRectangles[characterIndex];

            glyph = new Glyph {
                GlyphIndex = (uint)characterIndex,
                Character = ch,
                Texture = Texture,
                BoundsInTexture = Texture.BoundsFromRectangle(in rect),
                XOffset = cropping.X,
                YOffset = cropping.Y,
                LeftSideBearing = kerning.X,
                RightSideBearing = kerning.Z,
                Width = kerning.Y,
                CharacterSpacing = Font.Spacing,
                LineSpacing = Font.LineSpacing,
                // FIXME
                Baseline = Font.LineSpacing
            };
        }

        public bool GetGlyph (uint ch, out Glyph result) {
            var characterIndex = Fields.Characters.BinarySearch((char)ch);
            if (characterIndex < 0)
                characterIndex = DefaultCharacterIndex;

            if (characterIndex < 0) {
                result = default(Glyph);
                return false;
            }

            MakeGlyphForCharacter(ch, characterIndex, out result);
            return true;
        }

        public uint GetGlyphIndex (uint codepoint) {
            var characterIndex = Fields.Characters.BinarySearch((char)codepoint);
            if (characterIndex < 0)
                return 0;

            // HACK: 1 offset since 0 is invalid according to freetype rules
            return (uint)characterIndex + 1;
        }

        public SpriteFontGlyphSource (SpriteFont font) {
            if (font == null)
                throw new ArgumentNullException(nameof(font));
            Font = font;

            if (TextUtils.GetPrivateFields(font, out Fields)) {
                // XNA SpriteFont
                Texture = Fields.Texture;

                if (Font.DefaultCharacter.HasValue)
                    DefaultCharacterIndex = Fields.Characters.BinarySearch(Font.DefaultCharacter.Value);
                else
                    DefaultCharacterIndex = -1;
            } else {
                throw new NotImplementedException("Unsupported SpriteFont implementation");
            }
        }
    }

    public struct Glyph {
        public uint GlyphIndex;
        public AbstractTextureReference Texture;
        public uint Character;
        public Bounds BoundsInTexture;
        public float XOffset, YOffset;
        public float LeftSideBearing;
        public float RightSideBearing;
        public float Width;
        public float CharacterSpacing;
        public float LineSpacing;
        public float Baseline;
        private float RenderScaleMinusOne;
        public float RenderScale {
            get => RenderScaleMinusOne + 1;
            set => RenderScaleMinusOne = value - 1;
        }

        // Additional properties not available for SpriteFont below
        public Color? DefaultColor;

        public IKerningProvider KerningProvider;
        public ILigatureProvider LigatureProvider;

        public float Height;
        public float VerticalBearing;
        /// <summary>
        /// Glyph bounding box intervals in freetype coordinate space,
        ///  where x=0 is the 'pen position' and y=0 is the baseline
        /// </summary>
        public Interval XBounds, YBounds;

        public float WidthIncludingBearing {
            get {
                return LeftSideBearing + RightSideBearing + Width;
            }
        }

        public float WorstCaseWidth {
            get {
                return Math.Max(LeftSideBearing, 0) + Math.Max(RightSideBearing, 0) + Width;
            }
        }
    }
}

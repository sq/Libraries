using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Threading;
using Squared.Util;
using Squared.Util.Hash;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public class DynamicStringLayout {
        /// <summary>
        /// Strings under this length will have a hash computed and stored in order to allow
        ///  layout.Text = value to avoid invalidating if the text has not changed even if
        ///  value is a StringBuilder or ArraySegment. This threshold is applied to avoid
        ///  wasting CPU to hash very long strings - it's your responsibility to not invalidate
        ///  yourself constantly by setting Text in this case.
        /// You can disable this entirely by setting the threshold to 0.
        /// </summary>
        public static int TextHashLimit = 2048;
        /// <summary>
        /// Hashing small strings is a waste of time.
        /// </summary>
        public static int TextHashMinimum = 64;

        private ArraySegment<BitmapDrawCall> _Buffer; 
        private StringLayout? _CachedStringLayout;
        private int _CachedGlyphVersion = -1, _TextVersion = -1;

        private RichTextConfiguration _RichTextConfiguration;
        private string _StyleName;
        private Dictionary<char, KerningAdjustment> _KerningAdjustments;
        private Func<IGlyphSource> _GlyphSourceProvider;
        private IGlyphSource _GlyphSource;
        private uint? _TextHash;
        private AbstractString _Text, _TruncatedIndicator;
        private Vector2 _Position;
        private Color _DefaultColor;
        private Color? _Color;
        private float _Scale;
        private float _Spacing;
        private float _SortKey;
        private float _DesiredWidth;
        private float? _MaxExpansion;
        private int _CharacterSkipCount;
        private int _CharacterLimit;
        private float _XOffsetOfFirstLine;
        private float _XOffsetOfNewLine;
        private float? _LineBreakAtX;
        private float? _StopAtY;
        private bool _WordWrap;
        private bool _CharacterWrap;
        private float _WrapIndentation;
        private float _ExtraLineBreakSpacing;
        private GlyphPixelAlignment _AlignToPixels = GlyphPixelAlignment.Default;
        private char _WrapCharacter;
        private int _Alignment;
        private bool _ReverseOrder;
        private int _LineLimit;
        private int _LineBreakLimit;
        private bool _MeasureOnly;
        private bool _RichText;
        private bool _HideOverflow;
        private bool _RecordUsedTextures;
        private bool _ExpandHorizontallyWhenAligning;
        private char? _ReplacementCharacter;
        private uint[] _WordWrapCharacters;
        private bool _AwaitingDependencies;

        private List<AsyncRichImage> _Dependencies = null;
        private Dictionary<Pair<int>, LayoutMarker> _Markers = null;
        private Dictionary<Vector2, LayoutHitTest> _HitTests = null;
        private List<LayoutMarker> _RichMarkers = null;
        private List<Bounds> _Boxes = null;

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

        public void Reset () {
            RichTextConfiguration = default;
            _StyleName = null;
            KerningAdjustments = default;
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
            LineBreakAtX = null;
            StopAtY = null;
            WordWrap = false;
            CharacterWrap = true;
            WrapIndentation = 0f;
            ExtraLineBreakSpacing = 0f;
            AlignToPixels = GlyphPixelAlignment.Default;
            WrapCharacter = '\0';
            Alignment = HorizontalAlignment.Left;
            ReverseOrder = false;
            LineLimit = int.MaxValue;
            LineBreakLimit = int.MaxValue;
            MeasureOnly = false;
            RichText = false;
            HideOverflow = false;
            RecordUsedTextures = false;
            ExpandHorizontallyWhenAligning = true;
            ReplacementCharacter = default;
            WordWrapCharacters = default;
            _AwaitingDependencies = false;
            _Dependencies?.Clear();
            _RichMarkers?.Clear();
            _Boxes?.Clear();
        }

        public void ResetMarkersAndHitTests () {
            if ((_Markers != null) && (_Markers.Count > 0)) {
                _Markers.Clear();
                Invalidate();
            }
            if ((_HitTests != null) && (_HitTests.Count > 0)) {
                _HitTests.Clear();
                Invalidate();
            }
        }

        private Dictionary<Pair<int>, LayoutMarker> GetMarkers () {
            if (_Markers == null)
                return _Markers = new Dictionary<Pair<int>, LayoutMarker>();
            else
                return _Markers;
        }

        private Dictionary<Vector2, LayoutHitTest> GetHitTests () {
            if (_HitTests == null)
                return _HitTests = new Dictionary<Vector2, LayoutHitTest>();
            else
                return _HitTests;
        }

        public LayoutMarker? Mark (int characterIndex) {
            var m = GetMarkers();
            var key = new Pair<int>(characterIndex, characterIndex);

            LayoutMarker result;
            if (!m.TryGetValue(key, out result)) {
                m[key] = new LayoutMarker(characterIndex, characterIndex);
                Invalidate();
                return null;
            }

            if (result.Bounds.Count > 0)
                return result;
            else
                return null;
        }

        public LayoutMarker? Mark (int firstCharacterIndex, int lastCharacterIndex) {
            var m = GetMarkers();
            var key = new Pair<int>(firstCharacterIndex, lastCharacterIndex);

            LayoutMarker result;
            if (!m.TryGetValue(key, out result)) {
                m[key] = new LayoutMarker(firstCharacterIndex, lastCharacterIndex);
                Invalidate();
                return null;
            }

            if (result.Bounds.Count > 0)
                return result;
            else
                return null;
        }

        /// <summary>
        /// Specifies additional codepoints that will be treated as valid word separators for word wrap
        /// </summary>
        public uint[] WordWrapCharacters {
            get {
                return _WordWrapCharacters;
            }
            set {
                InvalidatingReferenceAssignment(ref _WordWrapCharacters, value);
            }
        }

        private static readonly Dictionary<Pair<int>, LayoutMarker> EmptyMarkers = new Dictionary<Pair<int>, LayoutMarker>();
        private static readonly Dictionary<Vector2, LayoutHitTest> EmptyHitTests = new Dictionary<Vector2, LayoutHitTest>();
        private static readonly List<LayoutMarker> EmptyRichMarkers = new List<LayoutMarker>();
        private static readonly List<Bounds> EmptyBoxes = new List<Bounds>();
        private static readonly List<AsyncRichImage> EmptyDependencies = new List<AsyncRichImage>();

        // FIXME: Garbage
        public IReadOnlyDictionary<Pair<int>, LayoutMarker> Markers => _Markers ?? EmptyMarkers;
        public IReadOnlyDictionary<Vector2, LayoutHitTest> HitTests => _HitTests ?? EmptyHitTests;
        public IReadOnlyList<LayoutMarker> RichMarkers => _RichMarkers ?? EmptyRichMarkers;
        public IReadOnlyList<Bounds> Boxes => _Boxes ?? EmptyBoxes;
        public IReadOnlyList<AsyncRichImage> Dependencies => _Dependencies ?? EmptyDependencies;

        public LayoutHitTest? HitTest (Vector2 position) {
            var ht = GetHitTests();
            LayoutHitTest result;
            if (!ht.TryGetValue(position, out result)) {
                ht[position] = new LayoutHitTest {
                    Position = position
                };
                Invalidate();
                return null;
            }
            return result;
        }

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

        /// <summary>
        /// Update the text stored in this layout.
        /// </summary>
        /// <param name="newText">The new string</param>
        /// <param name="compareText">
        /// If true, the new and existing strings will have their content 
        /// compared (if possible) to skip invalidating the layout.
        /// </param>
        /// <param name="useHash">
        /// If true, a hash will be computed for the existing and new strings,
        /// and if the hashes match invalidation will be skipped.
        /// </param>
        /// <returns>true if the text was updated and the layout was invalidated.</returns>
        public bool SetText (AbstractString newText, bool compareText = true, bool useHash = false) {
            uint? newHash = null;
            if (
                compareText && 
                _Text.IsImmutable && 
                newText.IsImmutable &&
                (_Text.Length == newText.Length)
            ) {
                if (useHash && 
                    newText.Length > TextHashMinimum &&
                    newText.Length < TextHashLimit
                ) {
                    newHash = newText.ComputeTextHash();
                    if (newHash == _TextHash)
                        return false;
                }
                if (_Text.TextEquals(newText, StringComparison.Ordinal)) {
                    if (newHash.HasValue)
                        _TextHash = newHash;
                    return false;
                }
            }

            _Text = newText;
            _TextHash = newHash;
            _TextVersion++;
            Invalidate();
            return true;
        }

        public AbstractString Text {
            get {
                return _Text;
            }
            set {
                SetText(value, true, false);
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
        /// When using justify alignment modes, the amount of whitespace added to a line of text
        ///   will not exceed this amount.
        /// </summary>
        public float? MaxExpansion {
            get {
                return _MaxExpansion;
            }
            set {
                InvalidatingNullableAssignment(ref _MaxExpansion, value);
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
            get {
                return _RichText;
            }
            set {
                InvalidatingValueAssignment(ref _RichText, value);
            }
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
            get {
                return _HideOverflow;
            }
            set {
                InvalidatingValueAssignment(ref _HideOverflow, value);
            }
        }

        /// <summary>
        /// Attempt to wrap words to the next line when they extend past the wrap boundary
        /// </summary>
        public bool WordWrap {
            get {
                return _WordWrap;
            }
            set {
                InvalidatingValueAssignment(ref _WordWrap, value);
            }
        }

        /// <summary>
        /// Attempt to wrap characters to the next line when they extend past the wrap boundary
        /// </summary>
        public bool CharacterWrap {
            get {
                // FIXME: Is this right?
                return _CharacterWrap;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterWrap, value);
            }
        }

        /// <summary>
        /// Does not generate a table of draw calls, only measures the text
        /// </summary>
        public bool MeasureOnly {
            get {
                return _MeasureOnly;
            }
            set {
                // If we weren't in measurement-only mode, transitioning into it doesn't
                //  need to invalidate anything since all we'd do is throw away data
                if (_MeasureOnly == false)
                    InvalidatingValueAssignment(ref _MeasureOnly, value);
                else
                    _MeasureOnly = value;
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
                InvalidatingValueAssignment(ref _Alignment, (int)value);
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

        public Dictionary<char, KerningAdjustment> KerningAdjustments {
            get {
                return _KerningAdjustments;
            }
            set {
                InvalidatingReferenceAssignment(ref _KerningAdjustments, value);
            }
        }

        public bool ReverseOrder {
            get {
                return _ReverseOrder;
            }
            set {
                InvalidatingValueAssignment(ref _ReverseOrder, value);
            }
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
        /// If set, every texture used in the layout will be recorded so you can use it for tracking and lifetime management.
        /// </summary>
        public bool RecordUsedTextures {
            get => _RecordUsedTextures;
            set {
                InvalidatingValueAssignment(ref _RecordUsedTextures, value);
            }
        }

        public bool ExpandHorizontallyWhenAligning {
            get => _ExpandHorizontallyWhenAligning;
            set => InvalidatingValueAssignment(ref _ExpandHorizontallyWhenAligning, value);
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (
                    _CachedStringLayout.HasValue && 
                    (_CachedGlyphVersion >= _GlyphSource.Version) && 
                    !_GlyphSource.IsDisposed
                );
            }
        }

        public void Invalidate () {
            // Hey, you're the boss
            _CachedStringLayout = null;
            if (_RichMarkers != null)
                _RichMarkers.Clear();
        }

        /// <summary>
        /// Constructs a layout engine based on this configuration
        /// </summary>
        public void MakeLayoutEngine (out StringLayoutEngine result) {
            if (_GlyphSource == null)
                throw new ArgumentNullException("GlyphSource");

            result = new StringLayoutEngine {
                allocator = UnorderedList<BitmapDrawCall>.DefaultAllocator.Instance,
                buffer = _Buffer,
                position = _Position,
                overrideColor = _Color,
                defaultColor = _DefaultColor,
                scale = _Scale,
                spacing = _Spacing,
                sortKey = _SortKey,
                desiredWidth = _DesiredWidth,
                maxExpansion = _MaxExpansion,
                characterSkipCount = _CharacterSkipCount,
                characterLimit = _CharacterLimit,
                xOffsetOfFirstLine = _XOffsetOfFirstLine,
                xOffsetOfWrappedLine = _WrapIndentation,
                xOffsetOfNewLine = _XOffsetOfNewLine,
                extraLineBreakSpacing = _ExtraLineBreakSpacing,
                lineBreakAtX = _LineBreakAtX,
                stopAtY = _StopAtY,
                alignToPixels = _AlignToPixels.Or(_GlyphSource.DefaultAlignment),
                characterWrap = _CharacterWrap,
                wordWrap = _WordWrap,
                wrapCharacter = _WrapCharacter,
                hideOverflow = _HideOverflow,
                alignment = (HorizontalAlignment)_Alignment,
                reverseOrder = _ReverseOrder,
                lineLimit = _LineLimit,
                lineBreakLimit = _LineBreakLimit,
                measureOnly = _MeasureOnly,
                replacementCodepoint = _ReplacementCharacter,
                recordUsedTextures = _RecordUsedTextures,
                expandHorizontallyWhenAligning = _ExpandHorizontallyWhenAligning,
            };

            if (_WordWrapCharacters != null)
                foreach (var cp in _WordWrapCharacters)
                    result.WordWrapCharacters.Add(cp);

            if (_Markers != null)
                foreach (var kvp in _Markers)
                    result.Markers.Add(kvp.Value);

            if (_HitTests != null)
                foreach (var kvp in _HitTests)
                    result.HitTests.Add(new LayoutHitTest { Position = kvp.Key });
        }

        /// <summary>
        /// Copies all of source's configuration
        /// </summary>
        public void Copy (DynamicStringLayout source) {
            this.Alignment = source.Alignment;
            this.AlignToPixels = source.AlignToPixels;
            this.CharacterLimit = source.CharacterLimit;
            this.CharacterSkipCount = source.CharacterSkipCount;
            this.CharacterWrap = source.CharacterWrap;
            this.Color = source.Color;
            this.GlyphSource = source.GlyphSource;
            this.KerningAdjustments = source.KerningAdjustments;
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
            this._TextHash = source._TextHash;
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
            this.DesiredWidth = source.DesiredWidth;
        }

        /// <summary>
        /// If the current state is invalid, computes a current layout. Otherwise, returns the cached layout.
        /// </summary>
        public StringLayout Get () {
            if (_CachedStringLayout.HasValue && (_GlyphSource != null) &&
                ((_CachedGlyphVersion < _GlyphSource.Version) || _GlyphSource.IsDisposed)
            )
                Invalidate();

            if (_AwaitingDependencies) {
                bool stillWaiting = false;
                foreach (var d in _Dependencies) {
                    if (!d.HasValue) {
                        stillWaiting = true;
                        break;
                    }
                }

                if (!stillWaiting) {
                    Invalidate();
                }
            }

            if (!_CachedStringLayout.HasValue) {
                var glyphSource = GlyphSource;
                if (glyphSource == null) {
                    _CachedStringLayout = new StringLayout();
                    return _CachedStringLayout.Value;
                }

                if (_Text.IsNull) {
                    _CachedStringLayout = new StringLayout();
                    _CachedGlyphVersion = glyphSource.Version;
                    return _CachedStringLayout.Value;
                }

                int length = _Text.Length;

                int capacity = length + StringLayoutEngine.DefaultBufferPadding;

                if (_Buffer.Array != null) {
                    Array.Clear(_Buffer.Array, _Buffer.Offset, _Buffer.Count);
                }

                StringLayoutEngine le;
                var rls = default(RichTextLayoutState);
                MakeLayoutEngine(out le);
                if (_RichMarkers != null)
                    _RichMarkers.Clear();
                if (_Boxes != null)
                    _Boxes.Clear();
                if (_Dependencies != null)
                    _Dependencies.Clear();

                try {
                    le.Initialize();
                    if (_RichText) {
                        var ka = _RichTextConfiguration.KerningAdjustments;
                        _RichTextConfiguration.KerningAdjustments = _KerningAdjustments ?? ka;
                        rls = new RichTextLayoutState(ref le, glyphSource);
                        var dependencies = _RichTextConfiguration.Append(ref le, ref rls, _Text, _StyleName);
                        if (dependencies.Count > 0) {
                            if (_Dependencies == null)
                                _Dependencies = new List<AsyncRichImage>();

                            for (int i = 0, c = dependencies.Count; i < c; i++) {
                                var d = dependencies[i];
                                if (!d.HasValue)
                                    _AwaitingDependencies = true;
                                _Dependencies.Add(d);
                            }
                        } else
                            _AwaitingDependencies = false;
                        if (le.IsTruncated && !TruncatedIndicator.IsNull)
                            _RichTextConfiguration.Append(ref le, ref rls, TruncatedIndicator, _StyleName, overrideSuppress: false);
                        _RichTextConfiguration.KerningAdjustments = ka;
                    } else {
                        le.AppendText(glyphSource, _Text, _KerningAdjustments);
                        if (le.IsTruncated && !TruncatedIndicator.IsNull)
                            le.AppendText(glyphSource, TruncatedIndicator, _KerningAdjustments, overrideSuppress: false);
                    }

                    _CachedGlyphVersion = glyphSource.Version;
                    _CachedStringLayout = le.Finish();

                    if (le.Markers.Count > 0) {
                        var m = GetMarkers();
                        foreach (var kvp in le.Markers) {
                            if ((rls.MarkedStrings.Count > 0) && (kvp.MarkedString != default(AbstractString))) {
                                if (_RichMarkers == null)
                                    _RichMarkers = new List<LayoutMarker>();
                                _RichMarkers.Add(kvp);
                            } else {
                                m[new Pair<int>(kvp.FirstCharacterIndex, kvp.LastCharacterIndex)] = kvp;
                            }
                        }
                    }
                    if (le.HitTests.Count > 0) {
                        var ht = GetHitTests();
                        foreach (var kvp in le.HitTests) 
                            ht[kvp.Position] = kvp;
                    }

                    _Buffer = le.buffer;
                } finally {
                    le.Dispose();
                }
            }

            var result = _CachedStringLayout.Value;
            if (result.Boxes.Count > 0) {
                if (_Boxes == null)
                    _Boxes = new List<Bounds>(result.Boxes.Count);
                for (int i = 0, c = result.Boxes.Count; i < c; i++)
                    _Boxes.Add(result.Boxes[i]);
            }
            return result;
        }
    }

    public class FallbackGlyphSource : IGlyphSource, IDisposable, IEnumerable<IGlyphSource> {
        public bool IsDisposed { get; private set; }
        public bool MaxLineSpacing = true;
        public bool OwnsSources = true;
        public GlyphPixelAlignment? DefaultAlignment { get; set; }
        private readonly IGlyphSource[] Sources = null;

        public FallbackGlyphSource (bool ownsSources, params IGlyphSource[] sources) {
            OwnsSources = ownsSources;
            Sources = sources;
        }

        public FallbackGlyphSource (params IGlyphSource[] sources) {
            Sources = sources;
        }

        public SpriteFont SpriteFont
        {
            get
            {
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

        public float DPIScaleFactor {
            get {
                return Sources[0].DPIScaleFactor;
            }
        }

        public float LineSpacing {
            get {
                if (!MaxLineSpacing)
                    return Sources[0].LineSpacing;

                var ls = 0f;
                foreach (var s in Sources)
                    ls = Math.Max(ls, s.LineSpacing);
                return ls;
            }
        }

        int IGlyphSource.Version {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                int result = 0;
                foreach (var item in Sources)
                    result += item.Version;
                return result;
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
    }

    public interface IGlyphSource {
        bool GetGlyph (uint ch, out Glyph result);
        float LineSpacing { get; }
        float DPIScaleFactor { get; }
        GlyphPixelAlignment? DefaultAlignment { get; }
        bool IsDisposed { get; }
        int Version { get; }
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

        public static bool GetGlyph (this IGlyphSource source, char ch, out Glyph result) {
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

    public struct SpriteFontGlyphSource : IGlyphSource {
        public readonly SpriteFont Font;
        public readonly Texture2D Texture;
        public GlyphPixelAlignment? DefaultAlignment => GlyphPixelAlignment.None;

        public readonly TextUtils.FontFields Fields;
        public readonly int DefaultCharacterIndex;

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
                Character = ch,
                Texture = Texture,
                RectInTexture = rect,
                BoundsInTexture = Texture.BoundsFromRectangle(ref rect),
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
        public AbstractTextureReference Texture;
        public uint Character;
        public Rectangle RectInTexture;
        public Bounds BoundsInTexture;
        public float XOffset, YOffset;
        public float LeftSideBearing;
        public float RightSideBearing;
        public float Width;
        public float CharacterSpacing;
        public float LineSpacing;
        public float Baseline;
        public Color? DefaultColor;

        public float WidthIncludingBearing {
            get {
                return LeftSideBearing + RightSideBearing + Width;
            }
        }
    }
}

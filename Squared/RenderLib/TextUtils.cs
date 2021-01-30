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
using Squared.Util;
using Squared.Util.Text;

namespace Squared.Render.Text {
    public class DynamicStringLayout {
        private ArraySegment<BitmapDrawCall> _Buffer; 
        private StringLayout? _CachedStringLayout;
        private int _CachedGlyphVersion = -1;

        private RichTextConfiguration _RichTextConfiguration;
        private string _StyleName;
        private Dictionary<char, KerningAdjustment> _KerningAdjustments; 
        private IGlyphSource _GlyphSource;
        private AbstractString _Text;
        private Vector2 _Position = Vector2.Zero;
        private Color _DefaultColor = Microsoft.Xna.Framework.Color.White;
        private Color? _Color = null;
        private float _Scale = 1;
        private float _Spacing = 1;
        private float _SortKey = 0;
        private int _CharacterSkipCount = 0;
        private int _CharacterLimit = int.MaxValue;
        private float _XOffsetOfFirstLine = 0;
        private float _XOffsetOfNewLine = 0;
        private float? _LineBreakAtX = null;
        private float? _StopAtY = null;
        private bool _WordWrap = false;
        private bool _CharacterWrap = true;
        private float _WrapIndentation = 0f;
        private GlyphPixelAlignment _AlignToPixels = GlyphPixelAlignment.Default;
        private char _WrapCharacter = '\0';
        private int _Alignment = (int)HorizontalAlignment.Left;
        private bool _ReverseOrder = false;
        private int _LineLimit = int.MaxValue;
        private bool _MeasureOnly = false;
        private bool _RichText = false;
        private char? _ReplacementCharacter = null;

        private readonly Dictionary<Pair<int>, LayoutMarker> _Markers = new Dictionary<Pair<int>, LayoutMarker>();
        private readonly Dictionary<Vector2, LayoutHitTest> _HitTests = new Dictionary<Vector2, LayoutHitTest>();

        public DynamicStringLayout (SpriteFont font, string text = "") {
            _GlyphSource = new SpriteFontGlyphSource(font);
            _Text = text;
        }

        public DynamicStringLayout (IGlyphSource font = null, string text = "") {
            _GlyphSource = font;
            _Text = text;
        }

        public void ResetMarkersAndHitTests () {
            if ((_Markers.Count > 0) || (_HitTests.Count > 0))
                Invalidate();
            _Markers.Clear();
            _HitTests.Clear();
        }

        public LayoutMarker? Mark (int characterIndex) {
            var key = new Pair<int>(characterIndex, characterIndex);

            LayoutMarker result;
            if (!_Markers.TryGetValue(key, out result)) {
                _Markers[key] = new LayoutMarker {
                    FirstCharacterIndex = characterIndex,
                    LastCharacterIndex = characterIndex
                };
                Invalidate();
                return null;
            }

            if (result.Bounds.HasValue)
                return result;
            else
                return null;
        }

        public LayoutMarker? Mark (int firstCharacterIndex, int lastCharacterIndex) {
            var key = new Pair<int>(firstCharacterIndex, lastCharacterIndex);

            LayoutMarker result;
            if (!_Markers.TryGetValue(key, out result)) {
                _Markers[key] = new LayoutMarker {
                    FirstCharacterIndex = firstCharacterIndex,
                    LastCharacterIndex = lastCharacterIndex
                };
                Invalidate();
                return null;
            }

            if (result.Bounds.HasValue)
                return result;
            else
                return null;
        }

        // FIXME: Garbage
        public IReadOnlyDictionary<Pair<int>, LayoutMarker> Markers => _Markers;
        public IReadOnlyDictionary<Vector2, LayoutHitTest> HitTests => _HitTests;

        public LayoutHitTest? HitTest (Vector2 position) {
            LayoutHitTest result;
            if (!_HitTests.TryGetValue(position, out result)) {
                _HitTests[position] = new LayoutHitTest {
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

        private void InvalidatingValueAssignment<T> (ref T destination, T newValue) 
            where T : struct, IEquatable<T>
        {
            if (!destination.Equals(newValue)) {
                destination = newValue;
                Invalidate();
            }
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

        public AbstractString Text {
            get {
                return _Text;
            }
            set {
                InvalidatingValueAssignment(ref _Text, value);
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

                InvalidatingReferenceAssignment(
                    ref _GlyphSource, 
                    new SpriteFontGlyphSource(value)
                );
            }
        }

        public IGlyphSource GlyphSource {
            get {
                return _GlyphSource;
            }
            set {
                if (value == null)
                    throw new ArgumentNullException("value");
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

        public int CharacterSkipCount {
            get {
                return _CharacterSkipCount;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterSkipCount, value);
            }
        }

        public int CharacterLimit {
            get {
                return _CharacterLimit;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterLimit, value);
            }
        }

        public float XOffsetOfFirstLine {
            get {
                return _XOffsetOfFirstLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfFirstLine, value);
            }
        }

        public float XOffsetOfNewLine {
            get {
                return _XOffsetOfNewLine;
            }
            set {
                InvalidatingValueAssignment(ref _XOffsetOfNewLine, value);
            }
        }

        public float? LineBreakAtX {
            get {
                return _LineBreakAtX;
            }
            set {
                InvalidatingNullableAssignment(ref _LineBreakAtX, value);
            }
        }

        public float? StopAtY {
            get {
                return _StopAtY;
            }
            set {
                InvalidatingNullableAssignment(ref _StopAtY, value);
            }
        }

        public int LineLimit {
            get {
                return _LineLimit;
            }
            set {
                InvalidatingValueAssignment(ref _LineLimit, value);
            }
        }

        public RichTextConfiguration RichTextConfiguration {
            get {
                return _RichTextConfiguration;
            }
            set {
                InvalidatingValueAssignment(ref _RichTextConfiguration, value);
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

        public bool WordWrap {
            get {
                return _WordWrap;
            }
            set {
                InvalidatingValueAssignment(ref _WordWrap, value);
            }
        }

        public bool CharacterWrap {
            get {
                // FIXME: Is this right?
                return _CharacterWrap;
            }
            set {
                InvalidatingValueAssignment(ref _CharacterWrap, value);
            }
        }

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

        public char? ReplacementCharacter {
            get {
                return _ReplacementCharacter;
            }
            set {
                InvalidatingNullableAssignment(ref _ReplacementCharacter, value);
            }
        }

        public bool IsValid {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return (_CachedStringLayout.HasValue && (_CachedGlyphVersion >= _GlyphSource.Version) && !_GlyphSource.IsDisposed);
            }
        }

        public void Invalidate () {
            // Hey, you're the boss
            _CachedStringLayout = null;
        }

        /// <summary>
        /// Constructs a layout engine based on this configuration
        /// </summary>
        public void MakeLayoutEngine (out StringLayoutEngine result) {
            result = new StringLayoutEngine {
                buffer = _Buffer,
                position = _Position,
                overrideColor = _Color,
                defaultColor = _DefaultColor,
                scale = _Scale,
                spacing = _Spacing,
                sortKey = _SortKey,
                characterSkipCount = _CharacterSkipCount,
                characterLimit = _CharacterLimit,
                xOffsetOfFirstLine = _XOffsetOfFirstLine,
                xOffsetOfWrappedLine = _XOffsetOfNewLine + _WrapIndentation,
                xOffsetOfNewLine = _XOffsetOfNewLine,
                lineBreakAtX = _LineBreakAtX,
                stopAtY = _StopAtY,
                alignToPixels = _AlignToPixels,
                characterWrap = _CharacterWrap,
                wordWrap = _WordWrap,
                wrapCharacter = _WrapCharacter,
                alignment = (HorizontalAlignment)_Alignment,
                reverseOrder = _ReverseOrder,
                lineLimit = _LineLimit,
                measureOnly = _MeasureOnly,
                replacementCodepoint = _ReplacementCharacter
            };

            foreach (var kvp in _Markers)
                result.Markers.Add(kvp.Value);
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
            this.Position = source.Position;
            this.ReverseOrder = source.ReverseOrder;
            this.Scale = source.Scale;
            this.Spacing = source.Spacing;
            this.SortKey = source.SortKey;
            this.Text = source.Text;
            this.WordWrap = source.WordWrap;
            this.WrapCharacter = source.WrapCharacter;
            this.WrapIndentation = source.WrapIndentation;
            this.XOffsetOfFirstLine = source.XOffsetOfFirstLine;
            this.XOffsetOfNewLine = source.XOffsetOfNewLine;
            this.RichText = source.RichText;
            this.RichTextConfiguration = source.RichTextConfiguration;
            this.ReplacementCharacter = source.ReplacementCharacter;
        }

        /// <summary>
        /// If the current state is invalid, computes a current layout. Otherwise, returns the cached layout.
        /// </summary>
        public StringLayout Get () {
            if (_CachedStringLayout.HasValue && 
                ((_CachedGlyphVersion < _GlyphSource.Version) || _GlyphSource.IsDisposed)
            )
                Invalidate();

            if (!_CachedStringLayout.HasValue) {
                if (_Text.IsNull) {
                    _CachedStringLayout = new StringLayout();
                    return _CachedStringLayout.Value;
                }

                int length = _Text.Length;

                int capacity = length + StringLayoutEngine.DefaultBufferPadding;

                if ((_Buffer.Array != null) && (_Buffer.Count < capacity))
                    _Buffer = default(ArraySegment<BitmapDrawCall>);

                if (_Buffer.Array == null) {
                    var newCapacity = 1 << (int)Math.Ceiling(Math.Log(capacity, 2));
                    var array = new BitmapDrawCall[newCapacity];
                    _Buffer = new ArraySegment<BitmapDrawCall>(array);
                }

                if (_Buffer.Count < capacity)
                    throw new InvalidOperationException("Buffer too small");

                StringLayoutEngine le;
                MakeLayoutEngine(out le);

                try {
                    le.Initialize();
                    if (_RichText) {
                        var ka = _RichTextConfiguration.KerningAdjustments;
                        _RichTextConfiguration.KerningAdjustments = _KerningAdjustments ?? ka;
                        _RichTextConfiguration.Append(ref le, _GlyphSource, _Text, _StyleName);
                        _RichTextConfiguration.KerningAdjustments = ka;
                    } else
                        le.AppendText(_GlyphSource, _Text, _KerningAdjustments);

                    _CachedGlyphVersion = _GlyphSource.Version;
                    _CachedStringLayout = le.Finish();

                    foreach (var kvp in le.Markers)
                        _Markers[new Pair<int>(kvp.FirstCharacterIndex, kvp.LastCharacterIndex)] = kvp;
                    foreach (var kvp in le.HitTests)
                        _HitTests[kvp.Position] = kvp;
                } finally {
                    le.Dispose();
                }
            }

            return _CachedStringLayout.Value;
        }
    }

    public class FallbackGlyphSource : IGlyphSource, IDisposable, IEnumerable<IGlyphSource> {
        public bool IsDisposed { get; private set; }
        private readonly IGlyphSource[] Sources = null;

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
                return Sources[0].LineSpacing;
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
            foreach (var item in Sources) {
                if (item is IDisposable)
                    ((IDisposable)item).Dispose();
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

        bool IsDisposed { get; }
        int Version { get; }
    }

    public static class SpriteFontUtil {
        public struct FontFields {
            public Texture2D Texture;
            public List<Rectangle> GlyphRectangles;
            public List<Rectangle> CropRectangles;
            public List<char> Characters;
            public List<Vector3> Kerning;
        }
        internal static readonly FieldInfo textureValue, glyphData, croppingData, kerning, characterMap;

        static SpriteFontUtil () {
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

        public readonly SpriteFontUtil.FontFields Fields;
        public readonly int DefaultCharacterIndex;

        public bool IsDisposed => (Texture?.IsDisposed == false);

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
            Font = font;

            if (SpriteFontUtil.GetPrivateFields(font, out Fields)) {
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
    }
}

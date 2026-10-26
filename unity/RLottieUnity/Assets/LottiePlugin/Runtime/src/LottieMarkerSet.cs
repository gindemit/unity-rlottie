using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace LottiePlugin
{
    /// <summary>One standard Lottie marker mapped from composition time to rlottie's zero-based render frames.</summary>
    public struct LottieMarker
    {
        public string Name { get; internal set; }
        public double TimelineStart { get; internal set; }
        public double TimelineDuration { get; internal set; }
        public int FirstRenderFrame { get; internal set; }
        public int LastRenderFrameInclusive { get; internal set; }
    }

    /// <summary>Validated standard Lottie markers and deterministic clip-to-frame mapping.</summary>
    public sealed class LottieMarkerSet
    {
        private static readonly ConditionalWeakTable<string, ParsedDocument> sParsed =
            new ConditionalWeakTable<string, ParsedDocument>();
        private static readonly object sParsedLock = new object();

        private readonly ParsedDocument _document;
        private readonly Func<bool> _ownerDisposed;

        public double FrameRate { get { return _document.FrameRate; } }
        public double TimelineInPoint { get { return _document.InPoint; } }
        public IReadOnlyList<LottieMarker> Markers { get { return _document.Markers; } }

        private LottieMarkerSet(ParsedDocument document, Func<bool> ownerDisposed)
        {
            _document = document;
            _ownerDisposed = ownerDisposed;
        }

        public static LottieMarkerSet Parse(string jsonData, long totalFramesCount)
        {
            return Parse(jsonData, totalFramesCount, null);
        }

        internal static LottieMarkerSet Parse(string jsonData, long totalFramesCount, Func<bool> ownerDisposed)
        {
            if (string.IsNullOrEmpty(jsonData)) throw new ArgumentException("Lottie JSON is required.", nameof(jsonData));
            if (totalFramesCount <= 0 || totalFramesCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(totalFramesCount));

            // Keyed by string instance: owners that keep their JSON string share one parse.
            ParsedDocument document;
            if (sParsed.TryGetValue(jsonData, out document) && document.TotalFramesCount == totalFramesCount)
                return new LottieMarkerSet(document, ownerDisposed);
            document = ParseDocument(jsonData, totalFramesCount);
            lock (sParsedLock)
            {
                sParsed.Remove(jsonData);
                sParsed.Add(jsonData, document);
            }
            return new LottieMarkerSet(document, ownerDisposed);
        }

        private static ParsedDocument ParseDocument(string jsonData, long totalFramesCount)
        {
            var root = JsonSubsetParser.Parse(jsonData) as Dictionary<string, object>;
            if (root == null) throw new FormatException("The Lottie document root must be an object.");
            double frameRate = RequiredFiniteNumber(root, "fr");
            double inPoint = RequiredFiniteNumber(root, "ip");
            RequiredFiniteNumber(root, "op");
            if (frameRate <= 0) throw new FormatException("Lottie frame rate 'fr' must be greater than zero.");

            object markerValue;
            var rawMarkers = root.TryGetValue("markers", out markerValue) ? markerValue as List<object> : null;
            var markers = new List<LottieMarker>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            int roundedInPoint = RoundLikeRlottie(inPoint);
            int lastAnimationFrame = checked((int)totalFramesCount - 1);
            if (rawMarkers != null)
            {
                foreach (object raw in rawMarkers)
                {
                    var markerObject = raw as Dictionary<string, object>;
                    if (markerObject == null) throw new FormatException("Each Lottie marker must be an object.");
                    object nameValue;
                    string name = markerObject.TryGetValue("cm", out nameValue) ? nameValue as string : null;
                    if (string.IsNullOrEmpty(name)) throw new FormatException("Lottie marker name 'cm' is required.");
                    if (!names.Add(name)) throw new FormatException("Duplicate Lottie marker name: " + name);
                    double start = RequiredFiniteNumber(markerObject, "tm");
                    double duration = RequiredFiniteNumber(markerObject, "dr");
                    if (duration < 0) throw new FormatException("Lottie marker duration cannot be negative: " + name);

                    // rlottie rounds ip, adds it to zero-based render indices, and clamps to its
                    // inclusive TotalFramesCount. Lottie marker intervals remain half-open.
                    int firstTimeline = CheckedCeiling(start, name);
                    int lastTimeline = duration == 0
                        ? firstTimeline
                        : CheckedCeiling(start + duration, name) - 1;
                    if (duration > 0 && lastTimeline < firstTimeline)
                        throw new FormatException("Lottie marker has no renderable integral frame: " + name);
                    int first = Clamp((long)firstTimeline - roundedInPoint, 0, lastAnimationFrame);
                    int last = Clamp((long)lastTimeline - roundedInPoint, 0, lastAnimationFrame);
                    if (last < first) last = first;
                    markers.Add(new LottieMarker
                    {
                        Name = name,
                        TimelineStart = start,
                        TimelineDuration = duration,
                        FirstRenderFrame = first,
                        LastRenderFrameInclusive = last
                    });
                }
            }
            return new ParsedDocument(totalFramesCount, frameRate, inPoint, markers);
        }

        public bool TryGet(string name, out LottieMarker marker)
        {
            ThrowIfDisposed();
            if (name == null) { marker = default(LottieMarker); return false; }
            return _document.ByName.TryGetValue(name, out marker);
        }

        public LottieMarker Get(string name)
        {
            ThrowIfDisposed();
            if (name == null) throw new ArgumentNullException(nameof(name));
            LottieMarker marker;
            if (!_document.ByName.TryGetValue(name, out marker))
                throw new KeyNotFoundException("No Lottie marker named '" + name + "' exists.");
            return marker;
        }

        public int Frame(string name, float normalized)
        {
            if (float.IsNaN(normalized)) throw new ArgumentException("Normalized position must be finite.", nameof(normalized));
            LottieMarker marker = Get(name);
            double value = Math.Max(0.0, Math.Min(1.0, normalized));
            int count = marker.LastRenderFrameInclusive - marker.FirstRenderFrame + 1;
            long offset = (long)Math.Floor(value * count);
            if (offset >= count) offset = count - 1;
            return marker.FirstRenderFrame + (int)offset;
        }

        public double DurationSeconds(string name) { return Get(name).TimelineDuration / FrameRate; }

        private void ThrowIfDisposed()
        {
            if (_ownerDisposed != null && _ownerDisposed()) throw new ObjectDisposedException(nameof(LottieAnimation));
        }

        private sealed class ParsedDocument
        {
            public readonly long TotalFramesCount;
            public readonly double FrameRate;
            public readonly double InPoint;
            public readonly ReadOnlyCollection<LottieMarker> Markers;
            public readonly Dictionary<string, LottieMarker> ByName;

            public ParsedDocument(long totalFramesCount, double frameRate, double inPoint, List<LottieMarker> markers)
            {
                TotalFramesCount = totalFramesCount;
                FrameRate = frameRate;
                InPoint = inPoint;
                Markers = new ReadOnlyCollection<LottieMarker>(markers);
                ByName = new Dictionary<string, LottieMarker>(StringComparer.Ordinal);
                foreach (LottieMarker marker in markers)
                {
                    ByName.Add(marker.Name, marker);
                }
            }
        }

        private static double RequiredFiniteNumber(Dictionary<string, object> values, string key)
        {
            object raw;
            if (!values.TryGetValue(key, out raw) || !(raw is double))
                throw new FormatException("Required numeric Lottie field '" + key + "' is missing.");
            double value = (double)raw;
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new FormatException("Lottie field '" + key + "' must be finite.");
            return value;
        }

        private static int CheckedCeiling(double value, string marker)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < int.MinValue || value > int.MaxValue)
                throw new FormatException("Lottie marker timeline is outside the supported range: " + marker);
            return checked((int)Math.Ceiling(value));
        }

        private static int RoundLikeRlottie(double value)
        {
            double rounded = value >= 0 ? Math.Floor(value + 0.5) : Math.Ceiling(value - 0.5);
            if (rounded < int.MinValue || rounded > int.MaxValue) throw new FormatException("Lottie 'ip' is outside the supported range.");
            return (int)rounded;
        }

        private static int Clamp(long value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return (int)value;
        }

        // Validate the complete document, but materialize only marker metadata. Scratch
        // key sets are reused by object depth; skipped shape trees retain no DOM objects.
        // No Span APIs: this path also runs in Unity 2019.4.
        private sealed class JsonSubsetParser
        {
            // Private process salt and nonlinear mixing avoid predictable polynomial
            // collision families without changing decoded-key equality or JSON results.
            private static readonly ulong sKeyHashSeed = ((ulong)(uint)Guid.NewGuid().GetHashCode() << 32) |
                (uint)Guid.NewGuid().GetHashCode();
            private enum Selection { Skip, Root, Scalar, Markers, Marker }
            private readonly string _text;
            private readonly List<HashSet<KeyToken>> _keys = new List<HashSet<KeyToken>>();
            private readonly List<KeyToken> _activeKeys = new List<KeyToken>();
            private int _position, _objectDepth;
            private JsonSubsetParser(string text) { _text = text; }

            public static object Parse(string text)
            {
                var parser = new JsonSubsetParser(text);
                object value = parser.ReadValue(Selection.Root);
                parser.SkipWhiteSpace();
                if (parser._position != text.Length) throw parser.Error("Unexpected trailing JSON content.");
                return value;
            }

            private object ReadValue(Selection selection)
            {
                SkipWhiteSpace();
                if (_position >= _text.Length) throw Error("Unexpected end of JSON.");
                char c = _text[_position];
                if (c == '{') return ReadObject(selection);
                if (c == '[') return ReadArray(selection);
                if (c == '"')
                {
                    KeyToken value = ReadStringToken();
                    return selection == Selection.Scalar ? value.Materialize() : null;
                }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(selection == Selection.Scalar);
                if (Consume("true") || Consume("false") || Consume("null")) return null;
                throw Error("Unexpected JSON token.");
            }

            private Dictionary<string, object> ReadObject(Selection selection)
            {
                bool keep = selection == Selection.Root || selection == Selection.Marker;
                var result = keep ? new Dictionary<string, object>(StringComparer.Ordinal) : null;
                int depth = _objectDepth++;
                if (depth == _keys.Count) _keys.Add(new HashSet<KeyToken>());
                HashSet<KeyToken> keys = _keys[depth];
                int keyStart = _activeKeys.Count;
                try
                {
                    _position++; SkipWhiteSpace();
                    if (Take('}')) return result;
                    while (true)
                    {
                        SkipWhiteSpace();
                        if (_position >= _text.Length || _text[_position] != '"') throw Error("Expected an object key.");
                        KeyToken key = ReadStringToken(); SkipWhiteSpace();
                        if (!Take(':')) throw Error("Expected ':' after an object key.");
                        if (!keys.Add(key)) throw Error("Duplicate JSON object key: " + key.Materialize());
                        _activeKeys.Add(key);
                        string field = null;
                        Selection child = Selection.Skip;
                        if (selection == Selection.Root)
                        {
                            if (key.Is("fr")) field = "fr";
                            else if (key.Is("ip")) field = "ip";
                            else if (key.Is("op")) field = "op";
                            else if (key.Is("markers")) { field = "markers"; child = Selection.Markers; }
                        }
                        else if (selection == Selection.Marker)
                        {
                            if (key.Is("cm")) field = "cm";
                            else if (key.Is("tm")) field = "tm";
                            else if (key.Is("dr")) field = "dr";
                        }
                        if (field != null && child == Selection.Skip) child = Selection.Scalar;
                        object value = ReadValue(child);
                        if (field != null) result.Add(field, value);
                        SkipWhiteSpace();
                        if (Take('}')) return result;
                        if (!Take(',')) throw Error("Expected ',' or '}'.");
                    }
                }
                finally
                {
                    // Remove only this object's keys. HashSet.Clear would repeatedly clear
                    // a wide prior sibling's entire capacity for every later tiny object.
                    for (int i = _activeKeys.Count - 1; i >= keyStart; i--) keys.Remove(_activeKeys[i]);
                    _activeKeys.RemoveRange(keyStart, _activeKeys.Count - keyStart);
                    _objectDepth--;
                }
            }

            private List<object> ReadArray(Selection selection)
            {
                bool keep = selection == Selection.Markers;
                var result = keep ? new List<object>() : null;
                _position++; SkipWhiteSpace();
                if (Take(']')) return result;
                while (true)
                {
                    object value = ReadValue(keep ? Selection.Marker : Selection.Skip);
                    if (keep) result.Add(value);
                    SkipWhiteSpace();
                    if (Take(']')) return result;
                    if (!Take(',')) throw Error("Expected ',' or ']'.");
                }
            }

            // A validated string slice, hashed and compared by its decoded UTF-16 units.
            // Escaped-equivalent keys compare equal; hash collisions never establish equality.
            private struct KeyToken : IEquatable<KeyToken>
            {
                private readonly string _text;
                private readonly int _start, _end, _hash;
                private readonly bool _escaped;
                public KeyToken(string text, int start, int end, int hash, bool escaped)
                { _text = text; _start = start; _end = end; _hash = hash; _escaped = escaped; }
                public override int GetHashCode() { return _hash; }
                public bool Equals(KeyToken other)
                {
                    if (_hash != other._hash) return false;
                    int a = _start, b = other._start;
                    while (a < _end && b < other._end)
                        if (Next(_text, ref a) != Next(other._text, ref b)) return false;
                    return a == _end && b == other._end;
                }
                public override bool Equals(object other) { return other is KeyToken && Equals((KeyToken)other); }
                public bool Is(string field)
                {
                    int position = _start, index = 0;
                    while (position < _end && index < field.Length)
                        if (Next(_text, ref position) != field[index++]) return false;
                    return position == _end && index == field.Length;
                }
                public string Materialize()
                {
                    if (!_escaped) return _text.Substring(_start, _end - _start);
                    var result = new StringBuilder();
                    int position = _start;
                    while (position < _end) result.Append(Next(_text, ref position));
                    return result.ToString();
                }
                private static char Next(string text, ref int position)
                {
                    char c = text[position++];
                    if (c != '\\') return c;
                    char escape = text[position++];
                    if (escape == 'u')
                    {
                        int code;
                        UnicodeCode(text, position, out code);
                        position += 4;
                        return (char)code;
                    }
                    const string escapes = "\"\\/bfnrt";
                    const string replacements = "\"\\/\b\f\n\r\t";
                    return replacements[escapes.IndexOf(escape)];
                }
            }

            private KeyToken ReadStringToken()
            {
                int start = ++_position;
                ulong hash = sKeyHashSeed ^ 14695981039346656037UL;
                bool escaped = false;
                while (_position < _text.Length)
                {
                    char c = _text[_position++];
                    if (c == '"') return new KeyToken(_text, start, _position - 1, KeyHash(hash), escaped);
                    if (c == '\\')
                    {
                        escaped = true;
                        if (_position >= _text.Length) throw Error("Incomplete JSON escape.");
                        char escape = _text[_position++];
                        if (escape == 'u')
                        {
                            if (_position + 4 > _text.Length) throw Error("Incomplete Unicode escape.");
                            int code;
                            if (!UnicodeCode(_text, _position, out code)) throw Error("Invalid Unicode escape.");
                            c = (char)code; _position += 4;
                        }
                        else
                        {
                            const string escapes = "\"\\/bfnrt";
                            const string replacements = "\"\\/\b\f\n\r\t";
                            int index = escapes.IndexOf(escape);
                            if (index < 0) throw Error("Invalid JSON escape.");
                            c = replacements[index];
                        }
                    }
                    else if (c < 0x20) throw Error("Control character in JSON string.");
                    hash = unchecked((hash ^ c) * 1099511628211UL);
                }
                throw Error("Unterminated JSON string.");
            }

            private static int KeyHash(ulong value)
            {
                unchecked
                {
                    value ^= value >> 33;
                    value *= 0xff51afd7ed558ccdUL;
                    value ^= value >> 33;
                    value *= 0xc4ceb9fe1a85ec53UL;
                    value ^= value >> 33;
                    return (int)(value ^ (value >> 32));
                }
            }

            private static bool UnicodeCode(string text, int position, out int code)
            {
                code = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = text[position + i];
                    int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0)
                    {
                        // Keep the old HexNumber parser's uncommon whitespace behavior.
                        return int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code);
                    }
                    code = (code << 4) | digit;
                }
                return true;
            }

            private object ReadNumber(bool keep)
            {
                int start = _position;
                if (Take('-')) { }
                if (Take('0')) { }
                else { RequireDigits(); }
                if (Take('.')) RequireDigits();
                int exponent = -1;
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    exponent = _position;
                    RequireDigits();
                }
                if (!keep && OrdinaryFiniteNumber(start, exponent)) return null;
                // Required values retain the exact runtime's double conversion. Exceptional
                // skipped tokens (overflow, huge exponents or Unicode digits) also use the old
                // conversion, preserving its acceptance/rejection without newer Span APIs.
                double result;
                if (!double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result)) throw Error("Invalid JSON number.");
                return keep ? (object)result : null;
            }

            private bool OrdinaryFiniteNumber(int start, int exponent)
            {
                // At most 128 ASCII token characters and exponent magnitude <=128 guarantee
                // finite conversion (absolute magnitude <1e256). Grammar was already checked.
                if (_position - start > 128) return false;
                for (int i = start; i < _position; i++) if (_text[i] > 127) return false;
                int power = 0;
                if (exponent >= 0)
                    for (int i = exponent; i < _position; i++)
                    {
                        power = power * 10 + _text[i] - '0';
                        if (power > 128) return false;
                    }
                return true;
            }

            private void RequireDigits()
            {
                int start = _position;
                while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
                if (_position == start) throw Error("Expected digits in JSON number.");
            }

            private bool Consume(string token)
            {
                if (_position + token.Length > _text.Length ||
                    string.CompareOrdinal(_text, _position, token, 0, token.Length) != 0) return false;
                _position += token.Length; return true;
            }
            private bool Take(char value)
            {
                if (_position >= _text.Length || _text[_position] != value) return false;
                _position++; return true;
            }
            private void SkipWhiteSpace() { while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++; }
            private FormatException Error(string message) { return new FormatException(message + " At character " + _position + "."); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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
        private readonly Dictionary<string, LottieMarker> _byName;
        private readonly Func<bool> _ownerDisposed;

        public double FrameRate { get; private set; }
        public double TimelineInPoint { get; private set; }
        public IReadOnlyList<LottieMarker> Markers { get; private set; }

        private LottieMarkerSet(double frameRate, double inPoint, List<LottieMarker> markers,
            Func<bool> ownerDisposed)
        {
            FrameRate = frameRate;
            TimelineInPoint = inPoint;
            Markers = new ReadOnlyCollection<LottieMarker>(markers);
            _ownerDisposed = ownerDisposed;
            _byName = new Dictionary<string, LottieMarker>(StringComparer.Ordinal);
            foreach (LottieMarker marker in markers)
            {
                _byName.Add(marker.Name, marker);
            }
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
            return new LottieMarkerSet(frameRate, inPoint, markers, ownerDisposed);
        }

        public bool TryGet(string name, out LottieMarker marker)
        {
            ThrowIfDisposed();
            if (name == null) { marker = default(LottieMarker); return false; }
            return _byName.TryGetValue(name, out marker);
        }

        public LottieMarker Get(string name)
        {
            ThrowIfDisposed();
            if (name == null) throw new ArgumentNullException(nameof(name));
            LottieMarker marker;
            if (!_byName.TryGetValue(name, out marker))
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

        // Dependency-free parser kept internal so marker parsing works in Unity 2019.4.
        private sealed class JsonSubsetParser
        {
            private readonly string _text;
            private int _position;
            private JsonSubsetParser(string text) { _text = text; }

            public static object Parse(string text)
            {
                var parser = new JsonSubsetParser(text);
                object value = parser.ReadValue();
                parser.SkipWhiteSpace();
                if (parser._position != text.Length) throw parser.Error("Unexpected trailing JSON content.");
                return value;
            }

            private object ReadValue()
            {
                SkipWhiteSpace();
                if (_position >= _text.Length) throw Error("Unexpected end of JSON.");
                char c = _text[_position];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Consume("true")) return true;
                if (Consume("false")) return false;
                if (Consume("null")) return null;
                throw Error("Unexpected JSON token.");
            }

            private Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _position++; SkipWhiteSpace();
                if (Take('}')) return result;
                while (true)
                {
                    SkipWhiteSpace();
                    if (_position >= _text.Length || _text[_position] != '"') throw Error("Expected an object key.");
                    string key = ReadString(); SkipWhiteSpace();
                    if (!Take(':')) throw Error("Expected ':' after an object key.");
                    if (result.ContainsKey(key)) throw Error("Duplicate JSON object key: " + key);
                    result.Add(key, ReadValue()); SkipWhiteSpace();
                    if (Take('}')) return result;
                    if (!Take(',')) throw Error("Expected ',' or '}'.");
                }
            }

            private List<object> ReadArray()
            {
                var result = new List<object>();
                _position++; SkipWhiteSpace();
                if (Take(']')) return result;
                while (true)
                {
                    result.Add(ReadValue()); SkipWhiteSpace();
                    if (Take(']')) return result;
                    if (!Take(',')) throw Error("Expected ',' or ']'.");
                }
            }

            private string ReadString()
            {
                _position++;
                var result = new StringBuilder();
                while (_position < _text.Length)
                {
                    char c = _text[_position++];
                    if (c == '"') return result.ToString();
                    if (c == '\\')
                    {
                        if (_position >= _text.Length) throw Error("Incomplete JSON escape.");
                        char escape = _text[_position++];
                        if (escape == 'u')
                        {
                            if (_position + 4 > _text.Length) throw Error("Incomplete Unicode escape.");
                            int code;
                            if (!int.TryParse(_text.Substring(_position, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code)) throw Error("Invalid Unicode escape.");
                            result.Append((char)code); _position += 4;
                        }
                        else
                        {
                            const string escapes = "\"\\/bfnrt";
                            const string replacements = "\"\\/\b\f\n\r\t";
                            int index = escapes.IndexOf(escape);
                            if (index < 0) throw Error("Invalid JSON escape.");
                            result.Append(replacements[index]);
                        }
                    }
                    else
                    {
                        if (c < 0x20) throw Error("Control character in JSON string.");
                        result.Append(c);
                    }
                }
                throw Error("Unterminated JSON string.");
            }

            private double ReadNumber()
            {
                int start = _position;
                if (Take('-')) { }
                if (Take('0')) { }
                else { RequireDigits(); }
                if (Take('.')) RequireDigits();
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    RequireDigits();
                }
                double result;
                if (!double.TryParse(_text.Substring(start, _position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result)) throw Error("Invalid JSON number.");
                return result;
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

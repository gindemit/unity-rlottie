using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Collections;
using UnityEngine;

namespace LottiePlugin
{
    public enum LottieAlphaMode { PremultipliedBgra, StraightRgba }
    public enum LottieFramePreparation { Serial, Parallel }

    public struct LottieClipSampling
    {
        public string MarkerName;
        public int FramesPerSecond;
        public bool Loop;
        public bool IncludeExactEndFrame;

        public LottieClipSampling(string markerName, int framesPerSecond, bool loop,
            bool includeExactEndFrame = false)
        {
            MarkerName = markerName;
            FramesPerSecond = framesPerSecond;
            Loop = loop;
            IncludeExactEndFrame = includeExactEndFrame;
        }
    }

    public sealed class LottieFrameCacheOptions
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public IReadOnlyList<LottieClipSampling> Clips { get; set; }
        public FilterMode FilterMode { get; set; } = FilterMode.Bilinear;
        public TextureWrapMode WrapMode { get; set; } = TextureWrapMode.Clamp;
        public bool MakeNoLongerReadable { get; set; } = true;
        public LottieAlphaMode AlphaMode { get; set; } = LottieAlphaMode.PremultipliedBgra;
        public long MaximumRawPixelBytes { get; set; } = long.MaxValue;
        public IReadOnlyList<LottieColorOverride> ColorOverrides { get; set; }
        public LottieFramePreparation Preparation { get; set; } = LottieFramePreparation.Serial;
    }

    public sealed class LottieClipPreflight
    {
        public string MarkerName { get; internal set; }
        public int FrameCount { get; internal set; }
        public long RawPixelBytes { get; internal set; }
    }

    public sealed class LottieFrameCachePreflight
    {
        public IReadOnlyList<LottieClipPreflight> Clips { get; internal set; }
        public int TotalFrameCount { get; internal set; }
        public long TotalRawPixelBytes { get; internal set; }
    }

    /// <summary>Incrementally builds immutable, shareable Texture2D snapshots from marker clips.</summary>
    public sealed class LottieFrameCache : IDisposable
    {
        private sealed class Clip
        {
            public LottieClipSampling Sampling;
            public double Duration;
            public int[] SourceFrames;
            public double[] SamplePositions;
            public Texture2D[] Textures;
        }

        private readonly Dictionary<string, Clip> _clips = new Dictionary<string, Clip>(StringComparer.Ordinal);
        private readonly List<Clip> _orderedClips = new List<Clip>();
        private readonly LottieFrameCacheOptions _options;
        private LottieCpuRasterizer _rasterizer;
        private NativeArray<byte> _pixels;
        private int _warmClip;
        private int _warmFrame;
        private int _warmedFrameCount;
        private bool _disposed;
        private bool _ready;
        private LottieFrameCachePreflight _preflight;
        private readonly int _mainThreadId;
        private LottieRasterBatch _batch;
        private readonly LottieFramePreparationPool _pool;
        private byte[] _completed;
        private int _completedOffset;
        private int _reservedBytes;
        public bool FellBack { get; private set; }
        public string FallbackReason { get; private set; }
        public int ParallelFrameCount { get; private set; }

        public bool Ready { get { ThrowIfDisposed(); return _ready; } }
        public int CachedFrameCount { get { ThrowIfDisposed(); return _preflight.TotalFrameCount; } }
        public int WarmedFrameCount { get { ThrowIfDisposed(); return _warmedFrameCount; } }
        public long EstimatedRawPixelBytes { get { ThrowIfDisposed(); return _preflight.TotalRawPixelBytes; } }
        public LottieFrameCachePreflight Preflight { get { ThrowIfDisposed(); return _preflight; } }

        public LottieFrameCache(string jsonData, string resourcesPath, LottieFrameCacheOptions options)
            : this(jsonData, resourcesPath, options, null) { }

        public LottieFrameCache(string jsonData, string resourcesPath, LottieFrameCacheOptions options,
            LottieFramePreparationPool preparationPool)
            : this(jsonData, resourcesPath, options, preparationPool, null) { }

        internal LottieFrameCache(string jsonData, string resourcesPath, LottieFrameCacheOptions options,
            LottieFramePreparationPool preparationPool, Func<LottieRasterBatch> batchFactory)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            LottieCpuRasterizer.ValidateDimensions(options.Width, options.Height);
            if (options.Clips == null || options.Clips.Count == 0)
                throw new ArgumentException("At least one marker clip is required.", nameof(options));
            if (options.MaximumRawPixelBytes < 0) throw new ArgumentOutOfRangeException(nameof(options.MaximumRawPixelBytes));
            if (!Enum.IsDefined(typeof(LottieFramePreparation), options.Preparation))
                throw new ArgumentOutOfRangeException(nameof(options.Preparation));
            if (!Enum.IsDefined(typeof(LottieAlphaMode), options.AlphaMode))
                throw new ArgumentOutOfRangeException(nameof(options.AlphaMode));
            if (options.Preparation == LottieFramePreparation.Parallel && preparationPool == null)
                throw new ArgumentException("Parallel preparation requires an explicitly owned pool.", nameof(preparationPool));
            _pool = preparationPool;
            _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _options = new LottieFrameCacheOptions
            {
                Preparation = options.Preparation,
                Width = options.Width,
                Height = options.Height,
                Clips = new List<LottieClipSampling>(options.Clips).AsReadOnly(),
                FilterMode = options.FilterMode,
                WrapMode = options.WrapMode,
                MakeNoLongerReadable = options.MakeNoLongerReadable,
                AlphaMode = options.AlphaMode,
                MaximumRawPixelBytes = options.MaximumRawPixelBytes,
                ColorOverrides = options.ColorOverrides == null
                    ? null : new List<LottieColorOverride>(options.ColorOverrides).AsReadOnly()
            };
            try
            {
                _rasterizer = LottieCpuRasterizer.LoadFromJsonData(jsonData, resourcesPath,
                    options.Width, options.Height, _options.ColorOverrides);
                _preflight = BuildPreflight(_rasterizer.Markers, _options);
                if (_preflight.TotalRawPixelBytes > options.MaximumRawPixelBytes)
                    throw new InvalidOperationException("The frame cache exceeds MaximumRawPixelBytes.");
                _pixels = new NativeArray<byte>(_rasterizer.ByteCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
                if (_options.Preparation == LottieFramePreparation.Parallel)
                {
                    try { _batch = batchFactory == null ? _pool.CreateBatch(jsonData, resourcesPath, _options) : batchFactory(); }
                    catch (Exception error) { FallBack(error); }
                }
            }
            catch { Dispose(); throw; }
        }

        public bool WarmStep(int maxFrames = 1)
        {
            ThrowIfDisposed();
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                throw new InvalidOperationException("WarmStep must run on the thread that created the frame cache.");
            if (maxFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrames));
            if (_ready) return true;
            int remaining = maxFrames;
            while (remaining-- > 0 && _warmClip < _orderedClips.Count)
            {
                Clip clip = _orderedClips[_warmClip];
                if (_batch != null)
                {
                    try
                    {
                        if (_reservedBytes == 0)
                        {
                            int count = Math.Min(_pool.BatchSize, clip.SourceFrames.Length - _warmFrame);
                            int bytes = checked(count * _pixels.Length);
                            if (!_pool.TryReserve(bytes)) return false;
                            _reservedBytes = bytes;
                            var frames = new int[count];
                            Array.Copy(clip.SourceFrames, _warmFrame, frames, 0, count);
                            _batch.Start(frames);
                        }
                        if (_completed == null)
                        {
                            if (!_batch.TryComplete(out _completed)) return false;
                            if (_completed == null || _completed.Length != _reservedBytes)
                                throw new InvalidOperationException("Invalid raster batch length.");
                            _completedOffset = 0;
                        }
                        NativeArray<byte>.Copy(_completed, _completedOffset, _pixels, 0, _pixels.Length);
                    }
                    catch (Exception error) { FallBack(error); }
                }
                if (_batch == null) _rasterizer.RenderFrame(clip.SourceFrames[_warmFrame], _pixels);
                TextureFormat format = _options.AlphaMode == LottieAlphaMode.StraightRgba
                    ? TextureFormat.RGBA32 : TextureFormat.BGRA32;
                if (_options.AlphaMode == LottieAlphaMode.StraightRgba) ConvertToStraightRgba(_pixels);
                var texture = new Texture2D(_options.Width, _options.Height, format, false, false)
                {
                    filterMode = _options.FilterMode,
                    wrapMode = _options.WrapMode,
                    hideFlags = HideFlags.HideAndDontSave
                };
                try
                {
                    texture.LoadRawTextureData(_pixels);
                    texture.Apply(false, _options.MakeNoLongerReadable);
                }
                catch { UnityEngine.Object.DestroyImmediate(texture); throw; }
                clip.Textures[_warmFrame] = texture;
                _warmedFrameCount++;
                _warmFrame++;
                if (_batch != null)
                {
                    ParallelFrameCount++;
                    _completedOffset += _pixels.Length;
                    if (_completedOffset == _completed.Length) ReleaseBatchPixels();
                }
                if (_warmFrame == clip.SourceFrames.Length) { _warmClip++; _warmFrame = 0; }
            }
            if (_warmClip == _orderedClips.Count)
            {
                _ready = true;
                if (_batch != null) { _batch.Dispose(); _batch = null; }
                _rasterizer.Dispose();
                _rasterizer = null;
                _pixels.Dispose();
                _pixels = default(NativeArray<byte>);
            }
            return _ready;
        }

        public Texture2D Sample(string markerName, double seconds)
        {
            Clip clip = GetReadyClip(markerName);
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentException("Sample time must be finite.", nameof(seconds));
            double normalized;
            if (clip.Duration <= 0) normalized = 0;
            else if (clip.Sampling.Loop)
            {
                double wrapped = seconds % clip.Duration;
                if (wrapped < 0) wrapped += clip.Duration;
                normalized = wrapped / clip.Duration;
            }
            else normalized = Math.Max(0, Math.Min(1, seconds / clip.Duration));
            return SampleNormalized(clip, normalized);
        }

        public double DurationSeconds(string markerName)
        {
            return GetClip(markerName).Duration;
        }

        public Texture2D SampleNormalized(string markerName, float normalized)
        {
            if (float.IsNaN(normalized)) throw new ArgumentException("Normalized position must be finite.", nameof(normalized));
            return SampleNormalized(GetReadyClip(markerName), Math.Max(0, Math.Min(1, normalized)));
        }

        private static Texture2D SampleNormalized(Clip clip, double normalized)
        {
            if (clip.Sampling.Loop && normalized >= 1.0) normalized = 0.0;

            // Deduplication can make cached sample positions non-uniform. Select
            // the most recent sampled source frame instead of redistributing the
            // remaining textures uniformly across the marker duration.
            int low = 0;
            int high = clip.SamplePositions.Length;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (clip.SamplePositions[middle] <= normalized) low = middle + 1;
                else high = middle;
            }
            return clip.Textures[Math.Max(0, low - 1)];
        }

        private Clip GetReadyClip(string markerName)
        {
            Clip clip = GetClip(markerName);
            if (!_ready) throw new InvalidOperationException("The frame cache is not ready.");
            return clip;
        }

        private Clip GetClip(string markerName)
        {
            ThrowIfDisposed();
            if (markerName == null) throw new ArgumentNullException(nameof(markerName));
            Clip clip;
            if (!_clips.TryGetValue(markerName, out clip)) throw new KeyNotFoundException("No cached marker named '" + markerName + "'.");
            return clip;
        }

        private LottieFrameCachePreflight BuildPreflight(LottieMarkerSet markers, LottieFrameCacheOptions options)
        {
            long bytesPerFrame = checked((long)options.Width * options.Height * 4L);
            long totalBytes = 0;
            int totalFrames = 0;
            var descriptions = new List<LottieClipPreflight>();
            foreach (LottieClipSampling sampling in options.Clips)
            {
                if (string.IsNullOrEmpty(sampling.MarkerName)) throw new ArgumentException("MarkerName is required.");
                if (sampling.FramesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(sampling.FramesPerSecond));
                if (_clips.ContainsKey(sampling.MarkerName)) throw new ArgumentException("Duplicate frame-cache marker: " + sampling.MarkerName);
                LottieMarker marker = markers.Get(sampling.MarkerName);
                double duration = marker.TimelineDuration / markers.FrameRate;
                int intervals = Math.Max(1, checked((int)Math.Ceiling(duration * sampling.FramesPerSecond)));
                int candidateCount = !sampling.Loop && sampling.IncludeExactEndFrame ? intervals + 1 : intervals;
                var sourceFrames = new List<int>(candidateCount);
                var samplePositions = new List<double>(candidateCount);
                for (int index = 0; index < candidateCount; index++)
                {
                    double normalized = (double)index / intervals;
                    int frame = markers.Frame(sampling.MarkerName, (float)normalized);
                    if (sourceFrames.Count == 0 || sourceFrames[sourceFrames.Count - 1] != frame)
                    {
                        sourceFrames.Add(frame);
                        samplePositions.Add(normalized);
                    }
                }
                var clip = new Clip
                {
                    Sampling = sampling,
                    Duration = duration,
                    SourceFrames = sourceFrames.ToArray(),
                    SamplePositions = samplePositions.ToArray(),
                    Textures = new Texture2D[sourceFrames.Count]
                };
                _clips.Add(sampling.MarkerName, clip);
                _orderedClips.Add(clip);
                totalFrames = checked(totalFrames + sourceFrames.Count);
                long clipBytes = checked(bytesPerFrame * sourceFrames.Count);
                totalBytes = checked(totalBytes + clipBytes);
                descriptions.Add(new LottieClipPreflight { MarkerName = sampling.MarkerName, FrameCount = sourceFrames.Count, RawPixelBytes = clipBytes });
            }
            return new LottieFrameCachePreflight
            {
                Clips = new ReadOnlyCollection<LottieClipPreflight>(descriptions),
                TotalFrameCount = totalFrames,
                TotalRawPixelBytes = totalBytes
            };
        }

        internal static void ConvertToStraightRgba(NativeArray<byte> pixels)
        {
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                int b = pixels[offset]; int g = pixels[offset + 1]; int r = pixels[offset + 2]; int a = pixels[offset + 3];
                pixels[offset] = a == 0 ? (byte)0 : (byte)Math.Min(255, (r * 255 + a / 2) / a);
                pixels[offset + 1] = a == 0 ? (byte)0 : (byte)Math.Min(255, (g * 255 + a / 2) / a);
                pixels[offset + 2] = a == 0 ? (byte)0 : (byte)Math.Min(255, (b * 255 + a / 2) / a);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                throw new InvalidOperationException("Dispose must run on the thread that created the frame cache.");
            _disposed = true;
            if (_batch != null) { _batch.Dispose(); _batch = null; }
            ReleaseBatchPixels();
            if (_rasterizer != null) { _rasterizer.Dispose(); _rasterizer = null; }
            if (_pixels.IsCreated) _pixels.Dispose();
            foreach (Clip clip in _orderedClips)
                foreach (Texture2D texture in clip.Textures)
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }

        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(LottieFrameCache)); }

        private void ReleaseBatchPixels()
        {
            _completed = null;
            _completedOffset = 0;
            if (_reservedBytes != 0) { _pool.Release(_reservedBytes); _reservedBytes = 0; }
        }

        private void FallBack(Exception error)
        {
            if (_batch != null) { _batch.Dispose(); _batch = null; }
            ReleaseBatchPixels();
            FellBack = true;
            FallbackReason = error.Message;
            Debug.LogWarning("Lottie frame preparation fell back to serial: " + error.GetType().Name);
        }
    }
}

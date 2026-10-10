using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace LottiePlugin
{
    /// <summary>
    /// Explicitly owned shared CPU preparation budget. Dispose caches before this pool.
    /// Workers: 1..8; batch: 1..32; pending pixels: 4 bytes..64 MiB total;
    /// browser timeout: 0.1..60 seconds. Native retirement joins bounded work.
    /// </summary>
    public sealed class LottieFramePreparationPool : IDisposable
    {
        internal readonly SemaphoreSlim Slots;
        private readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        private bool _disposed;
        private int _sessions;
        private long _pending;
        private int _browserId;
        public int WorkerCount { get; private set; }
        public int BatchSize { get; private set; }
        public long MaximumPendingPixelBytes { get; private set; }
        public double BrowserTimeoutSeconds { get; private set; }
        public long PendingPixelBytes { get { CheckThread(); return _pending; } }

        public LottieFramePreparationPool(int workerCount = 2, int batchSize = 8,
            long maximumPendingPixelBytes = 64L * 1024 * 1024, double browserTimeoutSeconds = 5)
        {
            if (workerCount < 1 || workerCount > 8) throw new ArgumentOutOfRangeException(nameof(workerCount));
            if (batchSize < 1 || batchSize > 32) throw new ArgumentOutOfRangeException(nameof(batchSize));
            if (maximumPendingPixelBytes < 4 || maximumPendingPixelBytes > 64L * 1024 * 1024)
                throw new ArgumentOutOfRangeException(nameof(maximumPendingPixelBytes));
            if (double.IsNaN(browserTimeoutSeconds) || browserTimeoutSeconds < .1 || browserTimeoutSeconds > 60)
                throw new ArgumentOutOfRangeException(nameof(browserTimeoutSeconds));
            WorkerCount = workerCount;
            BatchSize = batchSize;
            MaximumPendingPixelBytes = maximumPendingPixelBytes;
            BrowserTimeoutSeconds = browserTimeoutSeconds;
            Slots = new SemaphoreSlim(workerCount, workerCount);
        }

        internal LottieRasterBatch CreateBatch(string json, string resourcesPath, LottieFrameCacheOptions options)
        {
            CheckThread();
            if (_disposed) throw new ObjectDisposedException(nameof(LottieFramePreparationPool));
            if (options.Width > 4096 || options.Height > 4096 ||
                checked((long)options.Width * options.Height * 4 * BatchSize) > MaximumPendingPixelBytes)
                throw new NotSupportedException("Parallel surface/batch exceeds the pool pixel budget; use serial.");
            LottieRasterBatch batch;
#if UNITY_WEBGL && !UNITY_EDITOR
#if LOTTIE_PARALLEL_PREPARATION
            // The CPU-only C ABI does not support external resources or semantic overrides.
            if (!string.IsNullOrEmpty(resourcesPath) || (options.ColorOverrides != null && options.ColorOverrides.Count != 0))
                throw new NotSupportedException("Browser parallel preparation requires embedded resources and no color overrides.");
            if (_browserId == 0) _browserId = LottieWebRasterBatch.CreatePool(WorkerCount);
            if (_browserId <= 0) { _browserId = 0; throw new InvalidOperationException("Browser workers are unavailable."); }
            batch = new LottieWebRasterBatch(this, _browserId, json, options.Width, options.Height);
#else
            throw new NotSupportedException("Browser parallel payload is not enabled for this build.");
#endif
#else
            batch = new LottieTaskRasterBatch(this, json, resourcesPath, options);
#endif
            _sessions++;
            return batch;
        }

        internal bool TryReserve(int bytes)
        {
            CheckThread();
            if (_disposed) throw new ObjectDisposedException(nameof(LottieFramePreparationPool));
            if (_pending + bytes > MaximumPendingPixelBytes) return false;
            _pending += bytes;
            return true;
        }
        internal void Release(int bytes) { CheckThread(); _pending -= bytes; }
        internal void Retire() { CheckThread(); _sessions--; }
        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                throw new InvalidOperationException("Preparation pool ownership belongs to its creating thread.");
        }
        public void Dispose()
        {
            CheckThread();
            if (_disposed) return;
            if (_sessions != 0 || _pending != 0) throw new InvalidOperationException("Dispose all frame caches before the preparation pool.");
            _disposed = true;
#if UNITY_WEBGL && !UNITY_EDITOR && LOTTIE_PARALLEL_PREPARATION
            if (_browserId > 0) LottieWebRasterBatch.DisposePool(_browserId);
#endif
            Slots.Dispose();
        }
    }

    internal abstract class LottieRasterBatch : IDisposable
    {
        public abstract void Start(int[] frames);
        public abstract bool TryComplete(out byte[] pixels);
        public abstract void Dispose();
    }

    internal sealed class LottieTaskRasterBatch : LottieRasterBatch
    {
        private readonly LottieFramePreparationPool _pool;
        private readonly LottieCpuRasterizer _rasterizer;
        private NativeArray<byte> _pixels;
        private Task<byte[]> _pending;
        private bool _disposed;
        internal LottieTaskRasterBatch(LottieFramePreparationPool pool, string json,
            string resourcesPath, LottieFrameCacheOptions options)
        {
            _pool = pool;
            _rasterizer = LottieCpuRasterizer.LoadFromJsonData(json, resourcesPath, options.Width, options.Height, options.ColorOverrides);
            try { _pixels = new NativeArray<byte>(_rasterizer.ByteCount, Allocator.Persistent); }
            catch { _rasterizer.Dispose(); throw; }
        }
        public override void Start(int[] frames)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LottieTaskRasterBatch));
            if (_pending != null) throw new InvalidOperationException("A raster batch is already pending.");
            _pending = Task.Run(async () =>
            {
                await _pool.Slots.WaitAsync().ConfigureAwait(false);
                try
                {
                    var result = new byte[checked(frames.Length * _pixels.Length)];
                    for (int index = 0; index < frames.Length; index++)
                    {
                        _rasterizer.RenderFrame(frames[index], _pixels);
                        NativeArray<byte>.Copy(_pixels, 0, result, index * _pixels.Length, _pixels.Length);
                    }
                    return result;
                }
                finally { _pool.Slots.Release(); }
            });
        }
        public override bool TryComplete(out byte[] pixels)
        {
            pixels = null;
            if (_pending == null || !_pending.IsCompleted) return false;
            Task<byte[]> completed = _pending;
            _pending = null;
            pixels = completed.GetAwaiter().GetResult();
            return true;
        }
        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_pending != null) _pending.GetAwaiter().GetResult(); }
            catch { /* Observe task failures during explicit retirement. */ }
            _pending = null;
            _pixels.Dispose();
            _rasterizer.Dispose();
            _pool.Retire();
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR && LOTTIE_PARALLEL_PREPARATION
    internal sealed class LottieWebRasterBatch : LottieRasterBatch
    {
        [DllImport("__Internal", EntryPoint = "LottieRasterPoolCreate")] internal static extern int CreatePool(int workers);
        [DllImport("__Internal", EntryPoint = "LottieRasterPoolDispose")] internal static extern void DisposePool(int pool);
        [DllImport("__Internal")] private static extern int LottieRasterCreate(int pool, string json, int width, int height);
        [DllImport("__Internal")] private static extern int LottieRasterStart(int id, int[] frames, int count);
        [DllImport("__Internal")] private static extern int LottieRasterPoll(int id, byte[] pixels, int count);
        [DllImport("__Internal")] private static extern void LottieRasterDispose(int id);
        private readonly LottieFramePreparationPool _pool;
        private readonly int _bytesPerFrame;
        private int _id;
        private byte[] _pixels;
        private double _started;
        internal LottieWebRasterBatch(LottieFramePreparationPool pool, int browserPool, string json, int width, int height)
        {
            _pool = pool;
            _bytesPerFrame = checked(width * height * 4);
            _id = LottieRasterCreate(browserPool, json, width, height);
            if (_id <= 0) throw new InvalidOperationException("Browser raster owner construction failed.");
        }
        public override void Start(int[] frames)
        {
            _pixels = new byte[checked(frames.Length * _bytesPerFrame)];
            _started = Time.realtimeSinceStartupAsDouble;
            if (LottieRasterStart(_id, frames, frames.Length) != 1)
                throw new InvalidOperationException("Browser raster batch was rejected.");
        }
        public override bool TryComplete(out byte[] pixels)
        {
            pixels = null;
            int result = LottieRasterPoll(_id, _pixels, _pixels.Length);
            if (result < 0 || Time.realtimeSinceStartupAsDouble - _started > _pool.BrowserTimeoutSeconds)
                throw new InvalidOperationException("Browser raster batch failed or timed out.");
            if (result == 0) return false;
            pixels = _pixels;
            _pixels = null;
            return true;
        }
        public override void Dispose()
        {
            if (_id <= 0) return;
            LottieRasterDispose(_id);
            _id = 0;
            _pixels = null;
            _pool.Retire();
        }
    }
#endif
}

using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace LottiePlugin
{
    /// <summary>
    /// Synchronously rasterizes premultiplied BGRA8 pixels into caller-owned storage.
    /// Rows use rlottie's native surface order; byte 0 is the bottom-left texture pixel
    /// when uploaded directly through Unity's LoadRawTextureData API.
    /// </summary>
    public sealed class LottieCpuRasterizer : IDisposable
    {
        private static readonly HashSet<LottieCpuRasterizer> sAlive = new HashSet<LottieCpuRasterizer>();
        private static readonly object sAliveLock = new object();
        private IntPtr _animation;
        private IntPtr _renderDataPointer;
        private LottieRenderData _renderData;
        private NativeArray<byte> _staging;
        private readonly object _renderLock = new object();
        private bool _disposed;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Stride { get { return checked(Width * 4); } }
        public int ByteCount { get { return _staging.IsCreated ? _staging.Length : checked(Width * Height * 4); } }
        public long TotalFramesCount { get { ThrowIfDisposed(); return _wrapper.totalFrames; } }
        public LottieMarkerSet Markers { get; private set; }
        private LottieAnimationWrapper _wrapper;

        static LottieCpuRasterizer()
        {
            UnityEngine.Application.quitting += DisposeAll;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
#endif
        }

        private LottieCpuRasterizer(string jsonData, string resourcesPath, int width, int height,
            IReadOnlyList<LottieColorOverride> colorOverrides)
        {
            ValidateDimensions(width, height);
            Width = width;
            Height = height;
            try
            {
                _wrapper = NativeBridge.LoadFromData(jsonData, resourcesPath, out _animation);
                if (colorOverrides != null)
                    ApplyColorOverrides(colorOverrides);
                Markers = LottieMarkerSet.Parse(jsonData, _wrapper.totalFrames, () => _disposed);
                InitializeSurface();
                lock (sAliveLock) sAlive.Add(this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static LottieCpuRasterizer LoadFromJsonData(string jsonData, string resourcesPath,
            int width, int height, IReadOnlyList<LottieColorOverride> colorOverrides = null)
        {
            if (string.IsNullOrEmpty(jsonData)) throw new ArgumentException("Lottie JSON is required.", nameof(jsonData));
            return new LottieCpuRasterizer(jsonData, resourcesPath, width, height, colorOverrides);
        }

        public static LottieCpuRasterizer LoadFromJsonFile(string path, int width, int height,
            IReadOnlyList<LottieColorOverride> colorOverrides = null)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A Lottie JSON path is required.", nameof(path));
            string json = File.ReadAllText(path);
            return new LottieCpuRasterizer(json, Path.GetDirectoryName(path), width, height, colorOverrides);
        }

        public void SetFillColor(string keyPath, UnityEngine.Color color)
        {
            ApplyColorOverrides(new[] { LottieColorOverride.Fill(keyPath, color) });
        }

        public void SetStrokeColor(string keyPath, UnityEngine.Color color)
        {
            ApplyColorOverrides(new[] { LottieColorOverride.Stroke(keyPath, color) });
        }

        public void ApplyColorOverrides(IReadOnlyList<LottieColorOverride> overrides)
        {
            lock (_renderLock)
            {
                ThrowIfDisposed();
                if (overrides == null)
                    throw new ArgumentNullException(nameof(overrides));
                int result = NativeBridge.ApplyColorOverrides(_animation, overrides);
                if (result != 0)
                    throw new InvalidOperationException("The native rlottie library could not apply color overrides.");
            }
        }

        public void RenderFrame(int frame, NativeArray<byte> destination)
        {
            lock (_renderLock)
            {
                ThrowIfDisposed();
                if (!destination.IsCreated) throw new ArgumentException("Destination storage is not created.", nameof(destination));
                if (destination.Length != ByteCount)
                    throw new ArgumentException("Destination must contain exactly " + ByteCount + " bytes.", nameof(destination));
                if (frame < 0 || frame >= _wrapper.totalFrames) throw new ArgumentOutOfRangeException(nameof(frame));
                int result = NativeBridge.LottieRenderImmediately(_animation, _renderDataPointer, frame, true, false);
                if (result != 0) throw new InvalidOperationException("Native rlottie rasterization failed.");
                NativeArray<byte>.Copy(_staging, destination);
            }
        }

        public void RenderMarkerFrame(string markerName, float normalized, NativeArray<byte> destination)
        {
            RenderFrame(Markers.Frame(markerName, normalized), destination);
        }

        private unsafe void InitializeSurface()
        {
            int count = checked(Width * Height * 4);
            _staging = new NativeArray<byte>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (NativeBridge.LottieAllocateRenderData(ref _renderDataPointer) != 0 || _renderDataPointer == IntPtr.Zero)
                throw new InvalidOperationException("Native rlottie render-data allocation failed.");
            _renderData = new LottieRenderData
            {
                buffer = _staging.GetUnsafePtr(),
                width = (uint)Width,
                height = (uint)Height,
                bytesPerLine = (uint)Stride
            };
            Marshal.StructureToPtr(_renderData, _renderDataPointer, false);
        }

        public void Dispose()
        {
            lock (_renderLock)
            {
                if (_disposed) return;
                _disposed = true;
                lock (sAliveLock) sAlive.Remove(this);
                if (_renderDataPointer != IntPtr.Zero) NativeBridge.LottieDisposeRenderData(ref _renderDataPointer);
                if (_staging.IsCreated) _staging.Dispose();
                if (_animation != IntPtr.Zero) NativeBridge.Dispose(ref _animation);
                _renderData = default(LottieRenderData);
                _wrapper = default(LottieAnimationWrapper);
            }
        }

        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(LottieCpuRasterizer)); }

        private static void DisposeAll()
        {
            LottieCpuRasterizer[] alive;
            lock (sAliveLock)
            {
                alive = new LottieCpuRasterizer[sAlive.Count];
                sAlive.CopyTo(alive);
            }
            foreach (LottieCpuRasterizer rasterizer in alive) rasterizer.Dispose();
        }
        internal static void ValidateDimensions(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (width > UnityEngine.SystemInfo.maxTextureSize || height > UnityEngine.SystemInfo.maxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(width), "The requested raster surface exceeds Unity's supported limits.");
            checked { int ignored = width * height * 4; }
        }
    }
}

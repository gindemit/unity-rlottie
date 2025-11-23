using LottiePlugin.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LottiePlugin
{
    public sealed class LottieAnimationOptions
    {
        public int TargetFps { get; set; } = 30;
        public int ResolutionDivider { get; set; } = 1;
        public bool PauseIfCulled { get; set; } = true;
        public Func<bool> VisibilityEvaluator { get; set; }
        public LottieLogLevel LogLevel { get; set; } = LottieLogLevel.Warning;
    }

    public sealed class LottieAnimation : IDisposable
    {
        private static bool sLoggerInitialized;
        private static readonly HashSet<LottieAnimation> sAlive = new HashSet<LottieAnimation>();
        private static readonly object sAliveLock = new object();

        public event Action<LottieAnimation> Started;
        public event Action<LottieAnimation> Paused;
        public event Action<LottieAnimation> Stopped;

        public Texture2D Texture { get; private set; }
        public int CurrentFrame { get; private set; }
        public double FrameRate => _animationWrapper.frameRate;
        public long TotalFramesCount => _animationWrapper.totalFrames;
        public double DurationSeconds => _animationWrapper.duration;
        public bool IsPlaying { get; private set; }
        public int TargetFps
        {
            get => _targetFps;
            set
            {
                int clamped = Mathf.Max(1, value);
                if (_targetFps != clamped)
                {
                    _targetFps = clamped;
                    UpdateFrameDelta();
                }
            }
        }
        public int ResolutionDivider => _resolutionDivider;
        public bool PauseIfCulled { get; set; }
        public Func<bool> VisibilityEvaluator
        {
            get => _visibilityEvaluator;
            set => _visibilityEvaluator = value;
        }
        public LottieLogLevel LogLevel
        {
            get => _logLevel;
            set
            {
                if (_logLevel != value)
                {
                    _logLevel = value;
                    if (_animationWrapperIntPtr != IntPtr.Zero)
                    {
                        NativeBridge.LottieSetLogLevel(_animationWrapperIntPtr, _logLevel);
                    }
                }
            }
        }

        private IntPtr _animationWrapperIntPtr;
        private LottieAnimationWrapper _animationWrapper;

        private IntPtr _lottieRenderDataIntPtr;
        private LottieRenderData _lottieRenderData;
        private NativeArray<byte> _pixelData;
        private bool _ownsPixelData;
        private float _timeSinceLastRenderCall;
        private double _frameDelta;
        private double _clipFrameDelta;
        private int _targetFps;
        private int _resolutionDivider = 1;
        private Func<bool> _visibilityEvaluator;
        private bool _asyncDrawWasCalled;
        private bool _disposed;
        private LottieLogLevel _logLevel = LottieLogLevel.Warning;

        private Action<int> DrawOneFrameCached;
        private Action<int> DrawOneFrameAsyncPrepareCached;

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private IntPtr _nativeTexturePtr;
#endif

        static LottieAnimation()
        {
            Application.quitting += DisposeAll;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DisposeAll;
#endif
        }

        private LottieAnimation(string jsonData, string resourcesPath, uint width, uint height, LottieAnimationOptions options)
        {
            ThrowIf.String.IsNullOrEmpty(jsonData, nameof(jsonData));
            ThrowIf.Value.IsZero(width, nameof(width));
            ThrowIf.Value.IsZero(height, nameof(height));
            _animationWrapper = NativeBridge.LoadFromData(jsonData, resourcesPath, out _animationWrapperIntPtr);
            _clipFrameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            InitializeOptions(options);
            uint scaledWidth = ApplyResolutionDivider(width, _resolutionDivider);
            uint scaledHeight = ApplyResolutionDivider(height, _resolutionDivider);
            CreateRenderDataTexture2DMarshalToNative(scaledWidth, scaledHeight);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
            RegisterAliveInstance();
        }
        private LottieAnimation(string jsonFilePath, uint width, uint height, LottieAnimationOptions options)
        {
            ThrowIf.String.IsNullOrEmpty(jsonFilePath, nameof(jsonFilePath));
            ThrowIf.Value.IsZero(width, nameof(width));
            ThrowIf.Value.IsZero(height, nameof(height));
            _animationWrapper = NativeBridge.LoadFromFile(jsonFilePath, out _animationWrapperIntPtr);
            _clipFrameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            InitializeOptions(options);
            uint scaledWidth = ApplyResolutionDivider(width, _resolutionDivider);
            uint scaledHeight = ApplyResolutionDivider(height, _resolutionDivider);
            CreateRenderDataTexture2DMarshalToNative(scaledWidth, scaledHeight);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
            RegisterAliveInstance();
        }

        private void InitializeOptions(LottieAnimationOptions options)
        {
            options = options ?? new LottieAnimationOptions();
            _resolutionDivider = Mathf.Max(1, options.ResolutionDivider);
            _targetFps = Mathf.Max(1, options.TargetFps);
            PauseIfCulled = options.PauseIfCulled;
            _visibilityEvaluator = options.VisibilityEvaluator;
            _logLevel = options.LogLevel;
            if (_animationWrapperIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieSetLogLevel(_animationWrapperIntPtr, _logLevel);
            }
            UpdateFrameDelta();
        }

        private void UpdateFrameDelta()
        {
            double targetDelta = 1.0 / _targetFps;
            _frameDelta = Math.Max(_clipFrameDelta, targetDelta);
        }

        private static uint ApplyResolutionDivider(uint value, int divider)
        {
            if (divider <= 1)
            {
                return value;
            }

            uint d = (uint)divider;
            uint scaled = (value + d - 1u) / d;
            return scaled == 0u ? 1u : scaled;
        }
        ~LottieAnimation()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (disposing)
            {
                Started = null;
                Paused = null;
                Stopped = null;
            }

            lock (sAliveLock)
            {
                sAlive.Remove(this);
            }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_asyncDrawWasCalled && _animationWrapperIntPtr != IntPtr.Zero && _lottieRenderDataIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr);
                _asyncDrawWasCalled = false;
            }
#endif

#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_nativeTexturePtr != IntPtr.Zero)
            {
                NativeBridge.LottieDestroyTexture(_animationWrapperIntPtr, _nativeTexturePtr);
                _nativeTexturePtr = IntPtr.Zero;
            }
#endif

            if (_lottieRenderDataIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieDisposeRenderData(ref _lottieRenderDataIntPtr);
                _lottieRenderDataIntPtr = IntPtr.Zero;
            }

            _lottieRenderData = default;

#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_ownsPixelData && _pixelData.IsCreated)
            {
                _pixelData.Dispose();
            }
#endif

            _pixelData = default;
            _ownsPixelData = false;

            if (Texture != null)
            {
                UnityEngine.Object.DestroyImmediate(Texture);
                Texture = null;
            }

            if (_animationWrapperIntPtr != IntPtr.Zero)
            {
                NativeBridge.Dispose(ref _animationWrapperIntPtr);
                _animationWrapperIntPtr = IntPtr.Zero;
            }

            _animationWrapper = default;
            _asyncDrawWasCalled = false;
        }
        public void Update(float animationSpeed = 1f)
        {
            UpdateInternal(animationSpeed, DrawOneFrameCached, false);
        }
        public void UpdateAsync(float animationSpeed = 1f)
        {
            UpdateInternal(animationSpeed, DrawOneFrameAsyncPrepareCached, true);
            DrawOneFrameAsyncGetResult();
        }
        public void TogglePlay()
        {
            IsPlaying = !IsPlaying;
        }
        public void Play()
        {
            IsPlaying = true;
            DrawOneFrame(++CurrentFrame);
            Started?.Invoke(this);
        }
        public void Pause()
        {
            IsPlaying = false;
            Paused?.Invoke(this);
        }
        public void Stop()
        {
            IsPlaying = false;
            CurrentFrame = 0;
            Stopped?.Invoke(this);
        }
        public void DrawOneFrame(int frameNumber)
        {
            NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true);
            CurrentFrame = frameNumber;
#if UNITY_WEBGL && !UNITY_EDITOR
            Texture.Apply();
#else
            RequestTextureUpload();
#endif
        }
        public void DrawOneFrameAsyncPrepare(int frameNumber)
        {
            NativeBridge.LottieRenderCreateFutureAsync(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true);
        }
        public void DrawOneFrameAsyncGetResult()
        {
            if (!_asyncDrawWasCalled)
            {
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr);
            Texture.Apply();
            _asyncDrawWasCalled = false;
#else
            if (NativeBridge.LottieRenderTryGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr, out int ready) == 0 && ready != 0)
            {
                RequestTextureUpload();
                _asyncDrawWasCalled = false;
            }
#endif
        }

        private unsafe void CreateRenderDataTexture2DMarshalToNative(uint width, uint height)
        {
            if (_lottieRenderDataIntPtr != IntPtr.Zero)
            {
                return;
            }

            NativeBridge.LottieAllocateRenderData(ref _lottieRenderDataIntPtr);
            _lottieRenderData = new LottieRenderData
            {
                width = width,
                height = height,
                bytesPerLine = width * sizeof(uint)
            };
#if UNITY_WEBGL && !UNITY_EDITOR
            Texture = new Texture2D(
                (int)width,
                (int)height,
                TextureFormat.BGRA32,
                0,
                false);
            _pixelData = Texture.GetRawTextureData<byte>();
            _ownsPixelData = false;
            _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
#else
            int bufferSize = (int)(width * height * sizeof(uint));
            _pixelData = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            _ownsPixelData = true;
            _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
            _nativeTexturePtr = NativeBridge.LottieCreateTexture(_animationWrapperIntPtr, (int)width, (int)height);
            
            if (_nativeTexturePtr == IntPtr.Zero)
            {
                throw new System.Exception("Failed to create native texture. Graphics device may not be initialized yet.");
            }
            
            Texture = Texture2D.CreateExternalTexture(
                (int)width,
                (int)height,
                TextureFormat.BGRA32,
                false,
                false,
                _nativeTexturePtr);
#endif
            Marshal.StructureToPtr(_lottieRenderData, _lottieRenderDataIntPtr, false);
        }
        private void UpdateInternal(float animationSpeed, Action<int> drawOneFrameMethod, bool scheduleAsync)
        {
            bool shouldRender = !PauseIfCulled || _visibilityEvaluator == null || _visibilityEvaluator();
            if (!shouldRender)
            {
                _timeSinceLastRenderCall = 0f;
                return;
            }

            if (IsPlaying)
            {
                _timeSinceLastRenderCall += Time.deltaTime * animationSpeed;
            }
            if (_timeSinceLastRenderCall >= _frameDelta)
            {
                if (scheduleAsync && _asyncDrawWasCalled)
                {
                    return;
                }
                int framesDelta = Mathf.RoundToInt(_timeSinceLastRenderCall / (float)_frameDelta);
                CurrentFrame += framesDelta;
                if (CurrentFrame >= _animationWrapper.totalFrames)
                {
                    CurrentFrame = 0;
                }
                if (scheduleAsync)
                {
                    if (LottieScheduler.TryStartRender())
                    {
                        drawOneFrameMethod(CurrentFrame);
                        _asyncDrawWasCalled = true;
                        _timeSinceLastRenderCall = 0f;
                    }
                    else
                    {
                        _timeSinceLastRenderCall -= framesDelta * (float)_frameDelta;
                        if (_timeSinceLastRenderCall < 0f)
                        {
                            _timeSinceLastRenderCall = 0f;
                        }
                    }
                }
                else
                {
                    drawOneFrameMethod(CurrentFrame);
                    _timeSinceLastRenderCall = 0f;
                }
            }
        }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private void RequestTextureUpload()
        {
            IntPtr currentPtr = NativeBridge.LottieGetNativeTexturePtr(_animationWrapperIntPtr);
            if (currentPtr != _nativeTexturePtr)
            {
                if (currentPtr != IntPtr.Zero)
                {
                    _nativeTexturePtr = currentPtr;
                    Texture.UpdateExternalTexture(currentPtr);
                }
                else
                {
                    _nativeTexturePtr = IntPtr.Zero;
                }
            }
            NativeBridge.LottieUpdateTexture(_animationWrapperIntPtr);
        }
#endif

        public static LottieAnimation LoadFromJsonFile(string filePath, uint width, uint height, LottieAnimationOptions options = null)
        {
            ThrowIf.String.IsNullOrEmpty(filePath, nameof(filePath));
            return new LottieAnimation(filePath, width, height, options);
        }
        public static LottieAnimation LoadFromJsonData(string jsonData, string resourcesPath, uint width, uint height, LottieAnimationOptions options = null)
        {
            ThrowIf.String.IsNullOrEmpty(jsonData, nameof(jsonData));
            return new LottieAnimation(jsonData, resourcesPath, width, height, options);
        }

        private void RegisterAliveInstance()
        {
            lock (sAliveLock)
            {
                sAlive.Add(this);
            }
        }

        private static void DisposeAll()
        {
            LottieAnimation[] alive;
            lock (sAliveLock)
            {
                if (sAlive.Count == 0)
                {
                    return;
                }

                alive = new LottieAnimation[sAlive.Count];
                sAlive.CopyTo(alive);
                sAlive.Clear();
            }

            foreach (LottieAnimation animation in alive)
            {
                if (animation == null)
                {
                    continue;
                }

                try
                {
                    animation.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}

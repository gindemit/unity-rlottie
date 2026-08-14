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
    public enum LottieTextureUploadBackend
    {
        ManagedTextureUpload,
        NativeVulkan,
        NativeExternalTexture,
        WebGLManagedTextureUpload,
        WebGLShaderConversion,
        NativeOpenGL
    }

    public sealed class LottieAnimationOptions
    {
        public int TargetFps { get; set; } = 30;
        public int ResolutionDivider { get; set; } = 1;
        public bool PauseIfCulled { get; set; } = true;
        public Func<bool> VisibilityEvaluator { get; set; }
        public LottieLogLevel LogLevel { get; set; } = LottieLogLevel.Warning;
        public bool UseManagedTextureUpload { get; set; }
    }

    public sealed partial class LottieAnimation : IDisposable
    {
        private static readonly HashSet<LottieAnimation> sAlive = new HashSet<LottieAnimation>();
        private static readonly object sAliveLock = new object();

        public event Action<LottieAnimation> Started;
        public event Action<LottieAnimation> Paused;
        public event Action<LottieAnimation> Stopped;

        public Texture2D Texture { get; private set; }
        
        /// <summary>
        /// Gets the output texture for rendering. On WebGL with shader conversion mode,
        /// this returns the RenderTexture after BGRA to RGBA conversion.
        /// On other platforms or with native conversion, this returns the same as Texture.
        /// </summary>
        public Texture OutputTexture
        {
            get
            {
                return GetOutputTexture();
            }
        }
        
        public int CurrentFrame { get; private set; }
        public double FrameRate => _animationWrapper.frameRate;
        public long TotalFramesCount => _animationWrapper.totalFrames;
        public double DurationSeconds => _animationWrapper.duration;
        public bool IsPlaying { get; private set; }
        public LottieTextureUploadBackend TextureUploadBackend { get; private set; }
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
        private bool _usesCPURendering;
        private bool _usesUnityOwnedNativeTexture;
        private bool _usesUnityOwnedOpenGLTexture;
        private bool _useManagedTextureUpload;
        private bool _vulkanReregisterAttempted;
        private bool _openGLReregisterAttempted;
        private static bool sVulkanNativeUploadLogged;
        private static bool sVulkanFallbackLogged;
        private static bool sOpenGLNativeUploadLogged;
        private static bool sOpenGLFallbackLogged;
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL shader-based BGRA to RGBA conversion
        private static Material s_BGRAtoRGBAMaterial;
        private static Shader s_BGRAtoRGBAShader;
        private Texture2D _sourceTexture; // Source texture in BGRA format
        private RenderTexture _convertedRT; // Render texture after shader conversion
        private bool _useShaderConversion;
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
            _useManagedTextureUpload = options.UseManagedTextureUpload;
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
                return;
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

            PlatformDisposeAsyncDraw();
            PlatformDisposeNativeTexture();
            if (_lottieRenderDataIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieDisposeRenderData(ref _lottieRenderDataIntPtr);
                _lottieRenderDataIntPtr = IntPtr.Zero;
            }
            _lottieRenderData = default;
            PlatformDisposePixelData();
            _pixelData = default;
            _ownsPixelData = false;
            PlatformDisposeWebGLTextures();
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
            PlatformDrawOneFrame(frameNumber);
        }
        public void DrawOneFrameAsyncPrepare(int frameNumber)
        {
            if (_asyncDrawWasCalled)
            {
                return;
            }

            PlatformDrawOneFrameAsyncPrepare(frameNumber);
            CurrentFrame = frameNumber;
            _asyncDrawWasCalled = true;
        }
        public void DrawOneFrameAsyncGetResult()
        {
            PlatformDrawOneFrameAsyncGetResult();
        }

        private unsafe void CreateRenderDataTexture2DMarshalToNative(uint width, uint height)
        {
            if (_lottieRenderDataIntPtr != IntPtr.Zero)
                return;

            NativeBridge.LottieAllocateRenderData(ref _lottieRenderDataIntPtr);
            _lottieRenderData = new LottieRenderData
            {
                width = width,
                height = height,
                bytesPerLine = width * sizeof(uint)
            };

            PlatformCreateRenderDataTexture(width, height);
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

        /// <summary>
        /// Sets the global log level for all Lottie animations. This affects animations that don't have an instance-specific log level set.
        /// </summary>
        /// <param name="logLevel">The global log level to set</param>
        public static void SetGlobalLogLevel(LottieLogLevel logLevel)
        {
            NativeBridge.LottieSetGlobalLogLevel(logLevel);
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

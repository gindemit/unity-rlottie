using LottiePlugin.Utility;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LottiePlugin
{
    public sealed class LottieAnimation : IDisposable
    {
        private static bool sLoggerInitialized;

        public event Action<LottieAnimation> Started;
        public event Action<LottieAnimation> Paused;
        public event Action<LottieAnimation> Stopped;

        public Texture2D Texture { get; private set; }
        public int CurrentFrame { get; private set; }
        public double FrameRate => _animationWrapper.frameRate;
        public long TotalFramesCount => _animationWrapper.totalFrames;
        public double DurationSeconds => _animationWrapper.duration;
        public bool IsPlaying { get; private set; }

        private IntPtr _animationWrapperIntPtr;
        private LottieAnimationWrapper _animationWrapper;

        private IntPtr _lottieRenderDataIntPtr;
        private LottieRenderData _lottieRenderData;
        private NativeArray<byte> _pixelData;
        private float _timeSinceLastRenderCall;
        private double _frameDelta;
        private bool _asyncDrawWasCalled;

        private Action<int> DrawOneFrameCached;
        private Action<int> DrawOneFrameAsyncPrepareCached;

        private LottieAnimation(string jsonData, string resourcesPath, uint width, uint height)
        {
            ThrowIf.String.IsNullOrEmpty(jsonData, nameof(jsonData));
            ThrowIf.Value.IsZero(width, nameof(width));
            ThrowIf.Value.IsZero(height, nameof(height));
            _animationWrapper = NativeBridge.LoadFromData(jsonData, resourcesPath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            CreateRenderDataTexture2DMarshalToNative(width, height);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
        }
        private LottieAnimation(string jsonFilePath, uint width, uint height)
        {
            ThrowIf.String.IsNullOrEmpty(jsonFilePath, nameof(jsonFilePath));
            ThrowIf.Value.IsZero(width, nameof(width));
            ThrowIf.Value.IsZero(height, nameof(height));
            _animationWrapper = NativeBridge.LoadFromFile(jsonFilePath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            CreateRenderDataTexture2DMarshalToNative(width, height);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
        }
        public void Dispose()
        {
            Started = null;
            Paused = null;
            Stopped = null;
            NativeBridge.Dispose(_animationWrapper);
            NativeBridge.LottieDisposeRenderData(ref _lottieRenderDataIntPtr);
            UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }
        public void Update(float animationSpeed = 1f)
        {
            UpdateInternal(animationSpeed, DrawOneFrameCached);
        }
        public void UpdateAsync(float animationSpeed = 1f)
        {
            UpdateInternal(animationSpeed, DrawOneFrameAsyncPrepareCached);
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
            Texture.Apply();
        }
        public void DrawOneFrameAsyncPrepare(int frameNumber)
        {
            NativeBridge.LottieRenderCreateFutureAsync(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true);
        }
        public void DrawOneFrameAsyncGetResult()
        {
            if (_asyncDrawWasCalled)
            {
                NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr);
                Texture.Apply();
                _asyncDrawWasCalled = false;
            }
        }

        private unsafe void CreateRenderDataTexture2DMarshalToNative(uint width, uint height)
        {
            _lottieRenderData = new LottieRenderData();
            _lottieRenderData.width = width;
            _lottieRenderData.height = height;
            _lottieRenderData.bytesPerLine = width * sizeof(uint);
            Texture = new Texture2D(
                (int)_lottieRenderData.width,
                (int)_lottieRenderData.height,
                TextureFormat.BGRA32,
                0,
                false);
            _pixelData = Texture.GetRawTextureData<byte>();
            _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
            NativeBridge.LottieAllocateRenderData(ref _lottieRenderDataIntPtr);
            Marshal.StructureToPtr(_lottieRenderData, _lottieRenderDataIntPtr, false);
        }
        private void UpdateInternal(float animationSpeed, Action<int> drawOneFrameMethod)
        {
            if (IsPlaying)
            {
                _timeSinceLastRenderCall += Time.deltaTime * animationSpeed;
            }
            if (_timeSinceLastRenderCall >= _frameDelta)
            {
                int framesDelta = Mathf.RoundToInt(_timeSinceLastRenderCall / (float)_frameDelta);
                CurrentFrame += framesDelta;
                if (CurrentFrame >= _animationWrapper.totalFrames)
                {
                    CurrentFrame = 0;
                }
                drawOneFrameMethod(CurrentFrame);
                _asyncDrawWasCalled = true;
                _timeSinceLastRenderCall = 0;
            }
        }

        public static LottieAnimation LoadFromJsonFile(string filePath, uint width, uint height)
        {
            ThrowIf.String.IsNullOrEmpty(filePath, nameof(filePath));
            InitializeLogger(Application.persistentDataPath, "rlottie.log", 1);
            return new LottieAnimation(filePath, width, height);
        }
        public static LottieAnimation LoadFromJsonData(string jsonData, string resourcesPath, uint width, uint height)
        {
            ThrowIf.String.IsNullOrEmpty(jsonData, nameof(jsonData));
            InitializeLogger(Application.persistentDataPath, "rlottie.log", 1);
            return new LottieAnimation(jsonData, resourcesPath, width, height);
        }
        public static void InitializeLogger(string logDirectoryPath, string logFileName, int logFileRollSizeMB)
        {
            ThrowIf.String.IsNullOrEmpty(logDirectoryPath, nameof(logDirectoryPath));
            ThrowIf.String.IsNullOrEmpty(logFileName, nameof(logFileName));
            if (sLoggerInitialized)
            {
                return;
            }
            if (!logDirectoryPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                logDirectoryPath += Path.DirectorySeparatorChar;
            }
            NativeBridge.InitializeLogger(logDirectoryPath, logFileName, logFileRollSizeMB);
            sLoggerInitialized = true;
        }
    }
}

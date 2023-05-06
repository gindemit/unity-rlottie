using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LottiePlugin
{
    public sealed class LottieAnimation : IDisposable
    {
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
            _animationWrapper = NativeBridge.LoadFromData(jsonData, resourcesPath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            CreateRenderDataTexture2DMarshalToNative(width, height);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
        }
        private LottieAnimation(string jsonFilePath, uint width, uint height)
        {
            _animationWrapper = NativeBridge.LoadFromFile(jsonFilePath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            CreateRenderDataTexture2DMarshalToNative(width, height);
            IsPlaying = true;
            DrawOneFrameCached = DrawOneFrame;
            DrawOneFrameAsyncPrepareCached = DrawOneFrameAsyncPrepare;
        }
        public void Dispose()
        {
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
        }
        public void Pause()
        {
            IsPlaying = false;
        }
        public void Stop()
        {
            Pause();
            CurrentFrame = 0;
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
            if (!System.IO.File.Exists(filePath))
            {
                throw new System.ArgumentException($"Can not find file at path: \"{filePath}\"");
            }
            return new LottieAnimation(filePath, width, height);
        }
        public static LottieAnimation LoadFromJsonData(string jsonData, string resourcesPath, uint width, uint height)
        {
            if (string.IsNullOrWhiteSpace(jsonData))
            {
                throw new System.ArgumentException($"The provided json animation file is empty");
            }
            return new LottieAnimation(jsonData, resourcesPath, width, height);
        }
    }
}

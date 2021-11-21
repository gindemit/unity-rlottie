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

        private IntPtr _animationWrapperIntPtr;
        private LottieAnimationWrapper _animationWrapper;

        private IntPtr _lottieRenderDataIntPtr;
        private LottieRenderData _lottieRenderData;
        private NativeArray<byte> _pixelData;
        private float _currentPlayTime;
        private double _frameDelta;
        private bool _isInPlayState;

        private LottieAnimation(string jsonData, string resourcesPath, uint width, uint height)
        {
            _animationWrapper = NativeBridge.LoadFromData(jsonData, resourcesPath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            CreateRenderDataTexture2DMarshalToNative(width, height);
            _isInPlayState = true;
        }
        private LottieAnimation(string jsonFilePath, uint width, uint height)
        {
            _animationWrapper = NativeBridge.LoadFromFile(jsonFilePath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrames;
            CreateRenderDataTexture2DMarshalToNative(width, height);
            _isInPlayState = true;
        }
        public void Dispose()
        {
            _pixelData.Dispose();
            NativeBridge.Dispose(_animationWrapper);
            NativeBridge.LottieDisposeRenderData(ref _lottieRenderDataIntPtr);
            UnityEngine.Object.DestroyImmediate(Texture);
            Texture = null;
        }
        public unsafe void Update()
        {
            if (_isInPlayState)
            {
                _currentPlayTime += Time.deltaTime;
            }
            if (_currentPlayTime > _frameDelta)
            {
                DrawOneFrame();
                _currentPlayTime = 0;
            }
            if (CurrentFrame >= _animationWrapper.totalFrames)
            {
                CurrentFrame = 0;
            }
        }
        public void TogglePlay()
        {
            _isInPlayState = !_isInPlayState;
        }
        public void Play()
        {
            _isInPlayState = true;
        }
        public void Pause()
        {
            _isInPlayState = false;
        }
        public void Stop()
        {
            Pause();
            CurrentFrame = 0;
        }
        public void DrawOneFrame()
        {
            NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, (int)CurrentFrame++, true);
            Texture.Apply();
        }
        public void DrawOneFrame(int frameNumber)
        {
            CurrentFrame = frameNumber;
            NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, CurrentFrame, true);
            Texture.Apply();
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
            _lottieRenderData.buffer = NativeArrayUnsafeUtility.GetUnsafePtr(_pixelData);
            NativeBridge.LottieAllocateRenderData(ref _lottieRenderDataIntPtr);
            Marshal.StructureToPtr(_lottieRenderData, _lottieRenderDataIntPtr, false);
        }

        public static LottieAnimation LoadFromJsonFile(string filePath, uint width, uint height)
        {
            return new LottieAnimation(filePath, width, height);
        }
        public static LottieAnimation LoadFromJsonData(string jsonData, string resourcesPath, uint width, uint height)
        {
            return new LottieAnimation(jsonData, resourcesPath, width, height);
        }
    }
}

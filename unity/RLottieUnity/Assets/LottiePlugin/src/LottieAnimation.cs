using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace LottiePlugin
{
    public sealed class LottieAnimation : IDisposable
    {
        public Texture2D Texture => _animationTexture;
        public double FrameRate => _animationWrapper.frameRate;
        public uint TotalFramesCount => _animationWrapper.totalFrames;
        public double DurationSeconds => _animationWrapper.duration;

        private IntPtr _animationWrapperIntPtr;
        private LottieAnimationWrapper _animationWrapper;

        private IntPtr _lottieRenderDataIntPtr;
        private LottieRenderData _lottieRenderData;
        private NativeArray<byte> _pixelData;
        private Texture2D _animationTexture;

        private int _currentFrame;
        private float _currentPlayTime;
        private double _frameDelta;
        private bool _isInPlayState;

        public LottieAnimation(string jsonFilePath, uint width, uint height)
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
            UnityEngine.Object.DestroyImmediate(_animationTexture);
            _animationTexture = null;
        }
        public unsafe void Update()
        {
            if (_isInPlayState)
            {
                _currentPlayTime += Time.deltaTime;
            }
            if (_currentPlayTime > _frameDelta)
            {
                NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, _currentFrame++, true);
                _animationTexture.Apply();
                _currentPlayTime = 0;
            }
            if (_currentFrame >= _animationWrapper.totalFrames)
            {
                _currentFrame = 0;
            }
        }
        public void TogglePlay()
        {
            _isInPlayState = !_isInPlayState;
        }

        private unsafe void CreateRenderDataTexture2DMarshalToNative(uint width, uint height)
        {
            _lottieRenderData = new LottieRenderData();
            _lottieRenderData.width = width;
            _lottieRenderData.height = height;
            _lottieRenderData.bytesPerLine = width * sizeof(uint);
            _animationTexture = new Texture2D(
                (int)_lottieRenderData.width,
                (int)_lottieRenderData.height,
                TextureFormat.BGRA32,
                0,
                false);
            _pixelData = _animationTexture.GetRawTextureData<byte>();
            _lottieRenderData.buffer = NativeArrayUnsafeUtility.GetUnsafePtr(_pixelData);
            NativeBridge.LottieAllocateRenderData(ref _lottieRenderDataIntPtr);
            Marshal.StructureToPtr(_lottieRenderData, _lottieRenderDataIntPtr, false);
        }
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin
{
    internal class TestMonoBeh : MonoBehaviour
    {
        [SerializeField] private RawImage _image;

        private IntPtr _animationWrapperIntPtr;
        private LottieAnimationWrapper _animationWrapper;

        private IntPtr _lottieRenderDataIntPtr;
        private LottieRenderData _lottieRenderData;
        private NativeArray<byte> _pixelData;
        private Texture2D _animationTexture;

        private int _currentFrame;
        private float _currentPlayTime;
        private double _frameDelta;

        private void Start()
        {
            string stickerJsonFilePath = Path.Combine(Application.streamingAssetsPath, "84638-marketing.json");
            _animationWrapper = NativeBridge.LoadFromFile(stickerJsonFilePath, out _animationWrapperIntPtr);
            _frameDelta = _animationWrapper.duration / _animationWrapper.totalFrame;
            Test();
        }
        private void OnDestroy()
        {
            _pixelData.Dispose();
            NativeBridge.Dispose(_animationWrapper);
            NativeBridge.LottieDisposeRenderData(ref _lottieRenderDataIntPtr);
            DestroyImmediate(_animationTexture);
            _animationTexture = null;
        }
        private unsafe void Test()
        {
            _lottieRenderData = new LottieRenderData();
            _lottieRenderData.width = 1024;
            _lottieRenderData.height = 1024;
            _lottieRenderData.bytesPerLine = 1024 * sizeof(uint);
            _animationTexture = new Texture2D(
                (int)_lottieRenderData.width,
                (int)_lottieRenderData.height,
                TextureFormat.BGRA32,
                0,
                false);
            _image.texture = _animationTexture;
            _pixelData = _animationTexture.GetRawTextureData<byte>();
            _lottieRenderData.buffer = NativeArrayUnsafeUtility.GetUnsafePtr(_pixelData);
            NativeBridge.LottieAllocateRenderData(ref _lottieRenderDataIntPtr);
            Marshal.StructureToPtr(_lottieRenderData, _lottieRenderDataIntPtr, false);

        }
        private void Update()
        {
            _currentPlayTime += Time.deltaTime;
            if (_currentPlayTime > _frameDelta)
            {
                NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, _currentFrame++, true);
                _animationTexture.Apply();
                _currentPlayTime = 0;
            }
            if (_currentFrame >= _animationWrapper.totalFrame)
            {
                _currentFrame = 0;
            }
            //if (_once == false)
            //{
            //    _once = true;
            //    byte[] bytes = _animationTexture.EncodeToPNG();
            //    string texturePath = Path.Combine("C:/Temp", "sticker.png");
            //    File.WriteAllBytes(texturePath, bytes);
            //}
        }
    }
}

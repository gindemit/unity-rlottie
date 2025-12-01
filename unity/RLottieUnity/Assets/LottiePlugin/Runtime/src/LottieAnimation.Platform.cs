using System;
using Unity.Collections;
using UnityEngine;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace LottiePlugin
{
    public sealed partial class LottieAnimation
    {
        // Platform-specific helpers to reduce #if clutter

        private Texture GetOutputTexture()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_useShaderConversion && _convertedRT != null)
                return _convertedRT;
#endif
            return Texture;
        }

        private void PlatformDisposeAsyncDraw()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_asyncDrawWasCalled && _animationWrapperIntPtr != IntPtr.Zero && _lottieRenderDataIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr);
                _asyncDrawWasCalled = false;
            }
#endif
        }

        private void PlatformDisposeNativeTexture()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_nativeTexturePtr != IntPtr.Zero)
            {
                NativeBridge.LottieDestroyTexture(_animationWrapperIntPtr, _nativeTexturePtr);
                _nativeTexturePtr = IntPtr.Zero;
            }
#endif
        }

        private void PlatformDisposePixelData()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_ownsPixelData && _pixelData.IsCreated)
            {
                _pixelData.Dispose();
            }
#endif
        }

        private void PlatformDisposeWebGLTextures()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_sourceTexture != null)
            {
                UnityEngine.Object.DestroyImmediate(_sourceTexture);
                _sourceTexture = null;
            }
            if (_convertedRT != null)
            {
                _convertedRT.Release();
                UnityEngine.Object.DestroyImmediate(_convertedRT);
                _convertedRT = null;
            }
#endif
        }

        private void PlatformDrawOneFrame(int frameNumber)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            bool useNativeConversion = !_useShaderConversion;
            NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, useNativeConversion);
#else
            NativeBridge.LottieRenderImmediately(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, false);
#endif
            CurrentFrame = frameNumber;
            if (_usesCPURendering)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (_useShaderConversion)
                {
                    _sourceTexture.Apply();
                    Graphics.Blit(_sourceTexture, _convertedRT, s_BGRAtoRGBAMaterial);
                }
                else
                {
                    Texture.Apply();
                }
#else
                Texture.Apply();
#endif
            }
            else
            {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
                RequestTextureUpload();
#endif
            }
        }

        private void PlatformDrawOneFrameAsyncPrepare(int frameNumber)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            bool useNativeConversion = !_useShaderConversion;
            NativeBridge.LottieRenderCreateFutureAsync(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, useNativeConversion);
#else
            NativeBridge.LottieRenderCreateFutureAsync(_animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, false);
#endif
        }

        private void PlatformDrawOneFrameAsyncGetResult()
        {
            if (!_asyncDrawWasCalled)
                return;
            if (_usesCPURendering)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr);
                if (_useShaderConversion)
                {
                    _sourceTexture.Apply();
                    Graphics.Blit(_sourceTexture, _convertedRT, s_BGRAtoRGBAMaterial);
                }
                else
                {
                    Texture.Apply();
                }
#else
                Texture.Apply();
#endif
                _asyncDrawWasCalled = false;
            }
            else
            {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
                if (NativeBridge.LottieRenderTryGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr, out int ready) == 0 && ready != 0)
                {
                    RequestTextureUpload();
                    _asyncDrawWasCalled = false;
                }
#endif
            }
        }

        private unsafe void PlatformCreateRenderDataTexture(uint width, uint height)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _usesCPURendering = true;
            _useShaderConversion = LottieWebGLSettings.UseShaderConversion;
            if (_useShaderConversion && s_BGRAtoRGBAMaterial == null)
            {
                s_BGRAtoRGBAShader = Shader.Find("Hidden/LottiePlugin/BGRAtoRGBA");
                if (s_BGRAtoRGBAShader != null)
                {
                    s_BGRAtoRGBAMaterial = new Material(s_BGRAtoRGBAShader);
                }
                else
                {
                    Debug.LogWarning("[LottiePlugin] BGRA to RGBA shader not found, falling back to native conversion");
                    _useShaderConversion = false;
                }
            }
#else
            var deviceType = UnityEngine.SystemInfo.graphicsDeviceType;
            _usesCPURendering = deviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan;
#endif
            if (_usesCPURendering)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (_useShaderConversion)
                {
                    _sourceTexture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 0, false);
                    _pixelData = _sourceTexture.GetRawTextureData<byte>();
                    _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                    _convertedRT = new RenderTexture((int)width, (int)height, 0, RenderTextureFormat.ARGB32);
                    _convertedRT.Create();
                    Texture = null;
                }
                else
                {
                    Texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 0, false);
                    _pixelData = Texture.GetRawTextureData<byte>();
                    _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                }
#else
                TextureFormat format = TextureFormat.BGRA32;
                Texture = new Texture2D((int)width, (int)height, format, 0, false);
                _pixelData = Texture.GetRawTextureData<byte>();
                _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
#endif
                _ownsPixelData = false;
            }
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            else
            {
                int bufferSize = (int)(width * height * sizeof(uint));
                _pixelData = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _ownsPixelData = true;
                _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                _nativeTexturePtr = NativeBridge.LottieCreateTexture(_animationWrapperIntPtr, (int)width, (int)height);
                if (_nativeTexturePtr == IntPtr.Zero)
                {
                    throw new System.Exception("Failed to create native texture. Graphics device may not be initialized yet.");
                }
                Texture = Texture2D.CreateExternalTexture((int)width, (int)height, TextureFormat.BGRA32, false, false, _nativeTexturePtr);
            }
#endif
        }

        private void RequestTextureUpload()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
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
#endif
        }
    }
}

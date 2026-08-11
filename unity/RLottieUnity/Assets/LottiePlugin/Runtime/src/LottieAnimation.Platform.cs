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

        private static void ConfigureRuntimeTexture(UnityEngine.Object textureObject)
        {
            if (textureObject == null)
            {
                return;
            }

            // Prevent Unity editor reload serialization from treating runtime render targets
            // as scene data (Unity 6 logs image-size warnings for external runtime textures).
            textureObject.hideFlags = HideFlags.HideAndDontSave;
        }

        private static bool IsDirect3DDevice(UnityEngine.Rendering.GraphicsDeviceType deviceType)
        {
            return deviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
                   deviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12;
        }

        private bool TryEnableNativeVulkanUpload(uint width, uint height)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const ulong affectedUploadBytes = 4UL * 1024UL * 1024UL;
            ulong uploadBytes = (ulong)width * height * 4UL;
            if (uploadBytes >= affectedUploadBytes &&
                SystemInfo.graphicsDeviceName.IndexOf("Mali-G76", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!sVulkanLargeTextureFallbackLogged)
                {
                    sVulkanLargeTextureFallbackLogged = true;
                    Debug.LogWarning(string.Format(
                        "[LottiePlugin] Using managed Vulkan texture upload for {0} byte textures on {1}; " +
                        "the native upload is not presented correctly at or above 4 MiB by this driver.",
                        uploadBytes, SystemInfo.graphicsDeviceName));
                }
                return false;
            }
#endif
            try
            {
                if (NativeBridge.LottieSupportsNativeVulkanUpload() != 0)
                {
                    if (!sVulkanNativeUploadLogged)
                    {
                        sVulkanNativeUploadLogged = true;
                        Debug.Log("[LottiePlugin] Vulkan native upload enabled");
                    }
                    return true;
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Older native plugins do not expose Vulkan upload capability.
            }
            catch (DllNotFoundException)
            {
                // Preserve the existing managed upload path when the native
                // plugin is not available yet (for example during import).
            }

            LogVulkanApplyFallback();
            return false;
        }

        private void LogVulkanApplyFallback()
        {
            if (sVulkanFallbackLogged)
            {
                return;
            }

            sVulkanFallbackLogged = true;
            Debug.LogWarning("[LottiePlugin] Vulkan native upload unavailable; using Texture2D.Apply fallback");
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
            TextureUploadBackend = _useShaderConversion
                ? LottieTextureUploadBackend.WebGLShaderConversion
                : LottieTextureUploadBackend.WebGLManagedTextureUpload;
#else
            var deviceType = UnityEngine.SystemInfo.graphicsDeviceType;
            bool isVulkan = deviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan;
            _usesUnityOwnedNativeTexture = isVulkan && !_useManagedTextureUpload && TryEnableNativeVulkanUpload(width, height);
            _usesCPURendering = _useManagedTextureUpload || (isVulkan && !_usesUnityOwnedNativeTexture);
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            // Unity's Linux OpenGL editor does not guarantee a current context on
            // the scripting thread. Keep Apply for OpenGL, while Vulkan uses the
            // native render-thread upload when the interface is available.
            if (!isVulkan)
            {
                _usesCPURendering = true;
            }
#endif
            TextureUploadBackend = _usesCPURendering
                ? LottieTextureUploadBackend.ManagedTextureUpload
                : (_usesUnityOwnedNativeTexture
                    ? LottieTextureUploadBackend.NativeVulkan
                    : LottieTextureUploadBackend.NativeExternalTexture);
#endif
            if (_usesCPURendering)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (_useShaderConversion)
                {
                    _sourceTexture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 0, false);
                    ConfigureRuntimeTexture(_sourceTexture);
                    _pixelData = _sourceTexture.GetRawTextureData<byte>();
                    _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                    _convertedRT = new RenderTexture((int)width, (int)height, 0, RenderTextureFormat.ARGB32);
                    ConfigureRuntimeTexture(_convertedRT);
                    _convertedRT.Create();
                    Texture = null;
                }
                else
                {
                    Texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 0, false);
                    ConfigureRuntimeTexture(Texture);
                    _pixelData = Texture.GetRawTextureData<byte>();
                    _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                }
#else
                TextureFormat format = TextureFormat.BGRA32;
                Texture = new Texture2D((int)width, (int)height, format, 0, false);
                ConfigureRuntimeTexture(Texture);
                _pixelData = Texture.GetRawTextureData<byte>();
                _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
#endif
                _ownsPixelData = false;
            }
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            else if (_usesUnityOwnedNativeTexture)
            {
                Texture = new Texture2D((int)width, (int)height, TextureFormat.BGRA32, 0, false);
                ConfigureRuntimeTexture(Texture);
                _pixelData = Texture.GetRawTextureData<byte>();
                _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                _ownsPixelData = false;

                _nativeTexturePtr = Texture.GetNativeTexturePtr();
                if (_nativeTexturePtr == IntPtr.Zero ||
                    NativeBridge.LottieRegisterUnityVulkanTexture(
                        _animationWrapperIntPtr,
                        _nativeTexturePtr,
                        (int)width,
                        (int)height) == 0)
                {
                    _nativeTexturePtr = IntPtr.Zero;
                    _usesUnityOwnedNativeTexture = false;
                    _usesCPURendering = true;
                    TextureUploadBackend = LottieTextureUploadBackend.ManagedTextureUpload;
                    LogVulkanApplyFallback();
                }
            }
            else
            {
                int bufferSize = (int)(width * height * sizeof(uint));
                _pixelData = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                _ownsPixelData = true;
                _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                bool isDirect3D = IsDirect3DDevice(deviceType);
                bool preferSrgbSampling = isDirect3D && QualitySettings.activeColorSpace == ColorSpace.Linear;
                _nativeTexturePtr = NativeBridge.LottieCreateTexture(
                    _animationWrapperIntPtr,
                    (int)width,
                    (int)height,
                    preferSrgbSampling);
                if (_nativeTexturePtr == IntPtr.Zero)
                {
                    throw new System.Exception("Failed to create native texture. Graphics device may not be initialized yet.");
                }

                // In Linear projects we request sRGB native textures on D3D and expose them as
                // non-linear external textures so Unity applies sRGB sampling correctly.
                // In Gamma projects we keep the previous linear external texture path.
                bool linearExternalTexture = isDirect3D && !preferSrgbSampling;
                Texture = Texture2D.CreateExternalTexture(
                    (int)width,
                    (int)height,
                    TextureFormat.BGRA32,
                    false,
                    linearExternalTexture,
                    _nativeTexturePtr);
                ConfigureRuntimeTexture(Texture);
            }
#endif
        }

        private void RequestTextureUpload()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            if (_usesUnityOwnedNativeTexture)
            {
                if (NativeBridge.LottieIsVulkanUploadAvailable(_animationWrapperIntPtr) == 0)
                {
                    if (!_vulkanReregisterAttempted)
                    {
                        _vulkanReregisterAttempted = true;
                        _nativeTexturePtr = Texture.GetNativeTexturePtr();
                        if (_nativeTexturePtr != IntPtr.Zero &&
                            NativeBridge.LottieRegisterUnityVulkanTexture(
                                _animationWrapperIntPtr,
                                _nativeTexturePtr,
                                Texture.width,
                                Texture.height) != 0)
                        {
                            // Keep this recovery frame visible while Unity and the
                            // render thread resume native uploads after device restart.
                            Texture.Apply();
                            return;
                        }
                    }

                    _usesUnityOwnedNativeTexture = false;
                    _usesCPURendering = true;
                    TextureUploadBackend = LottieTextureUploadBackend.ManagedTextureUpload;
                    LogVulkanApplyFallback();
                    Texture.Apply();
                    return;
                }

                _vulkanReregisterAttempted = false;
                NativeBridge.LottieUpdateTexture(_animationWrapperIntPtr);
                return;
            }

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

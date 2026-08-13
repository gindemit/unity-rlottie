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

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private unsafe void UseManagedTextureUploadFallback(uint width, uint height, string reason)
        {
            // A Unity-owned Android GLES texture is RGBA32, while rlottie's
            // managed output is BGRA. Preserve any frame already rendered into
            // the raw bytes, but replace the texture so Texture2D.Apply uses the
            // correct channel layout.
            Texture2D previousTexture = Texture;
            NativeArray<byte> previousPixelData = !_ownsPixelData && _pixelData.IsCreated
                ? _pixelData
                : default;
            if (_ownsPixelData && _pixelData.IsCreated)
            {
                _pixelData.Dispose();
            }

            _pixelData = default;
            _ownsPixelData = false;
            _nativeTexturePtr = IntPtr.Zero;
            _usesUnityOwnedNativeTexture = false;
            _usesUnityOwnedOpenGLTexture = false;
            _usesCPURendering = true;
            TextureUploadBackend = LottieTextureUploadBackend.ManagedTextureUpload;

            Texture = new Texture2D((int)width, (int)height, TextureFormat.BGRA32, 0, false);
            ConfigureRuntimeTexture(Texture);
            _pixelData = Texture.GetRawTextureData<byte>();
            _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
            if (previousPixelData.IsCreated && previousPixelData.Length == _pixelData.Length)
            {
                NativeArray<byte>.Copy(previousPixelData, _pixelData);
            }
            if (previousTexture != null)
            {
                UnityEngine.Object.Destroy(previousTexture);
            }

            Debug.LogWarning($"[LottiePlugin] Native texture upload unavailable ({reason}); using Texture2D.Apply fallback");
        }
#endif

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private bool TryEnableNativeVulkanUpload()
        {
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

        private void LogOpenGLApplyFallback()
        {
            if (sOpenGLFallbackLogged)
            {
                return;
            }

            sOpenGLFallbackLogged = true;
            Debug.LogWarning("[LottiePlugin] OpenGL native upload unavailable; using Texture2D.Apply fallback");
        }

        private bool TryRegisterUnityOwnedTexture(uint width, uint height)
        {
            _nativeTexturePtr = Texture.GetNativeTexturePtr();
            if (_nativeTexturePtr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (_usesUnityOwnedOpenGLTexture)
                {
                    bool registered = NativeBridge.LottieRegisterUnityOpenGLTexture(
                        _animationWrapperIntPtr,
                        _nativeTexturePtr,
                        (int)width,
                        (int)height) != 0;
                    if (registered && !sOpenGLNativeUploadLogged)
                    {
                        sOpenGLNativeUploadLogged = true;
                        Debug.Log("[LottiePlugin] Unity-owned OpenGL native upload enabled");
                    }
                    return registered;
                }

                return NativeBridge.LottieRegisterUnityVulkanTexture(
                    _animationWrapperIntPtr,
                    _nativeTexturePtr,
                    (int)width,
                    (int)height) != 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }
#endif

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
            _usesUnityOwnedNativeTexture = isVulkan && !_useManagedTextureUpload && TryEnableNativeVulkanUpload();
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || (UNITY_ANDROID && !UNITY_EDITOR)
            _usesUnityOwnedOpenGLTexture =
                (deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore ||
                 deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3) &&
                !_useManagedTextureUpload;
#else
            _usesUnityOwnedOpenGLTexture = false;
#endif
            _usesCPURendering = _useManagedTextureUpload || (isVulkan && !_usesUnityOwnedNativeTexture);
            TextureUploadBackend = _usesCPURendering
                ? LottieTextureUploadBackend.ManagedTextureUpload
                : (_usesUnityOwnedOpenGLTexture
                    ? LottieTextureUploadBackend.NativeOpenGL
                    : (_usesUnityOwnedNativeTexture
                    ? LottieTextureUploadBackend.NativeVulkan
                    : LottieTextureUploadBackend.NativeExternalTexture));
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
            else if (_usesUnityOwnedNativeTexture || _usesUnityOwnedOpenGLTexture)
            {
                // Native uploads update mip level 0 only. A single-level texture
                // prevents minified sampling from reading stale generated mips.
#if UNITY_ANDROID && !UNITY_EDITOR
                // Unity owns the GLES texture from the beginning, so the native
                // plug-in never exposes its deferred dummy GL name. GLES uploads
                // BGRA directly when supported and otherwise reuses a persistent
                // conversion buffer for RGBA.
                TextureFormat nativeTextureFormat = _usesUnityOwnedOpenGLTexture
                    ? TextureFormat.RGBA32
                    : TextureFormat.BGRA32;
#else
                TextureFormat nativeTextureFormat = TextureFormat.BGRA32;
#endif
                Texture = new Texture2D((int)width, (int)height, nativeTextureFormat, 1, false);
                ConfigureRuntimeTexture(Texture);
                _pixelData = Texture.GetRawTextureData<byte>();
                _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
                _ownsPixelData = false;

                if (!TryRegisterUnityOwnedTexture(width, height))
                {
                    _nativeTexturePtr = IntPtr.Zero;
                    bool wasOpenGL = _usesUnityOwnedOpenGLTexture;
                    UseManagedTextureUploadFallback(width, height,
                        wasOpenGL ? "OpenGL texture registration" : "Vulkan texture registration");
                }
            }
            else
            {
                // Plugin-owned native textures render through the native CPU
                // mailbox. Do not retain an otherwise unused managed frame buffer.
                _pixelData = default;
                _ownsPixelData = false;
                _lottieRenderData.buffer = null;
                bool isDirect3D = IsDirect3DDevice(deviceType);
                bool preferSrgbSampling = isDirect3D && QualitySettings.activeColorSpace == ColorSpace.Linear;
                _nativeTexturePtr = NativeBridge.LottieCreateTexture(
                    _animationWrapperIntPtr,
                    (int)width,
                    (int)height,
                    preferSrgbSampling);
                if (_nativeTexturePtr == IntPtr.Zero)
                {
                    // A renderer can be supported by Unity without having a native
                    // backend in this plugin. Clean up any partial native state and
                    // retain functional rendering through the managed upload path.
                    NativeBridge.LottieDestroyTexture(_animationWrapperIntPtr, IntPtr.Zero);
                    UseManagedTextureUploadFallback(width, height, deviceType.ToString());
                    return;
                }

                // In Linear projects we request sRGB native textures on D3D and expose them as
                // non-linear external textures so Unity applies sRGB sampling correctly.
                // In Gamma projects we keep the previous linear external texture path.
                bool linearExternalTexture = isDirect3D && !preferSrgbSampling;
                try
                {
                    Texture = Texture2D.CreateExternalTexture(
                        (int)width,
                        (int)height,
                        TextureFormat.BGRA32,
                        false,
                        linearExternalTexture,
                        _nativeTexturePtr);
                    if (Texture == null)
                    {
                        throw new InvalidOperationException("Unity did not create an external texture wrapper.");
                    }
                    ConfigureRuntimeTexture(Texture);
                }
                catch (Exception exception)
                {
                    NativeBridge.LottieDestroyTexture(_animationWrapperIntPtr, _nativeTexturePtr);
                    UseManagedTextureUploadFallback(width, height,
                        $"{deviceType}: {exception.GetType().Name}");
                }
            }
#endif
        }

        private void RequestTextureUpload()
        {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            // Do not let a one-shot render queue its only upload before the
            // render-thread event pump exists. Continuous animations used to
            // hide this startup race by naturally queueing another frame.
            LottieUploadPump.EnsureInstance();

            if (_usesUnityOwnedOpenGLTexture)
            {
                int uploadAvailable;
                try
                {
                    uploadAvailable = NativeBridge.LottieIsOpenGLUploadAvailable(_animationWrapperIntPtr);
                }
                catch (EntryPointNotFoundException)
                {
                    uploadAvailable = 0;
                }
                catch (DllNotFoundException)
                {
                    uploadAvailable = 0;
                }

                if (uploadAvailable == 0)
                {
                    if (!_openGLReregisterAttempted)
                    {
                        _openGLReregisterAttempted = true;
                        if (TryRegisterUnityOwnedTexture((uint)Texture.width, (uint)Texture.height))
                        {
                            // Keep this frame visible while native upload recovers.
                            Texture.Apply();
                            return;
                        }
                    }

                    uint width = (uint)Texture.width;
                    uint height = (uint)Texture.height;
                    UseManagedTextureUploadFallback(width, height, "OpenGL native upload failure");
                    Texture.Apply();
                    return;
                }

                _openGLReregisterAttempted = false;
                NativeBridge.LottieUpdateTexture(_animationWrapperIntPtr);
                // The render-thread plug-in updates this Unity-owned texture on
                // the GPU. Linux needs its texture update count advanced; doing
                // that on Android GLES can make Unity recreate/rebind the texture
                // while the native render event still owns the registered name.
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
                Texture.IncrementUpdateCount();
#endif
                return;
            }

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

using System;
using Unity.Collections;
using UnityEngine;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace LottiePlugin
{
    public sealed partial class LottieAnimation
    {
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
        private LottieWebGLNativeStateTrace.Identity _webGLTraceIdentity;

        public static string GetWebGLNativeStateTraceJson()
        {
            return LottieWebGLNativeStateTrace.ExportJson();
        }
#endif
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD
        [Serializable]
        private struct WebGLUploadDiagnosticEvent
        {
            public int frame, instanceId, width, height, available, backend;
            public bool alreadyRetried;
            public string reason;
            public int applyCalls, reregisterAttempts, reregisterSuccesses, reregisterFailures, fallbacks;
        }

        [Serializable]
        private sealed class WebGLUploadDiagnosticReport
        {
            public bool enabled = true;
            public string scope = "Managed upload observations only; native callback errors are not observed.";
            public string buildGuid = "";
            public int uploadChecks, availableZero, requestRejections, recoveryApplyCalls;
            public int reregisterAttempts, reregisterSuccesses, reregisterFailures, fallbacks;
            public int eventCount, eventsDropped, instancesWithEvents;
            public WebGLUploadDiagnosticEvent[] events = new WebGLUploadDiagnosticEvent[8];
        }

        private enum WebGLUploadDiagnosticAction { Apply, ReregisterAttempt, ReregisterSuccess, ReregisterFailure, Fallback }
        private static WebGLUploadDiagnosticReport sWebGLUploadDiagnostics;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeWebGLUploadDiagnostics()
        {
            // Unity API access belongs to the main-thread startup callback, not type initialization.
            sWebGLUploadDiagnostics = CreateWebGLUploadDiagnostics();
            if (sWebGLUploadDiagnostics != null) sWebGLUploadDiagnostics.buildGuid = Application.buildGUID;
        }
        private int _webGLDiagnosticInstanceId;

        private static WebGLUploadDiagnosticReport CreateWebGLUploadDiagnostics()
        {
            const string optIn = "lottieUploadDiagnostics=1";
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url) || url.IndexOf(optIn, StringComparison.Ordinal) < 0) return null;
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) || !parsed.IsLoopback ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) return null;
            int matches = 0;
            foreach (string part in parsed.Query.TrimStart('?').Split('&'))
            {
                int separator = part.IndexOf('=');
                string name = separator < 0 ? part : part.Substring(0, separator);
                name = Uri.UnescapeDataString(name.Replace("+", " "));
                if (name != "lottieUploadDiagnostics") continue;
                // Fail closed on duplicate/conflicting names, including encoded duplicates.
                if (++matches > 1 || part != optIn) return null;
            }
            return matches == 1 ? new WebGLUploadDiagnosticReport() : null;
        }

        /// <summary>Read-only, bounded Development WebGL diagnostics; call on the Unity main thread.
        /// Disabled unless an HTTP(S) loopback page explicitly opts in with lottieUploadDiagnostics=1.</summary>
        public static string GetWebGLUploadDiagnosticsJson()
        {
            return sWebGLUploadDiagnostics == null ? "{\"enabled\":false}" : JsonUtility.ToJson(sWebGLUploadDiagnostics);
        }

        private int BeginWebGLUploadDiagnostic(int available, string reason)
        {
            WebGLUploadDiagnosticReport report = sWebGLUploadDiagnostics;
            if (report == null) return -1;
            if (_webGLDiagnosticInstanceId == 0) _webGLDiagnosticInstanceId = ++report.instancesWithEvents;
            if (report.eventCount == report.events.Length) { report.eventsDropped++; return -1; }
            int index = report.eventCount++;
            report.events[index] = new WebGLUploadDiagnosticEvent
            {
                frame = Time.frameCount, instanceId = _webGLDiagnosticInstanceId,
                width = Texture.width, height = Texture.height, available = available,
                backend = (int)TextureUploadBackend, alreadyRetried = _webGLReregisterAttempted, reason = reason
            };
            return index;
        }

        private void ObserveWebGLUploadDiagnostic(int index, WebGLUploadDiagnosticAction action)
        {
            WebGLUploadDiagnosticReport report = sWebGLUploadDiagnostics;
            if (report == null) return;
            switch (action)
            {
                case WebGLUploadDiagnosticAction.Apply:
                    report.recoveryApplyCalls++;
                    if (index >= 0) report.events[index].applyCalls++;
                    break;
                case WebGLUploadDiagnosticAction.ReregisterAttempt:
                    report.reregisterAttempts++;
                    if (index >= 0) report.events[index].reregisterAttempts++;
                    break;
                case WebGLUploadDiagnosticAction.ReregisterSuccess:
                    report.reregisterSuccesses++;
                    if (index >= 0) report.events[index].reregisterSuccesses++;
                    break;
                case WebGLUploadDiagnosticAction.ReregisterFailure:
                    report.reregisterFailures++;
                    if (index >= 0) report.events[index].reregisterFailures++;
                    break;
                case WebGLUploadDiagnosticAction.Fallback:
                    report.fallbacks++;
                    if (index >= 0) report.events[index].fallbacks++;
                    break;
            }
            if (index >= 0) report.events[index].backend = (int)TextureUploadBackend;
        }
#endif

        // Platform-specific helpers to reduce #if clutter

        private Texture GetOutputTexture()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_useShaderConversion && _convertedRT != null)
                return _convertedRT;
#endif
            return Texture;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static bool IsWebGLNativeUploadDevice(UnityEngine.Rendering.GraphicsDeviceType deviceType)
        {
            // WebGL 1/2 report their GLES compatibility level. WebGPU and any
            // future renderer intentionally retain the managed upload path.
            return deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2 ||
                   deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3;
        }

        private bool TryRegisterUnityOwnedWebGLTexture(uint width, uint height)
        {
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            bool traceSelected = LottieWebGLNativeStateTrace.EnsureInitialized();
#endif
            NativeBridge.LottieRegisterWebGLRenderingPlugin();
            _nativeTexturePtr = Texture.GetNativeTexturePtr();
            bool registered = _nativeTexturePtr != IntPtr.Zero &&
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
                (traceSelected ? LottieWebGLNativeStateTrace.Register(ref _webGLTraceIdentity,
                    _animationWrapperIntPtr, _nativeTexturePtr, (int)width, (int)height, _webGLTraceIdentity.origin == 3) :
#endif
                NativeBridge.LottieRegisterUnityWebGLTexture(
                    _animationWrapperIntPtr,
                    _nativeTexturePtr,
                    (int)width,
                    (int)height)
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
                )
#endif
                != 0;
            if (registered && !sWebGLNativeUploadLogged)
            {
                sWebGLNativeUploadLogged = true;
                if (LottieLogSettings.GlobalLevel >= LottieLogLevel.Info)
                    Debug.Log("[LottiePlugin] Unity-owned WebGL native upload enabled");
            }
            return registered;
        }

        private void UseWebGLManagedTextureUploadFallback(string reason)
        {
            _usesUnityOwnedWebGLTexture = false;
            _usesCPURendering = true;
            _nativeTexturePtr = IntPtr.Zero;
            TextureUploadBackend = LottieTextureUploadBackend.WebGLManagedTextureUpload;
            if (!_pixelData.IsCreated)
            {
                AllocateCpuPixelData((uint)Texture.width, (uint)Texture.height);
            }
            if (!sWebGLFallbackLogged)
            {
                sWebGLFallbackLogged = true;
                Debug.LogWarning($"[LottiePlugin] WebGL native texture upload unavailable ({reason}); using Texture2D.Apply fallback");
            }
        }
#endif

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

        private static bool IsUnityOwnedOpenGLUploadDevice(UnityEngine.Rendering.GraphicsDeviceType deviceType)
        {
            return deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore ||
                   deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2 ||
                   deviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3;
        }

        private unsafe void AllocateCpuPixelData(uint width, uint height)
        {
            long byteCount = checked((long)width * height * 4L);
            if (byteCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "The requested raster surface is too large.");
            }

            if (_ownsPixelData && _pixelData.IsCreated)
            {
                _pixelData.Dispose();
            }

            _pixelData = new NativeArray<byte>((int)byteCount, Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            _ownsPixelData = true;
            _lottieRenderData.buffer = _pixelData.GetUnsafePtr();
        }

        private void UploadCpuPixels(Texture2D texture)
        {
            texture.LoadRawTextureData(_pixelData);
            texture.Apply(false, false);
        }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private unsafe void UseManagedTextureUploadFallback(uint width, uint height, string reason)
        {
            // A Unity-owned Android GLES texture is RGBA32, while rlottie's
            // managed output is BGRA. Replace it so Texture2D.Apply uses the
            // correct channel layout and detach the native mailbox before the
            // independently owned CPU buffer becomes the render target.
            Texture2D previousTexture = Texture;
            if (_animationWrapperIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieDestroyTexture(_animationWrapperIntPtr, _nativeTexturePtr);
            }
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

            Texture = new Texture2D((int)width, (int)height, TextureFormat.BGRA32, 1, false);
            ConfigureRuntimeTexture(Texture);
            AllocateCpuPixelData(width, height);
            if (previousTexture != null)
            {
                UnityEngine.Object.Destroy(previousTexture);
            }

            Debug.LogWarning($"[LottiePlugin] Native texture upload unavailable ({reason}); using Texture2D.Apply fallback");
        }

        private void RenderCurrentFrameAfterManagedFallback()
        {
            int result = NativeBridge.LottieRenderImmediately(
                _animationWrapperIntPtr, _lottieRenderDataIntPtr, CurrentFrame, true, false);
            if (result != 0)
                throw new InvalidOperationException("Native rlottie rasterization failed after switching to managed upload.");
            UploadCpuPixels(Texture);
        }
#endif

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        private bool TryEnableNativeVulkanUpload()
        {
            if (NativeBridge.LottieSupportsNativeVulkanUpload() != 0)
            {
                if (!sVulkanNativeUploadLogged)
                {
                    sVulkanNativeUploadLogged = true;
                    if (LottieLogSettings.GlobalLevel >= LottieLogLevel.Info)
                        Debug.Log("[LottiePlugin] Vulkan native upload enabled");
                }
                return true;
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
                    if (LottieLogSettings.GlobalLevel >= LottieLogLevel.Info)
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
#endif

        private void PlatformDisposeAsyncDraw()
        {
            if (_asyncDrawWasCalled && _animationWrapperIntPtr != IntPtr.Zero && _lottieRenderDataIntPtr != IntPtr.Zero)
            {
#if !(UNITY_WEBGL && !UNITY_EDITOR)
                NativeBridge.LottieRenderGetFutureResult(_animationWrapperIntPtr, _lottieRenderDataIntPtr);
#endif
                _asyncDrawWasCalled = false;
            }
        }

        private void PlatformDisposeNativeTexture()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (_usesUnityOwnedWebGLTexture && _animationWrapperIntPtr != IntPtr.Zero)
            {
                NativeBridge.LottieUnregisterUnityWebGLTexture(_animationWrapperIntPtr);
            }
            _usesUnityOwnedWebGLTexture = false;
            _nativeTexturePtr = IntPtr.Zero;
#else
            if (_nativeTexturePtr != IntPtr.Zero)
            {
                NativeBridge.LottieDestroyTexture(_animationWrapperIntPtr, _nativeTexturePtr);
                _nativeTexturePtr = IntPtr.Zero;
            }
#endif
        }

        private void PlatformDisposePixelData()
        {
            if (_ownsPixelData && _pixelData.IsCreated)
            {
                _pixelData.Dispose();
            }
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
            int result = NativeBridge.LottieRenderImmediately(
                _animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, useNativeConversion);
#else
            int result = NativeBridge.LottieRenderImmediately(
                _animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, false);
#endif
            if (result != 0) throw new InvalidOperationException("Native rlottie rasterization failed.");
            CurrentFrame = frameNumber;
            if (_usesCPURendering)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (_useShaderConversion)
                {
                    UploadCpuPixels(_sourceTexture);
                    Graphics.Blit(_sourceTexture, _convertedRT, s_BGRAtoRGBAMaterial);
                }
                else
                {
                    UploadCpuPixels(Texture);
                }
#else
                UploadCpuPixels(Texture);
#endif
            }
            else
            {
                RequestTextureUpload();
            }
        }

        private void PlatformDrawOneFrameAsyncPrepare(int frameNumber)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            bool useNativeConversion = !_useShaderConversion;
            int result = NativeBridge.LottieRenderCreateFutureAsync(
                _animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, useNativeConversion);
#else
            int result = NativeBridge.LottieRenderCreateFutureAsync(
                _animationWrapperIntPtr, _lottieRenderDataIntPtr, frameNumber, true, false);
#endif
            if (result != 0) throw new InvalidOperationException("Native rlottie asynchronous rasterization could not start.");
        }

        private void PlatformDrawOneFrameAsyncGetResult()
        {
            if (!_asyncDrawWasCalled)
                return;
            if (_usesCPURendering)
            {
                int result = NativeBridge.LottieRenderGetFutureResult(
                    _animationWrapperIntPtr,
                    _lottieRenderDataIntPtr);
                _asyncDrawWasCalled = false;
                if (result != 0)
                {
                    throw new InvalidOperationException("Native rlottie asynchronous rasterization failed.");
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                if (_useShaderConversion)
                {
                    UploadCpuPixels(_sourceTexture);
                    Graphics.Blit(_sourceTexture, _convertedRT, s_BGRAtoRGBAMaterial);
                }
                else
                {
                    UploadCpuPixels(Texture);
                }
#else
                UploadCpuPixels(Texture);
#endif
            }
            else
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL has no asynchronous worker render; the future API
                // completed synchronously, so publish the completed buffer.
                RequestTextureUpload();
                _asyncDrawWasCalled = false;
#else
                int result = NativeBridge.LottieRenderTryGetFutureResult(
                    _animationWrapperIntPtr, _lottieRenderDataIntPtr, out int ready);
                if (result != 0)
                {
                    _asyncDrawWasCalled = false;
                    throw new InvalidOperationException("Native rlottie asynchronous rasterization failed.");
                }
                if (ready != 0)
                {
                    RequestTextureUpload();
                    _asyncDrawWasCalled = false;
                }
#endif
            }
        }

        private unsafe void PlatformCreateRenderDataTexture(uint width, uint height)
        {
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            LottieWebGLNativeStateTrace.TextureCreated(ref _webGLTraceIdentity);
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
            _useShaderConversion = LottieWebGLSettings.UseShaderConversion;
            var webGLDeviceType = UnityEngine.SystemInfo.graphicsDeviceType;
            bool tryNativeUpload = !_useManagedTextureUpload && IsWebGLNativeUploadDevice(webGLDeviceType);

            if (tryNativeUpload)
            {
                // Native WebGL upload targets a Unity-owned RGBA texture. Do
                // the BGRA conversion in the existing native render routine;
                // this avoids both Texture2D.Apply and the shader blit.
                _useShaderConversion = false;
                Texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 1, false);
                ConfigureRuntimeTexture(Texture);
                // Allocate Unity's WebGL texture object once before requesting
                // its native name. Per-frame Apply calls are avoided on success.
                Texture.Apply(false, false);
                // The native upload event copies from the rlottie render-data
                // surface into Unity's texture. Avoiding Texture2D.Apply does
                // not remove the CPU raster surface itself.
                AllocateCpuPixelData(width, height);
                _usesUnityOwnedWebGLTexture = TryRegisterUnityOwnedWebGLTexture(width, height);
                _usesCPURendering = !_usesUnityOwnedWebGLTexture;
                TextureUploadBackend = _usesUnityOwnedWebGLTexture
                    ? LottieTextureUploadBackend.NativeWebGL
                    : LottieTextureUploadBackend.WebGLManagedTextureUpload;
                if (!_usesUnityOwnedWebGLTexture)
                {
                    UseWebGLManagedTextureUploadFallback("texture registration");
#if DEVELOPMENT_BUILD
                    int diagnostic = BeginWebGLUploadDiagnostic(-1, "texture registration failed");
                    ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.Fallback);
#endif
                }
                return;
            }

            _usesUnityOwnedWebGLTexture = false;
            _usesCPURendering = true;
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
#if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_WIN || (UNITY_ANDROID && !UNITY_EDITOR)
            _usesUnityOwnedOpenGLTexture =
                IsUnityOwnedOpenGLUploadDevice(deviceType) &&
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
                    _sourceTexture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 1, false);
                    ConfigureRuntimeTexture(_sourceTexture);
                    AllocateCpuPixelData(width, height);
                    _convertedRT = new RenderTexture((int)width, (int)height, 0, RenderTextureFormat.ARGB32);
                    ConfigureRuntimeTexture(_convertedRT);
                    _convertedRT.Create();
                    Texture = null;
                }
                else
                {
                    Texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, 1, false);
                    ConfigureRuntimeTexture(Texture);
                    AllocateCpuPixelData(width, height);
                }
#else
                TextureFormat format = TextureFormat.BGRA32;
                Texture = new Texture2D((int)width, (int)height, format, 1, false);
                ConfigureRuntimeTexture(Texture);
                AllocateCpuPixelData(width, height);
#endif
            }
#if !(UNITY_WEBGL && !UNITY_EDITOR)
            else if (_usesUnityOwnedNativeTexture || _usesUnityOwnedOpenGLTexture)
            {
                // Native uploads update mip level 0 only. A single-level texture
                // prevents minified sampling from reading stale generated mips.
                // Unity-owned OpenGL textures use RGBA storage. Desktop GL can
                // upload rlottie's BGRA bytes directly with GL_BGRA, while GLES
                // reuses a persistent conversion buffer when that upload format
                // is unavailable. Declaring the Unity texture as BGRA would add
                // a second sampling swizzle and exchange red and blue.
                TextureFormat nativeTextureFormat = _usesUnityOwnedOpenGLTexture
                    ? TextureFormat.RGBA32
                    : TextureFormat.BGRA32;
                Texture = new Texture2D((int)width, (int)height, nativeTextureFormat, 1, false);
                ConfigureRuntimeTexture(Texture);
                Texture.Apply(false, false);
                _pixelData = default;
                _lottieRenderData.buffer = null;
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
#if UNITY_WEBGL && !UNITY_EDITOR
            LottieUploadPump.EnsureInstance();
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            bool traceSelected = LottieWebGLNativeStateTrace.EnsureInitialized();
#endif
            int uploadAvailable = NativeBridge.LottieIsWebGLUploadAvailable(_animationWrapperIntPtr);
#if DEVELOPMENT_BUILD
            if (sWebGLUploadDiagnostics != null) sWebGLUploadDiagnostics.uploadChecks++;
#endif

            if (uploadAvailable == 0)
            {
#if DEVELOPMENT_BUILD
                if (sWebGLUploadDiagnostics != null) sWebGLUploadDiagnostics.availableZero++;
                int diagnostic = BeginWebGLUploadDiagnostic(uploadAvailable, "native upload unavailable");
#endif
                if (!_webGLReregisterAttempted)
                {
                    _webGLReregisterAttempted = true;
#if DEVELOPMENT_BUILD
                    ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.Apply);
#endif
                    // Make this frame visible and let Unity recreate the GPU
                    // object, if needed, before caching its native texture name.
                    Texture.Apply(false, false);
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
                    if (traceSelected) _webGLTraceIdentity.origin = 3;
#endif
#if DEVELOPMENT_BUILD
                    ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.ReregisterAttempt);
#endif
                    if (TryRegisterUnityOwnedWebGLTexture((uint)Texture.width, (uint)Texture.height))
                    {
#if DEVELOPMENT_BUILD
                        ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.ReregisterSuccess);
#endif
                        return;
                    }
#if DEVELOPMENT_BUILD
                    ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.ReregisterFailure);
#endif
                }

                UseWebGLManagedTextureUploadFallback("native upload failure");
#if DEVELOPMENT_BUILD
                ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.Fallback);
                ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.Apply);
#endif
                Texture.Apply(false, false);
                return;
            }

            _webGLReregisterAttempted = false;
            if (
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
                (traceSelected ? LottieWebGLNativeStateTrace.Request(ref _webGLTraceIdentity,
                    _animationWrapperIntPtr, _lottieRenderDataIntPtr) :
#endif
                NativeBridge.LottieRequestWebGLTextureUpload(
                    _animationWrapperIntPtr,
                    _lottieRenderDataIntPtr)
#if DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
                )
#endif
                == 0)
            {
#if DEVELOPMENT_BUILD
                if (sWebGLUploadDiagnostics != null) sWebGLUploadDiagnostics.requestRejections++;
                int diagnostic = BeginWebGLUploadDiagnostic(uploadAvailable, "upload request rejected");
#endif
                UseWebGLManagedTextureUploadFallback("upload request rejected");
#if DEVELOPMENT_BUILD
                ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.Fallback);
                ObserveWebGLUploadDiagnostic(diagnostic, WebGLUploadDiagnosticAction.Apply);
#endif
                Texture.Apply(false, false);
            }
#else
            // Do not let a one-shot render queue its only upload before the
            // render-thread event pump exists. Continuous animations used to
            // hide this startup race by naturally queueing another frame.
            LottieUploadPump.EnsureInstance();

            if (_usesUnityOwnedOpenGLTexture)
            {
                int uploadAvailable = NativeBridge.LottieIsOpenGLUploadAvailable(_animationWrapperIntPtr);

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
                    RenderCurrentFrameAfterManagedFallback();
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

                    uint width = (uint)Texture.width;
                    uint height = (uint)Texture.height;
                    UseManagedTextureUploadFallback(width, height, "Vulkan native upload failure");
                    LogVulkanApplyFallback();
                    RenderCurrentFrameAfterManagedFallback();
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

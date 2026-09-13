using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace LottiePlugin
{
    public enum LottieLogLevel
    {
        None = 0,
        Error = 1,
        Warning = 2,
        Info = 3
    }

    /// <summary>
    /// Handles plugin initialization for iOS.
    /// On iOS with IL2CPP, static libraries don't get UnityPluginLoad called automatically.
    /// We must explicitly register the plugin using UnityRegisterRenderingPluginV5.
    /// </summary>
    internal static class LottiePluginRegistration
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern void UnityRegisterRenderingPluginV5(IntPtr loadFunc, IntPtr unloadFunc);

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lottie_get_plugin_load_func();

        [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr lottie_get_plugin_unload_func();

        private static bool s_Registered = false;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private static bool s_WebGLRegistered;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void RegisterPlugin()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (s_Registered)
                return;

            try
            {
                IntPtr loadFunc = lottie_get_plugin_load_func();
                IntPtr unloadFunc = lottie_get_plugin_unload_func();
                
                Debug.Log($"[Lottie] Registering iOS plugin: loadFunc={loadFunc}, unloadFunc={unloadFunc}");
                
                UnityRegisterRenderingPluginV5(loadFunc, unloadFunc);
                s_Registered = true;
                
                Debug.Log("[Lottie] iOS plugin registered successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Lottie] Failed to register iOS plugin: {ex.Message}");
            }
#elif UNITY_WEBGL && !UNITY_EDITOR
            if (s_WebGLRegistered)
                return;

            try
            {
                NativeBridge.LottieRegisterWebGLRenderingPlugin();
                s_WebGLRegistered = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LottiePlugin] WebGL rendering plug-in registration failed; Texture2D.Apply will be used ({ex.GetType().Name})");
            }
#endif
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LottieAnimationWrapper
    {
        public IntPtr self;
        public IntPtr animation;
        public double frameRate;
        public long totalFrames;
        public double duration;
        public long width;
        public long height;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct LottieRenderData
    {
        public void* buffer;
        public uint width;
        public uint height;
        public uint bytesPerLine;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeLottieColorOverride
    {
        public IntPtr keyPath;
        public LottieColorProperty property;
        public float red;
        public float green;
        public float blue;
    }
    internal static class NativeBridge
    {
#if (UNITY_WEBGL || UNITY_IOS) && !UNITY_EDITOR
        private const string PLUGIN_NAME = "__Internal";
#else
        private const string PLUGIN_NAME = "LottiePlugin";
#endif

#if !(UNITY_WEBGL && !UNITY_EDITOR)
        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_create_texture_with_color_space")]
        internal static extern IntPtr LottieCreateTexture(
            IntPtr animationWrapper,
            int width,
            int height,
            bool preferSrgbSampling);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_destroy_texture")]
        internal static extern void LottieDestroyTexture(IntPtr animationWrapper, IntPtr texturePtr);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_get_native_texture_ptr")]
        internal static extern IntPtr LottieGetNativeTexturePtr(IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_update_texture")]
        internal static extern void LottieUpdateTexture(IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_supports_native_vulkan_upload")]
        internal static extern int LottieSupportsNativeVulkanUpload();

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_register_unity_vulkan_texture")]
        internal static extern int LottieRegisterUnityVulkanTexture(
            IntPtr animationWrapper,
            IntPtr nativeTexture,
            int width,
            int height);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_is_vulkan_upload_available")]
        internal static extern int LottieIsVulkanUploadAvailable(IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_register_unity_opengl_texture")]
        internal static extern int LottieRegisterUnityOpenGLTexture(
            IntPtr animationWrapper,
            IntPtr nativeTexture,
            int width,
            int height);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_is_opengl_upload_available")]
        internal static extern int LottieIsOpenGLUploadAvailable(IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_get_render_event_func")]
        internal static extern IntPtr LottieGetRenderEventFunc();
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_register_webgl_rendering_plugin")]
        internal static extern void LottieRegisterWebGLRenderingPlugin();

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_register_unity_webgl_texture")]
        internal static extern int LottieRegisterUnityWebGLTexture(
            IntPtr animationWrapper,
            IntPtr nativeTexture,
            int width,
            int height);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_unregister_unity_webgl_texture")]
        internal static extern void LottieUnregisterUnityWebGLTexture(IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_request_webgl_texture_upload")]
        internal static extern int LottieRequestWebGLTextureUpload(
            IntPtr animationWrapper,
            IntPtr renderData);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_is_webgl_upload_available")]
        internal static extern int LottieIsWebGLUploadAvailable(IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_get_webgl_render_event_func")]
        internal static extern IntPtr LottieGetWebGLRenderEventFunc();
#endif

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_load_from_data")]
        private static extern int LottieLoadFromData(
            [MarshalAs(UnmanagedType.LPStr)] string jsonData,
            [MarshalAs(UnmanagedType.LPStr)] string resourcePath,
            out IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_load_from_file")]
        private static extern int LottieLoadFromFile(
            [MarshalAs(UnmanagedType.LPStr)] string filePath,
            out IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_dispose_wrapper")]
        internal static extern int LottieDisposeWrapper(
            ref IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_render_immediately")]
        internal static extern int LottieRenderImmediately(
            IntPtr animationWrapper,
            IntPtr renderData,
            int frameNumber,
            bool keepAspectRatio,
            bool convertBgraToRgba);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_render_create_future_async")]
        internal static extern int LottieRenderCreateFutureAsync(
            IntPtr animationWrapper,
            IntPtr renderData,
            int frameNumber,
            bool keepAspectRatio,
            bool convertBgraToRgba);
        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_render_try_get_future_result")]
        internal static extern int LottieRenderTryGetFutureResult(
            IntPtr animationWrapper,
            IntPtr renderData,
            out int ready);
        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_render_get_future_result")]
        internal static extern int LottieRenderGetFutureResult(
            IntPtr animationWrapper,
            IntPtr renderData);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_allocate_render_data")]
        internal static extern int LottieAllocateRenderData(
            ref IntPtr animationWrapper);
        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_dispose_render_data")]
        internal static extern int LottieDisposeRenderData(
            ref IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_set_log_level")]
        internal static extern int LottieSetLogLevel(
            IntPtr animationWrapper,
            LottieLogLevel logLevel);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_set_global_log_level")]
        internal static extern int LottieSetGlobalLogLevel(LottieLogLevel logLevel);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_set_fill_color")]
        internal static extern int LottieSetFillColor(
            IntPtr animationWrapper,
            [MarshalAs(UnmanagedType.LPStr)] string keyPath,
            float red,
            float green,
            float blue);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_set_stroke_color")]
        internal static extern int LottieSetStrokeColor(
            IntPtr animationWrapper,
            [MarshalAs(UnmanagedType.LPStr)] string keyPath,
            float red,
            float green,
            float blue);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_apply_color_overrides")]
        private static extern int LottieApplyColorOverrides(
            IntPtr animationWrapper,
            IntPtr overrides,
            uint overrideCount);

        internal static int ApplyColorOverrides(
            IntPtr animationWrapper,
            IReadOnlyList<LottieColorOverride> overrides)
        {
            if (overrides == null || overrides.Count == 0)
                return 0;

            int itemSize = Marshal.SizeOf<NativeLottieColorOverride>();
            IntPtr items = Marshal.AllocHGlobal(checked(itemSize * overrides.Count));
            IntPtr[] keyPaths = new IntPtr[overrides.Count];
            try
            {
                for (int index = 0; index < overrides.Count; index++)
                {
                    LottieColorOverride colorOverride = overrides[index];
                    ValidateColorOverride(colorOverride, nameof(overrides));
                    keyPaths[index] = StringToHGlobalUtf8(colorOverride.KeyPath);
                    var nativeOverride = new NativeLottieColorOverride
                    {
                        keyPath = keyPaths[index],
                        property = colorOverride.Property,
                        red = colorOverride.Color.r,
                        green = colorOverride.Color.g,
                        blue = colorOverride.Color.b
                    };
                    Marshal.StructureToPtr(nativeOverride, IntPtr.Add(items, itemSize * index), false);
                }
                return LottieApplyColorOverrides(animationWrapper, items, checked((uint)overrides.Count));
            }
            finally
            {
                for (int index = 0; index < keyPaths.Length; index++)
                {
                    if (keyPaths[index] != IntPtr.Zero)
                        Marshal.FreeHGlobal(keyPaths[index]);
                }
                Marshal.FreeHGlobal(items);
            }
        }

        internal static void ValidateColorOverride(LottieColorOverride colorOverride, string parameterName)
        {
            if (string.IsNullOrEmpty(colorOverride.KeyPath))
                throw new ArgumentException("A non-empty rlottie keypath is required.", parameterName);
            if (colorOverride.Property != LottieColorProperty.FillColor &&
                colorOverride.Property != LottieColorProperty.StrokeColor)
                throw new ArgumentOutOfRangeException(parameterName, "Unsupported Lottie color property.");
            Color color = colorOverride.Color;
            if (float.IsNaN(color.r) || float.IsInfinity(color.r) ||
                float.IsNaN(color.g) || float.IsInfinity(color.g) ||
                float.IsNaN(color.b) || float.IsInfinity(color.b))
                throw new ArgumentException("Lottie override colors must contain finite RGB values.", parameterName);
        }

        private static IntPtr StringToHGlobalUtf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            IntPtr pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            return pointer;
        }

        internal static LottieAnimationWrapper LoadFromData(string filePath, string resourcesPath, out IntPtr animationWrapper)
        {
            int result = LottieLoadFromData(filePath, resourcesPath, out animationWrapper);
            if (result != 0 || animationWrapper == IntPtr.Zero)
            {
                if (animationWrapper != IntPtr.Zero)
                    LottieDisposeWrapper(ref animationWrapper);
                throw new InvalidDataException("The native rlottie library could not load the JSON data.");
            }
            return Marshal.PtrToStructure<LottieAnimationWrapper>(animationWrapper);
        }
        internal static LottieAnimationWrapper LoadFromFile(string filePath, out IntPtr animationWrapper)
        {
            int result = LottieLoadFromFile(filePath, out animationWrapper);
            if (result != 0 || animationWrapper == IntPtr.Zero)
            {
                if (animationWrapper != IntPtr.Zero)
                    LottieDisposeWrapper(ref animationWrapper);
                throw new InvalidDataException("The native rlottie library could not load the JSON file.");
            }
            return Marshal.PtrToStructure<LottieAnimationWrapper>(animationWrapper);
        }
        internal static void Dispose(ref IntPtr animationWrapperPtr)
        {
            LottieDisposeWrapper(ref animationWrapperPtr);
        }
    }
}

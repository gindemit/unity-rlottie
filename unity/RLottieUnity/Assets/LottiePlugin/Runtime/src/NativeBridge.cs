using System;
using System.Runtime.InteropServices;
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

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl, EntryPoint = "lottie_get_render_event_func")]
        internal static extern IntPtr LottieGetRenderEventFunc();
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

        internal static LottieAnimationWrapper LoadFromData(string filePath, string resourcesPath, out IntPtr animationWrapper)
        {
            LottieLoadFromData(filePath, resourcesPath, out animationWrapper);
            return Marshal.PtrToStructure<LottieAnimationWrapper>(animationWrapper);
        }
        internal static LottieAnimationWrapper LoadFromFile(string filePath, out IntPtr animationWrapper)
        {
            LottieLoadFromFile(filePath, out animationWrapper);
            return Marshal.PtrToStructure<LottieAnimationWrapper>(animationWrapper);
        }
        internal static void Dispose(ref IntPtr animationWrapperPtr)
        {
            LottieDisposeWrapper(ref animationWrapperPtr);
        }
    }
}

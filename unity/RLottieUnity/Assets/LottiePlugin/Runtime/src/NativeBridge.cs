using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LottiePlugin
{
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
#if UNITY_IOS && !UNITY_EDITOR
        private const string PLUGIN_NAME = "__Internal";
#else
        private const string PLUGIN_NAME = "LottiePlugin";
#endif

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_load_from_data")]
        private static extern int LottieLoadFromData(
            string jsonData,
            string resourcePath,
            out IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_load_from_file")]
        private static extern int LottieLoadFromFile(
            string filePath,
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
            bool keepAspectRatio);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_render_create_future_async")]
        internal static extern int LottieRenderCreateFutureAsync(
            IntPtr animationWrapper,
            IntPtr renderData,
            int frameNumber,
            bool keepAspectRatio);
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
            EntryPoint = "initialize_logger")]
        internal static extern int InitializeLogger(
            string logDirectoryPath,
            string logFileName,
            int logFileRollSizeMB);

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
        internal static void Dispose(LottieAnimationWrapper lottieAnimationWrapper)
        {
            LottieDisposeWrapper(ref lottieAnimationWrapper.self);
            Debug.Assert(lottieAnimationWrapper.self == IntPtr.Zero);
        }
    }
}

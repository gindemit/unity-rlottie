using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LottiePlugin
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LottieAnimationWrapper
    {
        public IntPtr self;
        public IntPtr animation;
        public double frameRate;
        public uint totalFrame;
        public double duration;
    }
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct LottieRenderData
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
        private const string PLUGIN_NAME = "lottieplugin";
#endif

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_load_from_file")]
        internal static extern int LottieLoadFromFile(
            string filePath,
            out System.IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_dispose_wrapper")]
        internal static extern int LottieDisposeWrapper(
            ref System.IntPtr animationWrapper);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_render_immediately")]
        internal static extern int LottieRenderImmediately(
            System.IntPtr animationWrapper,
            System.IntPtr renderData,
            int frameNumber,
            bool keepAspectRatio);

        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_allocate_render_data")]
        internal static extern int LottieAllocateRenderData(
            ref System.IntPtr animationWrapper);
        [DllImport(PLUGIN_NAME,
            CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "lottie_dispose_render_data")]
        internal static extern int LottieDisposeRenderData(
            ref System.IntPtr animationWrapper);

        internal static LottieAnimationWrapper LoadFromFile(string filePath, out IntPtr animationWrapper)
        {
            NativeBridge.LottieLoadFromFile(filePath, out animationWrapper);
            return Marshal.PtrToStructure<LottieAnimationWrapper>(animationWrapper);
        }
        internal static void Dispose(LottieAnimationWrapper lottieAnimationWrapper)
        {
            NativeBridge.LottieDisposeWrapper(ref lottieAnimationWrapper.self);
            Debug.Assert(lottieAnimationWrapper.self == IntPtr.Zero);
        }
        internal static void RenderImmediately(
            ref LottieAnimationWrapper lottieAnimationWrapper,
            ref LottieRenderData lottieRenderData,
            int frameNumber,
            bool keepAspectRatio)
        {

        }
    }
}

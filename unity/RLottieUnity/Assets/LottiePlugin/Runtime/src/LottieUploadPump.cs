using System;
using UnityEngine;

namespace LottiePlugin
{
    internal sealed class LottieUploadPump : MonoBehaviour
    {
        private static LottieUploadPump sInstance;
        private static IntPtr sRenderEventFunc = IntPtr.Zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void EnsureInstance()
        {
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            LottieWebGLNativeStateTrace.EnsureInitialized();
#endif
            if (sInstance != null)
            {
                return;
            }

            var go = new GameObject("LottieUploadPump");
            go.hideFlags = HideFlags.HideAndDontSave;
            sInstance = go.AddComponent<LottieUploadPump>();
            go.AddComponent<LottieSchedulerReset>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            LottieWebGLNativeStateTrace.EnsureInitialized();
#endif
            if (sInstance != null && sInstance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }

            sInstance = this;
            if (sRenderEventFunc == IntPtr.Zero)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                sRenderEventFunc = NativeBridge.LottieGetWebGLRenderEventFunc();
#else
                sRenderEventFunc = NativeBridge.LottieGetRenderEventFunc();
#endif
            }
        }

        private void LateUpdate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            bool traceSelected = LottieWebGLNativeStateTrace.EnsureInitialized();
#endif
            if (sRenderEventFunc == IntPtr.Zero)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                sRenderEventFunc = NativeBridge.LottieGetWebGLRenderEventFunc();
#else
                sRenderEventFunc = NativeBridge.LottieGetRenderEventFunc();
#endif
                if (sRenderEventFunc == IntPtr.Zero)
                {
                    return;
                }
            }

            int budget = LottieScheduler.MaxUploadsPerFrame;
#if !UNITY_WEBGL || UNITY_EDITOR
            if (budget > 0)
            {
                // This snapshot does not consume work. Async publications
                // after the poll and upload retries stay queued for the next
                // LateUpdate; no managed wakeup flag can lose them.
                budget = Math.Min(budget, NativeBridge.LottieGetPendingUploadCount());
            }
#endif
            for (int i = 0; i < budget; i++)
            {
                // Vulkan configures this stable event ID to execute outside a
                // render pass. The callback dequeues one upload per event.
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
                GL.IssuePluginEvent(sRenderEventFunc, traceSelected ? LottieWebGLNativeStateTrace.IssueEvent() : 1);
#else
                GL.IssuePluginEvent(sRenderEventFunc, 1);
#endif
            }
        }

        private void OnDestroy()
        {
            if (sInstance == this)
            {
                sInstance = null;
                sRenderEventFunc = IntPtr.Zero;
            }
        }
    }
}

using System;
using UnityEngine;

namespace LottiePlugin
{
#if !(UNITY_WEBGL && !UNITY_EDITOR)
    internal sealed class LottieUploadPump : MonoBehaviour
    {
        private static LottieUploadPump sInstance;
        private static IntPtr sRenderEventFunc = IntPtr.Zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void EnsureInstance()
        {
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
            if (sInstance != null && sInstance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }

            sInstance = this;
            if (sRenderEventFunc == IntPtr.Zero)
            {
                sRenderEventFunc = NativeBridge.LottieGetRenderEventFunc();
            }
        }

        private void LateUpdate()
        {
            if (sRenderEventFunc == IntPtr.Zero)
            {
                sRenderEventFunc = NativeBridge.LottieGetRenderEventFunc();
                if (sRenderEventFunc == IntPtr.Zero)
                {
                    return;
                }
            }

            int budget = LottieScheduler.MaxUploadsPerFrame;
            for (int i = 0; i < budget; i++)
            {
                // Vulkan configures this stable event ID to execute outside a
                // render pass. The callback dequeues one upload per event.
                GL.IssuePluginEvent(sRenderEventFunc, 1);
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
#endif
}

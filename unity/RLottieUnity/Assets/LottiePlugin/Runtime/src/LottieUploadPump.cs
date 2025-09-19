using System;
using UnityEngine;

namespace LottiePlugin
{
#if !(UNITY_WEBGL && !UNITY_EDITOR)
    internal sealed class LottieUploadPump : MonoBehaviour
    {
        private static LottieUploadPump sInstance;
        private static IntPtr sRenderEventFunc = IntPtr.Zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
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
                sRenderEventFunc = NativeBridge.LpGetRenderEventFunc();
            }
        }

        private void LateUpdate()
        {
            if (sRenderEventFunc != IntPtr.Zero)
            {
                GL.IssuePluginEvent(sRenderEventFunc, 0);
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

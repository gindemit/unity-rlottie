// Scheduling-only harness: compiles the actual pump against Unity/native doubles.
// This does not execute GPU uploads or replace Unity/backend acceptance.
using System;
using System.Reflection;
using LottiePlugin;

static class Program
{
    private static readonly MethodInfo LateUpdate = typeof(LottieUploadPump).GetMethod(
        "LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void Expect(bool condition, string message)
    {
        if (!condition) { throw new Exception(message); }
    }

    private static void Tick(LottieUploadPump pump, int pending, int expectedEvents)
    {
        NativeBridge.Pending = pending;
        UnityEngine.GL.Events = 0;
        LateUpdate.Invoke(pump, null);
        Expect(UnityEngine.GL.Events == expectedEvents, "unexpected callback count");
    }

    static void Main()
    {
        typeof(LottieUploadPump).GetField("sRenderEventFunc", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, new IntPtr(1));
        var pump = new LottieUploadPump();
        LottieScheduler.MaxUploadsPerFrame = 128;
        Tick(pump, 0, 0);
        Tick(pump, 3, 3);
        Tick(pump, 130, 128);
        Tick(pump, 1, 1);
        // A producer publishes after an empty poll; next frame polls again.
        Tick(pump, 0, 0);
        Tick(pump, 1, 1);
        // Retrying work remains pending and is serviced on the next frame.
        Tick(pump, 1, 1);
        Tick(pump, 0, 0);
        // Publication after a snapshot must not be consumed by that poll or
        // require a managed wakeup: the following tick discovers it directly.
        NativeBridge.PublishAfterQuery = 2;
        Tick(pump, 0, 0);
        Expect(NativeBridge.Pending == 2, "later publication must remain pending");
        UnityEngine.GL.Events = 0;
        LateUpdate.Invoke(pump, null);
        Expect(UnityEngine.GL.Events == 2, "next tick must discover later publication");
        int queries = NativeBridge.Queries;
        LottieScheduler.MaxUploadsPerFrame = 0;
        Tick(pump, 3, 0);
        LottieScheduler.MaxUploadsPerFrame = -1;
        Tick(pump, 3, 0);
        Expect(NativeBridge.Queries == queries, "disabled pump must not query the queue");
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
        LottieScheduler.MaxUploadsPerFrame = 128;
        LottieWebGLNativeStateTrace.Selected = true;
        Tick(pump, 3, 3);
        Expect(LottieWebGLNativeStateTrace.Issued == 3, "trace metadata must match submitted events");
        Tick(pump, 0, 0);
        Expect(LottieWebGLNativeStateTrace.Issued == 3, "empty snapshot must not reserve trace events");
#endif
        Console.WriteLine("Upload pump scheduling tests passed.");
    }
}

namespace LottiePlugin
{
    internal static class NativeBridge
    {
        public static int Pending;
        public static int Queries = 0;
        public static int PublishAfterQuery = -1;
        public static IntPtr LottieGetRenderEventFunc() => new IntPtr(1);
        public static IntPtr LottieGetWebGLRenderEventFunc() => new IntPtr(1);
#if UNITY_WEBGL && !UNITY_EDITOR
        public static int LottieGetWebGLPendingUploadCount()
#else
        public static int LottieGetPendingUploadCount()
#endif
        {
            Queries++;
            int snapshot = Pending;
            if (PublishAfterQuery >= 0)
            {
                Pending = PublishAfterQuery;
                PublishAfterQuery = -1;
            }
            return snapshot;
        }
    }
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
    internal static class LottieWebGLNativeStateTrace
    {
        public static bool Selected;
        public static int Issued;
        public static bool EnsureInitialized() => Selected;
        public static int IssueEvent() => 1 + ++Issued;
    }
#endif
    internal static class LottieScheduler { public static int MaxUploadsPerFrame; }
    internal sealed class LottieSchedulerReset : UnityEngine.MonoBehaviour { }
}

namespace UnityEngine
{
    public class MonoBehaviour
    {
        public GameObject gameObject;
        protected static void DestroyImmediate(GameObject value) { }
        protected static void DontDestroyOnLoad(GameObject value) { }
    }
    public sealed class GameObject
    {
        public HideFlags hideFlags;
        public GameObject(string name) { }
        public T AddComponent<T>() where T : new() => new T();
    }
    public enum HideFlags { HideAndDontSave }
    public enum RuntimeInitializeLoadType { BeforeSceneLoad }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    public static class GL
    {
        public static int Events;
        public static void IssuePluginEvent(IntPtr callback, int eventId)
        {
            bool validEvent = eventId == 1;
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
            if (LottieWebGLNativeStateTrace.Selected) validEvent = eventId > 1;
#endif
            if (callback == IntPtr.Zero || !validEvent) { throw new Exception("invalid render event"); }
            Events++;
        }
    }
}

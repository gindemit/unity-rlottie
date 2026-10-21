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
#if UNITY_WEBGL && !UNITY_EDITOR
        Tick(pump, 0, 128);
        Expect(NativeBridge.Queries == 0, "WebGL players must not invoke the native queue query");
#else
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
        int queries = NativeBridge.Queries;
        LottieScheduler.MaxUploadsPerFrame = 0;
        Tick(pump, 3, 0);
        LottieScheduler.MaxUploadsPerFrame = -1;
        Tick(pump, 3, 0);
        Expect(NativeBridge.Queries == queries, "disabled pump must not query the queue");
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
        public static IntPtr LottieGetRenderEventFunc() => new IntPtr(1);
        public static IntPtr LottieGetWebGLRenderEventFunc() => new IntPtr(1);
#if !UNITY_WEBGL || UNITY_EDITOR
        public static int LottieGetPendingUploadCount()
        {
            Queries++;
            return Pending;
        }
#endif
    }
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
            if (callback == IntPtr.Zero || eventId != 1) { throw new Exception("invalid render event"); }
            Events++;
        }
    }
}

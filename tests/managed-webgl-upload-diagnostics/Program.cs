using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LottiePlugin;
using UnityEngine;

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { BeforeSceneLoad }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public readonly RuntimeInitializeLoadType Kind;
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType kind) { Kind = kind; }
    }
    public static class Application
    {
        public static int MainThread, UrlReads, GuidReads;
        private static string url = "";
        public static void SetUrl(string value) { url = value; UrlReads = GuidReads = 0; }
        private static void Check() { if (Environment.CurrentManagedThreadId != MainThread) throw new Exception("Stub Unity API called off main thread"); }
        public static string absoluteURL { get { Check(); UrlReads++; return url; } }
        public static string buildGUID { get { Check(); GuidReads++; return "HOST-STUB-GUID"; } }
    }
    public static class Time { public static int frameCount; }
    public sealed class Texture2D
    {
        public int width = 32, height = 48, Applies;
        public bool ThrowNextApply;
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable)
        {
            Applies++;
            if (ThrowNextApply) { ThrowNextApply = false; throw new InvalidOperationException("Stub Apply failure"); }
        }
    }
    public static class JsonUtility
    {
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(),
            new JsonSerializerOptions { IncludeFields = true });
    }
}
namespace LottiePlugin
{
    internal static class NativeBridge
    {
        internal static int Available = 1, RequestResult = 1, Checks, Requests;
        internal static int LottieIsWebGLUploadAvailable(IntPtr animation) { Checks++; return Available; }
        internal static int LottieRequestWebGLTextureUpload(IntPtr animation, IntPtr data) { Requests++; return RequestResult; }
    }
    internal static class LottieUploadPump
    {
        internal static int Ensures;
        internal static void EnsureInstance() { Ensures++; }
    }
    public sealed partial class LottieAnimation
    {
        public Texture2D Texture = new Texture2D();
        public LottieTextureUploadBackend TextureUploadBackend = LottieTextureUploadBackend.NativeWebGL;
        private bool _webGLReregisterAttempted;
        private IntPtr _animationWrapperIntPtr = new IntPtr(1), _lottieRenderDataIntPtr = new IntPtr(2);
        public bool RegisterResult = true;
        public int Registers, Fallbacks;
        private bool TryRegisterUnityOwnedWebGLTexture(uint width, uint height) { Registers++; return RegisterResult; }
        private void UseWebGLManagedTextureUploadFallback(string reason)
        { Fallbacks++; TextureUploadBackend = LottieTextureUploadBackend.WebGLManagedTextureUpload; }
        public static void HostInitialize() => InitializeWebGLUploadDiagnostics();
        public void HostRequest() => RequestTextureUpload();
    }
}
internal static class Program
{
    private static readonly List<string> Passed = new List<string>();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static JsonElement Report() => JsonDocument.Parse(LottieAnimation.GetWebGLUploadDiagnosticsJson()).RootElement.Clone();
    private static int N(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static void Reset(string url = "http://127.0.0.1:12345/?lottieUploadDiagnostics=1")
    {
        Application.SetUrl(url); LottieAnimation.HostInitialize(); Time.frameCount = 10;
        NativeBridge.Available = 1; NativeBridge.RequestResult = 1; NativeBridge.Checks = NativeBridge.Requests = 0;
        LottieUploadPump.Ensures = 0;
    }
    private static void Test(string name, Action body) { body(); Passed.Add(name); Console.WriteLine("PASS " + name); }
    private static void Counts(JsonElement r, params (string Name, int Value)[] pairs)
    { foreach (var p in pairs) Check(N(r, p.Name) == p.Value, p.Name + " expected " + p.Value + " got " + N(r, p.Name)); }
    private static int Main()
    {
        try
        {
            Application.MainThread = Environment.CurrentManagedThreadId;
            Test("First background type use calls no stub Unity API", () =>
            {
                string result = Task.Run(() => { var a = new LottieAnimation(); return LottieAnimation.GetWebGLUploadDiagnosticsJson(); }).GetAwaiter().GetResult();
                Check(result == "{\"enabled\":false}", "Pre-startup export must be disabled");
                Check(Application.UrlReads == 0 && Application.GuidReads == 0, "Type initialization accessed Unity API");
            });
            var gates = new (string Name, string Url, bool Enabled)[] {
                ("missing", "http://127.0.0.1/", false),
                ("wrong-value", "http://127.0.0.1/?lottieUploadDiagnostics=0", false),
                ("wrong-word", "http://127.0.0.1/?lottieUploadDiagnostics=true", false),
                ("remote", "https://example.com/?lottieUploadDiagnostics=1", false),
                ("file", "file:///C:/test.html?lottieUploadDiagnostics=1", false),
                ("invalid", "not a URL", false),
                ("localhost-http", "http://localhost:123/?lottieUploadDiagnostics=1", true),
                ("loopback-https", "https://127.0.0.1/?lottieUploadDiagnostics=1", true),
                ("ipv6-loopback", "http://[::1]:123/?lottieUploadDiagnostics=1", true),
                ("unique-with-other", "http://127.0.0.1/?a=x&lottieUploadDiagnostics=1&b=y", true),
                ("substring-key", "http://127.0.0.1/?xlottieUploadDiagnostics=1", false),
                ("substring-value", "http://127.0.0.1/?x=lottieUploadDiagnostics=1", false),
                ("fragment", "http://127.0.0.1/#lottieUploadDiagnostics=1", false),
                ("duplicate-same", "http://127.0.0.1/?lottieUploadDiagnostics=1&lottieUploadDiagnostics=1", false),
                ("conflict-first", "http://127.0.0.1/?lottieUploadDiagnostics=0&lottieUploadDiagnostics=1", false),
                ("conflict-last", "http://127.0.0.1/?lottieUploadDiagnostics=1&lottieUploadDiagnostics=0", false),
                ("encoded-duplicate", "http://127.0.0.1/?lottieUploadDiagnostics=1&%6CottieUploadDiagnostics=1", false),
                ("encoded-conflict-first", "http://127.0.0.1/?%6CottieUploadDiagnostics=0&lottieUploadDiagnostics=1", false),
                ("encoded-only", "http://127.0.0.1/?%6CottieUploadDiagnostics=1", false),
                ("missing-value-duplicate", "http://127.0.0.1/?lottieUploadDiagnostics=1&lottieUploadDiagnostics", false),
                ("extra-equals", "http://127.0.0.1/?lottieUploadDiagnostics=1=0", false),
                ("empty", "", false)
            };
            foreach (var gate in gates) Test("Gate " + gate.Name, () =>
            {
                Reset(gate.Url); var r = Report();
                Check(r.GetProperty("enabled").GetBoolean() == gate.Enabled, gate.Name);
                Check(Application.UrlReads == 1 && Application.GuidReads == (gate.Enabled ? 1 : 0), "Gate Unity API reads");
            });
            Test("Zero episodes and exporter is read-only", () =>
            {
                Reset(); var a = new LottieAnimation(); a.HostRequest();
                string before = LottieAnimation.GetWebGLUploadDiagnosticsJson();
                var r = Report(); Counts(r, ("uploadChecks",1),("availableZero",0),("eventCount",0),("eventsDropped",0));
                Check(before == LottieAnimation.GetWebGLUploadDiagnosticsJson(), "Export mutated report");
                Check(r.GetProperty("events").GetArrayLength() == 8, "Record storage must remain fixed");
            });
            Test("One silent successful recovery", () =>
            {
                Reset(); NativeBridge.Available = 0; var a = new LottieAnimation(); a.HostRequest(); var r = Report();
                Counts(r, ("uploadChecks",1),("availableZero",1),("recoveryApplyCalls",1),("reregisterAttempts",1),
                    ("reregisterSuccesses",1),("reregisterFailures",0),("fallbacks",0),("eventCount",1),("instancesWithEvents",1));
                Check(a.Texture.Applies == 1 && a.Registers == 1 && a.Fallbacks == 0, "Stub effectors differ from recorded recovery");
                Counts(r.GetProperty("events")[0], ("frame",10),("width",32),("height",48),("available",0),("applyCalls",1));
            });
            Test("Apply failure does not count unattempted registration", () =>
            {
                Reset(); NativeBridge.Available = 0; var a = new LottieAnimation(); a.Texture.ThrowNextApply = true;
                try { a.HostRequest(); throw new Exception("Expected stub Apply exception"); }
                catch (InvalidOperationException) { }
                Counts(Report(), ("availableZero",1),("recoveryApplyCalls",1),("reregisterAttempts",0),("reregisterSuccesses",0),("fallbacks",0));
                Check(a.Registers == 0, "Register was called after throwing Apply");
            });
            Test("False registration records fallback and two Apply calls", () =>
            {
                Reset(); NativeBridge.Available = 0; var a = new LottieAnimation { RegisterResult = false }; a.HostRequest(); var r = Report();
                Counts(r, ("reregisterAttempts",1),("reregisterFailures",1),("reregisterSuccesses",0),("fallbacks",1),("recoveryApplyCalls",2));
                Check(a.Texture.Applies == 2 && a.Fallbacks == 1, "False registration stub effects");
                Check(N(r.GetProperty("events")[0], "backend") == (int)LottieTextureUploadBackend.WebGLManagedTextureUpload, "Fallback backend observation");
            });
            Test("Repeated unavailable after retry falls back without second registration", () =>
            {
                Reset(); NativeBridge.Available = 0; var a = new LottieAnimation(); a.HostRequest(); a.HostRequest(); var r = Report();
                Counts(r, ("availableZero",2),("reregisterAttempts",1),("reregisterSuccesses",1),("fallbacks",1),("recoveryApplyCalls",2),("eventCount",2));
                Check(r.GetProperty("events")[1].GetProperty("alreadyRetried").GetBoolean(), "Second episode did not preserve retry flag");
            });
            Test("Rejected upload records managed fallback without registration", () =>
            {
                Reset(); NativeBridge.RequestResult = 0; var a = new LottieAnimation(); a.HostRequest();
                Counts(Report(), ("uploadChecks",1),("requestRejections",1),("availableZero",0),("reregisterAttempts",0),("fallbacks",1),("recoveryApplyCalls",1));
                Check(a.Registers == 0 && NativeBridge.Requests == 1, "Rejected request call sequence");
            });
            foreach (int count in new[] { 8, 11 }) Test(count + " distinct-instance episodes stay bounded", () =>
            {
                Reset(); NativeBridge.Available = 0;
                for (int i = 0; i < count; i++) { Time.frameCount++; new LottieAnimation().HostRequest(); }
                var r = Report();
                Counts(r, ("eventCount",Math.Min(8,count)),("eventsDropped",Math.Max(0,count-8)),("instancesWithEvents",count),
                    ("uploadChecks",count),("availableZero",count),("reregisterAttempts",count),("reregisterSuccesses",count),("recoveryApplyCalls",count));
                for (int i=0;i<8;i++) Check(N(r.GetProperty("events")[i],"instanceId")==i+1,"Recorded IDs");
            });
            Test("Eleven episodes on one instance retain one observed instance", () =>
            {
                Reset(); var a = new LottieAnimation();
                for (int i=0;i<11;i++) { NativeBridge.Available=0;a.HostRequest();NativeBridge.Available=1;a.HostRequest(); }
                Counts(Report(), ("eventCount",8),("eventsDropped",3),("instancesWithEvents",1),("uploadChecks",22),("availableZero",11));
            });
            Test("Gate off preserves existing recovery effects and avoids later Unity URL reads", () =>
            {
                Reset("http://127.0.0.1/"); NativeBridge.Available=0;
                var a=new LottieAnimation();a.HostRequest();a.HostRequest();
                Check(a.Texture.Applies==2 && a.Registers==1 && a.Fallbacks==1,"Disabled gate changed recovery");
                Check(Application.UrlReads==1 && Application.GuidReads==0,"Per-upload Unity URL/Guid access");
                Check(!Report().GetProperty("enabled").GetBoolean(),"Off gate enabled itself");
            });
            Console.WriteLine("RESULT " + JsonSerializer.Serialize(new { passed=Passed.Count,failed=0,
                runtime=Environment.Version.ToString(), scope="Actual source-sliced C# gate/tracker/request method; Unity/native effectors are host stubs", tests=Passed }));
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
    }
}

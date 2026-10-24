#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RLottie
{
    /// <summary>Explicit diagnostic build entry. Ordinary builds never select external archives.</summary>
    public static class LottieWebGLDiagnosticBuild
    {
        internal const string Define = "LOTTIE_WEBGL_NATIVE_STATE_TRACE";
        internal const string ManifestOption = "-lottieWebGLNativeStateTraceManifest";
        internal const string HashOption = "-lottieWebGLNativeStateTraceManifestSha256";
        private static bool sBuilding;
        private static DiagnosticStagingTransaction sTransaction;
        internal static bool IsActive { get { return sTransaction != null; } }

        public static BuildReport BuildPlayer(BuildPlayerOptions options, string manifestPath, string expectedManifestSha256)
        {
            if (sBuilding || BuildPipeline.isBuildingPlayer) throw new BuildFailedException("Diagnostic builds cannot be nested.");
            sBuilding = true;
            BuildReport report = null;
            Exception buildError = null;
            DiagnosticStagingTransaction transaction = null;
            try
            {
                options.scenes = options.scenes == null ? null : (string[])options.scenes.Clone();
                options.extraScriptingDefines = options.extraScriptingDefines == null ? null : (string[])options.extraScriptingDefines.Clone();
                ValidateOptions(options, PlayerSettings.WebGL.threadsSupport,
                    PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL));
                ValidateGraphicsApis(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL),
                    PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL));
                ValidateCommandLine(Environment.GetCommandLineArgs(), manifestPath, expectedManifestSha256, true);
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var manifest = LottieWebGLDiagnosticManifest.Load(manifestPath, expectedManifestSha256,
                    project, LottieWebGLArchiveStager.GetPackagePath(), EditorApplication.applicationPath);
                DiagnosticPaths.External(options.locationPathName, project,
                    LottieWebGLArchiveStager.GetPackagePath(), System.IO.Path.GetDirectoryName(EditorApplication.applicationPath));
                DiagnosticPaths.External(options.locationPathName, project, manifest.ProtectedRoots.ToArray());
                if (DiagnosticPaths.Overlaps(options.locationPathName, manifest.Root))
                    throw new BuildFailedException("Build output overlaps immutable diagnostic inputs.");
                if (File.Exists(options.locationPathName) ||
                    (Directory.Exists(options.locationPathName) && Directory.EnumerateFileSystemEntries(options.locationPathName).Any()))
                    throw new BuildFailedException("Diagnostic build output must be fresh.");
                // Constructor validates and backs up prior ownership before changing any project bytes.
                transaction = new DiagnosticStagingTransaction(project, manifest);
                sTransaction = transaction;
                report = BuildPipeline.BuildPlayer(options);
                return report;
            }
            catch (Exception exception)
            {
                buildError = exception;
                throw;
            }
            finally
            {
                try
                {
                    if (transaction != null)
                    {
                        try { transaction.Restore(); }
                        catch (Exception restoreError)
                        {
                            if (buildError != null) throw new AggregateException("Build and diagnostic staging restoration failed.", buildError, restoreError);
                            throw;
                        }
                        transaction.WriteBuildReceipt(report, buildError);
                    }
                }
                finally { sTransaction = null; sBuilding = false; }
            }
        }

        internal static void ValidateOptions(BuildPlayerOptions options, bool threads, string persistentDefines)
        {
            if (options.target != BuildTarget.WebGL || (options.options & BuildOptions.Development) == 0 || threads)
                throw new BuildFailedException("Native trace requires single-thread WebGL Development.");
            if (SplitDefines(persistentDefines).Contains(Define))
                throw new BuildFailedException("The native trace define must not be persistent.");
            if (options.extraScriptingDefines == null || options.extraScriptingDefines.Count(x => x == Define) != 1 ||
                options.extraScriptingDefines.Any(x => x != null && x != Define && SplitDefines(x).Contains(Define)))
                throw new BuildFailedException("Supply exactly one native trace extra scripting define.");
        }

        private static string[] SplitDefines(string value) { return (value ?? "").Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries); }

        internal static void ValidateGraphicsApis(bool automatic, UnityEngine.Rendering.GraphicsDeviceType[] apis)
        {
            if (automatic || apis == null || apis.Length != 1 || apis[0] != UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3)
                throw new BuildFailedException("Native trace requires an explicit WebGL2 graphics API selection.");
        }

        internal static void ValidateCommandLine(string[] args, string manifest, string hash, bool active)
        {
            string pathValue = null, hashValue = null;
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (key.StartsWith(ManifestOption + "=", StringComparison.OrdinalIgnoreCase) || key.StartsWith(HashOption + "=", StringComparison.OrdinalIgnoreCase))
                    throw new BuildFailedException("Diagnostic options require separate exact option/value arguments.");
                if (!string.Equals(key, ManifestOption, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(key, HashOption, StringComparison.OrdinalIgnoreCase)) continue;
                if (key != ManifestOption && key != HashOption) throw new BuildFailedException("Use exact diagnostic option names.");
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new BuildFailedException("Missing diagnostic option value.");
                string value = args[++i];
                if (key == ManifestOption)
                {
                    if (pathValue != null) throw new BuildFailedException("Duplicate diagnostic manifest option.");
                    pathValue = value;
                }
                else
                {
                    if (hashValue != null) throw new BuildFailedException("Duplicate diagnostic hash option.");
                    hashValue = value;
                }
            }
            if ((pathValue == null) != (hashValue == null)) throw new BuildFailedException("Diagnostic manifest and hash must be paired.");
            if (pathValue == null) return;
            if (!active || !string.Equals(Path.GetFullPath(pathValue), Path.GetFullPath(manifest), StringComparison.OrdinalIgnoreCase) ||
                hashValue != hash) throw new BuildFailedException("Diagnostic CLI selection requires its exact validated build wrapper.");
        }

        internal static bool Preprocess(BuildReport report)
        {
            string persistent = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            if (SplitDefines(persistent).Contains(Define)) throw new BuildFailedException("Persistent native trace definition is unsupported.");
            if (sTransaction == null)
            {
                ValidateCommandLine(Environment.GetCommandLineArgs(), null, null, false);
                return false;
            }
            if (report.summary.platform != BuildTarget.WebGL || (report.summary.options & BuildOptions.Development) == 0 ||
                PlayerSettings.WebGL.threadsSupport) throw new BuildFailedException("Build report disagrees with validated diagnostic options.");
            ValidateGraphicsApis(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL),
                PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL));
            sTransaction.Stage();
            return true;
        }
    }

    internal sealed class LottieWebGLDiagnosticManifest
    {
        internal const string PluginHash = "088d14b0909acdc6076e1ff1525e8a6d69094d284d4151fc03b6a6ee75504a51";
        internal const string RlottieHash = "b17cda443208f9c746a6610e8edee79257684fdd8ae39f93f8df3a1263d908da";
        internal static readonly string[] AddedSymbols = {
            "lottie_webgl_trace_arm", "lottie_webgl_trace_copy", "lottie_webgl_trace_issue",
            "lottie_webgl_trace_register", "lottie_webgl_trace_request"
        };
        private static readonly string[] EmptySymbolMembers = {
            "InstanceRegistry.cpp.o", "UploadQueue.cpp.o", "UploadPipeline.cpp.o", "TextureBackend.cpp.o", "VulkanBackend.cpp.o",
            "UnityIntegration.cpp.o", "UnityExports.cpp.o", "lottieproxymodel.cpp.o", "vdebug.cpp.o", "vdrawhelper_neon.cpp.o", "vdrawhelper_sse2.cpp.o"
        };
        internal string Root, Path, Hash, Plugin, Rlottie, SourceCommit;
        internal readonly Dictionary<string, string> VerifiedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> ProtectedRoots = new List<string>();

        internal static LottieWebGLDiagnosticManifest Load(string path, string expectedHash, string project, string package, string editor)
        {
            DiagnosticPaths.External(path, project, package, System.IO.Path.GetDirectoryName(editor));
            if (!DiagnosticJson.IsHex(expectedHash, 64) || DiagnosticPaths.Hash(path) != expectedHash)
                throw new BuildFailedException("Diagnostic manifest does not match the trusted SHA-256.");
            var data = DiagnosticJson.Object(DiagnosticJson.Parse(File.ReadAllBytes(path)));
            DiagnosticJson.Keys(data, "schema", "abiVersion", "headerBytes", "recordBytes", "capacity", "nativeDefine", "managedDefine",
                "target", "variant", "configuration", "threading", "sourceCommit", "rlottieCommit", "unityPluginApiHeaders", "sourceInputs",
                "toolchain", "compilerFlags", "plugin", "rlottie", "baselinePluginSha256", "baselineRlottieSha256", "exports", "buildReceipt", "exportsReceipt");
            DiagnosticJson.Equal(data, "schema", "lottie.webgl.native-state-trace/1");
            DiagnosticJson.Equal(data, "abiVersion", 1L); DiagnosticJson.Equal(data, "headerBytes", 128L);
            DiagnosticJson.Equal(data, "recordBytes", 192L); DiagnosticJson.Equal(data, "capacity", 64L);
            foreach (string key in new[] { "nativeDefine", "managedDefine" }) DiagnosticJson.Equal(data, key, LottieWebGLDiagnosticBuild.Define);
            DiagnosticJson.Equal(data, "target", "WebGL"); DiagnosticJson.Equal(data, "variant", "WasmExceptions");
            DiagnosticJson.Equal(data, "configuration", "Development"); DiagnosticJson.Equal(data, "threading", "single-main-thread");
            DiagnosticJson.Equal(data, "baselinePluginSha256", PluginHash); DiagnosticJson.Equal(data, "baselineRlottieSha256", RlottieHash);
            string commit = DiagnosticJson.Hex(data, "sourceCommit", 40);
            DiagnosticJson.Hex(data, "rlottieCommit", 40);
            var pluginApi = DiagnosticJson.Object(data["unityPluginApiHeaders"]);
            DiagnosticJson.Keys(pluginApi, "ownerCommit", "files"); DiagnosticJson.Equal(pluginApi, "ownerCommit", commit);
            var sourceInputs = ValidateInputs(data["sourceInputs"]);
            var headers = ValidateInputs(pluginApi["files"]);
            foreach (var entry in headers)
                if (!entry.Key.StartsWith("dependency/unity-pluginapi/", StringComparison.Ordinal) ||
                    !sourceInputs.ContainsKey(entry.Key) || sourceInputs[entry.Key] != entry.Value)
                    throw new BuildFailedException("Unity PluginAPI header inventory must match actual source inputs.");
            var flags = DiagnosticJson.Strings(data["compilerFlags"]);
            foreach (string flag in new[] { "-fwasm-exceptions", "-sSUPPORT_LONGJMP=wasm", "-sWASM_LEGACY_EXCEPTIONS=1" })
                if (!flags.Contains(flag)) throw new BuildFailedException("Missing required compiler ABI flag.");
            var result = new LottieWebGLDiagnosticManifest { Root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),
                Path = System.IO.Path.GetFullPath(path), Hash = expectedHash, SourceCommit = commit };
            result.VerifiedFiles.Add(result.Path, expectedHash);
            result.Plugin = result.Descriptor(data["plugin"], project, "WasmExceptions/libLottiePlugin.trace.a");
            result.Rlottie = result.Descriptor(data["rlottie"], project, "WasmExceptions/librlottie.baseline.a");
            if (DiagnosticPaths.Hash(result.Rlottie) != RlottieHash) throw new BuildFailedException("The immutable baseline rlottie archive is required.");
            string buildReceipt = result.Descriptor(data["buildReceipt"], project, "webgl-native-state-trace.native-build-receipt.json");
            string exportsReceipt = result.Descriptor(data["exportsReceipt"], project, "webgl-native-state-trace.exports.json");
            var toolchain = DiagnosticJson.Object(data["toolchain"]);
            DiagnosticJson.Keys(toolchain, "unityVersion", "emscriptenVersion", "cmakeVersion", "ninjaVersion", "emcc", "emar", "llvmNm",
                "cmake", "ninja", "toolchainFile", "configurationReceipt");
            DiagnosticJson.Equal(toolchain, "unityVersion", "6000.5.3f1");
            DiagnosticJson.Equal(toolchain, "emscriptenVersion", "4.0.20-git");
            if (Application.unityVersion != "6000.5.3f1") throw new BuildFailedException("This trace ABI is qualified only for Unity6000.5.3f1.");
            DiagnosticJson.Text(toolchain, "cmakeVersion"); DiagnosticJson.Text(toolchain, "ninjaVersion");
            string unityRoot = System.IO.Path.GetDirectoryName(editor);
            string llvmNm = null;
            foreach (string key in new[] { "emcc", "emar", "llvmNm", "cmake", "ninja", "toolchainFile" })
            {
                string tool = result.ToolDescriptor(toolchain[key], unityRoot, key);
                if (key == "llvmNm") llvmNm = tool;
            }
            string configurationReceipt = result.Descriptor(toolchain["configurationReceipt"], project, "webgl-native-state-trace.configuration.json");
            // The selected package must be the actual immutable Git pin, not merely a caller label.
            var packageLock = DiagnosticJson.Object(DiagnosticJson.Parse(File.ReadAllBytes(System.IO.Path.Combine(project, "Packages", "packages-lock.json"))));
            var dependencies = DiagnosticJson.Object(packageLock["dependencies"]);
            var renderer = DiagnosticJson.Object(dependencies["com.gindemit.rlottie"]);
            DiagnosticJson.Equal(renderer, "hash", commit);
            string baselinePlugin = System.IO.Path.Combine(package, "Editor", "WebGLArchives", "WasmExceptions", "libLottiePlugin.a.bytes");
            string baselineRlottie = System.IO.Path.Combine(package, "Editor", "WebGLArchives", "WasmExceptions", "librlottie.a.bytes");
            DiagnosticPaths.NoLinks(baselinePlugin); DiagnosticPaths.NoLinks(baselineRlottie);
            if (DiagnosticPaths.Hash(baselinePlugin) != PluginHash || DiagnosticPaths.Hash(baselineRlottie) != RlottieHash)
                throw new BuildFailedException("Resolved production native baseline changed.");
            result.VerifiedFiles.Add(baselinePlugin, PluginHash); result.VerifiedFiles.Add(baselineRlottie, RlottieHash);
            result.ValidatePackageInputs(data["sourceInputs"], package);
            var exports = DiagnosticJson.Object(data["exports"]);
            DiagnosticJson.Keys(exports, "added", "baselineInventorySha256", "diagnosticInventorySha256");
            if (!DiagnosticJson.Strings(exports["added"]).SequenceEqual(AddedSymbols)) throw new BuildFailedException("Unexpected native trace export set.");
            string baselineSymbols = ReadSymbols(llvmNm, baselinePlugin);
            string diagnosticSymbols = ReadSymbols(llvmNm, result.Plugin);
            if (DiagnosticJson.Hex(exports, "baselineInventorySha256", 64) != DiagnosticPaths.HashBytes(Encoding.UTF8.GetBytes(baselineSymbols)) ||
                DiagnosticJson.Hex(exports, "diagnosticInventorySha256", 64) != DiagnosticPaths.HashBytes(Encoding.UTF8.GetBytes(diagnosticSymbols)))
                throw new BuildFailedException("Actual native symbol inventory differs from the trusted manifest.");
            var baselineSet = new HashSet<string>(baselineSymbols.Split('\n').Where(x => x.Length != 0), StringComparer.Ordinal);
            var diagnosticSet = new HashSet<string>(diagnosticSymbols.Split('\n').Where(x => x.Length != 0), StringComparer.Ordinal);
            if (!baselineSet.IsSubsetOf(diagnosticSet) || baselineSet.Any(x => x.StartsWith("lottie_webgl_trace_", StringComparison.Ordinal)) ||
                !diagnosticSet.Where(x => x.StartsWith("lottie_webgl_trace_", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(AddedSymbols))
                throw new BuildFailedException("Ordinary symbols were lost or native trace symbols differ.");
            result.ValidateReceipts(data, project, unityRoot, configurationReceipt, buildReceipt, exportsReceipt, baselinePlugin, baselineRlottie, diagnosticSet);
            result.Revalidate();
            return result;
        }

        private void ValidatePackageInputs(object inputs, string package)
        {
            const string prefix = "unity/RLottieUnity/Assets/LottiePlugin/";
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in DiagnosticJson.Array(inputs))
            {
                var item = DiagnosticJson.Object(value); string relative = DiagnosticJson.Text(item, "path");
                if (!relative.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string suffix = relative.Substring(prefix.Length); DiagnosticPaths.Relative(suffix);
                string path = System.IO.Path.Combine(package, suffix); DiagnosticPaths.NoLinks(path);
                VerifyDescriptor(path, item, true); found.Add(suffix);
            }
            foreach (string name in new[] { "NativeBridge.cs", "LottieAnimation.Platform.cs", "LottieUploadPump.cs", "LottieWebGLNativeStateTrace.cs" })
                if (!found.Contains("Runtime/src/" + name)) throw new BuildFailedException("Compiled source inventory omits an adopted managed trace source.");
        }

        private void ValidateReceipts(Dictionary<string, object> manifest, string project, string unityRoot,
            string configPath, string buildPath, string exportPath, string baselinePlugin, string baselineRlottie, HashSet<string> diagnosticDefined)
        {
            var toolchain = DiagnosticJson.Object(manifest["toolchain"]);
            var configuration = ReadReceipt(configPath, "lottie.webgl.native-state-trace.configuration/1",
                "sourceCommit", "rlottieCommit", "sourceInputs", "compilerFlags", "tools", "unityVersion", "emscriptenVersion", "cmakeVersion", "ninjaVersion",
                "emConfig", "cacheRoot", "sysroot", "frozenCache", "compileCommands", "dependencies", "cmakeCache", "configure", "compiler", "python", "compilerWrapper");
            foreach (string key in new[] { "sourceCommit", "rlottieCommit", "sourceInputs", "compilerFlags" }) Same(configuration[key], manifest[key]);
            foreach (string key in new[] { "unityVersion", "emscriptenVersion", "cmakeVersion", "ninjaVersion" }) Same(configuration[key], toolchain[key]);
            var tools = DiagnosticJson.Object(configuration["tools"]);
            DiagnosticJson.Keys(tools, "emcc", "emar", "llvmNm", "cmake", "ninja", "toolchainFile");
            foreach (string key in tools.Keys) Same(tools[key], toolchain[key]);
            foreach (string key in new[] { "compiler", "python", "compilerWrapper" }) ToolDescriptor(configuration[key], unityRoot, key);
            VerifyAbsolute(configuration["emConfig"], unityRoot);
            foreach (string key in new[] { "cacheRoot", "sysroot" })
            {
                string path = DiagnosticJson.Text(configuration, key); DiagnosticPaths.Absolute(path); DiagnosticPaths.NoLinks(path);
                if (!DiagnosticPaths.Inside(path, unityRoot) || !Directory.Exists(path)) throw new BuildFailedException("Compiler root is outside the installed Unity toolchain.");
                ProtectRoot(path);
            }
            DiagnosticJson.Equal(configuration, "frozenCache", true);
            string commandsPath = Descriptor(configuration["compileCommands"], project, "webgl-native-state-trace.compile-commands.json");
            string dependenciesPath = Descriptor(configuration["dependencies"], project, "webgl-native-state-trace.dependencies.json");
            Descriptor(configuration["cmakeCache"], project, "webgl-native-state-trace.cmake-cache.txt");
            var commands = DiagnosticJson.Array(DiagnosticJson.Parse(File.ReadAllBytes(commandsPath)));
            if (commands.Count == 0) throw new BuildFailedException("No actual compile commands.");
            var pluginSources = commands.Select(x => DiagnosticJson.Text(DiagnosticJson.Object(x), "file").Replace('\\', '/'))
                .Where(x => x.EndsWith("/src/WebGLBackend.cpp", StringComparison.Ordinal)).ToArray();
            if (pluginSources.Length != 1) throw new BuildFailedException("Actual native WebGL source compilation is absent or ambiguous.");
            string pluginDirectory = pluginSources[0].Substring(0, pluginSources[0].Length - "WebGLBackend.cpp".Length);
            foreach (var value in commands)
            {
                var command = DiagnosticJson.Object(value);
                if (command.ContainsKey("output")) DiagnosticJson.Keys(command, "directory", "command", "file", "output");
                else DiagnosticJson.Keys(command, "directory", "command", "file");
                string buildDirectory = DiagnosticJson.Text(command, "directory"); DiagnosticPaths.External(buildDirectory, project, unityRoot);
                ProtectRoot(buildDirectory); DiagnosticPaths.Absolute(DiagnosticJson.Text(command, "file"));
                string file = DiagnosticJson.Text(command, "file").Replace('\\', '/');
                ValidateCompileCommand(DiagnosticJson.Text(command, "command"), file.StartsWith(pluginDirectory, StringComparison.OrdinalIgnoreCase));
                if (command.ContainsKey("output")) DiagnosticJson.Text(command, "output");
            }
            var dependencies = DiagnosticJson.Array(DiagnosticJson.Parse(File.ReadAllBytes(dependenciesPath)));
            if (dependencies.Count == 0) throw new BuildFailedException("Compiled dependencies are absent.");
            foreach (var value in dependencies)
            {
                VerifyAbsolute(value, null, true);
                string path = DiagnosticJson.Text(DiagnosticJson.Object(value), "path");
                for (string current = System.IO.Path.GetDirectoryName(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
                    if (Directory.Exists(System.IO.Path.Combine(current, ".git")) || File.Exists(System.IO.Path.Combine(current, ".git")))
                    { ProtectRoot(current); break; }
            }
            string cmake = DiagnosticJson.Text(DiagnosticJson.Object(toolchain["cmake"]), "path");
            string ninja = DiagnosticJson.Text(DiagnosticJson.Object(toolchain["ninja"]), "path");
            ValidateCommand(configuration["configure"], project, new[] { cmake }, false);

            var native = ReadReceipt(buildPath, "lottie.webgl.native-state-trace.native-build/1", "sourceCommit", "rlottieCommit", "configurationReceipt", "plugin", "rlottie", "emittedArchives", "build", "undefinedSymbols");
            foreach (string key in new[] { "sourceCommit", "rlottieCommit", "plugin", "rlottie" }) Same(native[key], manifest[key]);
            Same(native["configurationReceipt"], toolchain["configurationReceipt"]);
            var archives = DiagnosticJson.Array(native["emittedArchives"]);
            if (archives.Count == 0) throw new BuildFailedException("Native archive inventory is absent.");
            foreach (var value in archives)
            {
                var item = DiagnosticJson.Object(value); string path = DiagnosticJson.Text(item, "path");
                DiagnosticPaths.External(path, project, unityRoot);
                if (!path.EndsWith(".a", StringComparison.Ordinal)) throw new BuildFailedException("Unexpected emitted dependency archive.");
                VerifyAbsolute(value, null);
            }
            ValidateCommand(native["build"], project, new[] { cmake, ninja }, true);
            string llvmNm = DiagnosticJson.Text(DiagnosticJson.Object(toolchain["llvmNm"]), "path");
            var undefined = DiagnosticJson.Array(native["undefinedSymbols"]);
            if (undefined.Count != 3) throw new BuildFailedException("Undefined symbol coverage is incomplete.");
            var inventories = new List<string[]>();
            string[] archivePaths = { baselinePlugin, Plugin, baselineRlottie };
            for (int i = 0; i < 3; i++)
            {
                var run = DiagnosticJson.Object(undefined[i]);
                DiagnosticJson.Keys(run, "archiveSha256", "argv", "exit", "inventorySha256", "symbols");
                ValidateSymbolRun(run, llvmNm, archivePaths[i], "--undefined-only");
                string[] symbols = DiagnosticJson.Strings(run["symbols"]); ValidateSymbolInventory(symbols, DiagnosticJson.Hex(run, "inventorySha256", 64));
                string observed = ReadSymbols(llvmNm, archivePaths[i], "--undefined-only", true);
                if (DiagnosticPaths.HashBytes(Encoding.UTF8.GetBytes(observed)) != DiagnosticJson.Hex(run, "inventorySha256", 64))
                    throw new BuildFailedException("Actual undefined symbols differ from native receipt.");
                inventories.Add(symbols);
            }
            var permitted = new HashSet<string>(inventories[0].Concat(inventories[2]), StringComparer.Ordinal);
            permitted.UnionWith(new[] { "emscripten_webgl_get_current_context", "emscripten_webgl_get_context_attributes", "emscripten_is_webgl_context_lost", "emscripten_get_heap_size" });
            ValidateExternalDependencies(inventories[1], diagnosticDefined, permitted);
            var exports = ReadReceipt(exportPath, "lottie.webgl.native-state-trace.exports/1", "sourceCommit", "llvmNm", "added", "baselineInventorySha256", "diagnosticInventorySha256", "runs");
            Same(exports["sourceCommit"], manifest["sourceCommit"]); Same(exports["llvmNm"], toolchain["llvmNm"]);
            var expected = DiagnosticJson.Object(manifest["exports"]);
            foreach (string key in expected.Keys) Same(exports[key], expected[key]);
            var runs = DiagnosticJson.Array(exports["runs"]);
            if (runs.Count != 2) throw new BuildFailedException("Defined symbol coverage is incomplete.");
            for (int i = 0; i < 2; i++)
            {
                var run = DiagnosticJson.Object(runs[i]); DiagnosticJson.Keys(run, "archiveSha256", "argv", "exit", "inventorySha256");
                ValidateSymbolRun(run, llvmNm, archivePaths[i], "--defined-only");
                Same(run["inventorySha256"], expected[i == 0 ? "baselineInventorySha256" : "diagnosticInventorySha256"]);
            }
        }

        internal static void ValidateExternalDependencies(IEnumerable<string> rawUndefined, ISet<string> diagnosticDefined, ISet<string> permitted)
        {
            // llvm-nm reports undefined references per archive member. Another member
            // can define them; only unresolved archive-level names are dependencies.
            if (rawUndefined.Any(x => !diagnosticDefined.Contains(x) && !permitted.Contains(x)))
                throw new BuildFailedException("Diagnostic native archive adds an unreviewed external dependency.");
        }

        private void ProtectRoot(string root)
        {
            if (DiagnosticPaths.Overlaps(Root, root)) throw new BuildFailedException("Immutable payload overlaps a source/build/cache/toolchain root.");
            if (!ProtectedRoots.Contains(root, StringComparer.OrdinalIgnoreCase)) ProtectedRoots.Add(root);
        }

        internal static Dictionary<string, object> ReadReceipt(string path, string schema, params string[] fields)
        {
            var value = DiagnosticJson.Object(DiagnosticJson.Parse(File.ReadAllBytes(path)));
            DiagnosticJson.Keys(value, new[] { "schema" }.Concat(fields).ToArray()); DiagnosticJson.Equal(value, "schema", schema);
            return value;
        }

        internal static void Same(object left, object right)
        {
            var a = left as Dictionary<string, object>; var b = right as Dictionary<string, object>;
            if (a != null && b != null)
            {
                if (a.Count != b.Count || a.Keys.Any(x => !b.ContainsKey(x))) throw new BuildFailedException("Receipt identity differs from manifest.");
                foreach (string key in a.Keys) Same(a[key], b[key]); return;
            }
            var xList = left as List<object>; var yList = right as List<object>;
            if (xList != null && yList != null)
            {
                if (xList.Count != yList.Count) throw new BuildFailedException("Receipt inventory differs from manifest.");
                for (int i = 0; i < xList.Count; i++) Same(xList[i], yList[i]); return;
            }
            if (!Equals(left, right)) throw new BuildFailedException("Receipt value differs from manifest.");
        }

        private void VerifyAbsolute(object value, string requiredRoot, bool allowEmpty = false)
        {
            var item = DiagnosticJson.Object(value); DiagnosticJson.Keys(item, "path", "sha256", "bytes");
            string path = DiagnosticJson.Text(item, "path"); DiagnosticPaths.Absolute(path); DiagnosticPaths.NoLinks(path);
            if (!allowEmpty && DiagnosticJson.Size(item, "bytes") == 0) throw new BuildFailedException("Diagnostic archive/tool/receipt inputs must be nonempty.");
            if (requiredRoot != null && !DiagnosticPaths.Inside(path, requiredRoot)) throw new BuildFailedException("Receipt input escapes its approved root.");
            if (VerifiedFiles.ContainsKey(path))
            {
                if (VerifiedFiles[path] != DiagnosticJson.Hex(item, "sha256", 64) || new FileInfo(path).Length != DiagnosticJson.Size(item, "bytes"))
                    throw new BuildFailedException("Conflicting receipt input descriptor.");
            }
            else VerifyDescriptor(path, item, allowEmpty);
        }

        private void ValidateCommand(object value, string project, string[] executables, bool build)
        {
            var command = DiagnosticJson.Object(value); DiagnosticJson.Keys(command, "argv", "exit", "output");
            string[] args = DiagnosticJson.Strings(command["argv"]); DiagnosticJson.Equal(command, "exit", 0L);
            if (args.Length == 0 || !executables.Contains(args[0], StringComparer.OrdinalIgnoreCase))
                throw new BuildFailedException("Unexpected native tool invocation.");
            if (build) ValidateBuildCommand(args);
            Descriptor(command["output"], project, null);
        }

        internal static void ValidateBuildCommand(string[] args)
        {
            int target = System.Array.IndexOf(args, "--target");
            if (target < 0 || args.Count(x => x == "--target") != 1 || args.Any(x => x.StartsWith("--target=", StringComparison.Ordinal)) || target + 1 >= args.Length || args[target + 1] != "LottiePlugin" ||
                (target + 2 < args.Length && !args[target + 2].StartsWith("-", StringComparison.Ordinal)))
                throw new BuildFailedException("Native build must select only the named LottiePlugin target.");
        }

        internal static void ValidateCompileCommand(string command, bool plugin)
        {
            string[] args = CompileArguments(command);
            if (args.Any(x => x.IndexOf("pthread", StringComparison.OrdinalIgnoreCase) >= 0 || x.Contains("USE_PTHREADS")))
                throw new BuildFailedException("Threaded native compiler settings are unsupported.");
            if (plugin && (!args.Contains("-DLOTTIE_WEBGL_NATIVE_STATE_TRACE=1") ||
                new[] { "-fwasm-exceptions", "-sSUPPORT_LONGJMP=wasm", "-sWASM_LEGACY_EXCEPTIONS=1" }.Any(x => !args.Contains(x))))
                throw new BuildFailedException("Actual plugin compiler command omits trace or Wasm exception ABI flags.");
        }

        // Windows CRT quoting, matching the producing runner's CommandLineToArgvW
        // for ordinary compiler argv. This parser never executes command text.
        private static string[] CompileArguments(string command)
        {
            var result = new List<string>(); int index = 0;
            while (index < command.Length)
            {
                while (index < command.Length && char.IsWhiteSpace(command[index])) index++;
                if (index == command.Length) break;
                var argument = new StringBuilder(); bool quoted = false;
                while (index < command.Length && (quoted || !char.IsWhiteSpace(command[index])))
                {
                    int slashes = 0;
                    while (index < command.Length && command[index] == '\\') { slashes++; index++; }
                    if (index < command.Length && command[index] == '"')
                    {
                        argument.Append('\\', slashes / 2);
                        if ((slashes & 1) != 0) argument.Append('"'); else quoted = !quoted;
                        index++;
                    }
                    else
                    {
                        argument.Append('\\', slashes);
                        if (index < command.Length && !quoted && char.IsWhiteSpace(command[index])) break;
                        if (index < command.Length) argument.Append(command[index++]);
                    }
                }
                if (quoted) throw new BuildFailedException("Unterminated quoted compiler argument.");
                result.Add(argument.ToString());
            }
            if (result.Count == 0) throw new BuildFailedException("Empty compiler command.");
            return result.ToArray();
        }

        private static void ValidateSymbolRun(Dictionary<string, object> run, string llvmNm, string archive, string mode)
        {
            DiagnosticJson.Equal(run, "exit", 0L); DiagnosticJson.Equal(run, "archiveSha256", DiagnosticPaths.Hash(archive));
            string[] args = DiagnosticJson.Strings(run["argv"]);
            if (args.Length != 5 || !args.Take(4).SequenceEqual(new[] { llvmNm, mode, "--extern-only", "--format=posix" }))
                throw new BuildFailedException("Native symbol invocation does not match the installed tool and archive.");
            // The producing tool observed its original archive; the adopted package may
            // resolve the byte-identical immutable baseline at another absolute path.
            DiagnosticPaths.Absolute(args[4]); DiagnosticPaths.NoLinks(args[4]);
            if (DiagnosticPaths.Hash(args[4]) != DiagnosticPaths.Hash(archive)) throw new BuildFailedException("Native symbol receipt archive bytes changed.");
        }

        internal static void ValidateSymbolInventory(string[] symbols, string expectedHash)
        {
            if (!symbols.SequenceEqual(symbols.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) ||
                symbols.Any(x => x.Any(char.IsWhiteSpace)) ||
                DiagnosticPaths.HashBytes(Encoding.UTF8.GetBytes(symbols.Length == 0 ? "" : string.Join("\n", symbols) + "\n")) != expectedHash)
                throw new BuildFailedException("Invalid canonical native symbol inventory.");
        }

        internal static Dictionary<string, string> ValidateInputs(object value)
        {
            var entries = DiagnosticJson.Array(value);
            if (entries.Count == 0) throw new BuildFailedException("Input inventory is empty.");
            string prior = null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (object entry in entries)
            {
                var item = DiagnosticJson.Object(entry); DiagnosticJson.Keys(item, "path", "sha256", "bytes");
                string path = DiagnosticJson.Text(item, "path"); DiagnosticPaths.Relative(path);
                if (prior != null && string.CompareOrdinal(prior, path) >= 0) throw new BuildFailedException("Input inventory must be sorted and unique.");
                prior = path; DiagnosticJson.Size(item, "bytes");
                result.Add(path, DiagnosticJson.Hex(item, "sha256", 64));
            }
            return result;
        }

        private string Descriptor(object value, string project, string expectedRelative)
        {
            var item = DiagnosticJson.Object(value); DiagnosticJson.Keys(item, "path", "sha256", "bytes");
            string relative = DiagnosticJson.Text(item, "path"); DiagnosticPaths.Relative(relative);
            if (expectedRelative != null && relative != expectedRelative) throw new BuildFailedException("Unexpected diagnostic payload name.");
            if (expectedRelative == null && relative.Contains("/")) throw new BuildFailedException("Diagnostic command receipts must be sibling payload files.");
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
            DiagnosticPaths.External(path, project);
            if (!DiagnosticPaths.Inside(path, Root)) throw new BuildFailedException("Diagnostic payload escapes its immutable input root.");
            VerifyDescriptor(path, item);
            return path;
        }

        private string ToolDescriptor(object value, string unityRoot, string key)
        {
            var item = DiagnosticJson.Object(value); DiagnosticJson.Keys(item, "path", "sha256", "bytes");
            string path = DiagnosticJson.Text(item, "path"); DiagnosticPaths.Absolute(path); DiagnosticPaths.NoLinks(path);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            bool allowed = DiagnosticPaths.Inside(path, unityRoot);
            if (key == "cmake" || key == "ninja")
                allowed |= DiagnosticPaths.Inside(path, System.IO.Path.Combine(programFiles, "CMake")) ||
                    DiagnosticPaths.Inside(path, System.IO.Path.Combine(programFiles, "Microsoft Visual Studio"));
            if (!allowed) throw new BuildFailedException("Tool is outside the installed approved toolchain roots.");
            string name = System.IO.Path.GetFileName(path);
            if (key == "llvmNm" && name != "llvm-nm.exe") throw new BuildFailedException("Expected installed llvm-nm.exe.");
            VerifyDescriptor(path, item);
            return path;
        }

        internal void VerifyDescriptor(string path, Dictionary<string, object> item, bool allowEmpty = false)
        {
            long size = DiagnosticJson.Size(item, "bytes");
            string hash = DiagnosticJson.Hex(item, "sha256", 64);
            if ((!allowEmpty && size == 0) || !File.Exists(path) || new FileInfo(path).Length != size || DiagnosticPaths.Hash(path) != hash)
                throw new BuildFailedException("Diagnostic input hash/length mismatch: " + path);
            if (VerifiedFiles.ContainsKey(path)) throw new BuildFailedException("Ambiguous repeated diagnostic input path.");
            VerifiedFiles.Add(path, hash);
        }

        internal void Revalidate()
        {
            foreach (var file in VerifiedFiles)
            {
                DiagnosticPaths.NoLinks(file.Key);
                if (DiagnosticPaths.Hash(file.Key) != file.Value) throw new BuildFailedException("Verified diagnostic input changed before use.");
            }
        }

        // Canonical inventory: distinct externally defined names, ordinal-sort, one UTF-8 LF per name.
        internal static string ReadSymbols(string executable, string archive, string mode = "--defined-only", bool allowEmpty = false)
        {
            if (mode != "--defined-only" && mode != "--undefined-only") throw new BuildFailedException("Unsupported native symbol query.");
            var start = new ProcessStartInfo(executable, mode + " --extern-only --format=posix " + QuoteArgument(archive))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(); throw new BuildFailedException("llvm-nm timed out."); }
                string text = output.GetAwaiter().GetResult(), stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new BuildFailedException("llvm-nm failed: " + stderr);
                ValidateNmDiagnostics(stderr, archive);
                return NormalizeSymbols(text, allowEmpty);
            }
        }

        internal static void ValidateNmDiagnostics(string stderr, string archive)
        {
            string prefix = archive + "[", suffix = "]: no symbols";
            foreach (string value in stderr.Split('\n'))
            {
                string line = value.EndsWith("\r", StringComparison.Ordinal) ? value.Substring(0, value.Length - 1) : value;
                if (line.Length == 0) continue;
                if (!line.StartsWith(prefix, StringComparison.Ordinal) || !line.EndsWith(suffix, StringComparison.Ordinal))
                    throw new BuildFailedException("Unexpected llvm-nm diagnostic: " + line);
                string member = line.Substring(prefix.Length, line.Length - prefix.Length - suffix.Length);
                if (!EmptySymbolMembers.Contains(member, StringComparer.Ordinal))
                    throw new BuildFailedException("Unexpected llvm-nm archive member diagnostic: " + line);
            }
        }

        internal static string NormalizeSymbols(string output, bool allowEmpty = false)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) || trimmed.EndsWith("]:", StringComparison.Ordinal)) continue;
                string[] fields = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || fields[1].Length != 1 || !char.IsLetter(fields[1][0]))
                    throw new BuildFailedException("Unknown llvm-nm POSIX output.");
                names.Add(fields[0]);
            }
            if (names.Count == 0 && !allowEmpty) throw new BuildFailedException("Empty symbol inventory.");
            return names.Count == 0 ? "" : string.Join("\n", names) + "\n";
        }

        private static string QuoteArgument(string value)
        {
            if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new BuildFailedException("Invalid native archive argument.");
            return "\"" + value + "\"";
        }
    }

    internal sealed class DiagnosticStagingTransaction
    {
        private readonly string mProject, mDirectory, mMeta, mJournalRoot;
        private readonly LottieWebGLDiagnosticManifest mManifest;
        private readonly Dictionary<string, byte[]> mBefore;
        private readonly Dictionary<string, string> mProtected;
        private bool mMutated, mRestored, mStaged;
        private readonly bool mDirectoryExisted;
        private readonly Action mRefresh;
        private readonly Action<string> mConfigure;
        internal static readonly string[] AllowedNames = { ".lottie-plugin-generated", "libLottiePlugin.a", "libLottiePlugin.a.meta", "librlottie.a", "librlottie.a.meta" };

        internal DiagnosticStagingTransaction(string project, LottieWebGLDiagnosticManifest manifest,
            Action refresh = null, Action<string> configure = null)
        {
            mProject = project; mManifest = manifest;
            mRefresh = refresh ?? (() => AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport));
            mConfigure = configure ?? LottieWebGLArchiveStager.ConfigureStagedPlugin;
            mDirectory = System.IO.Path.Combine(project, LottieWebGLArchiveStager.GeneratedRelativePath); mMeta = mDirectory + ".meta";
            mDirectoryExisted = Directory.Exists(mDirectory);
            mBefore = Snapshot(mDirectory);
            mProtected = Protected(project);
            manifest.Revalidate();
            string transactionParent = System.IO.Path.Combine(manifest.Root, "staging-transactions");
            DiagnosticPaths.External(transactionParent, project);
            if (Directory.Exists(transactionParent) && HasActiveJournal(transactionParent))
                throw new BuildFailedException("An unfinished diagnostic staging journal requires explicit Root recovery.");
            mJournalRoot = System.IO.Path.Combine(transactionParent, Guid.NewGuid().ToString("N"));
            DiagnosticPaths.NoLinks(mJournalRoot);
            // All manifest/options/ownership validation precedes these external-only backup writes.
            Directory.CreateDirectory(mJournalRoot);
            foreach (var file in mBefore) File.WriteAllBytes(System.IO.Path.Combine(mJournalRoot, file.Key), file.Value);
            // Prepare both verified archives and exact prior importer metadata externally.
            // A preparation failure therefore cannot partially replace project archives.
            string prepared = System.IO.Path.Combine(mJournalRoot, "prepared");
            Directory.CreateDirectory(prepared);
            Prepare(manifest.Plugin, prepared, "libLottiePlugin.a", "7bc73e1a83c24a18b3d0a16a58229e11");
            Prepare(manifest.Rlottie, prepared, "librlottie.a", "c2c1a24ddbe94c80993d743a687ec48f");
            var journal = new Dictionary<string, object> { { "schema", "lottie.webgl.native-state-trace.staging/1" },
                { "project", project }, { "manifestSha256", manifest.Hash }, { "before", Hashes(mBefore) },
                { "protected", mProtected }, { "directoryExisted", mDirectoryExisted } };
            File.WriteAllText(System.IO.Path.Combine(mJournalRoot, "active.json"), DiagnosticJson.Encode(journal), new UTF8Encoding(false));
        }

        private void Prepare(string source, string prepared, string name, string guid)
        {
            DiagnosticPaths.NoLinks(source);
            File.Copy(source, System.IO.Path.Combine(prepared, name));
            if (DiagnosticPaths.Hash(source) != DiagnosticPaths.Hash(System.IO.Path.Combine(prepared, name)))
                throw new BuildFailedException("Prepared diagnostic archive differs from verified input.");
            byte[] metadata;
            if (!mBefore.TryGetValue(name + ".meta", out metadata))
                metadata = new UTF8Encoding(false).GetBytes(LottieWebGLArchiveStager.PluginImporterTemplate.Replace("{GUID}", guid));
            File.WriteAllBytes(System.IO.Path.Combine(prepared, name + ".meta"), metadata);
        }

        internal static Dictionary<string, byte[]> Snapshot(string directory)
        {
            DiagnosticPaths.NoLinks(directory); DiagnosticPaths.NoLinks(directory + ".meta");
            if (File.Exists(directory)) throw new BuildFailedException("A file occupies the generated WebGL staging directory path.");
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (File.Exists(directory + ".meta")) result.Add("directory.meta", File.ReadAllBytes(directory + ".meta"));
            if (!Directory.Exists(directory)) return result;
            if (Directory.EnumerateDirectories(directory).Any()) throw new BuildFailedException("Unexpected directory in owned WebGL staging.");
            string marker = System.IO.Path.Combine(directory, LottieWebGLArchiveStager.OwnershipMarker);
            if (!File.Exists(marker) || File.ReadAllText(marker) != LottieWebGLArchiveStager.OwnershipValue)
                throw new BuildFailedException("Refusing unowned WebGL staging.");
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                DiagnosticPaths.NoLinks(file);
                string name = System.IO.Path.GetFileName(file);
                if (!AllowedNames.Contains(name)) throw new BuildFailedException("Unexpected file in owned WebGL staging.");
                result.Add(name, File.ReadAllBytes(file));
            }
            return result;
        }

        private static bool HasActiveJournal(string directory)
        {
            DiagnosticPaths.NoLinks(directory);
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                DiagnosticPaths.NoLinks(file);
                if (System.IO.Path.GetFileName(file) == "active.json") return true;
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
                if (HasActiveJournal(child)) return true;
            return false;
        }

        internal void Stage(Action afterDirectoryCreated = null)
        {
            if (mStaged) throw new BuildFailedException("Diagnostic preprocessing repeated.");
            mManifest.Revalidate();
            if (!Equal(Hashes(Snapshot(mDirectory)), Hashes(mBefore)) || !Equal(Protected(mProject), mProtected))
                throw new BuildFailedException("Project bytes changed since diagnostic preflight.");
            mMutated = true;
            Directory.CreateDirectory(mDirectory);
            if (afterDirectoryCreated != null) afterDirectoryCreated();
            File.WriteAllText(System.IO.Path.Combine(mDirectory, LottieWebGLArchiveStager.OwnershipMarker), LottieWebGLArchiveStager.OwnershipValue, new UTF8Encoding(false));
            Copy("libLottiePlugin.a");
            Copy("librlottie.a");
            mRefresh();
            foreach (string name in new[] { "libLottiePlugin.a", "librlottie.a" })
            {
                mConfigure(LottieWebGLArchiveStager.GeneratedRelativePath + "/" + name);
                string source = name == "libLottiePlugin.a" ? mManifest.Plugin : mManifest.Rlottie;
                if (DiagnosticPaths.Hash(System.IO.Path.Combine(mDirectory, name)) != DiagnosticPaths.Hash(source))
                    throw new BuildFailedException("Imported diagnostic archive differs from verified input.");
            }
            mStaged = true;
        }

        private void Copy(string name)
        {
            DiagnosticPaths.NoLinks(mDirectory);
            string destination = System.IO.Path.Combine(mDirectory, name);
            DiagnosticPaths.NoLinks(destination); DiagnosticPaths.NoLinks(destination + ".meta");
            string prepared = System.IO.Path.Combine(mJournalRoot, "prepared", name);
            DiagnosticPaths.NoLinks(prepared); DiagnosticPaths.NoLinks(prepared + ".meta");
            File.Copy(prepared, destination, true);
            File.Copy(prepared + ".meta", destination + ".meta", true);
        }

        internal void Restore()
        {
            if (mRestored) return;
            if (mMutated)
            {
                // Validate ownership and exact allowed names before deleting this attempt's staged bytes.
                ValidateRollbackInventory(mDirectory, mDirectoryExisted);
                foreach (string name in AllowedNames)
                {
                    string path = System.IO.Path.Combine(mDirectory, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                if (mDirectoryExisted) Directory.CreateDirectory(mDirectory);
                foreach (var file in mBefore)
                    if (file.Key != "directory.meta") File.WriteAllBytes(System.IO.Path.Combine(mDirectory, file.Key), file.Value);
                if (!mBefore.ContainsKey(LottieWebGLArchiveStager.OwnershipMarker))
                {
                    if (Directory.Exists(mDirectory))
                    {
                        if (Directory.EnumerateFileSystemEntries(mDirectory).Any()) throw new BuildFailedException("Unexpected bytes prevent diagnostic cleanup.");
                        Directory.Delete(mDirectory);
                    }
                }
                if (File.Exists(mMeta)) File.Delete(mMeta);
                if (mBefore.ContainsKey("directory.meta")) File.WriteAllBytes(mMeta, mBefore["directory.meta"]);
                mRefresh();
            }
            if (!Equal(Hashes(Snapshot(mDirectory)), Hashes(mBefore)) || !Equal(Protected(mProject), mProtected))
                throw new BuildFailedException("Diagnostic staging/protected input restoration did not match exact original bytes.");
            mManifest.Revalidate();
            mRestored = true;
            File.Move(System.IO.Path.Combine(mJournalRoot, "active.json"), System.IO.Path.Combine(mJournalRoot, "restored.json"));
        }

        internal static void ValidateRollbackInventory(string directory, bool originallyExisted)
        {
            DiagnosticPaths.NoLinks(directory); DiagnosticPaths.NoLinks(directory + ".meta");
            if (!Directory.Exists(directory))
            {
                if (originallyExisted) throw new BuildFailedException("Original staging directory disappeared during diagnostic build.");
                return;
            }
            if (Directory.EnumerateDirectories(directory).Any()) throw new BuildFailedException("Unexpected directory prevents diagnostic rollback.");
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                DiagnosticPaths.NoLinks(file);
                if (!AllowedNames.Contains(System.IO.Path.GetFileName(file))) throw new BuildFailedException("Unexpected file prevents diagnostic rollback.");
            }
            string marker = System.IO.Path.Combine(directory, LottieWebGLArchiveStager.OwnershipMarker);
            if ((originallyExisted && !File.Exists(marker)) ||
                (File.Exists(marker) && File.ReadAllText(marker) != LottieWebGLArchiveStager.OwnershipValue))
                throw new BuildFailedException("Diagnostic rollback ownership marker changed.");
        }

        internal void WriteBuildReceipt(BuildReport report, Exception error)
        {
            var receipt = new Dictionary<string, object> { { "schema", "lottie.webgl.native-state-trace.consumer-staging/1" },
                { "manifestPath", mManifest.Path }, { "manifestSha256", mManifest.Hash }, { "packageCommit", mManifest.SourceCommit },
                { "buildGuid", report == null ? null : report.summary.guid.ToString() },
                { "result", report == null ? "exception_before_report" : report.summary.result.ToString() },
                { "error", error == null ? null : error.ToString() }, { "stagingRestored", mRestored },
                { "staged", mStaged }, { "protected", mProtected }, { "originalStaging", Hashes(mBefore) },
                { "selectedArchives", new Dictionary<string, object> { { "plugin", DiagnosticPaths.Hash(mManifest.Plugin) }, { "rlottie", DiagnosticPaths.Hash(mManifest.Rlottie) } } } };
            File.WriteAllText(System.IO.Path.Combine(mJournalRoot, "consumer-staging-receipt.json"), DiagnosticJson.Encode(receipt), new UTF8Encoding(false));
        }

        internal static Dictionary<string, string> Hashes(Dictionary<string, byte[]> files)
        { return files.ToDictionary(x => x.Key, x => DiagnosticPaths.HashBytes(x.Value), StringComparer.Ordinal); }
        internal static bool Equal(Dictionary<string, string> left, Dictionary<string, string> right)
        { return left.Count == right.Count && left.All(x => right.ContainsKey(x.Key) && right[x.Key] == x.Value); }

        private static Dictionary<string, string> Protected(string project)
        {
            var names = new[] { "Packages/manifest.json", "Packages/packages-lock.json", "ProjectSettings/ProjectSettings.asset",
                "Assets/Plugins/PocketBloomAnalytics.jslib.meta", "Assets/Plugins/PocketBloomBrowserStore.jslib.meta",
                "Assets/Plugins/PocketBloomCrazyGames.jslib.meta", "Assets/Plugins/PocketBloomRaster.jslib.meta", "Assets/Plugins/PocketBloomYouTube.jslib.meta" };
            return names.ToDictionary(x => x, x => File.Exists(System.IO.Path.Combine(project, x)) ? DiagnosticPaths.Hash(System.IO.Path.Combine(project, x)) : null);
        }
    }

    internal static class DiagnosticPaths
    {
        internal static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        internal static string HashBytes(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        internal static bool Inside(string path, string root)
        {
            string p = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string r = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            return p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        internal static bool Overlaps(string a, string b) { return Inside(a, b) || Inside(b, a); }
        internal static void Absolute(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) ||
                path[1] != ':' || (path[2] != '\\' && path[2] != '/') || path.StartsWith("\\\\", StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal) || path.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0 ||
                path.IndexOf(':', 2) >= 0 || path.Substring(3).Split(new[] { '/', '\\' }).Any(UnsafeComponent))
                throw new BuildFailedException("Expected a regular absolute local path.");
            string full = System.IO.Path.GetFullPath(path);
            if (full.TrimEnd('\\', '/') == System.IO.Path.GetPathRoot(full).TrimEnd('\\', '/'))
                throw new BuildFailedException("Filesystem roots cannot be diagnostic paths.");
        }
        internal static void Relative(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("\\") || path.Contains(":") || path.Split('/').Any(UnsafeComponent) ||
                System.IO.Path.IsPathRooted(path))
                throw new BuildFailedException("Expected a normalized relative input path.");
        }
        private static bool UnsafeComponent(string value)
        {
            if (value.Length == 0 || value == "." || value == ".." || value.EndsWith(".", StringComparison.Ordinal) ||
                value.EndsWith(" ", StringComparison.Ordinal) || value.Any(c => c < 32 || "<>:\"|?*".IndexOf(c) >= 0)) return true;
            string name = value.Split('.')[0];
            return new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
        internal static void NoLinks(string path)
        {
            for (string current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
            {
                try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new BuildFailedException("Reparse/link diagnostic path rejected: " + current); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
        internal static void External(string path, string project, params string[] protectedRoots)
        {
            Absolute(path); NoLinks(path);
            if (Overlaps(path, project)) throw new BuildFailedException("Diagnostic path overlaps the consumer project.");
            foreach (string root in protectedRoots)
                if (!string.IsNullOrWhiteSpace(root) && Overlaps(path, root))
                    throw new BuildFailedException("Diagnostic path overlaps a protected source/package/toolchain root.");
            for (string current = System.IO.Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
                if (Directory.Exists(System.IO.Path.Combine(current, ".git")) || File.Exists(System.IO.Path.Combine(current, ".git")))
                    throw new BuildFailedException("Diagnostic path is inside a Git checkout.");
        }
    }

    /// <summary>Small bounded JSON reader: no dependencies, duplicate keys or permissive number grammar.</summary>
    internal sealed class DiagnosticJson
    {
        private readonly string mText;
        private int mPosition, mNodes;
        private DiagnosticJson(string text) { mText = text; }
        internal static object Parse(byte[] bytes)
        {
            if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024 || bytes[0] == 0xef)
                throw new BuildFailedException("Expected bounded strict UTF-8 JSON without BOM.");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { throw new BuildFailedException("Invalid UTF-8 JSON."); }
            var parser = new DiagnosticJson(text);
            object value = parser.Value(0); parser.Space();
            if (parser.mPosition != parser.mText.Length) throw new BuildFailedException("Trailing JSON data.");
            return value;
        }
        private object Value(int depth)
        {
            Space();
            if (depth > 32 || ++mNodes > 200000 || mPosition == mText.Length) throw new BuildFailedException("Invalid/bounded JSON.");
            char c = mText[mPosition];
            if (c == '"') return String();
            if (c == '{')
            {
                mPosition++; var result = new Dictionary<string, object>(StringComparer.Ordinal); Space();
                if (Take('}')) return result;
                do
                {
                    Space(); if (mPosition >= mText.Length || mText[mPosition] != '"') throw new BuildFailedException("Object key expected.");
                    string key = String(); Space(); Need(':');
                    if (result.ContainsKey(key)) throw new BuildFailedException("Duplicate JSON property.");
                    result.Add(key, Value(depth + 1)); Space();
                    if (Take('}')) return result;
                    Need(',');
                } while (true);
            }
            if (c == '[')
            {
                mPosition++; var result = new List<object>(); Space(); if (Take(']')) return result;
                do { result.Add(Value(depth + 1)); Space(); if (Take(']')) return result; Need(','); } while (true);
            }
            foreach (string literal in new[] { "true", "false", "null" })
                if (mText.Substring(mPosition).StartsWith(literal, StringComparison.Ordinal))
                { mPosition += literal.Length; return literal == "null" ? null : (object)(literal == "true"); }
            int start = mPosition;
            if (c == '-') mPosition++;
            if (mPosition >= mText.Length || !char.IsDigit(mText[mPosition])) throw new BuildFailedException("JSON integer expected.");
            if (mText[mPosition] == '0') mPosition++;
            else while (mPosition < mText.Length && mText[mPosition] >= '0' && mText[mPosition] <= '9') mPosition++;
            long number;
            if (!long.TryParse(mText.Substring(start, mPosition - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                throw new BuildFailedException("JSON integer out of range.");
            return number;
        }
        private string String()
        {
            Need('"'); var value = new StringBuilder();
            while (mPosition < mText.Length)
            {
                char c = mText[mPosition++]; if (c == '"') return ValidString(value.ToString());
                if (c < 32) throw new BuildFailedException("Unescaped JSON control character.");
                if (c != '\\') { value.Append(c); continue; }
                if (mPosition >= mText.Length) break;
                c = mText[mPosition++];
                switch (c)
                {
                    case '"': case '\\': case '/': value.Append(c); break;
                    case 'b': value.Append('\b'); break; case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break; case 'r': value.Append('\r'); break; case 't': value.Append('\t'); break;
                    case 'u':
                        if (mPosition + 4 > mText.Length) throw new BuildFailedException("Invalid JSON escape.");
                        ushort code;
                        if (!ushort.TryParse(mText.Substring(mPosition, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw new BuildFailedException("Invalid JSON unicode escape.");
                        value.Append((char)code); mPosition += 4; break;
                    default: throw new BuildFailedException("Invalid JSON escape.");
                }
            }
            throw new BuildFailedException("Unterminated JSON string.");
        }
        private static string ValidString(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw new BuildFailedException("Unpaired JSON Unicode surrogate.");
                }
                else if (char.IsLowSurrogate(value[i])) throw new BuildFailedException("Unpaired JSON Unicode surrogate.");
            }
            return value;
        }
        private void Space() { while (mPosition < mText.Length && (mText[mPosition] == ' ' || mText[mPosition] == '\r' || mText[mPosition] == '\n' || mText[mPosition] == '\t')) mPosition++; }
        private bool Take(char c) { if (mPosition < mText.Length && mText[mPosition] == c) { mPosition++; return true; } return false; }
        private void Need(char c) { if (!Take(c)) throw new BuildFailedException("Unexpected JSON token."); }
        internal static Dictionary<string, object> Object(object value) { var result = value as Dictionary<string, object>; if (result == null) throw new BuildFailedException("JSON object required."); return result; }
        internal static List<object> Array(object value) { var result = value as List<object>; if (result == null) throw new BuildFailedException("JSON array required."); return result; }
        internal static void Keys(Dictionary<string, object> value, params string[] keys)
        { if (value.Count != keys.Length || keys.Any(x => !value.ContainsKey(x))) throw new BuildFailedException("Unknown or missing JSON properties."); }
        internal static string Text(Dictionary<string, object> value, string key)
        { object item; if (!value.TryGetValue(key, out item) || !(item is string) || string.IsNullOrWhiteSpace((string)item)) throw new BuildFailedException("Nonempty JSON string required: " + key); return (string)item; }
        internal static void Equal(Dictionary<string, object> value, string key, object expected)
        { object actual; if (!value.TryGetValue(key, out actual) || !Equals(actual, expected)) throw new BuildFailedException("Unexpected diagnostic value: " + key); }
        internal static bool IsHex(string value, int length) { return value != null && value.Length == length && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')); }
        internal static string Hex(Dictionary<string, object> value, string key, int length)
        { string text = Text(value, key); if (!IsHex(text, length)) throw new BuildFailedException("Invalid lowercase hexadecimal value: " + key); return text; }
        internal static long Size(Dictionary<string, object> value, string key)
        { object item; if (!value.TryGetValue(key, out item) || !(item is long) || (long)item < 0 || (long)item > int.MaxValue) throw new BuildFailedException("Invalid byte length."); return (long)item; }
        internal static string[] Strings(object value)
        { return Array(value).Select(x => { if (!(x is string) || string.IsNullOrWhiteSpace((string)x)) throw new BuildFailedException("String array required."); return (string)x; }).ToArray(); }
        internal static string Encode(object value)
        {
            if (value == null) return "null";
            if (value is string)
            {
                var b = new StringBuilder("\"");
                foreach (char c in (string)value) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); }
                return b.Append('"').ToString();
            }
            if (value is bool) return (bool)value ? "true" : "false";
            var dictionary = value as System.Collections.IDictionary;
            if (dictionary != null)
            {
                var entries = new List<string>();
                foreach (object key in dictionary.Keys) entries.Add(Encode((string)key) + ":" + Encode(dictionary[key]));
                return "{" + string.Join(",", entries) + "}";
            }
            var list = value as System.Collections.IEnumerable;
            if (list != null) { var entries = new List<string>(); foreach (object entry in list) entries.Add(Encode(entry)); return "[" + string.Join(",", entries) + "]"; }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
#endif


using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace LottiePlugin.Editor
{
    /// <summary>Use around BuildPipeline.BuildPlayer and call CopyTo after success.
    /// Parallel WebGL builds also pass LOTTIE_PARALLEL_PREPARATION in extraScriptingDefines.
    /// The temporary Assets bridge is retired even if the build fails.</summary>
    public sealed class LottieRasterBuildScope : IDisposable
    {
        private const string GeneratedDirectory = "Assets/LottiePlugin.GeneratedRaster";
        private const string Marker = ".lottie-raster-generated";
        private const string Ownership = "LottiePlugin generated raster bridge; safe to clean.\n";
        private const string BridgeName = "LottieRaster.jslib";
        private readonly bool _parallelWeb;
        private static bool sActive;
        private bool _disposed;
        public bool BrowserWorkersIncluded { get { return _parallelWeb; } }
        public LottieRasterBuildScope(BuildTarget target, bool parallel)
        {
            if (sActive) throw new InvalidOperationException("Raster build scopes must be serialized.");
            // Older bitcode/fastcomp variants retain a compatible serial route.
            _parallelWeb = parallel && target == BuildTarget.WebGL &&
                (RLottie.LottieWebGLArchiveStager.SelectedVariant == "Legacy" || RLottie.LottieWebGLArchiveStager.SelectedVariant == "WasmExceptions");
            CleanupBridge();
            try
            {
                if (_parallelWeb)
                {
                    ValidatePayload();
                    // Git UPM is immutable. Stage into Assets just like the ABI-specific archives.
                    Directory.CreateDirectory(GeneratedDirectory);
                    File.WriteAllText(Path.Combine(GeneratedDirectory, Marker), Ownership);
                    string bridge = Path.Combine(GeneratedDirectory, BridgeName);
                    File.Copy(Path.Combine(RLottie.LottieWebGLArchiveStager.GetPackagePath(), "Plugins/WebGL", BridgeName), bridge);
                    File.WriteAllText(bridge + ".meta", RLottie.LottieWebGLArchiveStager.PluginImporterTemplate
                        .Replace("{GUID}", "0aa84ead8c514e75b7a3f57ef430f686"));
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    RLottie.LottieWebGLArchiveStager.ConfigureStagedPlugin(bridge);
                }
                sActive = true;
            }
            catch { CleanupBridge(); throw; }
        }
        private static void CleanupBridge()
        {
            if (!Directory.Exists(GeneratedDirectory)) return;
            string marker = Path.Combine(GeneratedDirectory, Marker);
            if (!File.Exists(marker) || File.ReadAllText(marker) != Ownership)
                throw new InvalidOperationException("Refusing to change unowned raster staging directory.");
            string bridge = Path.Combine(GeneratedDirectory, BridgeName);
            File.Delete(bridge);
            File.Delete(bridge + ".meta");
            // Preserve unrelated files even within an owned staging directory.
            if (Directory.EnumerateFileSystemEntries(GeneratedDirectory).All(path => path == marker))
            {
                File.Delete(marker);
                Directory.Delete(GeneratedDirectory);
                File.Delete(GeneratedDirectory + ".meta");
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        private static string Source
        {
            get
            {
                return Path.Combine(RLottie.LottieWebGLArchiveStager.GetPackagePath(), "Editor/WebGLArchives",
                    RLottie.LottieWebGLArchiveStager.SelectedVariant, "RasterWorker~");
            }
        }
        [Serializable] private sealed class Receipt { public string archiveSha256; public FileHash[] files; }
        [Serializable] private sealed class FileHash { public string file; public string sha256; }
        private static string Hash(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
        public static void ValidatePayload()
        {
            string source = Source;
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(Path.Combine(source, "provenance.json")));
            if (receipt == null || receipt.files == null || receipt.files.Length != 4 ||
                receipt.archiveSha256 != Hash(Path.Combine(source, "../librlottie.a.bytes")))
                throw new InvalidOperationException("Raster worker does not match the delivered renderer archive.");
            foreach (string name in new[] { "worker.js", "raster.js", "raster.wasm", "LICENSE.txt" })
            {
                FileHash[] matches = receipt.files.Where(entry => entry.file == name).ToArray();
                if (matches.Length != 1 || matches[0].sha256 != Hash(Path.Combine(source, name)))
                    throw new InvalidOperationException("Raster worker hash mismatch: " + name);
            }
        }
        public void CopyTo(string playerDirectory)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LottieRasterBuildScope));
            string destination = Path.Combine(playerDirectory, "LottieRaster");
            if (!_parallelWeb)
            {
                // Remove only the package's known outputs from a previous opt-in build.
                foreach (string name in new[] { "worker.js", "raster.js", "raster.wasm", "LICENSE.txt", "provenance.json" })
                    if (File.Exists(Path.Combine(destination, name))) File.Delete(Path.Combine(destination, name));
                if (Directory.Exists(destination) && !Directory.EnumerateFileSystemEntries(destination).Any()) Directory.Delete(destination);
                return;
            }
            ValidatePayload();
            Directory.CreateDirectory(destination);
            foreach (string name in new[] { "worker.js", "raster.js", "raster.wasm", "LICENSE.txt", "provenance.json" })
                File.Copy(Path.Combine(Source, name), Path.Combine(destination, name), true);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                CleanupBridge();
            }
            finally { sActive = false; }
        }
    }
}

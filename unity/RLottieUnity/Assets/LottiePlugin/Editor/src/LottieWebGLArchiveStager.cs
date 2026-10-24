using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.PackageManager;
using UnityEngine;

namespace RLottie
{
    /// <summary>
    /// Stages the WebGL ABI-specific native libraries into the consuming project's
    /// Assets folder. Unity 6000.5 currently links static .a PluginImporters even
    /// when their DefineConstraints do not match, so the package keeps source
    /// archives inert as .bytes and imports only the selected pair for a build.
    /// </summary>
    internal sealed class LottieWebGLArchiveStager : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        internal const string GeneratedRelativePath = "Assets/LottiePlugin.GeneratedWebGL";
        internal const string OwnershipMarker = ".lottie-plugin-generated";
        internal const string OwnershipValue = "LottiePlugin generated WebGL archive staging; safe to clean.\n";
        internal const string PluginImporterTemplate =
            "fileFormatVersion: 2\n" +
            "guid: {GUID}\n" +
            "PluginImporter:\n" +
            "  externalObjects: {}\n" +
            "  serializedVersion: 2\n" +
            "  iconMap: {}\n" +
            "  executionOrder: {}\n" +
            "  defineConstraints: []\n" +
            "  isPreloaded: 0\n" +
            "  isOverridable: 0\n" +
            "  isExplicitlyReferenced: 0\n" +
            "  validateReferences: 1\n" +
            "  platformData:\n" +
            "  - first:\n" +
            "      : Any\n" +
            "    second:\n" +
            "      enabled: 0\n" +
            "      settings:\n" +
            "        Exclude WebGL: 0\n" +
            "  - first:\n" +
            "      WebGL: WebGL\n" +
            "    second:\n" +
            "      enabled: 1\n" +
            "      settings: {}\n" +
            "  userData: \n" +
            "  assetBundleName: \n" +
            "  assetBundleVariant: \n";

        private static readonly string LegacyPluginHash = "d84c5244be266431a6759f40605cef89fbebda08497c1f04216d7192659a4ad8";
        private static readonly string LegacyRlottieHash = "754e15e226e819e83bc0d6e8e1ea22a8b1bf882c304ef1b2361bd4a2b7762cbb";
        private static readonly string Legacy2021PluginHash = "86c1c10be8541dee23cf9b98f47edd9cecfd1547ac571adcadb0840851a2458c";
        private static readonly string Legacy2021RlottieHash = "b5078d63c82615a8ad23940d5a67d49a6f01c0e40ebe3efea0142e16932b141c";
        private static readonly string Legacy2019PluginHash = "4894f1ca6be1a8c873e59589b17deb5e5de4d56f6da13086155b43ca48595010";
        private static readonly string Legacy2019RlottieHash = "42dc8c94ed02a010e77f84707015822adc6b18381e94f23273ecd9facf58424a";
        private static readonly string WasmPluginHash = "65f22f125419daa0b035167485cc56589501449f0d10e6a57071a5dff9a658db";
        private static readonly string WasmRlottieHash = "b17cda443208f9c746a6610e8edee79257684fdd8ae39f93f8df3a1263d908da";

        public int callbackOrder { get { return int.MinValue; } }

        public void OnPreprocessBuild(BuildReport report)
        {
#if UNITY_6000_5_OR_NEWER
            // Diagnostic preflight owns its transaction. Never enter ordinary cleanup on rejection.
            if (LottieWebGLDiagnosticBuild.Preprocess(report)) return;
#endif
            CleanupGeneratedFiles();
            if (report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }

            try
            {
                StageSelectedArchives();
            }
            catch
            {
                CleanupGeneratedFiles();
                throw;
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
#if UNITY_6000_5_OR_NEWER
            if (LottieWebGLDiagnosticBuild.IsActive) return;
#endif
            if (report.summary.platform == BuildTarget.WebGL)
            {
                CleanupGeneratedFiles();
            }
        }

        internal static string SelectedVariant
        {
            get
            {
#if UNITY_6000_5_OR_NEWER
                return "WasmExceptions";
#elif UNITY_2021_1_OR_NEWER && !UNITY_2022_1_OR_NEWER
                return "Legacy2021";
#elif UNITY_2019
                return "Legacy2019";
#else
                return "Legacy";
#endif
            }
        }

        private static void StageSelectedArchives()
        {
            string packagePath = GetPackagePath();
            string variant = SelectedVariant;
#if UNITY_6000_5_OR_NEWER
            string expectedPluginHash = WasmPluginHash;
            string expectedRlottieHash = WasmRlottieHash;
#elif UNITY_2021_1_OR_NEWER && !UNITY_2022_1_OR_NEWER
            string expectedPluginHash = Legacy2021PluginHash;
            string expectedRlottieHash = Legacy2021RlottieHash;
#elif UNITY_2019
            string expectedPluginHash = Legacy2019PluginHash;
            string expectedRlottieHash = Legacy2019RlottieHash;
#else
            string expectedPluginHash = LegacyPluginHash;
            string expectedRlottieHash = LegacyRlottieHash;
#endif
            string sourceDirectory = Path.Combine(packagePath, "Editor", "WebGLArchives", variant);
            string destinationDirectory = Path.GetFullPath(GeneratedRelativePath);
            Directory.CreateDirectory(destinationDirectory);
            File.WriteAllText(Path.Combine(destinationDirectory, OwnershipMarker), OwnershipValue);

            StageOneArchive(sourceDirectory, destinationDirectory, "libLottiePlugin.a.bytes",
                "libLottiePlugin.a", "7bc73e1a83c24a18b3d0a16a58229e11", expectedPluginHash);
            StageOneArchive(sourceDirectory, destinationDirectory, "librlottie.a.bytes",
                "librlottie.a", "c2c1a24ddbe94c80993d743a687ec48f", expectedRlottieHash);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureStagedPlugin(Path.Combine(GeneratedRelativePath, "libLottiePlugin.a"));
            ConfigureStagedPlugin(Path.Combine(GeneratedRelativePath, "librlottie.a"));
            Debug.Log("RLottie staged verified " + variant + " WebGL archives at " + GeneratedRelativePath + ".");
        }

        private static void StageOneArchive(
            string sourceDirectory, string destinationDirectory, string sourceName,
            string destinationName, string guid, string expectedHash)
        {
            string source = Path.Combine(sourceDirectory, sourceName);
            string destination = Path.Combine(destinationDirectory, destinationName);
            if (!File.Exists(source) || !string.Equals(HashFile(source), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException("RLottie WebGL archive is missing or has unexpected contents: " + source);
            }

            File.Copy(source, destination, true);
            File.WriteAllText(destination + ".meta", PluginImporterTemplate.Replace("{GUID}", guid));
        }

        internal static void ConfigureStagedPlugin(string assetPath)
        {
            PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
            {
                throw new BuildFailedException("Unity did not import the staged RLottie archive as a PluginImporter: " + assetPath);
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.WebGL, true);
            importer.SaveAndReimport();
            if (!importer.GetCompatibleWithPlatform(BuildTarget.WebGL))
            {
                throw new BuildFailedException("Staged RLottie archive is not enabled for WebGL: " + assetPath);
            }
        }

        internal static string GetPackagePath()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LottieWebGLArchiveStager).Assembly);
            string resolvedPath = package == null ? null : package.resolvedPath;
            string[] scriptGuids = AssetDatabase.FindAssets("LottieWebGLArchiveStager t:MonoScript");
            string scriptAssetPath = null;
            foreach (string guid in scriptGuids)
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(candidatePath);
                if (script != null && script.GetClass() == typeof(LottieWebGLArchiveStager))
                {
                    scriptAssetPath = candidatePath;
                    break;
                }
            }

            try
            {
                return ResolvePackagePath(resolvedPath, scriptAssetPath,
                    Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            }
            catch (ArgumentException exception)
            {
                throw new BuildFailedException(
                    "Could not resolve the RLottie package path for WebGL archives: " + exception.Message);
            }
        }

        internal static string ResolvePackagePath(
            string resolvedPackagePath, string stagerAssetPath, string projectRoot)
        {
            if (!string.IsNullOrEmpty(resolvedPackagePath))
            {
                return Path.GetFullPath(resolvedPackagePath);
            }

            if (string.IsNullOrEmpty(stagerAssetPath) || string.IsNullOrEmpty(projectRoot) ||
                !stagerAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException("A resolved UPM package path or an Assets-based stager path is required.");
            }

            string stagerPath = Path.GetFullPath(Path.Combine(projectRoot, stagerAssetPath));
            string editorSourceDirectory = Path.GetDirectoryName(stagerPath);
            string packageRoot = Path.GetFullPath(Path.Combine(editorSourceDirectory, "..", ".."));
            if (!File.Exists(stagerPath) || !Directory.Exists(Path.Combine(packageRoot, "Editor", "WebGLArchives")))
            {
                throw new ArgumentException("The Assets-based RLottie package root does not contain its WebGL archives.");
            }
            return packageRoot;
        }

        private static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] digest = sha.ComputeHash(stream);
                return BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void CleanupGeneratedFiles()
        {
            string directory = Path.GetFullPath(GeneratedRelativePath);
            if (!Directory.Exists(directory))
            {
                return;
            }

            string marker = Path.Combine(directory, OwnershipMarker);
            if (!File.Exists(marker) || File.ReadAllText(marker) != OwnershipValue)
            {
                throw new BuildFailedException("Refusing to modify unowned path " + GeneratedRelativePath + ".");
            }

            foreach (string name in new[] { "libLottiePlugin.a", "librlottie.a" })
            {
                AssetDatabase.DeleteAsset(GeneratedRelativePath + "/" + name);
                string file = Path.Combine(directory, name);
                if (File.Exists(file)) File.Delete(file);
                if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
            }
            File.Delete(marker);
            if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
            {
                Directory.Delete(directory);
                string directoryMeta = directory + ".meta";
                if (File.Exists(directoryMeta)) File.Delete(directoryMeta);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}

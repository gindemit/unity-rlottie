using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LottiePlugin.Editor
{
    internal sealed class LottieWebGLArchiveSelector :
        IPreprocessBuildWithReport,
        IPostprocessBuildWithReport
    {
        private const string LegacyVariant = "Legacy";
        private const string WasmExceptionsVariant = "WasmExceptions";
        private static readonly string[] sArchiveNames =
        {
            "libLottiePlugin.a",
            "librlottie.a"
        };

        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
            {
                return;
            }

            SelectVariant(UsesWasmExceptionLongjmp() ? WasmExceptionsVariant : LegacyVariant);
            // A failed build does not run post-process callbacks. Restore the
            // checked-in legacy archive pair as soon as control returns to the
            // editor so a 6000.5 build does not leave generated binary changes.
            EditorApplication.delayCall -= RestoreLegacyVariant;
            EditorApplication.delayCall += RestoreLegacyVariant;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL)
            {
                RestoreLegacyVariant();
            }
        }

        private static bool UsesWasmExceptionLongjmp()
        {
            string[] parts = Application.unityVersion.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out int release) ||
                !int.TryParse(parts[1], out int stream))
            {
                return false;
            }

            return release > 6000 || (release == 6000 && stream >= 5);
        }

        private static void RestoreLegacyVariant()
        {
            EditorApplication.delayCall -= RestoreLegacyVariant;
            SelectVariant(LegacyVariant);
        }

        private static void SelectVariant(string variant)
        {
            string archiveRoot = Path.Combine(
                Application.dataPath,
                "LottiePlugin",
                "Editor",
                "WebGLArchives",
                variant);
            string pluginRoot = Path.Combine(
                Application.dataPath,
                "LottiePlugin",
                "Plugins",
                "WebGL");

            foreach (string archiveName in sArchiveNames)
            {
                string sourcePath = Path.Combine(archiveRoot, archiveName + ".bytes");
                string destinationPath = Path.Combine(pluginRoot, archiveName);
                if (!File.Exists(sourcePath))
                {
                    throw new BuildFailedException("Missing WebGL native archive variant: " + sourcePath);
                }

                File.Copy(sourcePath, destinationPath, true);
                string assetPath = "Assets/LottiePlugin/Plugins/WebGL/" + archiveName;
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}

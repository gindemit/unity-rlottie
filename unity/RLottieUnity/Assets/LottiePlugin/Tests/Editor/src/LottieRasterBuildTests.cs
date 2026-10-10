using System;
using System.IO;
using LottiePlugin.Editor;
using NUnit.Framework;
using UnityEditor;

namespace LottiePlugin.Tests.Editor
{
    public sealed class LottieRasterBuildTests
    {
        [Test]
        public void ParallelThenSerialRestoresImporterAndRemovesOnlyOwnedSidecars()
        {
            string path = AssetImporter.GetAtPath("Packages/com.gindemit.rlottie/Plugins/WebGL/LottieRaster.jslib") != null
                ? "Packages/com.gindemit.rlottie/Plugins/WebGL/LottieRaster.jslib" : "Assets/LottiePlugin/Plugins/WebGL/LottieRaster.jslib";
            var importer = (PluginImporter)AssetImporter.GetAtPath(path);
            bool before = importer.GetCompatibleWithPlatform(BuildTarget.WebGL);
            string output = Path.Combine(Path.GetTempPath(), "lottie-raster-build-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var parallel = new LottieRasterBuildScope(BuildTarget.WebGL, true))
                {
#if UNITY_2021_1_OR_NEWER && !UNITY_2022_1_OR_NEWER || UNITY_2019
                    Assert.IsFalse(parallel.BrowserWorkersIncluded);
#else
                    Assert.IsTrue(parallel.BrowserWorkersIncluded);
                    parallel.CopyTo(output);
                    Assert.IsTrue(File.Exists(Path.Combine(output, "LottieRaster/raster.wasm")));
                    File.WriteAllText(Path.Combine(output, "LottieRaster/unrelated.txt"), "preserve");
#endif
                }
                Assert.AreEqual(before, importer.GetCompatibleWithPlatform(BuildTarget.WebGL));
                using (var serial = new LottieRasterBuildScope(BuildTarget.WebGL, false))
                {
                    Assert.IsFalse(serial.BrowserWorkersIncluded);
                    Assert.IsFalse(importer.GetCompatibleWithPlatform(BuildTarget.WebGL));
                    serial.CopyTo(output);
                }
                Assert.AreEqual(before, importer.GetCompatibleWithPlatform(BuildTarget.WebGL));
                Assert.IsFalse(File.Exists(Path.Combine(output, "LottieRaster/raster.wasm")));
#if !(UNITY_2021_1_OR_NEWER && !UNITY_2022_1_OR_NEWER) && !UNITY_2019
                Assert.AreEqual("preserve", File.ReadAllText(Path.Combine(output, "LottieRaster/unrelated.txt")));
#endif
            }
            finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
        }
    }
}

using System;
using System.IO;
using NUnit.Framework;

namespace RLottie.Tests.Editor
{
    public sealed class PackagePathResolutionTests
    {
        private string projectRoot;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(Path.GetTempPath(), "rlottie-package-path-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, true);
            }
        }

        [Test]
        public void UsesResolvedPathForGitPackage()
        {
            string packageRoot = Path.Combine(projectRoot, "Library", "PackageCache", "com.gindemit.rlottie@hash");
            Directory.CreateDirectory(packageRoot);

            string resolved = LottieWebGLArchiveStager.ResolvePackagePath(
                packageRoot, "Packages/com.gindemit.rlottie/Editor/src/LottieWebGLArchiveStager.cs", projectRoot);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(packageRoot)));
        }

        [Test]
        public void FindsPackageRootForEmbeddedAssetsLayout()
        {
            const string stagerAssetPath = "Assets/LottiePlugin/Editor/src/LottieWebGLArchiveStager.cs";
            string packageRoot = Path.Combine(projectRoot, "Assets", "LottiePlugin");
            string scriptPath = Path.Combine(packageRoot, "Editor", "src", "LottieWebGLArchiveStager.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            Directory.CreateDirectory(Path.Combine(packageRoot, "Editor", "WebGLArchives"));
            File.WriteAllText(scriptPath, "// test fixture");

            string resolved = LottieWebGLArchiveStager.ResolvePackagePath(null, stagerAssetPath, projectRoot);

            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(packageRoot)));
        }

        [Test]
        public void FailsClosedWhenNeitherPackagePathNorAssetsScriptIsAvailable()
        {
            Assert.Throws<ArgumentException>(() => LottieWebGLArchiveStager.ResolvePackagePath(
                null, "Packages/com.gindemit.rlottie/Editor/src/LottieWebGLArchiveStager.cs", projectRoot));
        }
    }
}

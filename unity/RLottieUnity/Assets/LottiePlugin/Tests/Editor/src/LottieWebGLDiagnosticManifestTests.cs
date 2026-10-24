#if UNITY_6000_5_OR_NEWER
using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Rendering;

namespace RLottie.Tests.Editor
{
    public sealed class LottieWebGLDiagnosticManifestTests
    {
        private string mRoot, mProject, mPayload, mStaging;

        [SetUp]
        public void SetUp()
        {
            mRoot = Path.Combine(Path.GetTempPath(), "rlottie-editor-contract-" + Guid.NewGuid().ToString("N"));
            mProject = Path.Combine(mRoot, "project"); mPayload = Path.Combine(mRoot, "payload");
            mStaging = Path.Combine(mProject, LottieWebGLArchiveStager.GeneratedRelativePath);
            Directory.CreateDirectory(Path.Combine(mProject, "Assets"));
            Directory.CreateDirectory(mPayload);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(mRoot)) Directory.Delete(mRoot, true);
        }

        [TestCase("{\"a\":1,\"a\":2}")]
        [TestCase("{\"a\":1,}")]
        [TestCase("[1,]")]
        [TestCase("01")]
        [TestCase("1.0")]
        [TestCase("1e2")]
        [TestCase("true false")]
        [TestCase("/*x*/1")]
        [TestCase("\"\\ud800\"")]
        [TestCase("\"\\udc00\"")]
        [TestCase("\"\\ud800x\"")]
        public void StrictJsonRejectsMalformedInput(string json)
        {
            Assert.Throws<BuildFailedException>(() => DiagnosticJson.Parse(Encoding.UTF8.GetBytes(json)));
        }

        [Test]
        public void StrictJsonRejectsInvalidEncodingAndBom()
        {
            Assert.Throws<BuildFailedException>(() => DiagnosticJson.Parse(new byte[] { 0xc0, 0xaf }));
            Assert.Throws<BuildFailedException>(() => DiagnosticJson.Parse(new byte[] { 0xef, 0xbb, 0xbf, 0x31 }));
            Assert.That(DiagnosticJson.Parse(Encoding.UTF8.GetBytes("\"\\ud83c\\udf38\"")), Is.EqualTo("🌸"));
        }

        [TestCase("C:relative")]
        [TestCase("\\rooted")]
        [TestCase("relative")]
        [TestCase("C:/")]
        [TestCase("C:/a/../b")]
        [TestCase("C:/a:stream")]
        [TestCase("C:/a./b")]
        [TestCase("C:/a /b")]
        [TestCase("C:/NUL.txt")]
        [TestCase("//server/share/a")]
        public void AbsolutePathsRejectAliases(string path)
        {
            Assert.Throws<BuildFailedException>(() => DiagnosticPaths.Absolute(path));
        }

        [TestCase("a/../b")]
        [TestCase("a\\b")]
        [TestCase("a//b")]
        [TestCase("a/CON")]
        [TestCase("a/stream:x")]
        [TestCase("a/line\nb")]
        [TestCase("a/quote\"b")]
        public void RelativePathsRejectAliases(string path)
        {
            Assert.Throws<BuildFailedException>(() => DiagnosticPaths.Relative(path));
        }

        [Test]
        public void ExternalPathsRejectProtectedAncestorsAndGitDescendants()
        {
            Assert.Throws<BuildFailedException>(() => DiagnosticPaths.External(mRoot, mProject));
            Assert.Throws<BuildFailedException>(() => DiagnosticPaths.External(mPayload, mProject, Path.Combine(mPayload, "toolchain")));
            Directory.CreateDirectory(Path.Combine(mPayload, ".git"));
            Assert.Throws<BuildFailedException>(() => DiagnosticPaths.External(Path.Combine(mPayload, "output"), mProject));
        }

        private static BuildPlayerOptions Options()
        {
            return new BuildPlayerOptions { target = BuildTarget.WebGL, options = BuildOptions.Development,
                extraScriptingDefines = new[] { LottieWebGLDiagnosticBuild.Define } };
        }

        [Test]
        public void OptionsRequireExactNonpersistentSingleThreadDevelopmentSelection()
        {
            var options = Options();
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticBuild.ValidateOptions(options, false, "OTHER"));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateOptions(options, true, ""));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateOptions(options, false, LottieWebGLDiagnosticBuild.Define));
            options.options = BuildOptions.None;
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateOptions(options, false, ""));
            options = Options(); options.target = BuildTarget.StandaloneWindows64;
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateOptions(options, false, ""));
            foreach (var defines in new[] { new string[0], new[] { LottieWebGLDiagnosticBuild.Define, LottieWebGLDiagnosticBuild.Define },
                new[] { "OTHER;" + LottieWebGLDiagnosticBuild.Define } })
            {
                options = Options(); options.extraScriptingDefines = defines;
                Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateOptions(options, false, ""));
            }
        }

        [Test]
        public void GraphicsApiMustSelectOnlyWebGl2Explicitly()
        {
            var webgl2 = new[] { GraphicsDeviceType.OpenGLES3 };
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticBuild.ValidateGraphicsApis(false, webgl2));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateGraphicsApis(true, webgl2));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateGraphicsApis(false, null));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateGraphicsApis(false, new GraphicsDeviceType[0]));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateGraphicsApis(false, new[] { GraphicsDeviceType.Vulkan }));
        }

        [Test]
        public void CommandLineRequiresExactPairedUniqueWrapperSelection()
        {
            string option = LottieWebGLDiagnosticBuild.ManifestOption, hashOption = LottieWebGLDiagnosticBuild.HashOption;
            string hash = new string('a', 64), path = Path.Combine(mPayload, "manifest.json");
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticBuild.ValidateCommandLine(new[] { option, path, hashOption, hash }, path, hash, true));
            foreach (var args in new[] { new[] { option }, new[] { option, path }, new[] { hashOption, hash },
                new[] { option, path, option, path, hashOption, hash }, new[] { option.ToUpperInvariant(), path, hashOption, hash } })
                Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateCommandLine(args, path, hash, true));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticBuild.ValidateCommandLine(new[] { option, path, hashOption, hash }, path, hash, false));
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticBuild.ValidateCommandLine(new string[0], null, null, false));
        }

        [Test]
        public void ManifestHashAndAbiFailureLeaveOwnedProjectBytesUntouched()
        {
            PriorOwned();
            var before = DiagnosticStagingTransaction.Hashes(DiagnosticStagingTransaction.Snapshot(mStaging));
            string path = Path.Combine(mPayload, "manifest.json"); File.WriteAllText(path, "{}");
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.Load(path, new string('a', 64), mProject,
                Path.Combine(mProject, "package"), "C:/Unity/Editor/Unity.exe"));
            var fields = new[] { "schema", "abiVersion", "headerBytes", "recordBytes", "capacity", "nativeDefine", "managedDefine",
                "target", "variant", "configuration", "threading", "sourceCommit", "rlottieCommit", "unityPluginApiHeaders", "sourceInputs",
                "toolchain", "compilerFlags", "plugin", "rlottie", "baselinePluginSha256", "baselineRlottieSha256", "exports", "buildReceipt", "exportsReceipt" };
            var data = fields.ToDictionary(x => x, x => (object)null);
            data["schema"] = "lottie.webgl.native-state-trace/1"; data["abiVersion"] = 2L;
            File.WriteAllText(path, DiagnosticJson.Encode(data));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.Load(path, DiagnosticPaths.Hash(path), mProject,
                Path.Combine(mProject, "package"), "C:/Unity/Editor/Unity.exe"));
            Assert.That(DiagnosticStagingTransaction.Equal(before, DiagnosticStagingTransaction.Hashes(DiagnosticStagingTransaction.Snapshot(mStaging))), Is.True);
            Assert.That(Directory.Exists(Path.Combine(mPayload, "staging-transactions")), Is.False);
        }

        [Test]
        public void ReceiptSchemasRejectUnknownKeysAndDifferentIdentity()
        {
            string path = Path.Combine(mPayload, "receipt.json");
            File.WriteAllText(path, "{\"schema\":\"receipt/1\",\"value\":1}");
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticManifest.ReadReceipt(path, "receipt/1", "value"));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ReadReceipt(path, "receipt/2", "value"));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ReadReceipt(path, "receipt/1"));
            var a = DiagnosticJson.Parse(Encoding.UTF8.GetBytes("{\"sourceCommit\":\"a\",\"inputs\":[1,2]}"));
            var b = DiagnosticJson.Parse(Encoding.UTF8.GetBytes("{\"inputs\":[1,2],\"sourceCommit\":\"a\"}"));
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticManifest.Same(a, b));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.Same(a,
                DiagnosticJson.Parse(Encoding.UTF8.GetBytes("{\"inputs\":[2,1],\"sourceCommit\":\"a\"}"))));
        }

        [Test]
        public void SymbolInventoriesRequireCanonicalCompleteNamesAndHash()
        {
            string hash = DiagnosticPaths.HashBytes(Encoding.UTF8.GetBytes("a\nb\n"));
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticManifest.ValidateSymbolInventory(new[] { "a", "b" }, hash));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateSymbolInventory(new[] { "b", "a" }, hash));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateSymbolInventory(new[] { "a", "a" }, hash));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateSymbolInventory(new[] { "a", "b c" }, hash));
            Assert.That(LottieWebGLDiagnosticManifest.NormalizeSymbols("archive.a:\na T 0 1\nb U 0 0\na T 0 1\n"), Is.EqualTo("a\nb\n"));
            Assert.That(LottieWebGLDiagnosticManifest.NormalizeSymbols("", true), Is.Empty);
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.NormalizeSymbols(""));
        }

        [Test]
        public void NativeBuildSelectsOnlyOneNamedTarget()
        {
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticManifest.ValidateBuildCommand(new[] { "cmake", "--build", "C:/build", "--target", "LottiePlugin", "--parallel", "2" }));
            foreach (var args in new[] { new[] { "cmake", "LottiePlugin" }, new[] { "cmake", "--target", "Other", "LottiePlugin" },
                new[] { "cmake", "--target", "LottiePlugin", "Other" }, new[] { "cmake", "--target", "LottiePlugin", "--target", "Other" } })
                Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateBuildCommand(args));
        }

        [Test]
        public void ActualCompileCommandsRequireAllAbiFlagsAndNoThreads()
        {
            string command = "\"C:/Program Files/Unity/python.exe\" \"C:/Unity/emcc.py\" -DLOTTIE_WEBGL_NATIVE_STATE_TRACE=1 -fwasm-exceptions -sSUPPORT_LONGJMP=wasm -sWASM_LEGACY_EXCEPTIONS=1 -c C:/source.cpp";
            Assert.DoesNotThrow(() => LottieWebGLDiagnosticManifest.ValidateCompileCommand(command, true));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateCompileCommand(command.Replace("-fwasm-exceptions", ""), true));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateCompileCommand(command.Replace("-DLOTTIE_WEBGL_NATIVE_STATE_TRACE=1", ""), true));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateCompileCommand(command + " -pthread", true));
            Assert.Throws<BuildFailedException>(() => LottieWebGLDiagnosticManifest.ValidateCompileCommand(command + " -sUSE_PTHREADS=1", false));
        }

        private LottieWebGLDiagnosticManifest Manifest()
        {
            var manifest = new LottieWebGLDiagnosticManifest { Root = mPayload, Hash = new string('a', 64), SourceCommit = new string('b', 40),
                Plugin = Path.Combine(mPayload, "plugin.a"), Rlottie = Path.Combine(mPayload, "rlottie.a") };
            File.WriteAllBytes(manifest.Plugin, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(manifest.Rlottie, new byte[] { 4, 5, 6 });
            manifest.VerifiedFiles.Add(manifest.Plugin, DiagnosticPaths.Hash(manifest.Plugin));
            manifest.VerifiedFiles.Add(manifest.Rlottie, DiagnosticPaths.Hash(manifest.Rlottie));
            return manifest;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StagingRestoresExactAbsentOrPriorOwnedBytes(bool priorOwned)
        {
            if (priorOwned) PriorOwned();
            var before = DiagnosticStagingTransaction.Hashes(DiagnosticStagingTransaction.Snapshot(mStaging));
            var transaction = new DiagnosticStagingTransaction(mProject, Manifest(), () => { }, path => { });
            transaction.Stage(); transaction.Restore(); transaction.Restore();
            Assert.That(DiagnosticStagingTransaction.Equal(before, DiagnosticStagingTransaction.Hashes(DiagnosticStagingTransaction.Snapshot(mStaging))), Is.True);
            Assert.That(Directory.Exists(mStaging), Is.EqualTo(priorOwned));
            Assert.That(Directory.GetFiles(mPayload, "active.json", SearchOption.AllDirectories), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImporterFailureRestoresExactBytes(bool priorOwned)
        {
            if (priorOwned) PriorOwned();
            var before = DiagnosticStagingTransaction.Hashes(DiagnosticStagingTransaction.Snapshot(mStaging));
            var transaction = new DiagnosticStagingTransaction(mProject, Manifest(), () => { }, path => { throw new IOException("injected importer failure"); });
            Assert.Throws<IOException>(() => transaction.Stage());
            transaction.Restore();
            Assert.That(DiagnosticStagingTransaction.Equal(before, DiagnosticStagingTransaction.Hashes(DiagnosticStagingTransaction.Snapshot(mStaging))), Is.True);
        }

        [Test]
        public void FailureBeforeOwnershipMarkerRestoresAbsentDirectory()
        {
            var transaction = new DiagnosticStagingTransaction(mProject, Manifest(), () => { }, path => { });
            Assert.Throws<IOException>(() => transaction.Stage(() => { throw new IOException("injected marker boundary failure"); }));
            transaction.Restore();
            Assert.That(Directory.Exists(mStaging), Is.False);
        }

        [Test]
        public void IncompleteRollbackRetainsJournalAndBlocksNextAttempt()
        {
            var manifest = Manifest();
            var transaction = new DiagnosticStagingTransaction(mProject, manifest, () => { }, path => { });
            transaction.Stage(); File.WriteAllText(Path.Combine(mStaging, "owner-file"), "preserve me");
            Assert.Throws<BuildFailedException>(() => transaction.Restore());
            Assert.That(Directory.GetFiles(mPayload, "active.json", SearchOption.AllDirectories).Length, Is.EqualTo(1));
            Assert.Throws<BuildFailedException>(() => new DiagnosticStagingTransaction(mProject, manifest, () => { }, path => { }));
            Assert.That(File.ReadAllText(Path.Combine(mStaging, "owner-file")), Is.EqualTo("preserve me"));
        }

        [Test]
        public void RollbackImportFailureKeepsRecoveryJournal()
        {
            int refreshes = 0;
            var transaction = new DiagnosticStagingTransaction(mProject, Manifest(), () => { if (++refreshes == 2) throw new IOException("rollback import failure"); }, path => { });
            transaction.Stage();
            Assert.Throws<IOException>(() => transaction.Restore());
            Assert.That(Directory.GetFiles(mPayload, "active.json", SearchOption.AllDirectories).Length, Is.EqualTo(1));
        }

        [Test]
        public void InvalidOwnedStateAndChangedInputsRejectBeforeProjectMutation()
        {
            var manifest = Manifest();
            Directory.CreateDirectory(mStaging); File.WriteAllText(Path.Combine(mStaging, "owner-file"), "preserve me");
            Assert.Throws<BuildFailedException>(() => new DiagnosticStagingTransaction(mProject, manifest, () => { }, path => { }));
            Assert.That(Directory.Exists(Path.Combine(mPayload, "staging-transactions")), Is.False);
            File.Delete(Path.Combine(mStaging, "owner-file")); Directory.Delete(mStaging);
            var transaction = new DiagnosticStagingTransaction(mProject, manifest, () => { }, path => { });
            File.AppendAllText(manifest.Plugin, "changed");
            Assert.Throws<BuildFailedException>(() => transaction.Stage());
            Assert.Throws<BuildFailedException>(() => transaction.Restore());
            Assert.That(Directory.Exists(mStaging), Is.False);
            Assert.That(Directory.GetFiles(mPayload, "active.json", SearchOption.AllDirectories).Length, Is.EqualTo(1));
        }

        private void PriorOwned()
        {
            Directory.CreateDirectory(mStaging);
            File.WriteAllText(Path.Combine(mStaging, LottieWebGLArchiveStager.OwnershipMarker), LottieWebGLArchiveStager.OwnershipValue);
            File.WriteAllBytes(Path.Combine(mStaging, "libLottiePlugin.a"), new byte[] { 99, 0, 31 });
            File.WriteAllText(Path.Combine(mStaging, "libLottiePlugin.a.meta"), "exact prior importer metadata\r\n");
            File.WriteAllText(mStaging + ".meta", "exact prior directory metadata\r\n");
        }
    }
}
#endif

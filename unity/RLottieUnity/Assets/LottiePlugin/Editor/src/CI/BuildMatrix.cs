using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace RLottie.CI
{
    public static class BuildMatrix
    {
        private const string SmokeScene = "Assets/Scenes/Main.unity";

        public static void Build()
        {
            try
            {
                string targetName = GetArgument("-ciTarget", "Windows64");
                string outputPath = GetArgument("-ciOutputPath", GetDefaultOutput(targetName));
                string expectedPipeline = GetArgument("-ciPipeline", "Auto");
                bool connectProfiler = GetBooleanArgument("-ciAutoconnectProfiler", false);
                bool developmentBuild = GetBooleanArgument("-ciDevelopment", false) || connectProfiler;

                ValidateRenderPipeline(expectedPipeline);
                EnsureSceneExists();

                BuildTarget target = ParseTarget(targetName);
                BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    throw new InvalidOperationException("Failed to switch the active build target to " + target + ".");
                }

                EnsureOutputDirectory(outputPath, target);

                BuildOptions buildOptions = BuildOptions.StrictMode;
                if (developmentBuild)
                {
                    buildOptions |= BuildOptions.Development;
                }
                if (connectProfiler)
                {
                    buildOptions |= BuildOptions.ConnectWithProfiler;
                }

                BuildPlayerOptions options = new BuildPlayerOptions
                {
                    scenes = new[] { SmokeScene },
                    locationPathName = outputPath,
                    target = target,
                    options = buildOptions
                };

                Debug.Log("RLottie CI build: target=" + target + ", output=" + outputPath +
                    ", development=" + developmentBuild + ", autoconnectProfiler=" + connectProfiler);
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log("RLottie CI result: " + summary.result + ", errors=" + summary.totalErrors +
                    ", warnings=" + summary.totalWarnings + ", size=" + summary.totalSize);

                if (summary.result != BuildResult.Succeeded || summary.totalErrors != 0)
                {
                    throw new InvalidOperationException("Player build failed: " + summary.result +
                        " (errors=" + summary.totalErrors + ").");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static BuildTarget ParseTarget(string targetName)
        {
            switch (targetName.ToLowerInvariant())
            {
                case "windows64":
                case "standalonewindows64":
                    return BuildTarget.StandaloneWindows64;
                case "android":
                    return BuildTarget.Android;
                case "webgl":
                    return BuildTarget.WebGL;
                case "ios":
                    return BuildTarget.iOS;
                default:
                    throw new ArgumentException("Unsupported -ciTarget value: " + targetName);
            }
        }

        private static string GetDefaultOutput(string targetName)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../out/ci"));
            switch (targetName.ToLowerInvariant())
            {
                case "android":
                    return Path.Combine(root, "android", "RLottieSmoke.apk");
                case "webgl":
                    return Path.Combine(root, "webgl");
                case "ios":
                    return Path.Combine(root, "ios");
                default:
                    return Path.Combine(root, "windows", "RLottieSmoke.exe");
            }
        }

        private static void EnsureOutputDirectory(string outputPath, BuildTarget target)
        {
            string directory = target == BuildTarget.WebGL || target == BuildTarget.iOS
                ? outputPath
                : Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Invalid output path: " + outputPath);
            }
            Directory.CreateDirectory(directory);
        }

        private static void EnsureSceneExists()
        {
            string absoluteScene = Path.GetFullPath(Path.Combine(Application.dataPath, "../", SmokeScene));
            if (!File.Exists(absoluteScene))
            {
                throw new FileNotFoundException("Smoke-test scene is missing.", absoluteScene);
            }
        }

        private static void ValidateRenderPipeline(string expectedPipeline)
        {
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/manifest.json"));
            string manifest = File.ReadAllText(manifestPath);
            string detected = manifest.Contains("com.unity.render-pipelines.high-definition") ? "HDRP" :
                manifest.Contains("com.unity.render-pipelines.universal") ? "URP" : "BuiltIn";

            if (!string.Equals(expectedPipeline, "Auto", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(expectedPipeline, detected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Render pipeline mismatch: expected " + expectedPipeline +
                    ", package manifest is " + detected + ".");
            }

            RenderPipelineAsset asset = GraphicsSettings.renderPipelineAsset;
            if (asset == null)
            {
                asset = QualitySettings.renderPipeline;
            }

            if (detected == "BuiltIn" && asset != null)
            {
                throw new InvalidOperationException("Built-in pipeline branch has a Scriptable Render Pipeline asset assigned: " + asset.name);
            }
            if (detected != "BuiltIn" && asset == null)
            {
                throw new InvalidOperationException(detected + " package is installed but no render pipeline asset is assigned.");
            }

            string assetType = asset == null ? "none" : asset.GetType().FullName;
            if (detected == "URP" && assetType.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("URP branch uses the wrong render pipeline asset: " + assetType);
            }
            if (detected == "HDRP" && assetType.IndexOf("HDRenderPipeline", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("HDRP branch uses the wrong render pipeline asset: " + assetType);
            }

            Debug.Log("RLottie render pipeline validation passed: " + detected + " (asset=" + assetType + ").");
        }

        private static string GetArgument(string name, string fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }
            return fallback;
        }

        private static bool GetBooleanArgument(string name, bool fallback)
        {
            string value = GetArgument(name, fallback ? "true" : "false");
            if (bool.TryParse(value, out bool parsed))
            {
                return parsed;
            }
            throw new ArgumentException("Invalid boolean value for " + name + ": " + value);
        }
    }
}

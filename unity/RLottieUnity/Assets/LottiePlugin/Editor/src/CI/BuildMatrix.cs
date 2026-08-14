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
            GraphicsApiState graphicsApiState = default(GraphicsApiState);
            try
            {
                string targetName = GetArgument("-ciTarget", "Windows64");
                string outputPath = GetArgument("-ciOutputPath", GetDefaultOutput(targetName));
                string expectedPipeline = GetArgument("-ciPipeline", "Auto");
                string requestedGraphicsApi = GetArgument("-ciGraphicsApi", "Auto");
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

                graphicsApiState = ConfigureGraphicsApi(target, requestedGraphicsApi);
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
            finally
            {
                RestoreGraphicsApi(graphicsApiState);
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
                case "linux64":
                case "standalonelinux64":
                    return BuildTarget.StandaloneLinux64;
                case "webgl":
                    return BuildTarget.WebGL;
                case "ios":
                    return BuildTarget.iOS;
                case "macos":
                case "standaloneosx":
                    return BuildTarget.StandaloneOSX;
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
                case "linux64":
                case "standalonelinux64":
                    return Path.Combine(root, "linux", "RLottieSmoke.x86_64");
                case "webgl":
                    return Path.Combine(root, "webgl");
                case "ios":
                    return Path.Combine(root, "ios");
                case "macos":
                case "standaloneosx":
                    return Path.Combine(root, "macos", "RLottieSmoke.app");
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

        private static GraphicsApiState ConfigureGraphicsApi(BuildTarget target, string requestedGraphicsApi)
        {
            if (string.Equals(requestedGraphicsApi, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return default(GraphicsApiState);
            }

            GraphicsDeviceType graphicsApi = ParseGraphicsApi(requestedGraphicsApi);
            ValidateGraphicsApiTarget(graphicsApi, target);

            GraphicsApiState state = new GraphicsApiState
            {
                Target = target,
                Automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(target),
                Apis = PlayerSettings.GetGraphicsAPIs(target),
                Changed = true
            };

            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            GraphicsDeviceType[] graphicsApis = graphicsApi == GraphicsDeviceType.Vulkan &&
                target == BuildTarget.StandaloneLinux64
                ? new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore }
                : new[] { graphicsApi };
            PlayerSettings.SetGraphicsAPIs(target, graphicsApis);
            Debug.Log("RLottie CI graphics APIs configured as " + string.Join(", ", graphicsApis) +
                " for " + target + ".");
            return state;
        }

        private static GraphicsDeviceType ParseGraphicsApi(string requestedGraphicsApi)
        {
            switch (requestedGraphicsApi.ToLowerInvariant())
            {
                case "direct3d11":
                    return GraphicsDeviceType.Direct3D11;
                case "direct3d12":
                    return GraphicsDeviceType.Direct3D12;
                case "openglcore":
                    return GraphicsDeviceType.OpenGLCore;
                case "vulkan":
                    return GraphicsDeviceType.Vulkan;
                case "opengles3":
                    return GraphicsDeviceType.OpenGLES3;
                default:
                    throw new ArgumentException("Unsupported -ciGraphicsApi value: " + requestedGraphicsApi);
            }
        }

        private static void ValidateGraphicsApiTarget(GraphicsDeviceType graphicsApi, BuildTarget target)
        {
            if (graphicsApi == GraphicsDeviceType.OpenGLES3 && target != BuildTarget.Android)
            {
                throw new InvalidOperationException("OpenGLES3 is only supported by the Android CI target.");
            }

            if ((graphicsApi == GraphicsDeviceType.Direct3D11 || graphicsApi == GraphicsDeviceType.Direct3D12) &&
                target != BuildTarget.StandaloneWindows64)
            {
                throw new InvalidOperationException(graphicsApi + " is only supported by the Windows CI target.");
            }

            if (graphicsApi == GraphicsDeviceType.OpenGLCore && target != BuildTarget.StandaloneWindows64 &&
                target != BuildTarget.StandaloneLinux64)
            {
                throw new InvalidOperationException("OpenGLCore is only supported by the Windows and Linux CI targets.");
            }

            if (graphicsApi == GraphicsDeviceType.Vulkan && target != BuildTarget.Android &&
                target != BuildTarget.StandaloneLinux64 && target != BuildTarget.StandaloneWindows64)
            {
                throw new InvalidOperationException("Vulkan is not supported by this CI target: " + target);
            }
        }

        private static void RestoreGraphicsApi(GraphicsApiState state)
        {
            if (!state.Changed)
            {
                return;
            }

            try
            {
                PlayerSettings.SetGraphicsAPIs(state.Target, state.Apis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(state.Target, state.Automatic);
                Debug.Log("RLottie CI graphics API settings restored for " + state.Target + ".");
            }
            catch (Exception exception)
            {
                Debug.LogError("Failed to restore graphics API settings for " + state.Target + ": " + exception);
            }
        }

        private struct GraphicsApiState
        {
            public BuildTarget Target;
            public bool Automatic;
            public GraphicsDeviceType[] Apis;
            public bool Changed;
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

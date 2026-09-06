using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RLottie.CI
{
    /// <summary>
    /// Selects a specific WebGL graphics API for CI, then delegates to the
    /// regular player builder. Unity 2019 is required for WebGL 1 builds.
    /// </summary>
    public static class WebGLBuildMatrix
    {
        public static void Build()
        {
            bool automatic = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.WebGL);
            GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);

            try
            {
                string version = GetArgument("-ciWebGLVersion", "2");
                GraphicsDeviceType graphicsApi;
                switch (version)
                {
                    case "1":
                        graphicsApi = GraphicsDeviceType.OpenGLES2;
                        break;
                    case "2":
                        graphicsApi = GraphicsDeviceType.OpenGLES3;
                        break;
                    default:
                        throw new ArgumentException("Unsupported -ciWebGLVersion value: " + version);
                }

                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { graphicsApi });
                Debug.Log("RLottie CI WebGL version configured as WebGL " + version +
                    " (" + graphicsApi + ").");
                BuildMatrix.Build();
            }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, graphicsApis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, automatic);
                Debug.Log("RLottie CI WebGL graphics API settings restored.");
            }
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
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LottiePlugin.Tests.Runtime
{
    public class ColorCalibrationAnimationTests
    {
        private const int TextureSize = 512;
        private const byte ColorTolerance = 2;

        [UnityTest]
        public IEnumerator CalibrationBarsRenderExactDeviceColors()
        {
            TextAsset animationJson = Resources.Load<TextAsset>("device_color_calibration_bars");
            Assert.NotNull(animationJson);

            LottieAnimation animation = LoadManaged(animationJson);
            try
            {
                animation.DrawOneFrame(0);
                yield return null;

                AssertCalibrationBars(animation.Texture.GetPixels32(), "managed upload");
            }
            finally
            {
                animation.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator CalibrationBarsRenderExactDeviceColorsWithNativeUpload()
        {
            TextAsset animationJson = Resources.Load<TextAsset>("device_color_calibration_bars");
            Assert.NotNull(animationJson);

            LottieAnimation animation = LottieAnimation.LoadFromJsonData(
                animationJson.text,
                string.Empty,
                TextureSize,
                TextureSize);
            try
            {
                animation.DrawOneFrame(0);
                yield return null;

                Assert.AreEqual(LottieTextureUploadBackend.NativeExternalTexture, animation.TextureUploadBackend);
                AssertCalibrationBars(Readback(animation.OutputTexture), "native upload");
            }
            finally
            {
                animation.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator AnimatedReferenceCyclesThroughExactRgbAndCmyColors()
        {
            TextAsset animationJson = Resources.Load<TextAsset>("device_color_calibration_alpha");
            Assert.NotNull(animationJson);

            LottieAnimation animation = LoadManaged(animationJson);
            try
            {
                int[] frames = { 0, 15, 30, 45, 60, 75 };
                Color32[] expected =
                {
                    new Color32(255, 0, 0, 255),
                    new Color32(0, 255, 0, 255),
                    new Color32(0, 0, 255, 255),
                    new Color32(0, 255, 255, 255),
                    new Color32(255, 0, 255, 255),
                    new Color32(255, 255, 0, 255)
                };

                for (int index = 0; index < frames.Length; index++)
                {
                    animation.DrawOneFrame(frames[index]);
                    yield return null;
                    AssertColor(expected[index], Sample(animation.Texture.GetPixels32(), 256, 428),
                        $"animated exact-color reference at frame {frames[index]}");
                }
            }
            finally
            {
                animation.Dispose();
            }
        }

        private static LottieAnimation LoadManaged(TextAsset animationJson)
        {
            return LottieAnimation.LoadFromJsonData(
                animationJson.text,
                string.Empty,
                TextureSize,
                TextureSize,
                new LottieAnimationOptions
                {
                    UseManagedTextureUpload = true
                });
        }

        private static Color32 Sample(Color32[] pixels, int x, int y)
        {
            return pixels[y * TextureSize + x];
        }

        private static Color32[] Readback(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture renderTexture = RenderTexture.GetTemporary(
                TextureSize,
                TextureSize,
                0,
                RenderTextureFormat.ARGB32);
            Texture2D readable = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(texture, renderTexture);
                RenderTexture.active = renderTexture;
                readable.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
                readable.Apply();
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                Object.DestroyImmediate(readable);
            }
        }

        private static void AssertCalibrationBars(Color32[] pixels, string uploadBackend)
        {
            int[] centers = { 36, 99, 162, 225, 288, 351, 414, 477 };
            Color32[] expected =
            {
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 0, 255),
                new Color32(0, 255, 255, 255),
                new Color32(0, 255, 0, 255),
                new Color32(255, 0, 255, 255),
                new Color32(255, 0, 0, 255),
                new Color32(0, 0, 255, 255),
                new Color32(0, 0, 0, 255)
            };

            for (int index = 0; index < centers.Length; index++)
            {
                AssertColor(expected[index], Sample(pixels, centers[index], 256),
                    $"{uploadBackend} calibration bar {index}");
            }
        }

        private static void AssertColor(Color32 expected, Color32 actual, string context)
        {
            Assert.That(actual.r, Is.InRange(expected.r - ColorTolerance, expected.r + ColorTolerance),
                $"Unexpected red channel for {context}: {actual}.");
            Assert.That(actual.g, Is.InRange(expected.g - ColorTolerance, expected.g + ColorTolerance),
                $"Unexpected green channel for {context}: {actual}.");
            Assert.That(actual.b, Is.InRange(expected.b - ColorTolerance, expected.b + ColorTolerance),
                $"Unexpected blue channel for {context}: {actual}.");
            Assert.That(actual.a, Is.InRange(255 - ColorTolerance, 255),
                $"Unexpected alpha channel for {context}: {actual}.");
        }
    }
}

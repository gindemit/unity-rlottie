using System;
using System.IO;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace LottiePlugin.Tests.Runtime
{
    public sealed class LottieCpuAndFrameCacheTests
    {
        [Test]
        public void LiveAndCpuLoadersValidateJsonStructureBeforeNativeParsing()
        {
            const string incompleteJson = "{";
            string path = Path.Combine(Application.temporaryCachePath, Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path, incompleteJson);
                Assert.Throws<FormatException>(() => LottieAnimation.LoadFromJsonData(incompleteJson, "", 8, 8));
                Assert.Throws<FormatException>(() => LottieAnimation.LoadFromJsonFile(path, 8, 8));
                Assert.Throws<FormatException>(() => LottieCpuRasterizer.LoadFromJsonData(incompleteJson, "", 8, 8));
                Assert.Throws<FormatException>(() => LottieCpuRasterizer.LoadFromJsonFile(path, 8, 8));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void FileAndMemoryAnimationsRenderTheSameMarkerFrame()
        {
            string json = WithMarker(Resources.Load<TextAsset>("device_color_calibration_alpha").text, "clip", 5, 28);
            string path = Path.Combine(Application.temporaryCachePath, Guid.NewGuid() + ".json");
            var options = new LottieAnimationOptions { UseManagedTextureUpload = true };
            var previousConversion = LottieWebGLSettings.ColorConversionMode;
            try
            {
                LottieWebGLSettings.ColorConversionMode = WebGLColorConversionMode.Native;
                File.WriteAllText(path, json);
                using (var file = LottieAnimation.LoadFromJsonFile(path, 16, 16, options))
                using (var data = LottieAnimation.LoadFromJsonData(json, "", 16, 16, options))
                {
                    Assert.AreEqual(data.TotalFramesCount, file.TotalFramesCount);
                    Assert.AreEqual(data.Markers.Frame("clip", 1), file.Markers.Frame("clip", 1));
                    data.DrawMarkerFrame("clip", 1);
                    file.DrawMarkerFrame("clip", 1);
                    Assert.AreEqual(Hash(data.Texture.GetRawTextureData<byte>()), Hash(file.Texture.GetRawTextureData<byte>()));
                }
            }
            finally
            {
                LottieWebGLSettings.ColorConversionMode = previousConversion;
                File.Delete(path);
            }
        }

        [Test]
        public void ReloadingAReplacedFileUsesItsCurrentMarkersAndPixels()
        {
            string first = Resources.Load<TextAsset>("semantic_palette").text;
            string second = first.Replace("\"clip\"", "\"replacement\"").Replace("[0,0,1,1]", "[0,1,0,1]");
            string path = Path.Combine(Application.temporaryCachePath, Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(path, first);
                AssertFileFrame(path, "clip", new Color32(0, 0, 255, 255));
                File.WriteAllText(path, second);
                AssertFileFrame(path, "replacement", new Color32(0, 255, 0, 255));
            }
            finally { File.Delete(path); }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        [Test]
        public void FileLoadersRenderExternalPngRelativeToJsonDirectory()
        {
            // A solid green 16x16 PNG. The PNG and JSON share a temporary
            // directory so that neither Unity Resources nor the cwd can supply it.
            const string greenPng = "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAFklEQVR4nGNg+E8hHDVg1IBRA4aLAQAxtf4QMNSIagAAAABJRU5ErkJggg==";
            const string json = "{\"v\":\"5.7.4\",\"fr\":30,\"ip\":0,\"op\":2,\"w\":16,\"h\":16," +
                "\"assets\":[{\"id\":\"pixel\",\"w\":16,\"h\":16,\"u\":\"images/\",\"p\":\"green.png\",\"e\":0}]," +
                "\"markers\":[{\"cm\":\"image\",\"tm\":0,\"dr\":2}]," +
                "\"layers\":[{\"ty\":2,\"refId\":\"pixel\",\"ind\":1,\"ip\":0,\"op\":2,\"st\":0," +
                "\"ks\":{\"o\":{\"a\":0,\"k\":100},\"r\":{\"a\":0,\"k\":0},\"p\":{\"a\":0,\"k\":[0,0]}," +
                "\"a\":{\"a\":0,\"k\":[0,0]},\"s\":{\"a\":0,\"k\":[100,100]}}}]}";
            string directory = Path.Combine(Application.temporaryCachePath, Guid.NewGuid().ToString());
            string images = Path.Combine(directory, "images");
            string pngPath = Path.Combine(images, "green.png");
            string jsonPath = Path.Combine(directory, "animation.json");
            Directory.CreateDirectory(images);
            try
            {
                File.WriteAllBytes(pngPath, Convert.FromBase64String(greenPng));
                File.WriteAllText(jsonPath, json);
                AssertFileFrame(jsonPath, "image", new Color32(0, 255, 0, 255));
            }
            finally
            {
                File.Delete(jsonPath);
                File.Delete(pngPath);
                Directory.Delete(images);
                Directory.Delete(directory);
            }
        }
#endif

        private static void AssertFileFrame(string path, string marker, Color32 expected)
        {
            var options = new LottieAnimationOptions { UseManagedTextureUpload = true };
            var previousConversion = LottieWebGLSettings.ColorConversionMode;
            try
            {
                LottieWebGLSettings.ColorConversionMode = WebGLColorConversionMode.Native;
                using (var live = LottieAnimation.LoadFromJsonFile(path, 16, 16, options))
                using (var cpu = LottieCpuRasterizer.LoadFromJsonFile(path, 16, 16))
                using (var pixels = new NativeArray<byte>(cpu.ByteCount, Allocator.Temp))
                {
                    live.DrawMarkerFrame(marker, 0);
                    cpu.RenderMarkerFrame(marker, 0, pixels);
                    AssertBgraApproximately(pixels, 16, 8, 8, expected.b, expected.g, expected.r, expected.a);
                    const int center = 8 * 16 + 8;
                    int offset = center * 4;
                    var cpuColor = new Color32(pixels[offset + 2], pixels[offset + 1], pixels[offset], pixels[offset + 3]);
                    Assert.AreEqual(cpuColor, live.Texture.GetPixels32()[center]);
                }
            }
            finally { LottieWebGLSettings.ColorConversionMode = previousConversion; }
        }

        [Test]
        public void BatchRenderWritesOnlyTheRequestedFrameRange()
        {
            string json = Resources.Load<TextAsset>("device_color_calibration_alpha").text;
            using (var rasterizer = LottieCpuRasterizer.LoadFromJsonData(json, "", 16, 16))
            using (var expected = new NativeArray<byte>(rasterizer.ByteCount, Allocator.Temp))
            {
                var pixels = new byte[rasterizer.ByteCount + 2];
                pixels[0] = 23;
                pixels[pixels.Length - 1] = 42;
                rasterizer.RenderFrame(30, expected);
                rasterizer.RenderIntoBatch(30, pixels, 1);
                Assert.AreEqual(23, pixels[0]);
                Assert.AreEqual(42, pixels[pixels.Length - 1]);
                for (int index = 0; index < expected.Length; index++)
                    Assert.AreEqual(expected[index], pixels[index + 1]);
                Assert.Throws<ArgumentOutOfRangeException>(() => rasterizer.RenderIntoBatch(30, pixels, 3));
            }
        }

        [Test]
        public void CpuRasterizerUsesCallerOwnedExactBufferAcrossFrames()
        {
            TextAsset source = Resources.Load<TextAsset>("device_color_calibration_alpha");
            string json = WithMarker(source.text, "all", 0, 120);
            using (var rasterizer = LottieCpuRasterizer.LoadFromJsonData(json, string.Empty, 64, 64))
            using (var pixels = new NativeArray<byte>(64 * 64 * 4, Allocator.Temp))
            {
                rasterizer.RenderFrame(0, pixels);
                uint first = Hash(pixels);
                for (int index = 0; index < 128; index++)
                {
                    rasterizer.RenderFrame(index % 90, pixels);
                    GC.Collect();
                }
                rasterizer.RenderFrame(30, pixels);
                Assert.AreNotEqual(first, Hash(pixels));
                var wrongSize = new NativeArray<byte>(1, Allocator.Temp);
                try { Assert.Throws<ArgumentException>(() => rasterizer.RenderFrame(0, wrongSize)); }
                finally { wrongSize.Dispose(); }
            }
        }

        [Test]
        public void RasterizersFromSameJsonShareMarkersButNotDisposal()
        {
            string json = WithMarker(Resources.Load<TextAsset>("device_color_calibration_alpha").text, "all", 0, 120);
            using (var small = LottieCpuRasterizer.LoadFromJsonData(json, string.Empty, 32, 32))
            using (var large = LottieCpuRasterizer.LoadFromJsonData(json, string.Empty, 64, 64))
            {
                Assert.AreSame(small.Markers.Markers, large.Markers.Markers);
                small.Dispose();
                Assert.Throws<ObjectDisposedException>(() => small.Markers.Get("all"));
                Assert.AreEqual(0, large.Markers.Get("all").FirstRenderFrame);
            }
        }

        [Test]
        public void SemanticFillAndStrokeOverridesMatchAcrossPublicAndBatchApis()
        {
            TextAsset source = Resources.Load<TextAsset>("semantic_palette");
            var overrides = new[]
            {
                LottieColorOverride.Fill("**.palette.base", Color.green),
                LottieColorOverride.Stroke("**.palette.ink", Color.blue)
            };
            using (var baseline = LottieCpuRasterizer.LoadFromJsonData(
                source.text, string.Empty, 16, 16))
            using (var sequential = LottieCpuRasterizer.LoadFromJsonData(
                source.text, string.Empty, 16, 16))
            using (var batched = LottieCpuRasterizer.LoadFromJsonData(
                source.text, string.Empty, 16, 16, overrides))
            using (var baselinePixels = new NativeArray<byte>(16 * 16 * 4, Allocator.Temp))
            using (var sequentialPixels = new NativeArray<byte>(16 * 16 * 4, Allocator.Temp))
            using (var batchPixels = new NativeArray<byte>(16 * 16 * 4, Allocator.Temp))
            {
                baseline.RenderFrame(0, baselinePixels);
                sequential.SetFillColor("**.palette.base", Color.green);
                sequential.SetStrokeColor("**.palette.ink", Color.blue);
                sequential.RenderFrame(0, sequentialPixels);
                batched.RenderFrame(0, batchPixels);

                Assert.AreNotEqual(Hash(baselinePixels), Hash(sequentialPixels));
                Assert.AreEqual(Hash(sequentialPixels), Hash(batchPixels));
                AssertBgraApproximately(sequentialPixels, 16, 8, 8, 0, 255, 0, 255);
                AssertContainsBlueDominantPixel(sequentialPixels);
                Assert.Throws<ArgumentException>(() => sequential.SetFillColor(string.Empty, Color.red));
                Assert.Throws<ArgumentException>(() => sequential.SetFillColor("**.palette.base\0suffix", Color.red));
                Assert.Throws<ArgumentException>(() => sequential.SetStrokeColor(
                    "**.palette.ink", new Color(float.NaN, 0, 0)));
            }
        }

        [Test]
        public void SemanticOverridesApplyAfterRenderAndEmptyBatchIsANoOp()
        {
            TextAsset source = Resources.Load<TextAsset>("semantic_palette");
            using (var rasterizer = LottieCpuRasterizer.LoadFromJsonData(
                source.text, string.Empty, 16, 16))
            using (var pixels = new NativeArray<byte>(16 * 16 * 4, Allocator.Temp))
            {
                rasterizer.RenderFrame(0, pixels);
                uint baseline = Hash(pixels);
                rasterizer.ApplyColorOverrides(Array.Empty<LottieColorOverride>());
                rasterizer.RenderFrame(1, pixels);
                Assert.AreEqual(baseline, Hash(pixels));

                rasterizer.ApplyColorOverrides(new[]
                {
                    LottieColorOverride.Fill("**.palette.base", Color.green),
                    LottieColorOverride.Stroke("**.palette.ink", Color.blue)
                });
                rasterizer.RenderFrame(0, pixels);
                Assert.AreNotEqual(baseline, Hash(pixels));
                AssertBgraApproximately(pixels, 16, 8, 8, 0, 255, 0, 255);
                AssertContainsBlueDominantPixel(pixels);
            }
        }

        [Test]
        public void FrameCacheAppliesColorOverridesBeforeWarming()
        {
            TextAsset source = Resources.Load<TextAsset>("semantic_palette");
            var options = new LottieFrameCacheOptions
            {
                Width = 16,
                Height = 16,
                Clips = new[] { new LottieClipSampling("clip", 30, false, true) },
                MakeNoLongerReadable = false,
                ColorOverrides = new[]
                {
                    LottieColorOverride.Fill("**.palette.base", Color.green),
                    LottieColorOverride.Stroke("**.palette.ink", Color.blue)
                }
            };
            using (var cache = new LottieFrameCache(source.text, string.Empty, options))
            {
                while (!cache.WarmStep(2)) { }
                Texture2D texture = cache.SampleNormalized("clip", 0);
                using (var expected = LottieCpuRasterizer.LoadFromJsonData(
                    source.text, string.Empty, 16, 16, options.ColorOverrides))
                using (var expectedPixels = new NativeArray<byte>(16 * 16 * 4, Allocator.Temp))
                {
                    expected.RenderFrame(0, expectedPixels);
                    Assert.AreEqual(Hash(expectedPixels), Hash(texture.GetRawTextureData<byte>()));
                }
            }
        }

        [Test]
        public void AnimatedTextureSupportsOptionAndMethodOverridesBeforeFirstRender()
        {
            TextAsset source = Resources.Load<TextAsset>("semantic_palette");
            var options = new LottieAnimationOptions
            {
                UseManagedTextureUpload = true,
                ColorOverrides = new[]
                {
                    LottieColorOverride.Fill("**.palette.base", Color.green),
                    LottieColorOverride.Stroke("**.palette.ink", Color.blue)
                }
            };
            using (var baseline = LottieAnimation.LoadFromJsonData(
                source.text, string.Empty, 16, 16,
                new LottieAnimationOptions { UseManagedTextureUpload = true }))
            using (var configured = LottieAnimation.LoadFromJsonData(
                source.text, string.Empty, 16, 16, options))
            using (var methods = LottieAnimation.LoadFromJsonData(
                source.text, string.Empty, 16, 16,
                new LottieAnimationOptions { UseManagedTextureUpload = true }))
            {
                methods.SetFillColor("**.palette.base", Color.green);
                methods.SetStrokeColor("**.palette.ink", Color.blue);
                baseline.DrawOneFrame(0);
                configured.DrawOneFrame(0);
                methods.DrawOneFrame(0);
                uint configuredHash = Hash(configured.Texture.GetRawTextureData<byte>());
                Assert.AreNotEqual(Hash(baseline.Texture.GetRawTextureData<byte>()), configuredHash);
                Assert.AreEqual(configuredHash, Hash(methods.Texture.GetRawTextureData<byte>()));
            }
        }

        [Test]
        public void CachePreflightDeduplicatesFramesAndReusesTextureReferences()
        {
            TextAsset source = Resources.Load<TextAsset>("device_color_calibration_alpha");
            string json = WithMarker(source.text, "clip", 0, 30);
            var options = new LottieFrameCacheOptions
            {
                Width = 32,
                Height = 32,
                Clips = new[] { new LottieClipSampling("clip", 12, false, true) },
                MakeNoLongerReadable = false
            };
            using (var cache = new LottieFrameCache(json, string.Empty, options))
            {
                Assert.AreEqual(7, cache.CachedFrameCount);
                Assert.AreEqual(0, cache.WarmedFrameCount);
                Assert.AreEqual(0.5, cache.DurationSeconds("clip"), 0.0001);
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(
                    () => cache.DurationSeconds("missing"));
                Assert.AreEqual(7L * 32 * 32 * 4, cache.EstimatedRawPixelBytes);
                Assert.IsFalse(cache.WarmStep(2));
                Assert.AreEqual(2, cache.WarmedFrameCount);
                Assert.AreEqual(7, cache.CachedFrameCount);
                while (!cache.WarmStep(2)) { }
                Assert.AreEqual(cache.CachedFrameCount, cache.WarmedFrameCount);
                Texture2D firstConsumer = cache.SampleNormalized("clip", 0);
                Texture2D secondConsumer = cache.SampleNormalized("clip", 0);
                Assert.AreSame(firstConsumer, secondConsumer);
                Assert.AreNotSame(firstConsumer, cache.SampleNormalized("clip", 1));
                Assert.Throws<ArgumentException>(() => cache.Sample("clip", double.NaN));
                Assert.IsTrue(cache.WarmStep());
            }
        }

        [Test]
        public void CacheSamplingPreservesPositionsAfterOversamplingDeduplication()
        {
            TextAsset source = Resources.Load<TextAsset>("device_color_calibration_alpha");
            string json = WithMarker(source.text, "short", 0, 3);
            var options = new LottieFrameCacheOptions
            {
                Width = 16,
                Height = 16,
                Clips = new[] { new LottieClipSampling("short", 120, false, true) },
                MakeNoLongerReadable = false
            };
            using (var cache = new LottieFrameCache(json, string.Empty, options))
            {
                while (!cache.WarmStep(8)) { }
                Texture2D beforeSecondSourceFrame = cache.SampleNormalized("short", 0.2f);
                Texture2D secondSourceFrame = cache.SampleNormalized("short", 0.4f);

                Assert.AreNotSame(beforeSecondSourceFrame, secondSourceFrame);
                Assert.AreSame(secondSourceFrame, cache.SampleNormalized("short", 0.5f));
            }
        }

        [Test]
        public void CacheRejectsBudgetAndCallsAfterDispose()
        {
            TextAsset source = Resources.Load<TextAsset>("device_color_calibration_alpha");
            string json = WithMarker(source.text, "loop", 0, 30);
            var options = new LottieFrameCacheOptions
            {
                Width = 32,
                Height = 32,
                Clips = new[] { new LottieClipSampling("loop", 12, true) },
                MaximumRawPixelBytes = 1
            };
            Assert.Throws<InvalidOperationException>(() => new LottieFrameCache(json, string.Empty, options));
            options.MaximumRawPixelBytes = long.MaxValue;
            var cache = new LottieFrameCache(json, string.Empty, options);
            cache.Dispose();
            Assert.Throws<ObjectDisposedException>(() => cache.WarmStep());
            Assert.Throws<ObjectDisposedException>(() => { int ignored = cache.WarmedFrameCount; });
            Assert.Throws<ObjectDisposedException>(() => cache.DurationSeconds("loop"));
        }

        [Test]
        public void HighSamplingRateRetainsOnlyDistinctSourceFrames()
        {
            string json = WithMarker(Resources.Load<TextAsset>("device_color_calibration_alpha").text, "clip", 0, 60);
            var options = new LottieFrameCacheOptions
            {
                Width = 1,
                Height = 1,
                Clips = new[] { new LottieClipSampling("clip", int.MaxValue, false, true) },
                MaximumRawPixelBytes = 60 * 4
            };
            using (var cache = new LottieFrameCache(json, string.Empty, options))
            {
                Assert.AreEqual(60, cache.CachedFrameCount);
                Assert.AreEqual(options.MaximumRawPixelBytes, cache.EstimatedRawPixelBytes);
            }
            options.MaximumRawPixelBytes--;
            Assert.Throws<InvalidOperationException>(() => new LottieFrameCache(json, string.Empty, options));
        }

        [Test]
        public void ManagedUploadStagingSurvivesTextureRawDataInspectionAndMutation()
        {
            TextAsset source = Resources.Load<TextAsset>("device_color_calibration_alpha");
            using (var first = LottieAnimation.LoadFromJsonData(source.text, string.Empty, 64, 64,
                new LottieAnimationOptions { UseManagedTextureUpload = true }))
            using (var second = LottieAnimation.LoadFromJsonData(source.text, string.Empty, 64, 64,
                new LottieAnimationOptions { UseManagedTextureUpload = true }))
            {
                first.DrawOneFrame(0);
                uint frameZero = Hash(first.Texture.GetRawTextureData<byte>());
                NativeArray<byte> unityStorage = first.Texture.GetRawTextureData<byte>();
                unityStorage[0] ^= 0xff;
                first.Texture.Apply(false, false);
                for (int frame = 1; frame <= 128; frame++)
                {
                    first.DrawOneFrame(frame % 90);
                    second.DrawOneFrame((frame + 30) % 90);
                    first.Texture.GetRawTextureData<byte>();
                    if ((frame & 31) == 0) GC.Collect();
                }
                first.DrawOneFrame(30);
                Assert.AreNotEqual(frameZero, Hash(first.Texture.GetRawTextureData<byte>()));
                Assert.AreNotEqual(0u, Hash(second.Texture.GetRawTextureData<byte>()));
            }
        }

        [Test]
        public void StraightAlphaCacheUnpremultipliesColoredTransparentPixels()
        {
            var pixels = new NativeArray<byte>(new byte[] { 0, 0, 64, 64, 20, 10, 0, 0 }, Allocator.Temp);
            try
            {
                LottieFrameCache.ConvertToStraightRgba(pixels);
                CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 64, 0, 0, 0, 0 }, pixels.ToArray());
            }
            finally { pixels.Dispose(); }
        }

        private static string WithMarker(string json, string name, double start, double duration)
        {
            string marker = "\"markers\":[{\"cm\":\"" + name + "\",\"tm\":" +
                start.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"dr\":" +
                duration.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]";
            return json.Replace("\"markers\":[]", marker);
        }

        private static uint Hash(NativeArray<byte> pixels)
        {
            uint hash = 2166136261;
            for (int index = 0; index < pixels.Length; index++) hash = (hash ^ pixels[index]) * 16777619;
            return hash;
        }

        private static void AssertBgraApproximately(
            NativeArray<byte> pixels,
            int width,
            int x,
            int y,
            byte blue,
            byte green,
            byte red,
            byte alpha)
        {
            int offset = (y * width + x) * 4;
            const int tolerance = 3;
            Assert.That(pixels[offset], Is.EqualTo(blue).Within(tolerance), "blue channel");
            Assert.That(pixels[offset + 1], Is.EqualTo(green).Within(tolerance), "green channel");
            Assert.That(pixels[offset + 2], Is.EqualTo(red).Within(tolerance), "red channel");
            Assert.That(pixels[offset + 3], Is.EqualTo(alpha).Within(tolerance), "alpha channel");
        }

        private static void AssertContainsBlueDominantPixel(NativeArray<byte> pixels)
        {
            for (int offset = 0; offset < pixels.Length; offset += 4)
            {
                if (pixels[offset + 3] > 0 && pixels[offset] > pixels[offset + 1] &&
                    pixels[offset] > pixels[offset + 2])
                {
                    return;
                }
            }
            Assert.Fail("Expected a blue-dominant stroke pixel in the rendered pixels.");
        }
    }
}

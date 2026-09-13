using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;

namespace LottiePlugin.Tests.Runtime
{
    public sealed class LottieCpuAndFrameCacheTests
    {
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
                Assert.Throws<ArgumentException>(() => sequential.SetFillColor(string.Empty, Color.red));
                Assert.Throws<ArgumentException>(() => sequential.SetStrokeColor(
                    "**.palette.ink", new Color(float.NaN, 0, 0)));
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
    }
}

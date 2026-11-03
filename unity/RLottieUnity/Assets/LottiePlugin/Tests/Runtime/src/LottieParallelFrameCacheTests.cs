using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LottiePlugin.Tests.Runtime
{
    public sealed class LottieParallelFrameCacheTests
    {
        private static string Json
        {
            get
            {
                string source = Resources.Load<TextAsset>("device_color_calibration_alpha").text;
                return source.Replace("\"markers\":[]", "\"markers\":[{\"cm\":\"clip\",\"tm\":0,\"dr\":3},{\"cm\":\"loop\",\"tm\":0,\"dr\":30}]");
            }
        }
        private static LottieFrameCacheOptions Options(int width, int height, LottieAlphaMode alpha, bool parallel)
        {
            return new LottieFrameCacheOptions
            {
                Width = width, Height = height, AlphaMode = alpha,
                Preparation = parallel ? LottieFramePreparation.Parallel : LottieFramePreparation.Serial,
                MakeNoLongerReadable = false, FilterMode = FilterMode.Point, WrapMode = TextureWrapMode.Repeat,
                Clips = new[] { new LottieClipSampling("clip", 120, false, true), new LottieClipSampling("loop", 12, true) }
            };
        }
        [UnityTest]
        public IEnumerator ParallelMatchesCanonicalSamplingPixelsAndOptions()
        {
            foreach (var size in new[] { new Vector2Int(16, 32), new Vector2Int(96, 96), new Vector2Int(256, 128) })
            foreach (LottieAlphaMode alpha in Enum.GetValues(typeof(LottieAlphaMode)))
            using (var pool = new LottieFramePreparationPool(2, 8))
            using (var serial = new LottieFrameCache(Json, "", Options(size.x, size.y, alpha, false)))
            using (var parallel = new LottieFrameCache(Json, "", Options(size.x, size.y, alpha, true), pool))
            {
                while (!serial.Ready) serial.WarmStep(3);
                double deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (!parallel.Ready && Time.realtimeSinceStartupAsDouble < deadline)
                {
                    int before = parallel.WarmedFrameCount;
                    parallel.WarmStep(2);
                    Assert.LessOrEqual(parallel.WarmedFrameCount - before, 2);
                    Assert.LessOrEqual(pool.PendingPixelBytes, pool.MaximumPendingPixelBytes);
                    yield return null;
                }
                Assert.IsTrue(parallel.Ready);
                Assert.IsFalse(parallel.FellBack);
                Assert.AreEqual(serial.CachedFrameCount, parallel.ParallelFrameCount);
                Assert.AreEqual(serial.EstimatedRawPixelBytes, parallel.EstimatedRawPixelBytes);
                foreach (string marker in new[] { "clip", "loop" })
                {
                    double duration = serial.DurationSeconds(marker);
                    for (double seconds = -.2; seconds < duration + .2; seconds += 1.0 / 120)
                        CollectionAssert.AreEqual(serial.Sample(marker, seconds).GetRawTextureData<byte>().ToArray(),
                            parallel.Sample(marker, seconds).GetRawTextureData<byte>().ToArray());
                    CollectionAssert.AreEqual(serial.Sample(marker, duration).GetRawTextureData<byte>().ToArray(),
                        parallel.Sample(marker, duration).GetRawTextureData<byte>().ToArray());
                    Texture2D first = parallel.Sample(marker, 0);
                    Assert.AreEqual(FilterMode.Point, first.filterMode);
                    Assert.AreEqual(TextureWrapMode.Repeat, first.wrapMode);
                    Assert.IsTrue(parallel.WarmStep());
                    Assert.AreSame(first, parallel.Sample(marker, 0));
                }
                Assert.AreEqual(0, pool.PendingPixelBytes);
            }
        }
        private sealed class FailedBatch : LottieRasterBatch
        {
            internal bool Retired;
            internal bool Malformed;
            public override void Start(int[] frames) { }
            public override bool TryComplete(out byte[] pixels)
            {
                pixels = new byte[1];
                if (!Malformed) throw new InvalidOperationException("injected failure");
                return true;
            }
            public override void Dispose() { Retired = true; }
        }
        private sealed class RecordingBatch : LottieRasterBatch
        {
            internal readonly List<int[]> Requests = new List<int[]>();
            private int[] _frames;
            private int[] _snapshot;
            private readonly int _bytesPerFrame;
            internal RecordingBatch(int bytesPerFrame) { _bytesPerFrame = bytesPerFrame; }
            public override void Start(int[] frames)
            {
                Assert.IsNull(_frames, "A batch must finish before its indices are reused.");
                _frames = frames;
                _snapshot = (int[])frames.Clone();
                Requests.Add(frames);
            }
            public override bool TryComplete(out byte[] pixels)
            {
                CollectionAssert.AreEqual(_snapshot, _frames, "Pending indices were overwritten.");
                pixels = new byte[_frames.Length * _bytesPerFrame];
                _frames = null;
                return true;
            }
            public override void Dispose() { _frames = null; }
        }
        [Test]
        public void FrameIndicesReuseEqualLengthBatchesAndKeepExactClipTails()
        {
            using (var pool = new LottieFramePreparationPool(batchSize: 2))
            {
                var batch = new RecordingBatch(16 * 32 * 4);
                using (var cache = new LottieFrameCache(Json, "", Options(16, 32, LottieAlphaMode.StraightRgba, true), pool, () => batch))
                {
                    var expectedLengths = new List<int>();
                    foreach (LottieClipPreflight clip in cache.Preflight.Clips)
                        for (int remaining = clip.FrameCount; remaining > 0; remaining -= 2)
                            expectedLengths.Add(Math.Min(remaining, 2));
                    while (!cache.Ready) cache.WarmStep();
                    Assert.AreEqual(expectedLengths.Count, batch.Requests.Count);
                    bool observedReuse = false, observedTail = false;
                    for (int index = 0; index < expectedLengths.Count; index++)
                    {
                        Assert.AreEqual(expectedLengths[index], batch.Requests[index].Length);
                        observedTail |= expectedLengths[index] < 2;
                        if (index > 0 && expectedLengths[index] == expectedLengths[index - 1])
                        {
                            Assert.AreSame(batch.Requests[index - 1], batch.Requests[index]);
                            observedReuse = true;
                        }
                    }
                    Assert.IsTrue(observedReuse);
                    Assert.IsTrue(observedTail);
                    Assert.AreEqual(0, pool.PendingPixelBytes);
                }
            }
        }
#if !UNITY_WEBGL || UNITY_EDITOR || LOTTIE_PARALLEL_PREPARATION
        [UnityTest]
        public IEnumerator BatchReusesCompletedPixelsAndResizesExactTails()
        {
            using (var pool = new LottieFramePreparationPool(batchSize: 3))
            using (LottieRasterBatch batch = pool.CreateBatch(Json, "", Options(16, 32, LottieAlphaMode.StraightRgba, true)))
            {
                byte[] previous = null;
                int previousCount = 0;
                foreach (int count in new[] { 3, 3, 1, 1, 3 })
                {
                    var frames = new int[count];
                    for (int index = 0; index < count; index++) frames[index] = index;
                    batch.Start(frames);
                    Assert.Throws<InvalidOperationException>(() => batch.Start(frames));
                    byte[] completed;
                    double deadline = Time.realtimeSinceStartupAsDouble + 15;
                    while (!batch.TryComplete(out completed) && Time.realtimeSinceStartupAsDouble < deadline)
                        yield return null;
                    Assert.IsNotNull(completed, "Raster batch timed out.");
                    Assert.AreEqual(count * 16 * 32 * 4, completed.Length);
                    if (previousCount == count) Assert.AreSame(previous, completed);
                    previous = completed;
                    previousCount = count;
                }
            }
        }
#endif
        [TestCase(false)]
        [TestCase(true)]
        public void FailedOrMalformedBatchRetiresAndFinishesSerial(bool malformed)
        {
            using (var pool = new LottieFramePreparationPool())
            {
                var failed = new FailedBatch { Malformed = malformed };
                using (var cache = new LottieFrameCache(Json, "", Options(16, 32, LottieAlphaMode.StraightRgba, true), pool, () => failed))
                using (var serial = new LottieFrameCache(Json, "", Options(16, 32, LottieAlphaMode.StraightRgba, false)))
                {
                    LogAssert.Expect(LogType.Warning, "Lottie frame preparation fell back to serial: InvalidOperationException");
                    while (!cache.Ready) cache.WarmStep();
                    while (!serial.Ready) serial.WarmStep();
                    Assert.IsTrue(failed.Retired);
                    Assert.IsTrue(cache.FellBack);
                    CollectionAssert.AreEqual(serial.Sample("clip", 10).GetRawTextureData<byte>().ToArray(),
                        cache.Sample("clip", 10).GetRawTextureData<byte>().ToArray());
                    Assert.AreEqual(0, pool.PendingPixelBytes);
                }
            }
        }
        [Test]
        public void PendingDisposalJoinsAndPoolRejectsPrematureRetirement()
        {
            using (var pool = new LottieFramePreparationPool())
            {
                var cache = new LottieFrameCache(Json, "", Options(96, 96, LottieAlphaMode.StraightRgba, true), pool);
                cache.WarmStep();
                Assert.Throws<InvalidOperationException>(() => pool.Dispose());
                cache.Dispose();
                cache.Dispose();
                Assert.AreEqual(0, pool.PendingPixelBytes);
                Assert.AreEqual(2, pool.Slots.CurrentCount);
                Assert.Throws<ObjectDisposedException>(() => cache.WarmStep());
            }
        }
        [Test]
        public void ValidatesBudgetsBeforeSchedulingAndDefaultsToSerial()
        {
            Assert.AreEqual(LottieFramePreparation.Serial, new LottieFrameCacheOptions().Preparation);
            Assert.Throws<ArgumentOutOfRangeException>(() => new LottieFramePreparationPool(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LottieFramePreparationPool(batchSize: 33));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LottieFramePreparationPool(browserTimeoutSeconds: double.NaN));
            var options = Options(16, 32, LottieAlphaMode.StraightRgba, true);
            Assert.Throws<ArgumentException>(() => new LottieFrameCache(Json, "", options));
            options.MaximumRawPixelBytes = 1;
            using (var pool = new LottieFramePreparationPool())
                Assert.Throws<InvalidOperationException>(() => new LottieFrameCache(Json, "", options, pool));
            options.MaximumRawPixelBytes = long.MaxValue;
            using (var pool = new LottieFramePreparationPool(maximumPendingPixelBytes: 4))
            {
                LogAssert.Expect(LogType.Warning, "Lottie frame preparation fell back to serial: NotSupportedException");
                using (var cache = new LottieFrameCache(Json, "", options, pool))
                {
                    while (!cache.Ready) cache.WarmStep();
                    Assert.IsTrue(cache.FellBack);
                }
            }
        }
    }
}

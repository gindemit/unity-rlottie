// Staged into the plugin runtime assembly only in benchmark players.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using LottiePlugin;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CorePerformanceProbe : MonoBehaviour
{
    private const int Rounds = 9;
    private const int Size = 512;
    private static readonly int[] Frames = { 0, 3, 6, 9, 12, 15, 18, 21 };
    private readonly List<string> _results = new List<string>();
    private string _output;
    private string _file;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartProbe()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-lottieCorePerformance");
        if (index < 0) return;
        var probe = new GameObject(nameof(CorePerformanceProbe)).AddComponent<CorePerformanceProbe>();
        probe._output = args[index + 1];
        probe._file = args[index + 2];
        probe.StartCoroutine(probe.Run(Array.IndexOf(args, "-lottieCoreD3D12") >= 0));
    }

    private IEnumerator Run(bool d3d12)
    {
        // This scene is shared with the interactive demo. Pause its updates and
        // hide its UI while measuring the plugin.
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            if (root != gameObject) root.SetActive(false);
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;

        var workload = d3d12 ? MeasureD3D12() : MeasureCpu();
        using (workload)
        {
            while (true)
            {
                try
                {
                    if (!workload.MoveNext()) break;
                }
                catch (Exception error)
                {
                    UnityEngine.Debug.LogException(error);
                    Application.Quit(1);
                    yield break;
                }
                yield return workload.Current;
            }
        }
        File.WriteAllLines(_output, _results);
        Application.Quit(0);
    }

    private IEnumerator<object> MeasureCpu()
    {
        string json = File.ReadAllText(_file);
        var options = new LottieAnimationOptions { UseManagedTextureUpload = true };
        var cacheOptions = new LottieFrameCacheOptions { Width = Size, Height = Size };
        using (var raster = LottieCpuRasterizer.LoadFromJsonData(json, "", Size, Size))
        using (var pixels = new NativeArray<byte>(raster.ByteCount, Allocator.Persistent))
        using (var pool = new LottieFramePreparationPool(workerCount: 1, batchSize: Frames.Length))
        using (var batch = pool.CreateBatch(json, "", cacheOptions))
        {
            if (raster.TotalFramesCount <= Frames[Frames.Length - 1])
                throw new InvalidOperationException("Fixture has too few frames.");

            // Round zero warms the disk cache, native model, JIT and worker.
            for (int round = 0; round <= Rounds; round++)
            {
                Add(round, "load_file", Measure(16, _ =>
                {
                    using (var animation = LottieAnimation.LoadFromJsonFile(_file, 64, 64, options)) { }
                }));
                yield return null;
                Add(round, "load_data", Measure(16, _ =>
                {
                    using (var animation = LottieAnimation.LoadFromJsonData(json, "", 64, 64, options)) { }
                }));
                yield return null;
                Add(round, "raster", Measure(Frames.Length * 4, frame =>
                    raster.RenderFrame(Frames[frame % Frames.Length], pixels)));
                Add(round, "prepare", Measure(8, _ =>
                {
                    batch.Start(Frames);
                    byte[] result;
                    while (!batch.TryComplete(out result)) Thread.Yield();
                    if (result.Length != raster.ByteCount * Frames.Length)
                        throw new InvalidOperationException("Worker returned an incomplete batch.");
                }));
                yield return null;
            }
        }
    }

    private IEnumerator<object> MeasureD3D12()
    {
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
            throw new InvalidOperationException("D3D12 is required for the GPU benchmark.");
        string json = File.ReadAllText(_file);
        using (var animation = LottieAnimation.LoadFromJsonData(json, "", Size, Size))
        {
            for (int round = 0; round <= Rounds; round++)
            {
                double drawMs = 0;
                double frameMs = 0;
                for (int frame = 0; frame < 48; frame++)
                {
                    int index = Frames[frame % Frames.Length];
                    drawMs += Measure(1, _ => animation.DrawOneFrame(index));
                    yield return null;
                    frameMs += Time.unscaledDeltaTime * 1000.0;
                    if (animation.TextureUploadBackend != LottieTextureUploadBackend.NativeExternalTexture)
                        throw new InvalidOperationException("D3D12 native upload was not active.");
                }
                Add(round, "d3d12_draw", drawMs / 48);
                Add(round, "d3d12_frame", frameMs / 48);
                var disposals = new double[24];
                for (int i = 0; i < disposals.Length; i++)
                {
                    using (var cycle = LottieAnimation.LoadFromJsonData(json, "", Size, Size))
                    {
                        cycle.DrawOneFrame(Frames[i % Frames.Length]);
                        yield return null;
                        if (cycle.TextureUploadBackend != LottieTextureUploadBackend.NativeExternalTexture)
                            throw new InvalidOperationException("Lifecycle cycle did not reach native upload.");
                        disposals[i] = Measure(1, _ => cycle.Dispose());
                    }
                    yield return null;
                }
                Array.Sort(disposals);
                Add(round, "d3d12_dispose_p95", disposals[(int)Math.Ceiling(disposals.Length * .95) - 1]);
            }
        }
    }

    private static double Measure(int count, Action<int> action)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) action(i);
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency / count;
    }

    private void Add(int round, string metric, double milliseconds)
    {
        if (round > 0)
            _results.Add($"{round},{metric},{milliseconds.ToString("R", CultureInfo.InvariantCulture)}");
    }
}

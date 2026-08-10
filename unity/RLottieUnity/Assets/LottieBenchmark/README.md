# rlottie performance lab

Open `Assets/Scenes/Main.unity` and enter Play mode, or build that scene for a target device.

- **Spawn Live** creates the requested number of rlottie instances at the exact custom output size and continuously renders them.
- **Run Selected** performs warm-up frames, then records deterministic render-batch and observed Unity-frame timings.
- **Run 3 x 4 Matrix** tests all three Patch Pal variants at 128, 256, 512, and 1024 square output sizes using the configured instance count and sample counts.
- **Export CSV** writes device, graphics API, load, memory, throughput, mean, p50, p95, maximum, and 60 FPS budget data to `Application.persistentDataPath`.

Use the Unity Profiler markers `LottieBenchmark.RenderBatch` and `LottieBenchmark.RenderInstance` to correlate the benchmark with render-thread and texture-upload work. Development builds with Autoconnect Profiler are recommended for Windows and device measurements.

The stopwatch batch metric covers each synchronous `LottieAnimation.DrawOneFrame` call. The observed-frame metric also reflects player overhead, upload synchronization, presentation, and frame limiting; interpret it alongside the Unity Profiler, especially when VSync is enabled.

## Automated player run

A standalone player can run the full matrix and exit without UI interaction:

```powershell
RLottieSmoke.exe -lottieBenchmarkMatrix -lottieBenchmarkInstances 10 -lottieBenchmarkWarmup 30 -lottieBenchmarkSamples 180 -lottieBenchmarkUncapped -lottieBenchmarkQuit -lottieBenchmarkOutput C:\benchmarks\patch-pal.csv
```

Omit `-lottieBenchmarkUncapped` when the observed frame metric should include the player's configured VSync/frame cap. On iOS, use the in-scene controls and retrieve the logged summary or CSV from the app container.

The existing `RLottie.CI.BuildMatrix.Build` method accepts `-ciTarget Windows64` or `-ciTarget iOS`. Add `-ciDevelopment true -ciAutoconnectProfiler true` to produce a development player that connects to the Unity Profiler and exposes the benchmark markers.

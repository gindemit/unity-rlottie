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

Omit `-lottieBenchmarkUncapped` when the observed frame metric should include the player's configured VSync/frame cap.

On iOS and Android, `-lottieBenchmarkUncapped` requests a target frame rate above any
current mobile display refresh rather than `Application.targetFrameRate = -1`, because
`-1` restores the platform default of 30 fps on mobile and would cap the observed-frame
metric instead of releasing it. The platform still clamps to the display's real maximum,
and the iOS player setting *Adjust iOS FPS based on thermal state* can lower it further.
The stopwatch batch metric is unaffected either way: it times synchronous
`LottieAnimation.DrawOneFrame` calls and never depends on presentation.

On iOS, Unity does not populate `Environment.GetCommandLineArgs()` from the arguments
passed by `devicectl device process launch`, so command-line options never reach the
controller there. Pass the same options as `LOTTIE_BENCHMARK_*` environment variables
instead; every command-line option has an equivalent, and command-line arguments keep
priority when both are present:

```bash
xcrun devicectl device process launch --device <device-id> --console \
  -e '{"LOTTIE_BENCHMARK_MATRIX":"1","LOTTIE_BENCHMARK_INSTANCES":"10","LOTTIE_BENCHMARK_WARMUP":"30","LOTTIE_BENCHMARK_SAMPLES":"180","LOTTIE_BENCHMARK_UNCAPPED":"1","LOTTIE_BENCHMARK_QUIT":"1"}' \
  -- com.DefaultCompany.LottiePlugin
```

Flag variables (`MATRIX`, `QUIT`, `UNCAPPED`, `VALIDATE_PIXELS`, `HOLD_AFTER_WARMUP`,
`MANAGED_UPLOAD`, `VULKAN_DIAGNOSTICS`) are enabled by `1` or `true`. Value variables
(`INSTANCES`, `WARMUP`, `SAMPLES`, `OUTPUT`, `RESOLUTIONS`) take the same values as their
command-line counterparts. Omit `LOTTIE_BENCHMARK_OUTPUT` on iOS: `ExportCsv` uses the
path verbatim, so a relative path would target the read-only bundle directory. With it
omitted the CSV is auto-named into `Application.persistentDataPath`, from where it can be
retrieved with `xcrun devicectl device copy from --domain-type appDataContainer`.

The in-scene controls remain available on all platforms as a manual alternative.

The existing `RLottie.CI.BuildMatrix.Build` method accepts Windows, Linux,
Android, WebGL, and iOS targets. Add `-ciDevelopment true
-ciAutoconnectProfiler true` to produce a development player that connects to
the Unity Profiler and exposes the benchmark markers.

Android release players accept benchmark options through the
`lottieBenchmarkArguments` activity intent extra. The repository runner builds,
installs, executes, cools down between, and pulls CSV files sequentially:

```powershell
scripts\benchmarks\run-android-performance-matrix.ps1
```

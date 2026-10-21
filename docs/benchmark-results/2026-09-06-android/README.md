# Android rlottie performance comparison — 2026-09-06

Two GPU vendors, both native upload backends, Unity 6000.5.3f1 URP.

This run is **not** a re-run of `../2026-08-10-android/`: different handsets, and ten
animation instances per case instead of one. Do not compare the two directly.

## Configuration

| | |
| --- | --- |
| Unity | 6000.5.3f1, URP |
| Devices | Samsung SM-G985F (Galaxy S20+), Exynos 990, **Mali-G77**, Android 13, 60 Hz panel |
| | Samsung SM-A525F (Galaxy A52), Snapdragon 720G, **Adreno 618**, Android 14, 90 Hz panel |
| Graphics APIs | OpenGL ES 3 and Vulkan, built as separate APKs |
| Instances per case | 10 |
| Warm-up / sample frames | 30 / 180 |
| Animations | 3 Patch Pal variants, 4 resolutions (128/256/512/1024) |
| Plugin | dev at `9a3d077`, native libraries 0.5.0-dev.240 |

Raw data: `s20plus-mali-g77-{gles3,vulkan}.csv`, `a52-adreno-618-{gles3,vulkan}.csv`.

## Upload backends

Every one of the 48 cases took its intended native path. **No managed `Texture2D.Apply()`
fallback occurred anywhere.**

| API | `upload_backend` | Runtime log |
| --- | --- | --- |
| OpenGL ES 3 | `NativeOpenGL` | `[LottiePlugin] Unity-owned OpenGL native upload enabled` |
| Vulkan | `NativeVulkan` | `[LottiePlugin] Vulkan native upload enabled` |

Note the GLES label is `NativeOpenGL`, which is the Unity-owned-RGBA native GL path, not
`NativeExternalTexture` and not a fallback.

## OpenGL ES 3 vs Vulkan

**Mali-G77 (S20+): Vulkan wins clearly at high resolution.** Mean batch ms:

| Variant | 1024 GLES3 | 1024 Vulkan | Vulkan faster by |
| --- | --- | --- | --- |
| A - Stitches | 56.45 | 43.20 | 23% |
| B - Vector felt | 59.12 | 46.94 | 21% |
| C - Hybrid felt PNG | 76.27 | 63.53 | 17% |

At 512 the Vulkan advantage is 6-14%. At 128 and 256 the two APIs are a wash, and Vulkan
is sometimes slightly slower (B/128: 12.97 vs 11.00).

**Adreno 618 (A52): essentially a wash, Vulkan marginally worse at 1024** — A +5%, B +6%,
C +2% against GLES3. Differences from 128 to 512 are within run-to-run noise.

The vendor split matters: choosing Vulkan is a clear win on this Mali part at large
surfaces and a slight loss on this Adreno part. Neither result should be generalized to
other GPUs from the same vendor without measurement.

## Mali vs Adreno

The S20+ is roughly **1.4x to 2.5x faster** than the A52 at every point, widening with
resolution. At 1024: 56.45 ms vs 102.66 ms on GLES3, and 43.20 ms vs 108.16 ms on Vulkan
(2.5x). These are very different market segments (2020 flagship vs mid-range), so the gap
is expected; it is recorded here to bound the plugin's behaviour across the range.

Reported Vulkan device properties differ in a way worth noting for the upload ring:
`Mali-G77` reports `nonCoherentAtom=64`, `Adreno (TM) 618` reports `nonCoherentAtom=1`.

## 60 FPS budget

At 10 instances the workload is heavy. On the S20+ only 128 and 256 stay near or under the
16.67 ms budget; every 512 and 1024 case is over it. On the A52 only the 128 cases are
sometimes under budget. For per-frame use at 1024 with ten simultaneous animations,
neither device is viable — that is a property of the workload, not a plugin defect.

## Frame-cap verification (why this run exists)

This session also verified commit `9a3d077`, which fixed `-lottieBenchmarkUncapped`
setting `Application.targetFrameRate = -1` — a value that restores the 30 fps mobile
platform default instead of removing the cap.

A/B at 1 instance and 128/256, where batch work is 2-4 ms and cannot mask presentation:

| Device | API | `mean_observed_frame_ms` without flag | with fixed flag |
| --- | --- | --- | --- |
| S20+ (Mali-G77) | GLES3 | 33.20 - 33.30 | **16.60 - 16.79** |
| S20+ (Mali-G77) | Vulkan | 33.21 - 33.31 | **16.61 - 16.89** |
| A52 (Adreno 618) | GLES3 | 33.82 - 34.24 | **15.98 - 17.41** |
| A52 (Adreno 618) | Vulkan | 33.45 - 33.52 | **16.41 - 17.35** |

The 30 Hz pin is gone on both vendors and both APIs. `mean_batch_ms` was unchanged within
noise across the same A/B, marginally lower when uncapped.

**This differs from the iOS result.** The equivalent iOS A/B on an iPhone 11 showed
`mean_batch_ms` dropping 30-40% when the cap was lifted. On Android the same comparison
moved batch times only slightly. Both platforms agree the observed-frame pin is fixed, but
the large iOS batch shift is not reproduced here, so the CPU-governor explanation offered
for iOS should be treated as unconfirmed. See `../2026-09-01-ios/README.md`.

**Secondary finding:** on the A52's 90 Hz panel, an uncapped run settles at ~16.7 ms
(60 fps) rather than 11.1 ms, despite requesting 240. Samsung Game Manager
(`SGM:GameManager identifyForegroundApp`) is active on both handsets and appears to hold
the surface at 60 Hz. "Let the platform clamp to the display maximum" therefore yields 60,
not 90, on this device.

## Method notes

Benchmark options were passed through the `lottieBenchmarkArguments` activity intent extra
(`adb shell am start -e lottieBenchmarkArguments "..."`), which is the Android entry point;
the `LOTTIE_BENCHMARK_*` environment variables added for iOS are not needed here.

No thermal throttling: battery temperatures stayed at 27.6-33.1 °C on the S20+ and
28.9-30.8 °C on the A52, with 45-90 s cooldowns between runs. Power saving was off on both.

One Adreno Vulkan run lost its final case to a transient process-detection miss and was
re-run; the 11 rows it did produce match the re-run within noise. The re-run is the data
published here.

No `[LottiePlugin]` warnings or errors appeared in any of the runs. A benign
`ClassNotFoundException` for `com.google.android.play.core.assetpacks.AssetPackManager`
appears in every Unity Android run and is unrelated to this plugin.

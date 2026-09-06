# iOS rlottie performance comparison — 2026-09-01

> **The absolute timings in this document are depressed by roughly 35% and must not be
> quoted as this plugin's iOS throughput.** Every run here was captured while
> `-lottieBenchmarkUncapped` was silently pinning the player to 30 fps, which left the
> render loop idle for most of each frame and let iOS DVFS park the CPU at a low clock.
> A 2026-09-06 re-measurement on the same iPhone 11 with the flag fixed produced batch
> times 30-40% lower for identical work. See "Frame cap and CPU clock" below.
>
> The relative comparisons below remain valid, because all four runs shared the same cap:
> A13 vs A14, Built-in vs URP, and the resolution scaling curve are unaffected. Only the
> absolute millisecond and renders-per-second values are wrong.

## Result

The fastest complete configuration was **URP on the iPhone 12 Pro Max (A14)**, averaging
**9.2240 ms** per synchronous render batch of 10 animation instances across the 12
animation/resolution cases, or **0.9224 ms per animation**. Built-in on the same device
was 1.1% behind at 9.3240 ms. The iPhone 11 (A13) trailed both, at 10.3411 ms (URP) and
10.5484 ms (Built-in).

All 48 cases ran on the native Metal external-texture path
(`upload_backend=NativeExternalTexture`); no case fell back to `Texture2D.Apply()`.

## Configuration

| | |
| --- | --- |
| Unity | 6000.5.3f1 |
| Graphics API | Metal (the only iOS graphics API in Unity 6; GLES was removed on Apple platforms) |
| Pipelines | Built-in, URP (HDRP is not supported on iOS) |
| Devices | iPhone 11 (A13 GPU, iOS 18.7.8), iPhone 12 Pro Max (A14 GPU, iOS 26.6) |
| Instances per case | 10 |
| Warm-up / sample frames | 30 / 180 |
| Animations | 3 Patch Pal variants (A stitches, B vector felt, C hybrid felt PNG) |
| Resolutions | 128, 256, 512, 1024 square |
| Plugin | dev at `04ca51f`, native libraries 0.5.0-dev.240 |

Raw data: `builtin-a13.csv`, `builtin-a14.csv`, `urp-a13.csv`, `urp-a14.csv`.
Derived: `device-summary.csv`, `pipeline-comparison.csv`.

## A13 vs A14

Mean batch time averaged over both pipelines and all three variants:

| Resolution | A13 (ms) | A14 (ms) | A14 faster by |
| --- | --- | --- | --- |
| 128 | 5.7502 | 5.5785 | 2.98% |
| 256 | 6.5784 | 6.7808 | **-3.08%** (A13 faster) |
| 512 | 8.4598 | 7.5972 | 10.20% |
| 1024 | 20.9907 | 17.1396 | 18.35% |

The A14's advantage is a large-surface effect that scales monotonically with pixel count.
At 128 and 256 the two devices are within run-to-run noise, and the A13 wins 256 in all
six matched cases. This is consistent with small resolutions being bound by rlottie's CPU
rasterizer rather than by texture upload. From 512 upward the A14 pulls clear, peaking at
18.35% at 1024. The largest single improvement is the hybrid PNG variant at 1024, which
falls from 28.447 ms to 22.209 ms on Built-in.

**This is not a clean silicon comparison.** The A13 device runs iOS 18.7.8 and the A14
device runs iOS 26.6, so operating-system and driver differences are folded into every
figure above.

## Render pipeline

| Device | URP wins | Matched pairs | Mean URP advantage |
| --- | --- | --- | --- |
| iPhone 11 (A13) | 11 | 12 | 3.10% |
| iPhone 12 Pro Max (A14) | 9 | 12 | 1.70% |
| Combined | 20 | 24 | **2.40%** |

The combined 2.40% closely matches the 2.39% URP advantage measured on Android on
2026-08-10, so iOS/Metal agrees with Android/OpenGL ES 3 in both direction and magnitude.

Unlike the Android run, URP's win here is not universal: all four Built-in wins are 1024
cases, and they are small (0.24% to 0.66%, with two effectively ties). URP's advantage
concentrates at 128 and 256, where it reaches 7.5%, and disappears at 1024. That pattern
suggests a fixed per-frame pipeline overhead that is swamped once rasterization dominates.
Sub-1% differences at 1024 should not be treated as decisive on a single run.

## 60 FPS budget

At 10 instances, only 1024 cases exceed the 16.67 ms per-batch budget. On the A14 that
breach disappears entirely for variants A and B (0/180 batches over budget, against
157/180 and 179/180 on the A13). The hybrid PNG variant at 1024 is over budget on both
devices in every sampled batch.

## Comparison with the Android baseline — methodology mismatch

The Android baseline in `../2026-08-10-android/` is **not directly comparable** to this
run. It used **one animation instance per case**; this matrix used **ten**. Its headline
5.6805 ms is therefore a per-animation cost, whereas `mean_batch_ms` here covers ten
animations amortized into a single batch.

| Configuration | Grand mean batch (ms) | Grand mean per animation (ms) |
| --- | --- | --- |
| Android Note10+, Mali-G76, URP GLES3, 1 instance | 5.6805 | 5.6805 |
| iOS A14, URP Metal, 10 instances | 9.2240 | 0.9224 |
| iOS A14, Built-in Metal, 10 instances | 9.3240 | 0.9324 |
| iOS A13, URP Metal, 10 instances | 10.3411 | 1.0341 |
| iOS A13, Built-in Metal, 10 instances | 10.5484 | 1.0548 |

Batching amortizes fixed per-batch cost, so the per-animation column overstates the true
hardware gap by an unknown factor. **No cross-platform speed multiple should be quoted
from this table.** A defensible comparison requires either an Android re-run at 10
instances or an iOS re-run at 1 instance; neither has been done.

## Frame cap and CPU clock — read before using any absolute number

`mean_observed_frame_ms` and `p95_observed_frame_ms` in these CSVs are pinned at
33.3-33.5 ms (30 Hz) in all four runs despite `-lottieBenchmarkUncapped` being set. The
flag set `Application.targetFrameRate = -1`, which on mobile restores the platform default
of 30 fps instead of removing the cap. Those columns are meaningless here and should be
ignored.

The batch metrics were expected to be immune, since they time synchronous
`LottieAnimation.DrawOneFrame` calls and never depend on presentation. **That expectation
was wrong.** Re-running the 128 and 256 cases on the same iPhone 11 on 2026-09-06 with the
fixed flag produced:

| Variant | Resolution | 2026-09-01 mean batch (ms) | 2026-09-06 mean batch (ms) | Change |
| --- | --- | --- | --- | --- |
| A - Stitches | 128 | 5.7953 | 3.4733 | -40.1% |
| A - Stitches | 256 | 6.4342 | 4.1880 | -34.9% |
| B - Vector felt | 128 | 6.1186 | 3.7408 | -38.9% |
| B - Vector felt | 256 | 6.5629 | 4.5847 | -30.1% |
| C - Hybrid felt PNG | 128 | 5.9163 | 3.6559 | -38.2% |
| C - Hybrid felt PNG | 256 | 7.1823 | 4.6792 | -34.8% |

The most likely cause is CPU frequency scaling rather than any change in work done: at
30 fps the render loop idled for roughly 27 ms of every 33 ms frame, so the SoC governor
selected a low clock. Supporting evidence is that `load_ms` (13.51 -> 13.49) and
`memory_delta_bytes` (6160 in both runs) are unchanged, while `renders_per_second` rose
proportionally with the batch-time drop. Thermal state was `Nominal` throughout both runs,
so throttling is not the explanation.

This has not been proven directly. Confirming it requires a run with the fix present but
the frame rate forced back to 30 fps; that experiment has not been done. Until it is,
treat the mechanism as probable and the 2026-09-01 absolute numbers as unusable either
way.

Practical consequence: **re-measure before quoting any absolute iOS figure.** The full
3x4 matrix has not yet been re-run on either device with the fixed flag, and the A14 has
not been re-measured at all.

## Method notes

Each run executed unattended via the `LOTTIE_BENCHMARK_*` environment variables, which are
the only working entry point on iOS: Unity does not populate
`Environment.GetCommandLineArgs()` from `devicectl device process launch` arguments.

No thermal throttling occurred. Every run logged exactly one `thermalStateDidChange:
Nominal` event and no subsequent transition. Matrices took roughly 80 seconds each, with
2-5 minutes of build and install work between runs on the same device. Run-to-run
stability was good: the A13 hybrid PNG 1024 case landed at 28.447 ms and 28.443 ms in two
separate runs, 0.01% apart.

No `[LottiePlugin]` warnings or errors, no exceptions, and no managed-upload fallbacks
appeared in any of the six console logs from this session.

# Android rlottie performance comparison — 2026-08-10

## Result

The fastest complete configuration on the tested Galaxy Note10+ was **Unity
6000.5.3f1, URP, OpenGL ES 3**, averaging **5.6805 ms** per synchronous
render batch and **236.0 renders/s** across the 12 animation/resolution cases.
Unity 6000.3.7f1 URP/OpenGL ES 3 was effectively tied at 5.6892 ms (0.15%
slower), so the exact ordering of those two should not be treated as decisive
without repeated runs.

Across editor versions, after averaging every available pipeline/API
configuration, Unity 6000.5.3f1 was fastest at 5.8506 ms. Unity 6000.3.7f1
(5.8698 ms) and 6000.4.5f1 (5.8808 ms) were within 0.6% of it. Unity 6 was
about 6.6–6.8% faster than the tested Unity 2021 and 2022 configurations.

## Main comparisons

| Comparison | Result |
| --- | --- |
| Best full configuration | 6000.5.3f1 / URP / OpenGL ES 3 — 5.6805 ms |
| Slowest full configuration | 2022.3.62f3 / Built-in / Vulkan — 6.3889 ms |
| Best-to-slowest difference | 11.09% lower mean batch time |
| Graphics API | OpenGL ES 3 won all 8 matched pairs; Vulkan averaged 3.26% slower |
| Unity 6 render pipeline | URP won all 6 matched pairs; averaged 2.39% faster than Built-in |
| Fastest animation | A — Stitches, 5.5515 ms average |
| Slowest animation | C — Hybrid felt PNG, 6.2533 ms average |
| Resolution scaling | 1024² averaged 11.0556 ms, 4.08× the 128² cost |

The hybrid PNG animation rendered 12.64% slower and loaded 90.36% slower
than Stitches on average. Vector felt sat between them for batch time, but had
the lowest average load time.

At one animation instance, every 128, 256, and 512 case stayed within the
16.67 ms batch budget. Across 1024 cases, 3.68% of samples exceeded that
budget. This budget applies to the synchronous `DrawOneFrame` batch metric,
not total presentation latency.

## Test matrix and method

- Device: Samsung SM-N975F (Galaxy Note10+), Exynos 9825, Mali-G76, Android
  12 / API 31.
- 16 sequential device runs; no builds or benchmarks ran in parallel.
- One animation instance per case.
- Three animations: Stitches, Vector felt, and Hybrid felt PNG.
- Four output sizes: 128², 256², 512², and 1024².
- 30 warm-up frames and 180 measured frames per case.
- Uncapped player mode.
- Primary ranking metric: mean synchronous rlottie render-batch time.
- Device temperature stayed between 33.5°C and 36.2°C.

The observed Unity frame columns are retained in the raw data, but are not the
primary ranking metric. On this 60 Hz Android device they include presentation
and scheduling effects and commonly quantize to multi-vsync intervals.

## Coverage and exclusions

Included:

- Built-in pipeline: Unity 2021.3.45f2, 2022.3.62f3, 6000.3.7f1,
  6000.4.5f1, and 6000.5.3f1.
- URP: Unity 6000.3.7f1, 6000.4.5f1, and 6000.5.3f1.
- Vulkan and OpenGL ES 3 for every included editor/pipeline pair.

Excluded:

- Unity 2019.4.41f2: its legacy editor is not activated for unattended
  batch-mode builds on this machine.
- Unity 6000.2.13f1: the checkout exists, but its editor/Android module is not
  installed.
- HDRP checkouts: HDRP is not an Android-supported target in this workspace.
- The WebGL-focused checkout: it is not an Android comparison target and
  duplicates the 2022.3 Built-in editor line.

## Files

- `all-results.csv`: all 192 device measurements with run metadata.
- `variant-summary.csv`: ranked full editor/pipeline/API configurations.
- `editor-version-summary.csv`: editor-version averages.
- `animation-summary.csv`: animation averages.
- `resolution-summary.csv`: resolution scaling and budget misses.
- `graphics-api-comparison.csv`: matched Vulkan versus OpenGL ES 3 pairs.
- `render-pipeline-comparison.csv`: matched URP versus Built-in pairs.
- `case-winners.csv` and `case-win-counts.csv`: fastest configuration per
  animation/resolution case.

Raw APKs, per-run CSVs, Unity build logs, Android logs, and metadata are stored
outside the repository at
`C:\Work\git\gindemit\unity-rlottie\results\android-benchmark-20260810-full`.

These are single-run measurements on one physical device. The small gaps among
the top Unity 6 configurations are useful directional evidence, not a claim of
statistical significance; repeat runs and more target devices are the next step
for production selection.

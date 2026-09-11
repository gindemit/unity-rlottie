# Marker and shared-frame-cache validation — 2026-09-11

This report records validation of the package implementation based on source
commit `4bdb94bdd4c2566cc02357988a05af7ca2af5e01` plus the task working-tree
changes. It covers `unity-rlottie` only. MrSquare repository integration,
WebGL, macOS, and iOS were not completed or validated as part of this work.

## Result summary

| Area | Environment | Result | Evidence |
|---|---|---|---|
| Native ABI | Windows x64, Release | Passed, 1/1 | `out/implementation-native-tests` |
| Unity PlayMode | Unity 2022.3.62f3 | Passed, 33/33 | `../results/review-2022-final2/playmode-results.xml` |
| Unity PlayMode | Unity 6000.5.3f1 | Passed, 33/33 | `../results/atlas-validation/unity-6000-adjacent-tests/playmode-results.xml` |
| Windows player | Windows 11, RTX 3080 Ti, D3D11 | Passed all 16 smoke checks; `NativeExternalTexture` | `../results/atlas-validation/windows-d3d11/smoke.json` |
| Windows player | Windows 11, RTX 3080 Ti, D3D12 | Passed all 16 smoke checks; `NativeExternalTexture` | `../results/atlas-validation/windows-d3d12/smoke.json` |
| Android player | SM-N975F, Android 12/API 31, Mali-G76, Vulkan | Passed all 16 smoke checks; `NativeVulkan` | `../results/atlas-validation/android-vulkan/smoke.json` |
| Android player | Same device, OpenGL ES 3 | Passed all 16 smoke checks; `NativeOpenGL` | `../results/atlas-validation/android-gles3/smoke.json` |
| Android player | Same device, OpenGL ES 2 | Passed all 16 smoke checks; `NativeOpenGL` | `../results/atlas-validation/android-gles2/smoke-final.json` |
| Android performance | Same device, Vulkan and OpenGL ES 3 | Completed 24 cases | `../results/atlas-validation/android-performance-10` |
| Managed-upload performance | Unity 2022.3.62f3 Windows rendered D3D11 player | Completed 12/12 cases; all pixel checks valid | `../results/atlas-validation/windows-d3d11/benchmark-managed-rendered.csv` |

The two PlayMode suites include marker parsing/frame math, CPU raster lifetime
and hashes, cache sampling/preflight/disposal, straight-alpha conversion,
managed staging after texture inspection/mutation, existing UI behavior, and
color calibration. The rendered smoke checks verify GPU-visible first-frame and
changing-frame hashes, exact Gamma color calibration, interaction, and 12
lifecycle stress cycles. Backend selection was native in every rendered native
smoke listed above.

Unity 2019.4 validation was attempted but could not start because the local
editor had no activatable ULF license and no cached licensing token. This is an
unexecuted environment gate, not a test or code failure. The retained logs are
`../results/atlas-validation/unity-2019-tests/editmode.log` and
`../results/atlas-validation/unity-2019-tests/playmode.log`.

The native ABI rerun passed 1/1 tests (0.10 seconds total; 0.09 seconds for the
test itself). Its command was:

```powershell
ctest --test-dir out/implementation-native-tests -C Release --output-on-failure
```

Unity player and test execution used the repository harnesses under
`scripts/ci`; Android performance used
`scripts/benchmarks/run-android-performance-matrix.ps1`. The JSON, XML, CSV,
build logs, and device logs named above are the authoritative per-run records.

## Performance results

The physical-device matrix used Unity 2022.3.62f3 Built-in, 10 live animation
instances, 15 warmup frames, and 60 sampled frames. It ran three animations at
128, 256, 512, and 1024 pixels on both Vulkan and OpenGL ES 3: 12 cases per API,
24 total. Device temperature moved from 29.6 °C to 30.4 °C for Vulkan and from
30.4 °C to 31.6 °C for OpenGL ES 3.

| API | Cases | Average mean batch | Observed p95 range |
|---|---:|---:|---:|
| Vulkan | 12 | 24.922 ms | 12.568–77.287 ms |
| OpenGL ES 3 | 12 | 26.963 ms | 12.162–81.379 ms |

Across both APIs and all three animations, resolution aggregates were:

| Resolution | Average mean batch | Average p95 | Average renders/s | Batches over 16.67 ms |
|---:|---:|---:|---:|---:|
| 128 | 10.275 ms | 13.128 ms | 974.39 | 0.83% |
| 256 | 12.890 ms | 16.124 ms | 779.16 | 4.17% |
| 512 | 22.611 ms | 26.844 ms | 450.72 | 97.5% |
| 1024 | 57.994 ms | 65.578 ms | 176.57 | 100% |

These are live-animation benchmark cases, not a claim that cached consumer UI
cost was benchmarked. The raw rows and derived summaries are under
`../results/atlas-validation/android-performance-10`.

The forced managed-upload Windows run covered the same three animations and
four resolutions with one instance, 15 warmup frames, and 60 samples: 12 cases
total. It selected `ManagedTextureUpload` on rendered D3D11, and every row has
`pixel_valid=True`. Mean batch time ranged from 0.261 to 4.549 ms and p95 from
0.287 to 5.254 ms. The earlier headless `benchmark-managed.csv` used Unity's
Null graphics device, where `AsyncGPUReadback` is unsupported; it is not used as
pixel-validation evidence.

## Evidence boundaries

- Smoke success proves the selected backend, GPU-visible frame changes, color
  calibration, interaction, and repeated create/draw/dispose behavior on the
  listed hardware and API. It does not establish support on other GPU vendors.
- The cache's raw byte estimate and budget checks were functionally tested, but
  no 10/100/500-consumer cache/UI performance matrix was retained in this run.
- Unity 2019.4 was license-blocked. Unity 2021.3 was not run in this validation
  set.
- No result in this report claims MrSquare integration or validation of WebGL,
  macOS, or iOS.

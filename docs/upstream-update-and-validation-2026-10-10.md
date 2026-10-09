# Upstream update and platform validation — 2026-10-10

## Source and binaries

The rlottie fork now includes all seven Samsung commits identified as missing
in the October 9 audit, through Samsung `ea06d2f`. Native merge `e9bf55b`
preserves the fork's Windows, SIMD, gradient, and memory-budget changes and
retains both upstream bitmap tests and the fork's draw-helper tests. The native
fork's default `master` and `unity-rlottie-win-fixes` branches contain the merge.
There are no outstanding commits from the fetched Samsung `master`.

The Unity dependency update is `e48599d`. The successful native release build
published version **0.5.0-dev.246**, consumed by Unity commit `6dbea88`.
[Native build and test run](https://github.com/gindemit/unity-rlottie/actions/runs/37997278860)
passed Windows x86/x64, all four Android ABIs, Linux, macOS, iOS device/simulator,
and WebGL builds.

The actively staged WebGL `.bytes` archives needed separate rebuilding: updating
the inactive `Plugins/WebGL/*.a` assets did not update them. All four staged
archive pairs now use the merged native sources. Unity 2019 and 2021 receive
archives compiled with their own bundled toolchains; Unity 2022 uses the legacy
pair, and Unity 6000.5 uses its wasm exception ABI pair. SHA-256 verification
remains mandatory before staging. Toolchains and hashes are recorded in
`unity/RLottieUnity/Assets/LottiePlugin/Editor/WebGLArchives/PROVENANCE.md`.

## Completed local tests

| Test | Result | Configuration |
|---|---|---|
| Native rlottie tests | 35/35 passed | Windows x64, Release; includes new upstream bitmap tests |
| Plugin ABI test | 1/1 passed | Windows x64, Vulkan-enabled plugin |
| Unity EditMode | 3/3 passed | Unity 2022.3.62f3; repeated after the final C# change |
| Unity PlayMode | 37/37 passed | Unity 2022.3.62f3 |
| Standalone Windows runtime tests | 37/37 passed | Unity 2022.3.62f3 |
| Windows D3D11 rendering | 17/17 passed | NativeExternalTexture, RTX 3080 Ti |
| Windows D3D12 rendering | 17/17 passed | NativeExternalTexture, RTX 3080 Ti |
| Windows Vulkan rendering | 17/17 passed | NativeVulkan, RTX 3080 Ti |
| Windows Built-in and URP rendering | 17/17 passed per player | Unity 6000.5.3f1 cloud-built players, tested locally; D3D11 |
| Windows HDRP rendering | 17/17 passed | Unity 6000.5.3f1, built with the Windows Editor; D3D11, Linear |
| WebGL 2 browser rendering | 17/17 passed per player | Unity 2021.3.45f2, 2022.3.62f3, and 6000.5.3f1; Chrome; NativeWebGL |
| Android player builds | Passed for Vulkan and OpenGLES3 | Unity 6000.5.3f1; rebuilt native libraries |

Rendering results use schema 3 and cover color calibration, semantic color
overrides, animation lifecycle stress, and the expected native upload backend.
Local logs and JSON/XML evidence are under the isolated worktree's ignored
`out/validation/` directory; no generated evidence was written under `results/`.

## Cloud validation

[Expanded platform matrix](https://github.com/gindemit/unity-rlottie/actions/runs/37999573079)
tests the updated native binaries and WebGL implementation at `79a7d81`.
The subsequent `129101c` change adds the opt-in Android assertion build and
its separate workflow; `a0bcb1c` corrects its GLES backend expectation and the
HDRP build host. Neither change alters the plugin implementation.

Confirmed downloaded artifacts show EditMode 3/3, PlayMode 37/37, Windows
D3D11 and D3D12 rendering 17/17 each, and WebGL 2 browser rendering 17/17.
Windows Vulkan packaging and Linux Vulkan build/player smoke also passed.

The WebGL browser runner now explicitly requests the complete smoke suite using
the player URL, waits for the completed encoded JSON result, and validates the
expected WebGL API/backend and exact color calibration. A first rendered frame
alone does not satisfy the job.

[Android rendering assertions](https://github.com/gindemit/unity-rlottie/actions/runs/38000233174)
builds dedicated Vulkan and OpenGLES3 assertion players. The opt-in compilation
symbol automatically starts the suite in Firebase; ordinary Android builds keep
their existing launch behavior. The job collects the application's result JSON
and requires the correct API/backend and all rendering checks to pass, in
addition to Firebase's test outcome.

The first physical run returned **17/17 passing checks for both Vulkan and
OpenGLES3**, on a Fujitsu F-01L running Android API 27 with an Adreno 506 GPU.
Both Firebase Robo tests passed. Vulkan's CI assertion gate also passed.
The GLES gate initially expected `NativeExternalTexture`; the runtime correctly
reports `NativeOpenGL` on Android GLES. The expectation was corrected after
checking the runtime source. Both downloaded JSON results passed the corrected
shared validator, including exact color calibration, semantic overrides,
lifecycle stress, schema 3, and native upload.

[Corrected Android gate rerun](https://github.com/gindemit/unity-rlottie/actions/runs/38002535081)
completed successfully: both builds, both physical Firebase tests, and both
application rendering assertion gates passed. Downloaded final JSON artifacts
again contain 17/17 passing checks for each API with the expected native backend.

The expanded matrix's HDRP Windows cross-build failed on the Linux Editor with
64 shader errors: Unity cannot compile HDRP's Direct3D DXC shaders on that host.
This was a build-host restriction, before player runtime testing. HDRP now uses
a Windows runner in the expanded matrix, and a dedicated
[Windows-host HDRP build/render run](https://github.com/gindemit/unity-rlottie/actions/runs/38002604629)
passed both build and rendering assertion jobs; the downloaded result confirms
17/17 passing checks on D3D11 in Linear color space with native upload.
The already built Unity 6000.5 Built-in
and URP players were downloaded and each passed 17/17 rendering checks locally;
the failed build dependency had prevented their cloud smoke jobs from starting.
The local Windows Editor HDRP build succeeded, and its player also passed
17/17 rendering checks on D3D11 in Linear color space.

The expanded matrix finished with the original Linux-host HDRP failure as its
only failed job. Unity 6000.5.3f1 Android Built-in and URP player builds both
passed. Its dependent render-pipeline Windows/Firebase jobs were skipped because
of that failed build dependency. Windows pipeline runtime coverage was recovered
with the local tests and successful dedicated HDRP cloud run described above.
The separate physical Android assertion workflow passed for both graphics APIs;
it uses the default Unity 2022.3.62f3 Built-in project. No new Unity 6000.5 URP
Android runtime pass is claimed from its successful APK build alone.

The Windows-host HDRP correction also separates the expanded matrix's Unity
Library cache by runner OS. Every launched workflow has finished; the corrected
Android and dedicated Windows HDRP workflows are green.

## Coverage boundaries

This is new runtime evidence for the tested configurations, not a claim that
every Unity version, browser, GPU, or Android device has been validated.

Unity 2019.4.41f2's Fastcomp native archives rebuilt successfully, but its local
player build was blocked by the installed editor's inactive license. Unity
2019 browser runtime and WebGL 1 are therefore still unverified.

The connected Samsung Galaxy Note10+ was locked and did not initialize its
renderer during the attempted local smoke run. That attempt is not a rendering
pass or a plugin regression. Physical-device cloud results are reported
separately above.

No new macOS or iOS runtime tests were run. Their successful native builds do
not resolve the previously reported macOS HDRP color-calibration failure.

All source and release updates are synchronized from `dev` to the twelve
`unity/**` branches. The original `main` checkout and its pre-existing local
changes were preserved.

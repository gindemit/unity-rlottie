# Unity RLottie implementation review · 22 November 2026

## Scope

The review started at `83e6d11f` on `main`. The 12 preserved `origin/unity/*` branches have identical Git trees for native source, Unity runtime, editor code and samples. Those trees match historical `origin/dev`. Their package and project settings differ by editor version and render pipeline. This makes the branch snapshots valuable for compatibility testing without requiring separate implementations of the fixes.

| Preserved editor | Configurations compared |
| --- | --- |
| 2019.4.41f2, 2021.3.45f2, 6000.2.13f1 | One version branch each |
| 6000.3.7f1, 6000.4.5f1, 6000.5.3f1 | Built-in, URP and HDRP for each |
| Additional historical refs | An older HDRP import and the two WebGL feature tips at `8b87b42c` and `f63121f3` |

The former WebGL working directory has no verified one-to-one mapping to a surviving branch. Current `main` also contains newer WebGL upload scheduling, archive staging, diagnostics and managed frame-cache work. All changes are in the canonical checkout.

## Findings and corrections

| Area | Previous behavior and impact | Change |
| --- | --- | --- |
| Async rendering | Reusing an outstanding request could replace future or slot bookkeeping. Waiting twice could invoke an empty future on an exception-disabled Android build. | Validate ownership and pending state, preserve the original request on misuse and release acquired slots on exceptional exits. |
| Texture lifetime | Creation used a registry pointer after unlocking. Failed replacement could leave stale native texture dimensions or handles. | Hold the state lifetime lock during creation and handoff. Clear cached texture state during reset. A synchronous waiter selects a ready slot under the same lifetime protection. |
| Native upload | WebGL conversion treated padded rows as contiguous pixels. D3D12 ring offsets needed independent placement alignment and checked multiplication. A failed fence-event setup could allow premature release. | Use the supplied row stride and share the WebGL sync implementation. Align and bound D3D12 slots, validate fence availability before recording a copy, and use fixed-size storage for the three Vulkan upload slots. |
| Managed parsing and cache sizing | Ignored JSON subtrees could recurse without a depth bound. Dense sampling allocated based on the candidate count before deduplication and budget enforcement. | Limit managed JSON to 128 nested containers. Check remaining pixel budget per distinct frame and skip repeated samples while preserving the first sample position. Release warmup indices once each clip is ready. |
| Worker failure and cleanup | Broad fallback handling could hide missing native symbols. A batch retirement failure could interrupt cleanup or leave the cache reporting readiness. | Surface binding failures, retain serial fallback for operational errors, release reservations and publish readiness after cleanup. |
| Inspector and sample presentation | A repaint or selection could change authored state. Frame controls could use the frame count as a drawable index. Sample views bound an intermediate texture on some backends. | Preserve mixed values, apply edits before rebinding previews, clamp the last frame, and bind the renderer's output texture. A preview releases its animation even after partial initialization or independent destruction. |
| On-device presentation | The automated run passed pixel checks while the performance lab covered the scene in its screenshot. The FPS unit wrapped below its value. | Collapse the benchmark panel when the smoke run starts. Measure the FPS string with the drawing style, cache it between updates, and position it within the screen safe area. |
| Shared-device testing | The smoke runner cleared the phone's entire log buffer and ignored failure to remove an old result. | Capture logs from device time at launch, require stale-result removal to succeed and clean up the temporary screenshot after a failed pull. |

Independent reviews caught a lifetime gap in an intermediate retry implementation, incomplete D3D12 fence failure handling, mixed-selection inspector behavior, and a failure during cache retirement. Each received a correction and a second review.

## Remaining limits

- D3D12 keeps GPU references alive when fence completion cannot be confirmed. The CPU footprint is released. Repeated fence failures can still accumulate GPU resources. A deferred release implementation needs a Windows GPU test that covers later fence completion and device shutdown.
- The managed marker depth limit applies to the managed parser. Native animation loading happens first on the loading path. The pinned rlottie parser still needs a separate assessment for applications that accept untrusted animation content.
- Historical branch comparison is source-level. Device qualification here covers the canonical Unity project and the Android hardware listed below. Other Unity versions and pipelines require their own player runs.

## Verification

The canonical project uses Unity 2022.3.62f3. The physical device was a Samsung Galaxy Note10+ (SM-N975F), running Android 12 on a Mali-G76. Before deployment, the coordinator checked the Orca terminal inventory, the previous Android lead's completed run, supervised worker status, ADB clients and forwarding, and foreground activity.

| Test | Result |
| --- | --- |
| Android IL2CPP player, full plugin runtime assembly | 75 passed, 0 failed, 0 skipped. The final player wrote its NUnit XML on the device in 69.2 seconds. USB retrieval succeeded after the editor's network callback timed out. |
| Android rendered scene, Vulkan and OpenGL ES 3 | 17/17 checks passed on each backend using native texture uploads. Both players presented changing pixels, responded to a button press, passed color calibration and semantic color overrides, and completed 12 create/dispose cycles. The final Vulkan screenshot shows the scene and the FPS unit on a single line. |
| Native ABI executable on the phone | Passed against the rebuilt ARM64 shared library, including duplicate waits, ownership mismatch, surface reuse and padded row conversion. |
| Windows x64 CTest | Native ABI and upload queue suites both passed. |
| Windows Direct3D 12 player | 17/17 rendered checks passed on a GeForce RTX 3080 Ti using the native external-texture backend. The packaged DLL matched the rebuilt x64 library. |
| Unity EditMode | 9/9 passed, including frame endpoints and mixed-selection serialization. |
| Binary verification | All four Android libraries retained their public plugin exports and have at least 16 KiB ELF load-segment alignment. The ARM64 and ARMv7 libraries in the player APK matched the rebuilt files byte for byte. Windows x64 retained its plugin exports. |
| Sample compilation | The SceneUI sample runtime sources compiled against the project's Unity player references. |

This pass rebuilt the four Android ABIs and the Windows x64 editor library. Other platforms retain their previously packaged native binaries until they are rebuilt and qualified. No package or release tag was published. The device lifecycle checks exercise repeated creation and disposal. A long-duration allocation profile and D3D12 device-loss run remain open.

## Second pass

Three reviewers examined managed rendering, native resource ownership and device automation on canonical `main`, starting from `7f56e754`. They also reviewed each other's changes.

| Finding | Change |
| --- | --- |
| A D3D12 reset could block the Unity thread and lose track of GPU resources when a wait failed. | Each texture now owns its mapped upload buffer, last-use fence and deferred cleanup. A completed fence releases resources immediately. Pending work keeps the DLL loaded until its callback finishes. Failed event registration uses a bounded-interval completion check. |
| Native parsing ran before the managed structural check. A file could change between the managed read and native loading, and a path-keyed model could outlive the file version. | The live and CPU file loaders take one snapshot and validate its structure before native parsing. Markers and pixels come from the same contents. Reloading an unchanged file now reparses its native model. |
| Background frame preparation allocated two full-size pixel buffers per worker and copied every frame twice. | Each worker copies the native rasterizer's staging pixels directly to their checked offset in the batch under the rasterizer's lifetime lock. This saves one full-frame buffer per worker and one full-frame copy per prepared frame. |
| A native upload target could disappear before submission or fail later. Recovery could leave the native descriptor pointing at the old surface and a UI component displaying a destroyed texture. | Recovery publishes the CPU surface and retries a failed submission once when Vulkan or OpenGL reports the target unavailable. UI components update their own bindings and preserve textures assigned by callers. |
| Color override marshalling read the collection size repeatedly and could allocate unmanaged memory before allocating its cleanup bookkeeping. | Snapshot the count, allocate cleanup storage first, and reject embedded NULs before passing keypaths to native code. |

### Device qualification

The Android runner builds both graphics backends, requires all 19 named checks, runs the Unity runtime assembly, and writes a compact `qualification.json`. Inspect this file first. It points to the NUnit XML, per-backend JSON and screenshots. The command is in the [README](../README.md#android-device-tests).

The rendered suite compares frame 30 against its synchronous reference after drawing a visibly different frame 0 on a 101 × 83 texture. The lifecycle check alternates synchronous and asynchronous rasterization and disposes each animation with a render outstanding. It verifies that Unity destroys the output texture. A stationary button's initial upload can arrive after the first readback. The test waits for frame zero without drawing it again. On three consecutive D3D12 launches, the second readback showed exactly 922 visible pixels.

The Windows native tests exercised normal completion, a delayed GPU fence, refused event registration, unloading the host DLL reference while work was pending, and process-local D3D12 device removal. The x86 and x64 release binaries passed all three CTest suites and preserved all 75 exported names on each architecture. The x64 DLL inside the Windows player matched the tested build.

The Galaxy Note10+ completed 600 cycles on each native backend. Android process-memory samples spanned 95 seconds on Vulkan and 101 seconds on OpenGL ES 3. Median allocated native heap in the middle and final thirds was 51.88 and 51.90 MiB on Vulkan, and 58.12 and 58.08 MiB on OpenGL ES 3. The samples cover the whole player. The disposal assertion checks every output texture individually. A longer session could expose slower accumulation.

| Second-pass verification | Result |
| --- | --- |
| Galaxy Note10+, Android 12, IL2CPP runtime assembly | 86/86 passed, including same-path file replacement and rendering a PNG relative to the animation file. After the editor heartbeat timed out, the runner recovered the player's XML over USB and verified all 86 individual verdicts. |
| Galaxy Note10+, Vulkan and OpenGL ES 3 | 19/19 rendered checks on each backend, including pixel equality after asynchronous rendering and texture destruction over 600 create/dispose cycles per backend. Two further 48-cycle runs per backend passed with an assertion that the scene finished on its original upload backend. |
| Galaxy Note10+, ARM64 native ABI | Passed on the phone against the packaged library. Both Android player builds contained byte-identical copies of the repository's ARM64 and ARMv7 libraries. |
| Windows, RTX 3080 Ti, D3D12 player | 19/19 rendered checks on three consecutive launches with 180 create/dispose cycles each. Two further launches passed all 19 checks and 180 cycles with the final backend assertion. |
| Unity 2022.3.62f3 on Windows | 9/9 EditMode and 83/83 PlayMode tests passed. |
| Windows native ABI, upload queue and D3D12 lifetime | 3/3 suites passed on x86 and 3/3 on x64. |
| Host-side automation | All three runner and result-validation suites passed on Windows PowerShell 5.1 and PowerShell 7. They cover stale files, partial results, conflicting device packages and transient USB result retrieval. |

An Android Gradle failure exposed a cleanup issue in Unity batch mode. The build now explicitly saves the restored settings before exiting. Repeating the failure with a rejected package identifier preserved the previous APK and left the project settings byte-for-byte identical. After device verification, the temporary apps were removed and the phone returned to its launcher.

### Remaining limits

A live D3D12 fence that never advances or reports device removal must keep its referenced allocations alive. The structural validation applies to Unity's managed loaders. Direct native ABI callers still use the pinned rlottie parser. The pinned Windows image decoder can render a relative external PNG as transparent. Embedded PNGs render correctly on Windows. Historical Unity configurations and other operating systems require separate player runs.

## Core performance follow-up

The first paired run against `7f56e754` found that repeated loads of the same 216 KB animation file were about 90% slower at `1246a098` on both Linux and Windows. The correction at `dfbe8b30` still reads the current file on every load. For four recent files of at most 1 MiB each, a byte-for-byte match reuses the decoded JSON and its managed validation. The native model still parses from that snapshot, including when relative assets are present. Tests passed for UTF-8 and UTF-16 with BOMs and for a replacement that preserved the original length and modification time.

The Windows run used Unity 2022.3.62f3 and an RTX 3080 Ti. It compared the corrected code with `7f56e754` across five paired launches.

| Workload | Baseline | With correction | Median paired change |
| --- | ---: | ---: | ---: |
| Repeated file load | 2.47 ms | 2.46 ms | −0.1% |
| JSON-data load | 2.09 ms | 2.10 ms | +0.7% |
| CPU rasterization at 512 × 512 | 0.95 ms | 0.96 ms | 0.0% |
| Eight-frame worker batch at 512 × 512 | 9.16 ms | 7.76 ms | −15.2% |
| D3D12 draw | 0.93 ms | 0.95 ms | +1.2% |
| D3D12 frame | 1.06 ms | 1.05 ms | −0.5% |
| D3D12 disposal p95 | 1.54 ms | 0.38 ms | −77.0% |

Unity passed 9/9 EditMode and 85/85 PlayMode tests. The hosted Linux gate passed all four workloads against the original baseline, with repeated file loads 12.8% faster by the median paired comparison. It also passed a rerun against the pre-correction code and the automatic control run after the benchmark workloads were isolated. [Original regression](https://github.com/gindemit/unity-rlottie/actions/runs/45506766449) · [Corrected comparison](https://github.com/gindemit/unity-rlottie/actions/runs/45508935296) · [Automatic control](https://github.com/gindemit/unity-rlottie/actions/runs/45508924110)

On the Galaxy Note10+, five launch pairs per graphics API exercised three animations with four 512 × 512 instances and 180 sampled frames per case. The 30 paired pixel hashes matched. Median paired p95 render-batch changes ranged from −3.0% to +1.2% on Vulkan and from −4.8% to +6.9% on OpenGL ES 3. In the OpenGL ES 3 case with the largest difference, the candidate was slower when it ran second and faster when it ran first. Observed frame-time p95 changed by at most 0.1% across both APIs.

The automatic gate covers Linux CPU timings. The Windows D3D12 and Android numbers are physical-device measurements. The repeated-file result applies to the tested fixture within the snapshot cache limit. Large files and working sets that exceed the cache can still pay for decoding and managed validation on reload.

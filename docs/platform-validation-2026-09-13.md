# Platform validation — 2026-09-13

Validated tip: `4a6fdcc` "Defer runtime texture destruction safely", native libraries
0.5.0-dev.244. Covers everything on `dev` since the 2026-09-06 device run, including
`47153b1` (dev.244 libraries), `fa06ba1` (Vulkan shutdown fix), `59d5787` (runtime color
overrides), `72232ed` (marker-driven shared frame caching), and `7b75882` (Unity-owned
textures for Windows OpenGL).

## How

- macOS: `RLottie.CI.BuildMatrix.Build` (`-ciTarget macOS`, Auto pipeline/API/color
  space) per clone, then the built player run with `-lottieSmokeResult <path>
  -lottieSmokeQuit`. Each result verified from the smoke JSON (17 checks).
- iOS: same BuildMatrix iOS export, `xcodebuild -scheme Unity-iPhone -configuration
  ReleaseForRunning -destination id=<device> -allowProvisioningUpdates
  -allowProvisioningDeviceRegistration`, install and launch via `devicectl` with
  `LOTTIE_SMOKE_RESULT`/`LOTTIE_SMOKE_QUIT` environment variables, result pulled from
  the app data container. Use a unique result filename per run — the container
  persists across reinstalls and a stale `smoke.json` from an earlier session
  otherwise masquerades as the new result.

Each combination below was built and run at least twice with identical results.

## Results

| Platform | Unity | Pipeline | Graphics API | Smoke | Upload backend |
|---|---|---|---|---|---|
| macOS (M4 Pro) | 6000.5.3f1 | Built-in | Metal | 17/17 | NativeExternalTexture |
| macOS (M4 Pro) | 6000.5.3f1 | URP | Metal | 17/17 | NativeExternalTexture |
| macOS (M4 Pro) | 6000.5.3f1 | HDRP | Metal | **16/17** | NativeExternalTexture |
| iPhone 11 (A13) | 6000.5.3f1 | Built-in | Metal | 17/17 | NativeExternalTexture |
| iPhone 11 (A13) | 6000.5.3f1 | URP | Metal | 17/17 | NativeExternalTexture |

Metal is the only graphics API available for macOS standalone in Unity 6000.x;
`-force-glcore` on a built player is ignored (the run still reports Metal).

## Open defect: HDRP macOS fails `exactColorCalibration`

Deterministic across three consecutive builds. In Linear color space the readback
returns the raw encoded value `RGBA(36,36,36,255)` where the check expects the
sRGB-decoded `RGBA(4,4,4,255)` — the external texture appears to be sampled without
sRGB decode under HDRP. Built-in and URP pass the same check on the same machine, and
both iOS pipelines pass it on device, so the behavior is specific to HDRP on macOS
standalone. The check is new (`ef510a1`, 2026-09-07), so this is first evidence for
this combination, not a proven regression.

## Not covered

Windows, Linux, Android, WebGL, and the HDRP editor-vs-standalone comparison for the
defect above. The iPhone 12 Pro Max (A14) was unavailable to `devicectl`.

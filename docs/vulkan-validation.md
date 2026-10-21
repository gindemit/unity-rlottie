# Vulkan validation

The rendered-player smoke test is the reusable Vulkan qualification test. Each
result records the graphics device name, vendor and PCI-style vendor/device IDs,
driver/API version, operating system, graphics API, active color space, and the
upload backend selected by both scene components. It also validates:

- visible and changing pixels from the sample `AnimatedImage` and
  `AnimatedButton`;
- exact RGBA values for eight white/RGB/CMY/black calibration bars and eight
  grayscale steps, including the expected sRGB-to-linear conversion in Linear
  projects;
- 12 alternating synchronous/asynchronous create, draw, readback, and dispose
  cycles across four texture sizes;
- native Vulkan upload when the runner is invoked with the required backend.

## Desktop Vulkan, Gamma and Linear

Build a separate player for each color space. The build helper restores the
project's original graphics API and color-space settings after each build.

```powershell
scripts/ci/build-player.ps1 `
  -Unity '<Unity.exe>' `
  -ProjectPath 'unity/RLottieUnity' `
  -Target Windows64 `
  -GraphicsApi Vulkan `
  -ColorSpace Gamma `
  -OutputPath '<output>/gamma/RLottieSmoke.exe' `
  -LogFile '<output>/gamma-build.log'

scripts/ci/build-player.ps1 `
  -Unity '<Unity.exe>' `
  -ProjectPath 'unity/RLottieUnity' `
  -Target Windows64 `
  -GraphicsApi Vulkan `
  -ColorSpace Linear `
  -OutputPath '<output>/linear/RLottieSmoke.exe' `
  -LogFile '<output>/linear-build.log'
```

Run each built player through repeated process teardown and initialization:

```powershell
scripts/ci/run-windows-vulkan-relaunch.ps1 `
  -Player '<output>/gamma/RLottieSmoke.exe' `
  -OutputDirectory '<results>/gamma' `
  -ExpectedColorSpace Gamma `
  -RelaunchCount 3
```

Repeat for the Linear player. The generated `vulkan-relaunch-summary.json`
keeps the hardware identity and paths to every full smoke result and player
log. A relaunch covers Unity graphics-device/plugin shutdown followed by fresh
initialization in another process. It is deliberately not described as an
OS-level GPU driver reset, which Unity's player API cannot request safely.

Linux uses the existing Vulkan-capable build path and should run the player
with `-lottieSmokeResult <path> -lottieSmokeQuit`. Confirm that the result says
`graphicsApi: Vulkan`; the Linux build retains OpenGL Core as a startup fallback
because Unity may reject Vulkan on machines without a suitable physical GPU.

## GPU-vendor coverage

Archive the JSON result from each physical GPU rather than inferring vendor
coverage from API support. At minimum, qualify one current device from each
available desktop Vulkan driver family (AMD, Intel, and NVIDIA) and retain the
reported vendor/device IDs and driver version. Android Mali and Adreno evidence
remains documented in the dated benchmark directories.

Passing on one GPU or a software Vulkan implementation is not evidence for a
different vendor. Do not mark a row as tested until its rendered-player JSON is
available.

## Evidence status

On 2026-09-06, Unity 2022.3.62f3 Windows players passed this suite on an NVIDIA
GeForce RTX 3080 Ti (vendor ID `4318`, device ID `8712`, reported API/driver
string `Vulkan 1.1.0 [0x93d58000]`) under Windows 11:

| Color space | Clean launches | Lifecycle cycles | Exact colors | Upload backend |
|---|---:|---:|---|---|
| Gamma | 3/3 | 36/36 | 3/3 | `NativeVulkan` |
| Linear | 3/3 | 36/36 | 3/3 | `NativeVulkan` |

Each launch passed every scene animation/readback check as well. Local complete
JSON summaries and player logs from the original run are under
`results/vulkan-coverage/`; the independent review rerun, including the
color-space-sensitive grayscale assertions, is under `results/vulkan-review/`.
They are generated evidence rather than source files.

AMD and Intel desktop Vulkan, physical Linux Vulkan, and an OS-level GPU driver
reset remain untested. The harness must not be described as evidence for those
rows until results from that hardware are archived.

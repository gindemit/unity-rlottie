# Fork, branches, and platform validation audit — 2026-10-09

## Scope and source snapshots

Inspected fetched remote refs for `gindemit/unity-rlottie` and its native
dependency fork `gindemit/rlottie`. GitHub reports `dev` as the Unity repository's
default branch and `master` as the native fork's default branch.

- Unity integration: `d15ff28387fc311c1dcc79ada76183b85e7e94f9`, release
  `0.5.0-dev.245`, 2026-10-04.
- Native fork and Unity's pinned submodule:
  `3840c6e33e56b15df4e8b1b5d4639579b2d5677e`, 2026-09-13.
- Samsung upstream: `ea06d2f`, 2026-09-23.

## Native fork completeness

The native fork's `master` contains the entire `unity-rlottie-win-fixes` branch.
The shape-budget branch's two remaining commits have patch equivalents in
`master`. The legacy-color branch has a different commit identity, but its
parser implementation is present unchanged; the only parser difference is an
explanatory comment added on `master`. These old fork branches have no missing
functional change that needs merging into the pinned native dependency.

The `.gitmodules` tracking branch `unity-rlottie-win-fixes` was two commits
behind `master`. It was fast-forwarded and pushed to `3840c6e` so a future
`git submodule update --remote` follows the same native revision already pinned
by canonical Unity `dev`, including the gradient/NEON optimization and static
layer invalidation fix. No native source change or history rewrite was needed.

The fork includes Windows path canonicalization, legacy 0–255 color
normalization, shape/keyframe budget handling and tests, an MSVC compilation
fix, SSE2/NEON raster blending, gradient/NEON optimizations, and invalidation of
static layers after runtime property changes.

Samsung upstream has seven commits absent from the fork: shape-group nesting
limits, README changes, fractional image-ratio handling, merged-scanline span
limits, a totalFrame test correction, bitmap allocation tests, and skipping
images that cannot be allocated. They were identified but not imported in this
audit: updating the dependency and rebuilding/revalidating every platform is a
separate code change. Consequently this fork is not fully synchronized with
current Samsung upstream.

## Unity branch completeness and synchronization

Before synchronization, all 12 remote `unity/**` branches contained the latest
shared source change `a5ceb75`, but lacked the native-binary release `d15ff28`.
The synchronization accompanying this report merges canonical `dev` into all
12 targets using `scripts/sync-unity-branches.ps1`, preserving each target's
package manifests and Unity-version metadata. Isolated temporary clones are
used because adjacent Unity-version clones are absent from this workspace.

Targets: 2019.4.41f2, 2021.3.45f2, 6000.2.13f1, and 6000.3.7f1,
6000.4.5f1, 6000.5.3f1 with Built-in, URP, and HDRP variants for those final
three Unity versions.

`development` and several older feature branches are ancestors of `dev`.
The old Direct3D12 command-list fix has a patch equivalent in `dev`.
Other historical WebGL and source-refactoring branch tips are not universally
ancestors or exact patch equivalents: their functionality has evolved in
subsequent integration work, so commit ancestry alone cannot establish that
every historical branch is incorporated verbatim.

`main` has four commits outside `dev`; one is patch-equivalent and the Linux
OpenGL capability/backend work is present in later `dev` implementations.
The macOS setup script exists only on `main`. This audit does not modify `main`
or merge its older implementation over current runtime fixes. The local
checkout also has a private Test Framework manifest commit; canonical `dev`
already uses the registry Test Framework package. Local branch tips and
uncommitted/generated files are preserved.

## Platform evidence and limitations

Successful native-library build workflow for `a5ceb75`, 2026-10-04:
[Build and Test](https://github.com/gindemit/unity-rlottie/actions/runs/37221161672).
It built Windows, Android, macOS, iOS device, iOS x64 simulator, Linux, and WebGL
libraries and published `d15ff28`. A native-library build alone is not a Unity
runtime test.

Successful Unity workflow for `d15ff28`, 2026-10-04:
[Unity platform test matrix](https://github.com/gindemit/unity-rlottie/actions/runs/37221401423).

| Platform | Evidence | Remaining qualification |
|---|---|---|
| Windows | Latest CI: D3D11 and D3D12 rendered-player smokes passed; Vulkan player built and backend packaging verified | Latest CI does not run the Vulkan player; broader GPU/pipeline coverage remains incomplete |
| Android | Latest CI: Vulkan player build and Firebase Robo test passed; September physical-device GLES/Vulkan results are recorded | Optional Built-in/URP physical-device gate was skipped in latest CI; Robo success does not prove every rendering assertion |
| macOS | Latest native build passed; September 13 M4 Pro/Unity 6000.5.3f1 Metal Built-in and URP players passed 17/17 checks | HDRP passed 16/17: open exactColorCalibration failure; latest October runtime rerun not evidenced |
| iOS | Latest device/simulator native builds passed; September 13 iPhone 11/Unity 6000.5.3f1 Metal Built-in and URP players passed 17/17 | Latest October runtime rerun not evidenced; device/version coverage is limited |
| WebGL | Latest native build passed; September 29 verified archive staging and a Unity 6000.5.3f1 build succeeded; September 7 browser smoke passed WebGL 2 | Full current browser/assertion matrix absent; older 2019/2021 toolchains had archive failures, and the September 29 build does not establish those versions are repaired |

See [macOS/iOS validation](platform-validation-2026-09-13.md),
[WebGL browser validation](benchmark-results/2026-09-07-webgl/README.md), and
[archive provenance](../unity/RLottieUnity/Assets/LottiePlugin/Editor/WebGLArchives/PROVENANCE.md).

No new platform runtime tests were executed by this audit. Branch
synchronization distributes previously built libraries and this report;
it does not establish that every Unity version/pipeline has passed on every
platform.

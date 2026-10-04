# WebGL archive provenance

The ABI-specific archives were first introduced by upstream commit `d91e616`
(`Support Unity 6000.5 WebGL exception ABI`). Inspection of that commit's
archives found they lacked C API exports referenced by the package's current
`NativeBridge.cs` (including `lottie_apply_color_overrides`), so those stale
artifacts are replaced here with builds from the exact upstream source checkout
below. Both variants are stored as inert `.bytes` assets so Unity does not
import or link them directly. The Editor prebuild stage selects one pair,
verifies its SHA-256, and imports temporary PluginImporters under the consuming
project's `Assets/LottiePlugin.GeneratedWebGL`.

| Variant | Unity selection | `libLottiePlugin.a.bytes` SHA-256 | `librlottie.a.bytes` SHA-256 |
|---|---|---|---|
| Legacy | Before `UNITY_6000_5_OR_NEWER` | `ad339f1cffb40a9fc17a201d0cc4fbfe2d1b4eb4561f4643229fa6612b68359d` | `5b1240fdcc79e8b23fad54063956fa2baf3c9be06696cef4d27aa33e785f11da` |
| WasmExceptions | `UNITY_6000_5_OR_NEWER` | `9a32ad55afd774ccf6ddee4aaddead625697dddb021ce381bc8800e0b4519274` | `31bf7e54da95c553ef598640ede9faedee02cb6a74c7ab56d048c653c6e2d994` |

Both pairs were built from source commit `2921ac263c3538487708417d59381ea0d6d21a91`
with rlottie dependency `3840c6e33e56b15df4e8b1b5d4639579b2d5677e`, CMake 4.4.2,
and the Emscripten 4.0.20-git toolchain bundled with Unity 6000.5.3f1. The
`LottiePlugin` CMake target was built with `RLOTTIE_WEB_ASSEMBLY=1`,
`LOTTIE_MODULE=OFF`, `BUILD_SHARED_LIBS=OFF`, Release mode, and
`RLOTTIE_WEBGL_WASM_EXCEPTIONS=OFF` for Legacy or `ON` for WasmExceptions.
`llvm-nm` confirmed all three color API exports exist in both generated plugin
archives. The source tree's default CMake `all` target also attempts to build
rlottie's unrelated `lottie2gif.js` example, which fails under Unity's emsdk
4.0.20; the named `LottiePlugin` target itself succeeds.

The generated `.bytes` archives are not byte-identical to the package's
existing `Plugins/WebGL/*.a` files. Those files remain in place with their
existing GUIDs for source history, but their PluginImporter settings now
exclude WebGL. Their upstream binary provenance is `dd2ccf6` for
`libLottiePlugin.a` and `904c054` for `librlottie.a` (generated binaries shipped
in 0.5.0-dev.242 and 0.5.0-dev.243 respectively). No equality between those
binaries and the regenerated ABI-specific archives is claimed.

The two checked-in `Plugins/WebGL/*.a` assets keep their existing GUIDs while
their importer compatibility is disabled. All four archived `.bytes` assets
keep their upstream GUIDs. Removing the old editor selector retires only its
script GUID (`2f70a322a1014ad08bd91bfba9e85107`); it is not referenced by a
scene or serialized asset. The replacement stager has GUID
`4a80ef42f8f948639f03db66489d0c27`. Generated player-local archive GUIDs are
stable constants in the stager and do not live in PackageCache.

The temporary generated PluginImporter GUIDs are stable constants in the
stager and are removed with the generated assets after a successful build. A
failed build can leave the owned staging directory behind; the next build
preprocess callback verifies its ownership marker and removes it before staging
again. No file is written under PackageCache or the resolved package path.

The stager resolves its source root from the assembly's resolved UPM package
path when imported as a Git package. For the repository's direct
`Assets/LottiePlugin` layout it locates its own MonoScript and derives the
package root from `Assets/LottiePlugin/Editor/src`. Focused Editor tests cover
both layouts and the fail-closed case. A WebGL build of the upstream
`unity/RLottieUnity` project (which uses the direct Assets layout) succeeded
under Unity 6000.5.3f1 and logged selection of the verified WasmExceptions
pair.

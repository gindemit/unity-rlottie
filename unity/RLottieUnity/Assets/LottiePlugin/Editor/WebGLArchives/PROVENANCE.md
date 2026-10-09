# WebGL archive provenance

The ABI-specific archives were first introduced by upstream commit `d91e616`
(`Support Unity 6000.5 WebGL exception ABI`). Inspection of that commit's
archives found they lacked C API exports referenced by the package's current
`NativeBridge.cs` (including `lottie_apply_color_overrides`), so those stale
artifacts are replaced here with builds from the exact upstream source checkout
below. All variants are stored as inert `.bytes` assets so Unity does not
import or link them directly. The Editor prebuild stage selects one pair,
verifies its SHA-256, and imports temporary PluginImporters under the consuming
project's `Assets/LottiePlugin.GeneratedWebGL`.

| Variant | Unity selection | `libLottiePlugin.a.bytes` SHA-256 | `librlottie.a.bytes` SHA-256 |
|---|---|---|---|
| Legacy | Unity 2020, 2022, and 6000 before 6000.5 | `b3d32adefdcfab13612323900f31a0addeafd254959d69d0b9a7fe6c9399e6e1` | `754e15e226e819e83bc0d6e8e1ea22a8b1bf882c304ef1b2361bd4a2b7762cbb` |
| WasmExceptions | `UNITY_6000_5_OR_NEWER` | `088d14b0909acdc6076e1ff1525e8a6d69094d284d4151fc03b6a6ee75504a51` | `b17cda443208f9c746a6610e8edee79257684fdd8ae39f93f8df3a1263d908da` |
| Legacy2021 | Unity 2021 | `c57c5d811ebf986dfb1c6008cb319ad041ecba7c5a62a0b8d59e93bc2d9462ed` | `b5078d63c82615a8ad23940d5a67d49a6f01c0e40ebe3efea0142e16932b141c` |
| Legacy2019 | Unity 2019 | `e03a47196fb953fd15904324c378d9a9b74394ab17c0dfe44136c1a3ff7418de` | `42dc8c94ed02a010e77f84707015822adc6b18381e94f23273ecd9facf58424a` |

All four pairs were rebuilt on 2026-10-10 from source commit `e48599d`
with rlottie dependency `e9bf55b` (Samsung upstream through `ea06d2f`).
Legacy uses Unity 2022.3.62f3's bundled Emscripten toolchain; WasmExceptions
uses Unity 6000.5.3f1's bundled Emscripten 4.0.20-git toolchain. The
Legacy2021 pair uses the bundled Unity 2021.3.45f2 toolchain against the same
native sources, because the Unity 2022 archives reference newer libc++ symbols
that Unity 2021 cannot link. Legacy2019 uses Unity 2019.4.41f2's bundled
Fastcomp compiler with `EMCC_WASM_BACKEND=0` against the same sources. The
`LottiePlugin` CMake target was built with `RLOTTIE_WEB_ASSEMBLY=1`,
`LOTTIE_MODULE=OFF`, `BUILD_SHARED_LIBS=OFF`, Release mode, and
`RLOTTIE_WEBGL_WASM_EXCEPTIONS=OFF` for the Legacy variants or `ON` for
WasmExceptions.
`llvm-nm` confirmed the color override and WebGL render-event exports in the
rebuilt plugin archives. The source tree's default CMake `all` target also
attempts to build
rlottie's unrelated `lottie2gif.js` example, which fails under Unity's emsdk
4.0.20; the named `LottiePlugin` target itself succeeds.

The generated `.bytes` archives are not byte-identical to the package's
existing `Plugins/WebGL/*.a` files. Those files remain in place with their
existing GUIDs for source history, but their PluginImporter settings now
exclude WebGL. The native release workflow updates these inactive assets
independently of the ABI-specific archives. No equality between those binaries
and the regenerated ABI-specific archives is claimed.

The two checked-in `Plugins/WebGL/*.a` assets keep their existing GUIDs while
their importer compatibility is disabled. The original four archived `.bytes`
assets keep their upstream GUIDs; the Unity 2019 and 2021 pairs have their own
asset GUIDs. Removing the old editor selector retires only its
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

# Unity RLottie Plugin

## Branch policy

- The owner explicitly replaced the old `dev`/adjacent-clone workflow on
  2026-10-21. Use `main` in the single working checkout at
  `C:/Work/git/gindemit/unity-rlottie/unity-rlottie`.
- Commit and push each coherent reviewed step to `origin/main`, including clearly
  identified unfinished work. Do not create more source clones or worktrees.
- Historical `dev`, `unity/**` branches and release tags retain their old source
  identities; they are not parallel working copies. Do not run the old
  `scripts/sync-unity-branches.ps1` workflow or recreate adjacent clones.
- Serialize imports, native builds, tests and Git indexes in the canonical copy;
  keep generated outputs and raw evidence outside the source repository.
- Main-source backup is separate from package generation/publication and actual
  platform qualification. Preserve immutable `.251` delivery/tag identities;
  do not claim a new binary build from a source-history merge.
- See `docs/recovery/single-checkout-2026-10-21.md` and workspace
  `plans/REPOSITORY-CONSOLIDATION-2026-10-21.md` for preserved history and checks.

## Native API contract

- Ship C# and native libraries together with the current API on every supported
  platform. Assume matching, up-to-date binaries; missing libraries or symbols
  are integration errors that must surface.
- Do not add missing-entry-point/library exception fallbacks, availability caches,
  legacy polling, or platform exclusions solely to support outdated binaries.
  This also applies to subagents and broad exception handlers around native calls.
- Preserve real device/backend capability checks, native failure return values,
  resource cleanup and recovery. iOS uses the native Metal renderer and pending
  upload query through `__Internal`; WebGL uses its separate upload path.

## Adjacent Unity clones

The old adjacent-clone layout is retired by the owner decision above. Recover
historical Unity-version/pipeline states from preserved Git history or private
archives when investigating old results; do not restore persistent parallel
working copies. Required dependency submodules remain part of this checkout.

This repository hosts a Unity plugin that brings the [rlottie](https://github.com/Samsung/rlottie) animation library to the Unity ecosystem. The project builds native binaries for multiple platforms using CMake and provides C# scripts to integrate Lottie animations inside Unity games or applications.

## Repository layout

```
/ (root)
├── dependency/          # External dependencies as git submodules
│   ├── cmake-ios-toolchain/   # iOS toolchain for CMake (submodule)
│   ├── pixman/                # Assembly files required for rlottie on ARM
│   └── rlottie/               # rlottie library source (submodule)
├── projects/
│   ├── AndroidStudio/   # Android example project
│   └── CMake/           # CMake configuration to build the native plugin
├── src/                 # C++ implementation of the plugin interface
├── unity/
│   └── RLottieUnity/    # Unity package with assets, runtime and editor code
├── README.md            # Usage instructions
└── LICENSE              # MIT license information
```

### Native plugin
- **src/** contains three files that expose rlottie functions to Unity via a C style API.
- **projects/CMake/CMakeLists.txt** builds `LottiePlugin` along with the rlottie dependency for each target platform.
- The CMake script handles architecture specific flags (e.g. NEON assembly on Android) and copies the output into `out/Plugins` for Unity to consume.

### Unity package
- **unity/RLottieUnity/Assets** holds the Unity package contents.
  - `Runtime` contains C# wrappers around the native library (`LottieAnimation`, `NativeBridge`, etc.).
  - `Editor` includes custom inspector scripts.
  - `Samples~` provides sample scenes and prefabs demonstrating how to use the plugin.
- Prebuilt plugin binaries for platforms (Windows, Android, iOS, Linux, macOS, WebGL) reside under `Assets/LottiePlugin/Plugins`.

### Examples & Tools
- The Android Studio project under `projects/AndroidStudio` is a minimal Android application showcasing the plugin.
- Sample scenes in the Unity package (`Samples~/SceneUI`) show how to play Lottie JSON animations as `Texture2D`.

## Building
1. Initialise the git submodules to fetch rlottie and the iOS toolchain:
   ```bash
git submodule update --init --recursive
   ```
2. Use CMake from the `projects/CMake` directory to build the native library for your target platform. Generated binaries will appear in `out/Plugins/<Platform>/<arch>`.

## Usage in Unity
Import the Unity package found under `unity/RLottieUnity` into your project or reference it via Git using the path `unity/RLottieUnity/Assets/LottiePlugin`. The runtime scripts expose an `AnimatedImage` component that can play Lottie JSON files on UI `RawImage` elements.

# Unity RLottie Plugin

## Branch policy

- Treat `dev` as the canonical integration branch for all shared development.
- Prefer starting work from `dev`, commit completed shared changes to `dev`, and push to `origin/dev`.
- Do not commit, merge, cherry-pick, push, or otherwise modify `main`. Only touch `main` when the user explicitly overrides this rule in the current request.
- Use `dev` as the source branch when propagating shared changes into the Unity-version and render-pipeline branches. Do not use `main` as the propagation source.
- Keep version- or pipeline-specific work on its matching `unity/**` branch or adjacent clone, while bringing shared fixes in from `dev`.
- Every push to `dev` must be accompanied by synchronization to all `unity/**`
  branches. Do not use a standalone `git push origin dev`; after committing on
  `dev`, run `scripts/sync-unity-branches.ps1`. It pushes `dev`, merges it into
  every adjacent Unity-version/render-pipeline clone, and pushes each target
  branch. Treat any failed target as an incomplete push and resolve or report it
  before finishing the task.
- GitHub Actions for shared changes are expected to run from pushes to `dev`; verify the `dev` workflow run after pushing when CI status matters.

## Adjacent Unity clones

The parent directory contains separate repository clones for supported Unity versions and render pipelines. Prefer using the appropriate existing adjacent clone instead of creating temporary worktrees or switching this clone to a version/pipeline branch. Clone names follow `unity-rlottie-<version>`, `unity-rlottie-<version>-urp`, and `unity-rlottie-<version>-hdrp`; inspect the parent directory and use the newest installed version that matches the requested pipeline. Use this `unity-rlottie` clone for shared development work when no version/pipeline-specific clone is required.

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

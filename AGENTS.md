# Unity RLottie Plugin

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


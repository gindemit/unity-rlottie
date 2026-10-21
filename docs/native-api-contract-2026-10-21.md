# Current native API contract — 2026-10-21

Owner request: require matching, current native libraries; remove managed
compatibility paths for missing libraries/entry points, audit similar code, and
explain the iOS exclusion from pending-upload polling.

## Implementation

- `LottieUploadPump` queries pending work directly on native players and Editor,
  including iOS. Removed the availability flag, exception handler and legacy
  fixed-budget fallback for outdated native binaries.
- Removed five missing-entry-point/library catch pairs from texture registration,
  Vulkan capability querying and WebGL/OpenGL upload availability querying.
- Removed broad handlers around native plugin registration that logged and
  continued after native contract failures.
- Kept device/backend capability results, upload rejection/recovery, resource
  cleanup and the Unity external-texture creation handler. These handle actual
  operational failures rather than API-version compatibility.
- Recorded the contract for contributors and subagents in `AGENTS.md`, `README.md`
  and the scheduling harness instructions. C# and native binaries ship together.

## Why iOS was excluded

Commit `2f41935` introduced both the queue query and the managed iOS exclusion.
Its harness instructions explicitly described avoiding unsatisfied linker
references in existing static libraries. This was old-binary compatibility,
not a renderer limitation.

iOS uses native Metal rendering and statically links through `__Internal`.
`UploadQueue.cpp` is a common CMake source. The query declaration excludes only
Emscripten, and its implementation has no iOS exclusion. Both bundled iOS plugin archives
(device and x86_64 simulator) contain `_lottie_get_pending_upload_count`.
The iOS exclusion is therefore removed. WebGL keeps its separate callback path
because it does not use this native upload queue; WebGL also has a native upload
bridge, so its exclusion does not mean it has no native rendering code.

## Validation and limits

- Scheduling harness: `dotnet run --project tests/managed-upload-pump/UploadPumpTests.csproj`,
  repeated with `-p:DefineConstants=UNITY_IOS`, `UNITY_WEBGL`,
  `UNITY_IOS%3BUNITY_EDITOR` and `UNITY_WEBGL%3BUNITY_EDITOR`: all passed.
- Exact-source disposable copy of the full managed Runtime compiled with .NET 9
  against installed Unity 6000.5.3f1 managed references and the existing
  `out/project-6000/Library/ScriptAssemblies/UnityEngine.UI.dll`. Configurations:
  `UNITY_STANDALONE_WIN`, `UNITY_IOS`, `UNITY_WEBGL`,
  `UNITY_EDITOR%3BUNITY_IOS`, `UNITY_EDITOR%3BUNITY_WEBGL`: all passed, with
  unused-field warnings. Copy: `%TEMP%/lottie-native-contract-_an0vlrv`.
- Static audit of Unity C# found no further typed missing-library/entry-point
  compatibility handlers. Cleanup/rethrow and parallel-worker recovery handlers
  remain; they were not introduced for outdated-native compatibility.
- No native source or binaries changed. No new Unity player build, IL2CPP link,
  GPU rendering, frame-time measurement or physical-device run was performed.
  Compile/scheduling checks and symbol inspection do not establish those results.

## Independent review

The read-only `native_compat_audit` subagent approved the runtime, harness and
instruction changes, independently reran iOS/WebGL scheduling checks and
`git diff --check`, then reviewed this receipt. Its requested declaration versus
implementation wording correction is included above. No remaining findings.

The reviewer verified both iOS archives with the installed NDK's
`llvm-nm.exe --defined-only --extern-only <archive>`, filtering for
`lottie_get_pending_upload_count`: both report a defined external `T` symbol.
Tool directory: Unity 6000.5.3f1's
`Editor/Data/PlaybackEngines/AndroidPlayer/NDK/toolchains/llvm/prebuilt/windows-x86_64/bin`.

GitHub API confirms `dev` is the default branch; the local `origin/HEAD -> main`
symbolic reference was stale. Deliver shared changes on `dev` and synchronize
the `unity/**` branches per repository policy.

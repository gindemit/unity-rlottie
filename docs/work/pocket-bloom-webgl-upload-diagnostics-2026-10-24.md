# Pocket Bloom managed WebGL upload diagnostics

The owner requested release acceptance and actual WebGL profiling. A single
startup `GL_INVALID_OPERATION` remains visible in Development players. The
existing managed path may recover through `Texture.Apply` and successful texture
re-registration without a fallback warning. Add bounded observation of that
path; do not change upload behavior or claim native GL correctness from counters.

## Source and package baseline

Canonical main starts at `b084301e96dbafe934cb1cfb61ec17a0089f2f6d`.
Pocket Bloom currently consumes immutable `.252` source
`36ea22b5e9e2a96d65ca6f8419cae0ef5a8082ec`. Main's package is `.251`.
Native source/header/CMake/dependency trees match; their WebGL payloads match.
The package differs in exactly six files. Restore those exact immutable Git
blobs before diagnostic implementation, preserving all other main files:

- `unity/RLottieUnity/Assets/LottiePlugin/Plugins/Windows/x86/LottiePlugin.dll`
- `unity/RLottieUnity/Assets/LottiePlugin/Plugins/Windows/x86_64/LottiePlugin.dll`
- `unity/RLottieUnity/Assets/LottiePlugin/Runtime/src/LottieMarkerSet.cs`
- `unity/RLottieUnity/Assets/LottiePlugin/Tests/Runtime/src/LottieCpuAndFrameCacheTests.cs`
- `unity/RLottieUnity/Assets/LottiePlugin/Tests/Runtime/src/LottieMarkerSetTests.cs`
- `unity/RLottieUnity/Assets/LottiePlugin/package.json`

Require the resulting 545-file package to match the `.252` Git tree exactly.
Do not copy Unity's injected package `_fingerprint`. This preserves the current
consumer behavior and binary identities; it is not a new native build. Preserve
main's AGENTS/recovery records and immutable delivery tags. No registry release,
tag rewrite, source clone, worktree or native rebuild is selected.

## Diagnostic contract

The only runtime writable path after baseline reconciliation is
`unity/RLottieUnity/Assets/LottiePlugin/Runtime/src/LottieAnimation.Platform.cs`.
Compile all added code only for Development WebGL outside Editor. Initialize
the optional report on the main thread before scene load, never through Unity
API calls in a type initializer. Enable only for HTTP(S) loopback with one exact
`lottieUploadDiagnostics=1` query parameter; reject duplicates/conflicts.

Observe existing availability checks, native request rejection, recovery Apply
calls, actual re-registration attempts/results and fallback transitions. Count
registration attempts immediately before the actual call, after Apply. Store
at most eight failure episodes; retain bounded aggregate/drop counts and clear
instance-count semantics. Export JSON only on an explicit read-only request.
With the gate off, add no per-frame logging or string/collection creation.

Do not add native calls, GL state changes/error reads, plugin events, upload
attempts, recovery fallbacks or missing-library compatibility paths. Existing
exceptions and native failure behavior must remain visible. All native package
payload hashes remain unchanged from the consumed `.252` baseline.

## Verification and integration

Root owns Git integration and serializes package/import/Editor/build leases.
Keep raw proof outside repositories. Fresh nonauthor review precedes activation.
Check active-source equivalence for Release WebGL, Editor and other platforms;
test the URL gate and 0/1/8/>8 event bounds, counter meanings and read-only export.
Compile Development and normal WebGL; verify diagnostic API absence in Release.

Only after package review, select a new immutable Git source pin in Pocket Bloom.
Its Development probe may request the public diagnostic export outside allocation
measurement windows. Bind source/package inventories, unchanged native hashes,
build GUID and actual browser observations. Report unexercised recovery branches
as unexercised; successful managed registration is not proof of native upload
completion or error-free GL. Preserve the separate allocation comparison,
normal-release acceptance, physical-device and portal/publication gates.

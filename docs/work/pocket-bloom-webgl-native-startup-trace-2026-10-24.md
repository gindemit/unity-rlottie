# Pocket Bloom WebGL native startup observation

## Reviewed consumer diagnostic adoption — resumed 2026-10-24

After the actual isolated native payload and strict manifest validation pass,
ROOT may pin consumer `Packages/manifest.json` to immutable `ca46187a19dd60a4a7593a27c3473810ec8edd15`
and let Unity generate `Packages/packages-lock.json`. Only
`Assets/Editor/GardenPreviewBuilder.cs` and
`Assets/Scripts/Garden/GardenWebAllocationProbe.cs` are additional writable files.
Preserve all six unrelated owner deltas and ordinary archive/package identities.

The builder selects the public transactional diagnostic wrapper only for an exact,
unique pair of manifest/hash options on a Development CrazyGames Player WebGL
build. Reject malformed/duplicate selections before scene/projection mutation;
add the trace define only to that build's extra scripting defines. Skip the
ordinary output-directory creation in the selected branch so wrapper preflight
comes first. Absent selection keeps ordinary BuildPipeline behavior unchanged.
This distinguishes CLI syntax validation before existing game-build projection
from complete manifest validation before any native staging cleanup/mutation.
The existing game scene/settings projection runs under ROOT's external exact-byte
backup/restore harness; a wrapper rejection must still restore those inputs.
ROOT also validates the actual payload read-only before adoption. Do not claim
that no game projection occurred merely because native staging was rejected.
For this selected diagnostic only, require the existing Player scene and skip
`CampaignPackageTestResourceProjection`: manifest source verification must still
see the six renderer test JSON resources and metadata. These unused root resource
keys do not collide with namespaced game loads; keep test assemblies excluded.
Inventory the retained resources. Normal builds retain their existing projection.
This larger diagnostic payload is not a release, size or loading/performance
qualification artifact. The outer harness restores exact importer metadata and
ProjectSettings bytes rather than relying only on logical importer restoration.
The explicit existing probe Snapshot may log the public native-state JSON only
inside the full WebGL/non-Editor/Development/trace compile guard and after its
existing overlap/readiness/bounds checks. No automatic export or graphics fix.

Require independent source review, actual imported Editor compilation, active
Release probe equivalence, exact native/package binding and actual Development
build/staging rollback receipts. The current normal `0734ec2` ZIP stays immutable;
diagnostic builds and source pushes do not qualify it or activate public gates.

## Owner-requested wrap-up checkpoint

The owner requested task-by-task continuation on 2026-10-24. The implementation
checkpoint is unfinished source backup, not permission to activate a diagnostic
package or build. Consumer remains `0734ec2`, renderer pin remains `def31c353`;
ordinary native archives and package versions are unchanged.

Managed helper/pump host contracts pass 134 assertions and 18 compile-off token
comparisons; runtime-core host compilation against installed Unity references
passes. Evidence is external `native-trace-managed-host/result.json`. These use
native/Unity stubs and do not establish constructor/recovery or IL2CPP execution.
Root reviewed the guarded managed source and matched all five source hashes.

Native source, ABI exports, bounded callback storage and external-output CMake
branch are drafted. Four host attempts in external `native-state-trace-host-v1`
through `v4` all failed before test execution (host headers, then linker launch).
No native host case passed; latest CMake protections are untested. Root's source
review preserves the baseline error-drain/upload/check/restore order; it is not
native compilation or runtime proof. CMake line endings were normalized to LF.

Editor manifest/wrapper/staging work is a separate unfinished checkpoint, reviewed
only as source backup. Editor tests and the new manifest's `.meta` are absent;
source/receipt contents are not fully validated, explicit WebGL2 build validation
and complete protected-root exclusions remain unfinished. Do not adopt or call
it until strict parsing, all-before-mutation validation, exact rollback,
compilation and actual Unity importer/build tests pass. No native Wasm
build, archive receipt/manifest assembly, consumer adoption, staging, graphics
trace, allocation comparison or release qualification has occurred in this step.
Continue the requirements below without treating source backup as completion.

Root-selected local engineering follow-up under the owner's ongoing CrazyGames
acceptance goal. This order selects a separate diagnostic path, not a rendering
fix, production archive replacement, package publication or release acceptance.
Fresh nonauthor review and this committed order precede implementation. Freeze
the exact native/managed ABI and manifest contract as the first delivery; review
and commit it before dependent runtime or staging implementation.

## Baselines and reason

Use only canonical `main` at `C:/Work/git/gindemit/unity-rlottie/unity-rlottie`,
base `def31c353c90fe5f33a34a251d1a4f79fa217a2a`. Consumer is canonical `main` at
`C:/Work/git/gindemit/MrSquareUnity`, currently
`0734ec2bd68eb4364a81162db43c088cd60e72bf`; revalidate before adoption. Preserve
the consumer's five unrelated jslib importer changes and ProjectSettings change.
No worktrees/clones, submodule changes, retired branch synchronization, tags or
registry publication. Root alone owns Git integration and resource activation.

Actual Development GUID `ca65ac1d4b5849ae9f4f51b367262866` retains one startup
GL_INVALID_OPERATION. Two later GPU readbacks show 2,443 and 3,042 changed visible
pixels on the same bound 256-square NativeWebGL texture. They establish bounded
rendering progress, not the error's cause or exact colors. Managed availability
starts true and cannot locate every native/Unity GL operation.

The callback saves texture binding/alignment, drains existing errors, checks one
upload error, then restores state without another error check. WebGL2 unpack/PBO
state is an unqualified input, not an established cause. Do not select a PBO,
unpack, texture-lifetime or restoration fix without stronger evidence.

Immutable WasmExceptions baseline archives remain:

- Plugin: `088d14b0909acdc6076e1ff1525e8a6d69094d284d4151fc03b6a6ee75504a51`.
- rlottie: `b17cda443208f9c746a6610e8edee79257684fdd8ae39f93f8df3a1263d908da`.

Retain normal `97399d9` artifact/ZIP and the new `0734ec2` normal artifact as
separate evidence. Neither may be overwritten or relabeled as trace-qualified.

## Writable paths and leases

Upstream implementation scope, after the contract freeze:

- `src/WebGLBackend.cpp`, `src/WebGLBackend.h`, `src/LottiePluginApi.cpp` and
  `src/LottiePlugin.h` for guarded trace ABI/state only.
- `projects/CMake/CMakeLists.txt` for default-off trace and external output routing.
- Package `Runtime/src/NativeBridge.cs`, `LottieAnimation.Platform.cs`,
  `LottieUploadPump.cs`, and `LottieWebGLNativeStateTrace.cs` with matching `.meta`.
- Package `Editor/src/LottieWebGLArchiveStager.cs` and a bounded diagnostic
  `LottieWebGLDiagnosticManifest.cs` with `.meta`, plus owning
  `Tests/Editor/src/LottieWebGLDiagnosticManifestTests.cs` with `.meta` and
  `tests/native/NativeWebGLTraceTests.cpp`.
- `tests/native/`, a scoped `tests/webgl-native-startup-trace/` contract/build
  harness, and this owning document. Add no unrelated test/build framework.

The selected executable producer is `tests/webgl-native-startup-trace/build_trace.py`
with bounded parser/output tests in `test_build_trace.py`. ROOT alone runs its
explicit `--execute` after the serialized native-build lease. It requires clean
committed `main`, fresh external build/output directories, the installed Unity
6000.5.3f1 toolchain, CMake and Ninja; it builds only `LottiePlugin`, preserves the
immutable production pair and emits the ABI-defined receipt chain. Its tests do
not authorize a native build, Unity staging, adoption or release qualification.

Package paths are under `unity/RLottieUnity/Assets/LottiePlugin/`. No production
binary/archive, package-version/tag, dependency or PackageCache edits are allowed.
Any additional writable path needs a reviewed owning amendment before editing.

Root serializes native compilation, package generation/import, Unity licenses,
Editor/tests/builds, native staging, browser profiling, ports and Git indexes.
Workers receive disjoint exact files/base heads/contracts and explicit leases;
they do not launch Editors/builds or integrate Git. No nested coordinator.
Finish the current consumer normal build before activating native/import work.

## Isolation and staging contract

The existing CMake POST_BUILD writes canonical `out/` even with external `-B`.
The existing stager accepts an immutable hard-coded archive pair and cleans its
owned directory first. Neither is a diagnostic override mechanism today.

Use `LOTTIE_WEBGL_NATIVE_STATE_TRACE` as the matching default-OFF native/C#
compile contract and freeze the external output/manifest option names in the
first contract delivery. Route every
diagnostic output externally; reject repository/unsafe output targets. Ordinary
build behavior and production archive bytes stay unchanged. Build only the named
plugin target with installed Unity 6000.5.3f1 Emscripten 4.0.20-git,
WasmExceptions/longjmp ABI, frozen cached dependencies, existing CMake/Ninja.
No toolchain/port downloads, cache unfreezing or old CI binary publication jobs.

Use an explicit external diagnostic manifest only for a WebGL Development build
with a matching additional compile define and runtime opt-in. It selects the
new external plugin archive and unchanged baseline rlottie archive. Bind source,
toolchain/configuration, ABI/version/record sizes, exact hashes and export set.
Matching current native/managed symbols are mandatory; no missing-symbol fallback.

Validate the entire pair/manifest/ABI/paths/compile gates BEFORE any cleanup or
staging mutation, including failure catch paths. Reject partial/mismatched pairs,
normal-release use, ambiguous flags and symlink/reparse escapes. Use only existing
owned staging/importer semantics with transactional exact rollback on failure.
Preserve and compare prior generated staging, metadata and all protected inputs;
never replace owner bytes with HEAD substitutes. Do not manually overwrite staged
archives or resolved packages. Keep absent-option production selection unchanged.

Root may adopt only a reviewed full immutable upstream SHA after source/contract
checks, then update consumer manifest/generated lock, its explicit build option
and guarded observation export. Those consumer changes need a separate scoped
reviewed amendment listing exact files before implementation.

## Early arming, identities and correlation

Use an idempotent main-thread helper at BOTH actual upload-pump creation and
common texture construction/registration entry points. Arm before the first
real native registration/event, including constructor-first execution. Enumerate
all actual entry points during review. Do not assume BeforeSceneLoad ordering or
call Unity APIs from static initializers/worker threads.

Gate on the diagnostic compile contract plus one exact, nonduplicate
`lottieNativeStateTrace=1` HTTP(S) loopback query selection (`localhost` or
`127.0.0.1`, no user information). Ordinary Release/Editor/other-platform builds contain
no new runtime calls/API; a trace-compiled but unselected player performs no
diagnostic state queries, event hooks or per-record work. No reset on snapshots.

Assign animation ID/generation and registration context/attempt before the first
native register call. Stamp every real NativeRequest attempt (including rejected
ones) with per-animation sequence and actual Unity frame. Carry an actual issued
event token through diagnostic WebGL GL.IssuePluginEvent and its callback; the
unchanged ordinary path retains its original event ID. Record the pending request
actually consumed, not every earlier request. A registration with no issued event
has explicitly unavailable event correlation, never a fabricated match.

Use bounded UInt32 identifiers/tokens with saturation/overflow flags. Export no
native addresses/pointers or lossy JavaScript numbers. Wider identities must be
exact hex strings or explicit high/low words. Record early-registration/late-arm,
unmatched-event and overflow coverage failures as inconclusive.

## Finite observations and GL behavior

Freeze a size/version checked native/managed record ABI and caller-buffer protocol
before coding dependent sides. Retain at most 64 primitive callback records in
fixed native storage, including skipped/no-pending/invalid/unsupported outcomes,
plus saturating total/drop counters. Bound native count, caller capacity and byte
size independently. Export never clears or silently restarts observations.
After 64 records, update only saturating counters and execute the baseline
callback: skip further diagnostic GL queries and record construction.

Record context support, texture token/dimensions, pending/availability, actual
request/event correlation and the EXISTING checked upload GLenum, with a separate
valid-result flag and restoration-completed flag. No vector/string growth,
per-callback logging/serialization or additional allocations. Export bounded
records only on explicit requests outside allocation/timing windows.

Prove actual current WebGL2 context and valid selectors before diagnostic state
getters; unsupported/no-current contexts get no diagnostic GL queries. Freeze
exact query placement preserving all existing error reads/drains and their order.
Do not add glGetError, extra drains, state normalization, upload retries, manual
Draw/Play, synthetic frames, new art or a rendering-behavior fix. A zero existing
upload result does not exonerate restoration or Unity operations. State getters
are themselves GL calls; retain that attribution limit.

Exhaustion before the first observed Unity error, unavailable early coverage,
unsupported context, late arming, unmatched/overflowed tokens, ABI mismatch or
missing records are INCONCLUSIVE. Correlate actual startup console error and
native request/event/frame observations; callback count alone is insufficient.

## Required proof and finish

1. Fresh review/freeze ABI, manifest and error-query placement before dependent
   implementation. Record actual paths/SHAs and exact tools, not invented CLI.
2. Exercise off/duplicate/conflicting opt-ins, constructor-first/pump-first,
   rejected requests, skipped callbacks, 64/full/drop/saturation, capacity/size/
   version failures and nonresetting exports with owning contract tests.
3. Exercise manifest rejection before ALL mutations, invalid pairs/paths/ABI,
   ordinary-build rejection, staging/importer success/failure rollback and exact
   protected-byte restoration. Host/source tests are not a Unity staging pass.
4. Prove default-off/nonselected runtime equivalence and ordinary exports/source
   behavior; production archives, unrelated backends, tags and dependencies stay
   unchanged. Compile actual diagnostic Wasm, verify matching symbols/toolchain/
   manifest and integrate through the reviewed immutable package pin.
5. Bind actual Development source/packages/archive/flags/GUID/stripped metadata,
   collect bounded startup trace and correlate it with the preserved Unity error.
   Repeat real same-texture GPU progress and current WebGL exact-color calibration.
   Old Metal calibration or visible-pixel smoke is not this proof.
6. Review results before selecting any native correction. Preserve inconclusive
   runs and cleanup receipts. Native diagnostics do not qualify normal graphics,
   allocation reduction, performance, physical devices, media or portal/public
   authority. Continue the owner's full acceptance goal; do not promote this
   diagnostic delivery into release readiness.

Primary state semantics: https://registry.khronos.org/webgl/specs/latest/2.0/ .

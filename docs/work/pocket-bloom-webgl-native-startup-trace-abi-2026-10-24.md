# WebGL native startup trace ABI and staging contract v1

This is the first contract delivery for [the owning order](pocket-bloom-webgl-native-startup-trace-2026-10-24.md), not an implemented API or a runtime qualification receipt. ROOT must obtain fresh nonauthor review and commit this contract before dispatching dependent implementation. The names, sizes, placement and failure rules below are normative for v1; changing them requires another reviewed contract revision.

Reviewed source baseline: upstream `86c114a5b03ef9c1b964464f7bfe6a425a3065d2`; consumer `0734ec2bd68eb4364a81162db43c088cd60e72bf`. No ordinary archive, package version/tag, dependency, PackageCache or consumer source change is selected by this document. The consumer normal build has its own lease and receipt.

## Observed interfaces and restrictions

The current `src/WebGLBackend.cpp` ignores the event ID, consumes the first pending texture in `gTextures`, clears pending, validates availability/texture/pixels using its existing short-circuit `glIsTexture`, saves texture binding and unpack alignment, drains prior errors, uploads, checks one error, restores alignment/binding and updates availability. Registration initializes availability true. A request copies pixels and sets pending; it does not prove a callback executed. Preserve that behavior and ordering.

Current managed sites are `Runtime/src/LottieAnimation.Platform.cs` (`PlatformCreateRenderDataTexture`, `TryRegisterUnityOwnedWebGLTexture`, `RequestTextureUpload`) and `Runtime/src/LottieUploadPump.cs` (`EnsureInstance`, `Awake`, `LateUpdate`). The texture constructor calls `Texture.Apply` before registration; recovery also calls Apply before re-registration. The existing pump issues the same number of events with ID 1, including events that consume no pending texture. Do not add events, reorders, requests, renders, retries, forced frames or Apply calls.

Current `Editor/src/LottieWebGLArchiveStager.cs` has callback order `int.MinValue`, calls cleanup before validating target and selects hard-coded archive hashes. Its catch path also cleans. It is **not** a diagnostic override today. Current CMake POST_BUILD commands write both canonical `out/<configuration>/...` and `out/Plugins/WebGL`, even with an external build directory. All those diagnostic output destinations must be redirected before a diagnostic build can run.

Consumer `GardenPreviewBuilder.Build` constructs `BuildPlayerOptions`, passes `extraScriptingDefines`, then calls `BuildPipeline.BuildPlayer`. Installed Unity 6000.5.3f1 documents `BuildPlayerOptions.extraScriptingDefines`, `BuildPlayerOptions.options`, `BuildSummary.options` and `PlayerSettings.WebGL.threadsSupport`. A BuildReport does **not** carry the caller's extra-scripting-defines array. Do not infer that array from BuildReport or mutate global PlayerSettings to communicate it.

## Compile, build and runtime selection

All new runtime declarations, fields, imports, helpers and call sites are guarded by:

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR && DEVELOPMENT_BUILD && LOTTIE_WEBGL_NATIVE_STATE_TRACE
```

All new native symbols/state/hooks are guarded by both `__EMSCRIPTEN__` and `LOTTIE_WEBGL_NATIVE_STATE_TRACE`. CMake's proposed boolean option is exactly `LOTTIE_WEBGL_NATIVE_STATE_TRACE`, default `OFF`; its target definition is `LOTTIE_WEBGL_NATIVE_STATE_TRACE=1` on **LottiePlugin only**. Do not rebuild/change rlottie to introduce diagnostic code. A diagnostic plugin is paired with the immutable existing WasmExceptions rlottie archive. CMake must reject trace ON without WebAssembly and `RLOTTIE_WEBGL_WASM_EXCEPTIONS=ON`.

The proposed consumer CLI selector is `-lottieWebGLNativeStateTraceManifest <absolute-json-path>`: exactly one occurrence and nonempty value. It is not implemented by the current builder. It requires an actual WebGL Development build and one additional define `LOTTIE_WEBGL_NATIVE_STATE_TRACE` in that build's `BuildPlayerOptions.extraScriptingDefines`. Within the supported wrapper, duplicate options, standalone extra define, wrong target/Development option or persistent global definition are errors before staging mutation. Existing ordinary Development/Release options remain unchanged when the selector is absent. No global scripting-define writes are allowed.

The proposed package entry is `RLottie.LottieWebGLDiagnosticBuild.BuildPlayer(BuildPlayerOptions options, string manifestPath, string expectedManifestSha256)`, returning `BuildReport`, implemented in the allowed editor diagnostic-manifest file. It clones the actual options/arrays, validates that clone and manifest, enters a nonnested scoped staging context, calls `BuildPipeline.BuildPlayer` itself with that same validated clone, and restores the staging transaction in `finally`. The preprocess callback compares the report platform/Development option with that context. The guarantee applies only to this wrapper and the separately reviewed consumer route that calls it. Detectable manifest CLI options without an active validated wrapper context fail before cleanup. An arbitrary unwrapped BuildPipeline call supplying only extraScriptingDefines cannot be inspected through BuildReport; it is unsupported, not a promised preflight rejection, and may fail at link time with missing diagnostic symbols. Never add missing-symbol fallback. The wrapper rejects reentry; no environment variable or caller claim replaces the actual-options handshake. The consumer amendment is not activated here. No new executeMethod or built-in Unity command is assumed.

v1 diagnostic builds require `PlayerSettings.WebGL.threadsSupport == false`, read without changing it. The single browser/main-thread callback model is an explicit v1 constraint: no unproven volatile/shared-memory snapshot protocol. A future threaded implementation needs a separately reviewed ABI/concurrency amendment.

Runtime query selection is exactly one decoded query-name `lottieNativeStateTrace` with exact decoded value `1`, HTTP or HTTPS, hostname exactly `localhost` or `127.0.0.1` (case-insensitive localhost), no user information. Reject missing/wrong values and every duplicate name, including encoded duplicates/conflicting values; fragments do not supply query parameters. Reject IPv6, other loopback aliases and remote hosts. Use a main-thread idempotent helper; never Unity APIs in static initialization. If unselected, do not call native Arm/export/annotation wrappers, assign diagnostic identities, register diagnostic event metadata, inspect GL/context state or construct per-observation data. The one cached query-gate evaluation at a required startup entry is the allowed initialization work; ordinary compile-off code is unchanged.

Call that helper at the start of BOTH `LottieUploadPump.EnsureInstance`/`Awake` (before native callback lookup/event issuing) and `PlatformCreateRenderDataTexture` (before Unity Texture2D construction/Apply), and at `TryRegisterUnityOwnedWebGLTexture` before native plugin registration as a defensive idempotent entry. `RequestTextureUpload` and pump LateUpdate must require that initialization before selecting the diagnostic branch. Enumerate constructor/resize/recovery and pump-first entry tests. Merely adding BeforeSceneLoad is insufficient. Do not add pre-arm counters/hooks to ordinary native entry points: that would do diagnostic work in an unselected player. Arm instead inspects the existing `gRenderingPluginRegistered` and `gTextures` state once; any preexisting plugin registration/state flags late coverage. This detects existing state, not a complete historical count of pre-arm callbacks. Correct source placement plus entry-order tests are required to establish the earlier empty-pump interval; if that placement is not proven, early coverage remains inconclusive. The managed gate being off must never result in Arm.

## C ABI v1

Use `extern "C"`, existing `EXPORT_API`, Cdecl and fixed-width `uint32_t`/`int32_t`. New exact symbols:

```c
int32_t lottie_webgl_trace_arm(uint32_t abi, uint32_t header_bytes,
    uint32_t record_bytes, uint32_t arm_unity_frame);
int32_t lottie_webgl_trace_register(lottie_animation_wrapper* animation,
    void* native_texture, int32_t width, int32_t height,
    uint32_t animation_id, uint32_t generation, uint32_t texture_token,
    uint32_t registration_attempt, uint32_t unity_frame, uint32_t origin);
int32_t lottie_webgl_trace_request(lottie_animation_wrapper* animation,
    lottie_render_data* render_data, uint32_t animation_id,
    uint32_t generation, uint32_t request_sequence, uint32_t unity_frame);
int32_t lottie_webgl_trace_issue(uint32_t event_token, uint32_t unity_frame);
int32_t lottie_webgl_trace_copy(uint32_t abi, uint32_t header_bytes,
    uint32_t record_bytes, void* header, uint32_t header_capacity_bytes,
    void* records, uint32_t record_capacity, uint32_t record_capacity_bytes);
```

These are proposed, not existing entry points. Existing production exports retain their exact signatures. Register/request wrappers perform their annotation and baseline call as one operation; do not introduce a mutable global “next animation/request” setter that can attach metadata to another call. Native input pointers are existing call inputs only; never put their values in exported data. There is no Reset/Disarm/clear-on-copy API. Arm is idempotent, does not erase accumulated records/coverage evidence, and rejects incompatible repeated sizes/version. Arming occurs once per player process; scene/pump recreation must not restart coverage.

Control API status: `0=OK`, `-1=ABI/version/size mismatch`, `-2=bad argument/capacity`, `-3=not armed`, `-4=unsupported thread mode`. These errors do not write caller buffers. Register/request return baseline `1=accepted, 0=rejected`; negative protocol status is an integration failure and must not be interpreted as renderer fallback or swallowed as missing-symbol support. Issue returns 0 on accepted metadata or a negative protocol status. Counter/identity saturation is a **coverage failure**, not a rendering failure: continue the unchanged actual registration/request/event path while flagging missing correlation. Managed code must not suppress an actual event because its diagnostic metadata table is full.

Copy accepts only aligned writable caller-owned Wasm memory, exact v1 version/sizes, header capacity at least 128, record capacity 0 through 64 inclusive, and `record_capacity_bytes >= record_capacity * 192`; validate capacity before multiplication, and both ranges within linear memory without wrap. Header pointer must be nonnull. Records pointer may be null only when capacity and bytes are zero. Header/record ranges must not overlap. Caller must actually own those writable buffers; arbitrary external addresses are outside the ABI. v1 is little-endian, four-byte aligned, contains no bool, pointer, size_t, enum-width assumptions, managed references or 64-bit fields. C++ static_assert size/offsets and C# `StructLayout(LayoutKind.Sequential, Pack=4)`/Marshal.SizeOf/offset tests are mandatory.

Copy first validates all arguments, then copies one main-thread snapshot. It always returns the earliest retained prefix of `min(record_capacity, storedRecords)` records without clearing or advancing anything; unused caller bytes are untouched. A capacity smaller than stored count is a successful truncated view, reported by header `returnedRecords`, not lost native storage. Capacity zero is a valid header-only export. Native total counts describe the whole retained process history, not this prefix. There is no size-probe status that writes a differently shaped header. Export formatting/JSON happens in managed code only on explicit request outside timing/allocation windows.

Managed explicit export is proposed `public static string LottieAnimation.GetWebGLNativeStateTraceJson()` in namespace `LottiePlugin`, under the full runtime guard. It returns schema `lottie.webgl.native-state-trace.runtime/1`, `enabled` boolean, captured `buildGuid` string, `header` object and `records` array using the exact ABI field names above/below. If the cached query gate is off, return `enabled:false`, reason `not_selected`, null header and empty records without any native call; do not evaluate Unity APIs from a type initializer. When on, export all stored records up to64, report protocol failure explicitly, and retain header coverage flags as data rather than mapping them to a graphics PASS. Capture build GUID on the same main-thread initialization path and serialize its full string. The exporter must not arm for the first time merely to hide missing early initialization: if its owning helper was never initialized, report `initialization_missing`/inconclusive without native calls. Wider future IDs must be exact hex strings or high/low UInt32 words, never floating point. Reserve buffer allocation and JsonUtility serialization for this explicit export, never callback/Update. Caller-side build receipt supplies artifact/manifest identity; a runtime-provided GUID alone is not the manifest proof.

### Header: 32 UInt32 words, exactly 128 bytes

Offset is four times the word index; every reserved word must be zero.

| Word | Field | Meaning |
|---|---|---|
| 0–4 | magic, abiVersion, headerBytes, recordBytes, capacity | `0x4c575431`, 1, 128, 192, 64 |
| 5–8 | storedRecords, returnedRecords, callbacksDropped, registrationsRejected | Stored <=64; totals saturating |
| 9–13 | callbacksTotal, registrationsTotal, requestsTotal, requestsRejected, eventsIssuedTotal | Includes outcomes not retained |
| 14–18 | preArmStateDetected, lateArmTotal, unmatchedEventsTotal, identityOverflowTotal, flags | First is 0/1 existing-state evidence at first Arm, not a historical activity count; remaining counters saturate |
| 19–21 | armUnityFrame, firstSequence, lastSequence | Zero for unavailable; retained record sequence bounds |
| 22–23 | registrationMetadataLossTotal, requestMetadataLossTotal | Saturating; loss without a correlatable existing texture is explicitly nonpass |
| 24–31 | reserved0 through reserved7 | Zero |

`flags`: bit0 armed, bit1 preArmCoverageLost, bit2 storageExhausted, bit3 counterSaturated, bit4 identityOverflow, bit5 eventMetadataOverflow, bit6 unmatchedEvent, bit7 unsupportedContextObserved, bit8 invalidCorrelation, bit9 unsupportedThread, bit10 registrationContextUnavailable, bit11 requestContextUnavailable. Other bits zero. All nonzero coverage flags except armed are reported and prevent an unconditional whole-session completeness claim. A bounded target-error attribution may use an independently correlated retained prefix only when the affected records have valid identities/context and the target error is proved to occur before the first coverage loss. Later exhaustion must remain reported but does not erase an already retained earlier observation. Exhaustion before the target error, or inability to place the error within the retained prefix, is inconclusive. Do not waive early-arm, identity, context or correlation failures affecting the target. Saturating additions stop at `UINT32_MAX`; they never wrap. `lateArmTotal` increments once on first Arm if preArmStateDetected is nonzero, not on harmless repeated Arm.

### Record: 48 UInt32 words, exactly 192 bytes

One fixed native array holds the **first 64 callback records only**, in callback-entry order, including no-pending, invalid and unsupported outcomes. Registration, request and issue activity never consumes these slots. There is no control-event log or four-kind union. Control attempts have bounded header totals and latest per-existing-texture annotations as specified below. Exhaustion before the target startup error is inconclusive.

| Word | Field |
|---|---|
| 0–7 | recordBytes, abiVersion, sequence, registrationUnityFrame, outcome, validFlags, reservedB, animationId |
| 8–15 | generation, registrationAttempt, registrationOrigin, requestSequence, requestUnityFrame, eventToken, eventUnityFrame, callbackSequence |
| 16–23 | textureToken, width, height, pendingBefore, pendingAfter, availableBefore, availableAfter, contextStatus |
| 24–31 | contextMajor, contextMinor, contextLost, isTextureResult, previousTextureRelation, previousUnpackAlignment, pixelUnpackBufferBound, unpackRowLength |
| 32–39 | unpackSkipRows, unpackSkipPixels, reservedC, reservedD, checkedUploadError, checkedUploadErrorValid, restorationCompleted, firstDrainedError |
| 40–47 | drainedErrorCount, reserved0, reserved1, reserved2, reserved3, reserved4, reserved5, reserved6 |

All reserved words are zero. Unavailable fields are zero with their validity bit absent; zero error/result/state without validity does not mean success. Signed GL state values use their exact 32-bit two's-complement bit pattern. `sequence` is the callback-entry ordinal (equal to callbackSequence in v1). `origin`: 1 initial construction, 2 resize/recreation, 3 existing recovery re-registration; classify the actual call site. `outcome`: 1 noPending, 2 invalidOrUnavailableTexture, 3 uploadCheckedZero, 4 uploadCheckedNonzero, 5 unsupportedContext. A callback that performs the baseline upload in an unsupported diagnostic context reports outcome5 with any existing checked error separately valid; it does not change baseline upload behavior. Registration/request rejection increments the relevant header count and must not fabricate a callback or imply GL execution.

`validFlags`: bit0 animation identity, bit1 registration metadata, bit2 request metadata, bit3 issued-event metadata, bit4 pending/available snapshot, bit5 context metadata, bit6 existing glIsTexture result, bit7 existing binding/alignment values, bit8 added WebGL2 unpack/PBO state, bit9 existing checked upload error, bit10 existing drain observation, bit11 restoration completed. Other bits zero. `checkedUploadErrorValid` and restorationCompleted are 0/1 and must agree with their validity flags. Pending/available/contextLost/isTextureResult/PBO-bound values are 0/1 when valid. `previousTextureRelation`: 0 binding zero, 1 binding equals the selected texture, 2 another binding. It is not a native address or raw texture name. `textureToken` is an assigned logical managed ID, never `GetNativeTexturePtr` bits; PBO exports only zero/nonzero. No context handle, Unity object pointer, native animation pointer or CPU pixel address is exported.

## Correlation and finite storage

Managed code assigns nonzero process-monotonic UInt32 animation IDs and logical texture tokens. Generation increments on texture replacement for that animation; each registration attempt (including rejected/recovery attempts) increments its own sequence. Every **actual call** to native request gets a per-animation sequence and `Time.frameCount` captured at that call. Absence of a request because the existing availability gate took recovery is not a rejected native request. Mark Unity frame unavailable on negative/wrapped/unrepresentable frame count rather than casting to a plausible positive frame. Overflow never reuses an identity; flag coverage invalid and keep rendering normally.

Registration's annotation is a fixed stack-local context passed with the actual call, before any texture state exists; no pointer-keyed diagnostic registry is added. On successful baseline registration, copy it to the resulting existing WebGLTextureState's fixed latest-successful-registration fields. On rejection keep any previous successful binding annotation, increment registrationsRejected, and set registrationContextUnavailable plus registrationMetadataLossTotal if no existing state can retain the rejected context. Each existing state may also retain exactly one latest-registration-attempt context/result, including a rejected attempt; that is not a history or a new container. A callback identifies the successful binding, not a later rejected replacement. Exported coverage must explicitly be nonpass when preregistration/rejected-context history is unavailable; do not substitute a plausible successful identity.

Likewise keep exactly one latest-request-attempt context/result per existing texture, plus the accepted pending-pixel context. Only an accepted request replaces pending correlation; a later accepted request overwriting pending pixels replaces that context too. Rejected requests increment requestsRejected and cannot relabel previously accepted pixels. Rejection without an existing state sets requestContextUnavailable and increments requestMetadataLossTotal. Header totals disclose all attempts; there is no claim to reconstruct every rejected attempt from callback records. At callback selection copy the actual selected binding/pending context before existing pending=false. Register resets pending as today and invalidates old pending correlation. Unregister discards annotations with the existing state; never match a reused pointer to a retired generation. Tests must cover missing/rejected preregistration context as an explicit nonpass outcome.

Event tokens are positive signed-int-compatible IDs 2 through `INT32_MAX`; 0 denotes unavailable and 1 is the unchanged ordinary event ID. Immediately before each **existing** `GL.IssuePluginEvent`, issue records the token/frame in a fixed table of at most 64 outstanding entries, then GL receives that same token unchanged. The callback resolves/consumes exactly its incoming ID, even when no pending state exists. Never infer an event token from callback order. Table exhaustion or an unknown/duplicate/stale callback ID sets a coverage flag; keep the baseline callback, but skip added diagnostic GL getters for that callback. Reuse a consumed table slot, never an event token. Tokens saturate instead of wrap; after exhaustion issue ordinary ID1 and mark unavailable correlation rather than adding events or changing upload budget. A failed/nonreturning IssuePluginEvent leaves unmatched table metadata; exporter cannot declare it consumed. Outstanding entries are not historical callback records.

The callback's actual issued Unity frame is `eventUnityFrame`; `requestUnityFrame` belongs to consumed pixels, and registration frame belongs to the per-texture annotation. Neither is claimed to equal a GPU execution frame. CallbackSequence is a saturating native callback-entry counter. No native Unity frame API or synthetic timestamp is invented. Console capture has its own observed time/frame and must be correlated with these facts, not equated by wall-clock proximity alone.

Native storage is bounded: one 64-callback-record array, one 64-entry outstanding-event table, constant-size counters and one stack-local registration context; fixed per-texture annotations extend existing texture state without a second growing registry. No additional vectors, strings, heap allocation, JSON, logs or per-callback managed export. Once the callback array is full, update saturating counters and consume outstanding metadata only as needed to keep the table truthful; do not construct more records or perform diagnostic context/GL queries. Baseline callback/error reads remain unchanged. Check capacity before constructing a record.

## Frozen context validation and GL placement

Additional diagnostic state is supported only for a current, nonlost **WebGL2** context. Use installed public Emscripten `emscripten_webgl_get_current_context()`, `emscripten_webgl_get_context_attributes()` and `emscripten_is_webgl_context_lost()` from `emscripten/html5_webgl.h`. Require nonzero handle, successful attributes result, `majorVersion == 2`, not lost, selected/correlated record capacity available, and single-thread v1. Do not call `glGetString` to guess context version, make a context current, normalize state, or export its handle. contextStatus: 0 notObserved, 1 WebGL2Supported, 2 noCurrent, 3 attributesFailed, 4 otherVersion, 5 lost, 6 correlationUnavailable. No additional GL getters for any non-1 status. WebGL1's baseline rendering remains executable but diagnostic state is unsupported/inconclusive.

Placement in the existing callback is exact:

1. Select pending state and capture CPU metadata. Preserve the existing no-pending return. Preserve pending clearing and the existing short-circuit invalidity checks/glIsTexture in their original order; record that result only if it was actually evaluated. No added GL queries on these returns.
2. Run the existing `glGetIntegerv(GL_TEXTURE_BINDING_2D)` and `glGetIntegerv(GL_UNPACK_ALIGNMENT)` at their current location. Retain their existing values; the diagnostic must not repeat them. Perform the public Emscripten context validation after those baseline getters. It does not suppress baseline calls on an unsupported context.
3. Only with status1, valid correlation and a reserved retained record, insert exactly four additional `glGetIntegerv` selectors, in this order: `GL_PIXEL_UNPACK_BUFFER_BINDING (0x88EF)`, `GL_UNPACK_ROW_LENGTH (0x0CF2)`, `GL_UNPACK_SKIP_ROWS (0x0CF3)`, `GL_UNPACK_SKIP_PIXELS (0x0CF4)`. They are WebGL2 selectors relevant to this 2D upload; compile-check GLES3/header availability without changing production includes when compile-off. No 3D unpack selectors, texture-level getters, PBO mapping, readback or new object lookup.
4. Execute the existing error-drain loop at its original location. Observe values returned by those **existing** reads: first nonzero enum and saturated count, including the same final zero read as baseline. Do not add a pre-query or post-query `glGetError`. Added getters precede this loop, so even a drained pre-upload error is not attributable solely to Unity; it may have arisen from a diagnostic getter. That attribution limit must be present in exported report interpretation.
5. Preserve `glBindTexture`, `glPixelStorei(GL_UNPACK_ALIGNMENT,4)`, `glTexSubImage2D` and the single existing checked `glGetError` in their exact order. Copy that enum and set its valid flag only when actually executed.
6. Preserve restoration `glPixelStorei(previousAlignment)`, then `glBindTexture(previousTexture)`, then availability assignment. Set restorationCompleted only after both restoration calls returned; it means calls completed, **not** that restoration succeeded. There is no additional GL query/error read after restoration.

A zero checked-upload error does not exonerate restoration, earlier Unity texture Apply, another callback, diagnostic getters or later Unity GL work. Unsupported/no-pending/invalid/correlation-lost records are coverage outcomes, not graphics success. Context loss between validation and getters remains possible; record what was observed and retain this limitation rather than adding error drains or retries.

## External outputs and manifest v1

Proposed CMake cache path option: `LOTTIE_WEBGL_TRACE_OUTPUT_ROOT`, required absolute fresh external directory when trace ON; empty by default. Proposed host runner options: `--source`, `--build-dir`, `--output`, `--unity-editor`, `--baseline-rlottie`, `--cmake`, `--ninja`. They are **not implemented commands**. The runner must use the installed Unity toolchain, existing frozen dependency/cache inputs and named `LottiePlugin` target only. No port/toolchain download, cache unfreezing or dependency update. `--source` is the canonical checkout, not permission to create a clone. Diagnostics must replace every canonical POST_BUILD destination, including platform/configuration copies. The ordinary OFF destinations and bytes remain unchanged.

Exact diagnostic payload names under the external output root:

```text
WasmExceptions/libLottiePlugin.trace.a
WasmExceptions/librlottie.baseline.a
webgl-native-state-trace.manifest.json
webgl-native-state-trace.native-build-receipt.json
webgl-native-state-trace.exports.json
```

The rlottie file is a byte-for-byte copy of the immutable baseline, not an unreviewed rebuilt replacement. The immutable native-build receipt separately binds all emitted linker/dependency archives so a rebuilt rlottie accidentally linked during plugin construction cannot be concealed. Reject an unexpected dependency symbol/ABI requirement even if the consumer-supplied pair hashes correctly.

Manifest JSON is strict UTF-8 with no BOM, duplicate properties, unknown properties, comments, trailing tokens or nonintegral numeric values. Required top-level fields (all mandatory):

| Field | Exact type/requirement |
|---|---|
| schema | string `lottie.webgl.native-state-trace/1` |
| abiVersion, headerBytes, recordBytes, capacity | integers 1,128,192,64 |
| nativeDefine, managedDefine | string `LOTTIE_WEBGL_NATIVE_STATE_TRACE` each |
| target, variant, configuration, threading | strings `WebGL`, `WasmExceptions`, `Development`, `single-main-thread` |
| sourceCommit, rlottieCommit | full immutable lowercase Git IDs; source is reviewed adopted package commit |
| unityPluginApiHeaders | object `{ownerCommit, files}`; ownerCommit equals sourceCommit, files is a sorted array of `{path, sha256, bytes}` for actually compiled tracked headers under dependency/unity-pluginapi. This is a plain directory owned by this repository, not an independent Git repository or submodule |
| sourceInputs | nonempty sorted array of `{path, sha256, bytes}` binding every compiled source/header/CMake/submodule input; normalized repo-relative paths, no traversal |
| toolchain | object with exact keys unityVersion=`6000.5.3f1`, emscriptenVersion=`4.0.20-git`, cmakeVersion, ninjaVersion (actual strings); emcc, emar, llvmNm, cmake, ninja, toolchainFile, configurationReceipt (each `{path, sha256, bytes}`). Tool paths are absolute within approved installed roots. configurationReceipt is a sibling external file binding actual compiler/sysroot path/configuration, effective cache settings, compiled-input/depfile hashes and flags; no mandatory whole-sysroot inventory |
| compilerFlags | string array of actual compile flags including `-fwasm-exceptions`, `-sSUPPORT_LONGJMP=wasm`, `-sWASM_LEGACY_EXCEPTIONS=1`; exact effective flags, no guessed defaults |
| plugin, rlottie | objects `{path, sha256, bytes}`; paths relative to manifest directory and exactly the payload pair names above |
| baselinePluginSha256 | string `088d14b0909acdc6076e1ff1525e8a6d69094d284d4151fc03b6a6ee75504a51` |
| baselineRlottieSha256 | string `b17cda443208f9c746a6610e8edee79257684fdd8ae39f93f8df3a1263d908da` matching rlottie.sha256 |
| exports | object `{added, baselineInventorySha256, diagnosticInventorySha256}`; added is exact five-symbol ABI list, sorted, no missing/extra trace exports |
| buildReceipt, exportsReceipt | objects `{path, sha256, bytes}` pointing to the exact sibling receipts above |

`bytes` is a nonnegative integral JSON number in range 0 through `INT32_MAX`; archive/receipt sizes must be >0. SHA256 strings are 64 lowercase hex characters. Full inventories also bind ordinary defined symbols and prove none were removed; absence of trace symbols from unchanged ordinary archives is separately verified. Archive symbol verification uses the installed matching LLVM toolchain; the receipt binds executable hash, complete argv, exit code and inventory hash. Undefined external dependencies and effective compile commands belong in the build receipt, not an invented `BuildReport` export field.

Receipt dependency order is acyclic. First finalize the immutable native-build receipt and native symbol/exports receipt using native sources, tools, flags, inputs and archive hashes only: neither contains a final manifest hash, consumer build GUID or downstream receipt hash. Then create the manifest referencing those finalized sibling receipts. Finally, downstream preflight, staging, consumer-build and runtime-export binding receipts record the exact manifest-file SHA256 and actual consumer identity externally. Never add a downstream receipt back into the manifest or mutate a finalized native receipt to include downstream data.

### Selected receipt producer and exact v1 receipt schemas

`tests/webgl-native-startup-trace/build_trace.py` is the selected producer. ROOT
runs it only after source review/commit and a serialized native-build lease. It
requires clean `main`, explicit installed tool paths, fresh disjoint external
build/output directories, installed `.emscripten` with `FROZEN_CACHE = True`,
`EM_FROZEN_CACHE=1`, and no inherited C/C++/linker/EMCC flags. It configures Ninja
with `CMAKE_BUILD_TYPE=Release`, WebAssembly, WasmExceptions, trace ON, testing OFF
and compile-command export ON, then builds only `LottiePlugin`. Native Release
optimization is independent from the required downstream Development player.
It copies the original baseline rlottie into the payload; any rebuilt dependency
archive remains separately identified. No Unity invocation or staging occurs.

All receipt objects reject duplicate/unknown/missing keys and use the manifest's
strict UTF-8, integer, lowercase SHA and descriptor rules. `command` means exactly
`{argv: string[], exit: 0, output: descriptor}`; output names a nonempty sibling
log. Configure/build argv begin with the actual approved CMake executable and
build selects exactly the literal `LottiePlugin` target. `nmRun` means exactly
`{archiveSha256, argv: string[], exit: 0, inventorySha256}`; argv is the installed
llvm-nm followed by `--defined-only` or `--undefined-only`, `--extern-only`,
`--format=posix` and the actual absolute archive path. Inventories use distinct
ordinal-sorted names, one UTF-8 LF after each name; an empty undefined inventory
hashes empty bytes. Undefined runs additionally require `symbols: string[]`
containing that exact sorted inventory. No commands from receipts are executed.

- `webgl-native-state-trace.configuration.json`: exact keys `schema`,
  `sourceCommit`, `rlottieCommit`, `sourceInputs`, `compilerFlags`, `tools`,
  `unityVersion`, `emscriptenVersion`, `cmakeVersion`, `ninjaVersion`, `emConfig`,
  `compiler`, `python`, `compilerWrapper`, `cacheRoot`, `sysroot`, `frozenCache`,
  `compileCommands`, `dependencies`, `cmakeCache`, `configure`. Schema is
  `lottie.webgl.native-state-trace.configuration/1`. Identities, inputs, flags and
  versions equal the manifest. `tools` has exactly the six manifest tool
  descriptors excluding configurationReceipt. `emConfig`, `compiler`, `python`,
  `compilerWrapper` are absolute installed Unity descriptors for `.emscripten`,
  `llvm/clang++.exe`, embedded `python/python.exe`, `emscripten/emcc.py`.
  `cacheRoot` and `sysroot` are absolute installed Emscripten cache/sysroot paths;
  `frozenCache` is boolean true. `configure` is command. The three sibling
  descriptors name `webgl-native-state-trace.compile-commands.json` (actual CMake
  JSON array, each entry exactly directory/command/file and optional output),
  `webgl-native-state-trace.dependencies.json` (sorted array of absolute
  descriptors from actual compile sources plus retained Ninja header dependencies,
  including generated and sysroot inputs), and
  `webgl-native-state-trace.cmake-cache.txt` (verbatim actual CMake cache).
  Only sourceInputs and compiled dependency descriptors permit zero-byte source
  files with the exact empty-byte SHA256; archives, tools, logs and receipt files
  remain strictly positive in length.
  `sourceInputs` also binds all tracked package C#, meta, asmdef and JSON files;
  the consumer checks those available within its adopted immutable package.
  `compilerFlags` is the sorted distinct actual plugin command option-token
  inventory excluding `-c` and `-o`; complete commands preserve order, argument
  values and per-source differences. Every plugin command must carry trace and
  frozen ABI flags; no effective compiler command may carry pthread flags.
- `webgl-native-state-trace.native-build-receipt.json`: exact keys `schema`,
  `sourceCommit`, `rlottieCommit`, `configurationReceipt`, `plugin`, `rlottie`,
  `emittedArchives`, `build`, `undefinedSymbols`. Schema is
  `lottie.webgl.native-state-trace.native-build/1`. Shared descriptors/identities
  equal the manifest/configuration. `emittedArchives` is a nonempty array of
  absolute external-build descriptors for every emitted `.a`, including rebuilt
  rlottie. `build` is command. `undefinedSymbols` contains exactly three undefined
  nmRuns, in baseline-plugin, diagnostic-plugin, baseline-rlottie order. Retain
  raw per-member undefined inventories. Subtract the diagnostic archive's actual
  defined inventory before checking external dependencies: another member can
  resolve an undefined reference within the same archive. Remaining diagnostic
  undefined names must be a subset of both baseline inventories' union plus the
  four selected diagnostic APIs `emscripten_webgl_get_current_context`,
  `emscripten_webgl_get_context_attributes`, `emscripten_is_webgl_context_lost`,
  `emscripten_get_heap_size`. Any other addition stops for evidence review.
  Successful llvm-nm stderr may contain only exact input-archive-qualified
  `archive[member]: no symbols` lines for the observed empty/platform-disabled
  members: `InstanceRegistry.cpp.o`, `UploadQueue.cpp.o`, `UploadPipeline.cpp.o`,
  `TextureBackend.cpp.o`, `VulkanBackend.cpp.o`, `UnityIntegration.cpp.o`,
  `UnityExports.cpp.o`, `lottieproxymodel.cpp.o`, `vdebug.cpp.o`,
  `vdrawhelper_neon.cpp.o`, `vdrawhelper_sse2.cpp.o`. Preserve those diagnostics
  in the external logs; any other stderr/member or nonzero exit rejects.
- `webgl-native-state-trace.exports.json`: exact keys `schema`, `sourceCommit`,
  `llvmNm`, `added`, `baselineInventorySha256`, `diagnosticInventorySha256`, `runs`.
  Schema is `lottie.webgl.native-state-trace.exports/1`. Shared identity,
  executable descriptor and inventories equal the manifest. `runs` contains
  exactly the baseline-plugin then diagnostic-plugin defined nmRuns. Baseline
  defined names must be retained and the five trace exports must match exactly.

Configuration precedes native/exports receipts, which precede the final manifest.
The payload root can retain the initial isolated CMake output subdirectory;
only the final exact manifest pair is selected. Build logs and failed outputs
remain external evidence. A generated manifest is evidence awaiting ROOT review,
not adoption or graphics acceptance.

Do not put a manifest's own hash inside itself. Manifest hash is identity, **not** approval: ROOT must select the exact reviewed SHA out of band, pass it to the scoped build context as the trusted expected hash, and compare it before parsing/use. The proposed CLI hash companion is `-lottieWebGLNativeStateTraceManifestSha256 <64-lowercase-hex>`; it is mandatory with the path, forbidden alone, single-occurrence and not implemented today. An attacker-authored self-consistent manifest must not select arbitrary code.

## Preflight, staging and exact rollback

On the supported wrapper/consumer route, before **any** cleanup, directory creation, archive copy, importer operation or failure-catch cleanup: validate paired CLI presence/hash, cloned actual build options and exact additional define, nonpersistent global define, WebGL2-capable selected build settings/single-thread constraint, strict manifest/schema/ABI/toolchain/source approval, immutable baseline pair/symbol inventories, archive hashes/lengths and paths. Reject detectable manifest options without the wrapper before cleanup. Do not claim that BuildReport can validate arbitrary unwrapped extra-define-only calls. The absent-option production route retains existing selected hashes/importer behavior; diagnostic preflight failure cannot fall back to production cleanup.

Reject relative/UNC/device paths, ADS/colon suffixes, traversal, symlink/reparse components, filesystem-root/output-root aliasing, paths inside **any** Git worktree/its .git, consumer Assets/Library/Packages/PackageCache, native source/dependency/cache/toolchain directories, or paths overlapping source/build/protected evidence. `--build-dir` and `--output` must be distinct fresh descendants of a ROOT-selected external evidence directory. Source/archive inputs must be regular files, have resolved paths inside their declared immutable external payload root, and not overlap staging/backup. Validate every existing ancestor and revalidate before access to prevent reparse replacement; serialize the lease. No shell-command execution from manifest fields.

After successful preflight, inventory exact existing `Assets/LottiePlugin.GeneratedWebGL` directory, its `.meta`, ownership marker, archive files and importer metas. An unowned directory or unexpected extra files fails without mutation. Preserve original bytes externally before changing anything, including exact metadata/GUIDs and absence states, and hash the backup. Never reconstruct owner bytes from HEAD or an importer template. Prepare the verified pair and normal existing importer metadata in an external transaction directory; only then replace the owned staged pair using existing fixed staged names and `PluginImporter` WebGL-only settings. Validate both imports/hashes before continuing BuildPipeline. PackageCache and source archives are read-only inputs.

On successful build, thrown preprocess/import/build failure, returned failed/cancelled BuildReport, or postprocess exception, one outer `finally` restores the exact prior owned staging state and directory-meta absence/presence, performs the necessary synchronous import, and compares the complete before/after byte inventory. Postprocess must not delete a restored prior directory; the scoped transaction owns diagnostic cleanup. The catch path may remove only transaction-created bytes whose ownership was established after preflight. If rollback/import fails, surface a combined failure, retain backup and all failed-state evidence, do not report clean restoration or continue to another build. Process kill/crash cannot be made transactional by finally: leave an external journal with original bytes and require explicit ROOT recovery before next import/build. Default nontrace cleanup behavior is unchanged.

The separate downstream consumer-build receipt binds adopted package SHA, consumer SHA and protected-input inventory, manifest SHA, exact selected/staged pair hashes, actual options and extra defines, Unity GUID from the **returned** `report.summary.guid`, stripped managed diagnostic inventory, output-file inventory, restoration receipt and exit status. It is not the immutable native-build receipt referenced by the manifest. GUID is not available as a trustworthy final build identity in preprocess. Caller claims or precomputed manifests cannot supply the final GUID. Retain diagnostics separately from ordinary normal release/ZIP artifacts.

## Required tests and unresolved activation gates

- Native host contracts: ABI size/offset/version, invalid arguments/capacity/overlap/overflow, zero/prefix/full copy without reset, callback outcomes and bounded registration/request rejection counters/annotations, preregistration context loss explicitly nonpass, actual pending replacement/correlation, no-pending callback, token duplicate/unknown/table full/INT_MAX, pre-arm/constructor-first/pump-first coverage, exactly64callbacks/full/drop/saturation and no diagnostic getters after exhaustion. Arbitrarily many register/request/issue calls must not consume callback slots. GL stubs prove call order/count only, never real WebGL behavior.
- Query/managed contracts: absent/wrong/remote/userinfo/duplicate/encoded-duplicate/fragment, idempotence and both early routes, compile-off and unselected equivalence, generation/recovery/resize identity, runtime protocol failures surfacing, actual event ID propagation, explicit snapshot does not reset, JSON no native addresses or lossy identities.
- Editor contracts: every malformed manifest/pair/define/options/path/ABI case rejects before every mutation including catches; trusted expected hash mismatch; actual wrapper options; source/baseline symbol inventory checks; exact absent/prior-owned staging and metadata restoration after success/import/preprocess/build/postprocess failure, and retained journal on incomplete rollback. Host parsing tests do not substitute for actual Unity importer/BuildPipeline proof.
- Actual toolchain/Unity/WebGL proof remains unrun for this contract: compile native diagnostic symbols, verify unchanged production pair and exports, exercise scoped Development staging and strip inventory, bind actual GUID, capture startup native/error correlation, repeat same-texture GPU progress and current exact-color calibration. Capacity loss before the target error, unsupported context, unmatched events, late arm or missing identity affecting the target yields inconclusive coverage; later exhaustion does not erase a proved retained-prefix observation.

Known implementation risks: four valid WebGL2 getters still perturb the GL call stream; the existing drain cannot separate their errors from prior state. Single-thread v1 is narrower than all WebGL builds. Empty pump callbacks can use the64callback slots; control attempts cannot. Rejected-context history without existing state is explicitly unavailable/nonpass, not silently reconstructed. No native API supplies a Unity GPU execution-frame number. Arbitrary unwrapped extra-define-only builds are unsupported and not preflight-inspectable through BuildReport. No diagnostic wrapper/parser/transactional stager/external-output option/symbol verifier exists yet. Unity import ordering and rollback must be proven in the installed Editor; compiler/library wrappers must be checked for hidden GL error reads. No cause or rendering fix is selected.

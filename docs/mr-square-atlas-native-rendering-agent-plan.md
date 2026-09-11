# AI Agent Plan: Generalize Marker Atlases, Restore Native Upload, and Remove MrSquare GardenNativeSurface

Status: implementation plan / cross-repository engineering brief.

Primary repositories:

- `gindemit/unity-rlottie` — implementation should start from the current `dev` branch, not from the older package tag alone.
- `gindemit/MrSquareUnity` — integration target. The prototype currently pins `com.gindemit.rlottie` to `0.5.0-dev.238`.

This document is intentionally written so an AI coding agent can use it as its main task brief.

---

## Objective

Replace the MrSquare-only `GardenNativeSurface` workaround with safe, first-class capabilities in `LottieAnimation`, use the plugin's optimized native texture-upload backends for live animations, and generalize the useful marker/frame-cache concepts so other users of `unity-rlottie` can optionally share one small Lottie animation across many visual instances without creating one native renderer per instance.

The final solution must preserve the existing native upload architecture and performance work. Do **not** solve the problem by forcing `Texture2D.Apply()` for all animations.

The implementation should provide two distinct reusable concepts:

1. **Marker / clip atlas** — one Lottie JSON document contains multiple named animation clips defined by standard Lottie markers.
2. **Shared rasterized frame cache** — optional pre-rasterization of selected marker clips so hundreds of visual instances can share the same immutable frames without hundreds of live rlottie renderers.

These concepts are related but must not be conflated.

---

## Current MrSquare state

### Current package dependency

`MrSquareUnity/Packages/manifest.json` currently references:

```text
com.gindemit.rlottie = ...#0.5.0-dev.238
```

The current `unity-rlottie/dev` branch already contains additional work beyond that pinned tag, including the newer native WebGL path. Start from current `dev`, preserve those changes, and integrate this work there rather than building a parallel implementation from the old tag.

### Current garden marker wrapper

`MrSquareUnity/Assets/Scripts/Garden/GardenLottieAtlas.cs` parses standard Lottie marker fields:

```text
cm = marker/comment name
tm = start frame
dr = duration
fr = document frame rate
```

It converts a marker plus normalized progress into an absolute Lottie frame.

This is a good reusable concept, but the implementation is game-local and should be generalized into the plugin without introducing garden-specific names or assumptions.

### Current plant frame cache

`GardenPlantFrameCache` currently:

- loads only the active board palette;
- supports at most seven species;
- uses 96 x 96 output frames;
- samples `dormant.loop`, `grow`, and `grown.loop` at 12 / 24 / 12 fps;
- produces 129 frames per species and 903 cached textures for a seven-species palette;
- releases the native renderer for a species after warmup;
- reuses the cached frames across every plant instance of that species.

This is the correct high-level scaling model for boards containing hundreds of plants: live rlottie renderer count should scale with the number of unique source animations being warmed or played live, not with visible plant instance count.

### Current live character rendering

`GardenCharacterView` renders the gardener at 192 x 192 and drives named markers such as idle, movement, impact, and celebration.

The character should be a live `LottieAnimation` user and should use native GPU upload where supported.

---

## Why `GardenNativeSurface` exists

This must be understood before changing anything.

The first Pocket Bloom garden implementation used `LottieAnimation`, but it explicitly configured:

```csharp
UseManagedTextureUpload = true
```

for `GardenLottieAtlas`.

That disabled the optimized native upload paths.

The managed `LottieAnimation` path keeps `_lottieRenderData.buffer` pointing at memory obtained from a Unity `Texture2D.GetRawTextureData<byte>()` view and calls `Texture.Apply()` after rendering.

Standalone/release-player testing in MrSquare later exposed failures around this ownership model:

- a writable Unity texture raw-data view was reacquired after the plugin had retained the original native pointer;
- warmup could crash;
- character source textures could freeze while frame counters continued advancing;
- an intermediate workaround created a second display texture and also produced an access violation during teardown;
- allocating a new `Color32[]` every changed frame was unacceptable.

`GardenNativeSurface` therefore moved ownership outside Unity texture storage:

```text
persistent pinned byte[]
      -> rlottie renders synchronously into it
      -> Texture2D.LoadRawTextureData(...)
      -> Texture2D.Apply()
```

This is safer than retaining a pointer into Unity texture storage, but it gives up the plugin's optimized native-upload backends.

The goal is **not** to preserve `GardenNativeSurface`. The goal is to put the correct ownership / snapshot APIs into `unity-rlottie` and then delete the local workaround.

---

## Important native-backend observation

Do not assume the same pointer problem applies unchanged to the native GPU-upload backends.

The native plugin already has a render-mailbox architecture. In `InstanceRegistry.cpp`, `AcquireRenderSlot(...)` replaces the externally supplied `renderData->buffer` with a plugin-owned render slot before rlottie rasterizes when a native instance state is active. After publication, the original external pointer is restored.

Conceptually:

```text
managed renderData.buffer
        |
        | AcquireRenderSlot
        v
plugin-owned cacheable CPU mailbox slot
        |
        | rlottie rasterization
        v
ready slot
        |
        | native render-thread upload
        v
Unity-owned or plugin-owned GPU texture
```

The platform matrix already avoids `Texture2D.Apply()` on supported native backends including D3D, Metal, Android GLES, and supported Vulkan paths.

Therefore the user's intuition is correct: **live garden animation should use the normal native `LottieAnimation` path rather than the current GardenNativeSurface Apply path.**

The managed/fallback path still needs to be made safe because:

- unsupported graphics APIs need a fallback;
- tests deliberately exercise managed upload;
- CPU frame baking benefits from a stable CPU-owned buffer;
- WebGL/fallback configurations may still require managed behavior;
- a generalized frame cache should not require GPU readback.

---

# Target architecture

## Layer 1 — `LottieAnimation`: safe live animation

`LottieAnimation` remains the primary live-animation abstraction.

Requirements:

- native upload is selected automatically when supported;
- no MrSquare code sets `UseManagedTextureUpload = true` merely to make the API usable;
- native mailbox/upload architecture is preserved;
- no per-frame managed allocations in steady-state drawing;
- managed fallback does not retain a native pointer into Unity texture storage across unsafe Unity operations;
- existing synchronous and asynchronous APIs continue to work;
- existing public behavior remains backward compatible where practical.

## Layer 2 — reusable marker clips

Add a general marker abstraction to the Unity package.

Suggested public concepts (names may be improved during implementation):

```csharp
public readonly struct LottieMarker
{
    public string Name;
    public int StartFrame;
    public int DurationFrames;
    public int EndFrameInclusive;
}

public sealed class LottieMarkerSet
{
    public double FrameRate { get; }
    public IReadOnlyList<LottieMarker> Markers { get; }

    public bool TryGet(string name, out LottieMarker marker);
    public LottieMarker Get(string name);
    public int Frame(string name, float normalized);
    public double DurationSeconds(string name);
}
```

Possible integration:

```csharp
LottieAnimation animation = ...;
LottieMarkerSet markers = animation.Markers;
animation.DrawMarkerFrame("move.right.loop", progress);
```

or keep marker mapping as a separate utility if that better preserves `LottieAnimation` responsibility.

### Marker rules

- parse standard `{cm, tm, dr}` marker data;
- use the Lottie document frame rate;
- inclusive final frame must be `tm + dr - 1`;
- normalized 1.0 must resolve to the marker's final displayed frame, not the next marker's first frame;
- reject or clearly define duplicate marker names;
- preserve float marker values if valid Lottie files can contain them, but expose deterministic integer sampling behavior;
- missing markers should produce a clear exception in strict APIs and a non-throwing result through `TryGet`;
- marker parsing must not require any garden-specific schema.

A lightweight Unity `JsonUtility` DTO is acceptable if it robustly handles the marker/header subset and adds no external dependency. Add tests with unrelated normal Lottie fields present in the same JSON.

---

## Layer 3 — safe CPU/staging output

The managed upload path should no longer depend on a long-lived pointer borrowed from Unity texture storage.

### Preferred direction

For non-native / managed rendering, own the raster buffer independently from the Unity texture.

For example:

```text
Persistent NativeArray<byte> / equivalent stable buffer
          -> rlottie output pointer
          -> upload/copy into Texture2D
          -> Apply only on managed fallback
```

Do not retain a `GetRawTextureData()` pointer as the authoritative native render target for the lifetime of an animation unless the lifetime guarantee is proven and regression-tested on all supported Unity versions.

A `NativeArray<byte>(Allocator.Persistent)` is preferable to the MrSquare pinned managed array if it fits all supported Unity/IL2CPP targets and disposal rules. Validate it rather than assuming.

### Required properties

- one stable allocation per animation/output surface, not per frame;
- correct disposal before native render-data teardown;
- constructor failure cleanup;
- application quit cleanup;
- editor domain/assembly reload cleanup;
- no stale pointer after `Texture.Apply`, resize/recreation, readback, or diagnostic inspection;
- no `Color32[]` per frame;
- correct BGRA/RGBA behavior;
- preserved premultiplied-alpha semantics.

### Backward compatibility

`UseManagedTextureUpload` can remain temporarily for API compatibility, but consider evolving the configuration toward a clearer enum in a later release:

```text
Auto / NativePreferred
Managed
CpuOnly / Bake
```

Do not break existing package users solely for this refactor.

---

## Layer 4 — CPU frame baking / snapshot API

The frame-cache use case is different from live native GPU upload.

A plant atlas needs to render a selected set of frames **once**, convert/store them as immutable shared textures, and then dispose the renderer. Sending every bake frame through native GPU upload only to read it back is the wrong pipeline.

Add a first-class safe way to obtain deterministic raster pixels from a requested frame without requiring GPU readback.

Possible API shapes:

```csharp
animation.RenderFrameToBuffer(frame, destination);
```

or a dedicated object:

```csharp
using var rasterizer = LottieCpuRasterizer.LoadFromJson(...);
rasterizer.RenderFrame(frame, destination);
```

or a package-internal CPU mode used by the frame-cache builder.

### Requirements

- synchronous completion;
- stable caller/plugin-owned output memory;
- no native GPU upload required;
- no `Texture2D.Apply()` required just to inspect the result;
- zero steady-state per-frame allocations after construction;
- exact dimensions/stride known;
- explicit pixel format;
- deterministic output equal to the ordinary renderer for the same size/frame/options;
- safe with multiple concurrent animation objects.

If implementation can reuse the existing managed render route cleanly after fixing buffer ownership, avoid adding unnecessary native C ABI. If it cannot, add a narrowly scoped render-to-buffer native entry point rather than exposing internal structs unsafely.

---

# Optional plugin feature: shared `LottieFrameCache`

Generalize the successful MrSquare plant-cache idea as an optional package utility.

The package utility must not know what a "plant" is.

Suggested concepts:

```csharp
public readonly struct LottieClipSampling
{
    public string MarkerName;
    public int FramesPerSecond;
    public bool Loop;
    public bool IncludeExactEndFrame;
}

public sealed class LottieFrameCacheOptions
{
    public int Width;
    public int Height;
    public IReadOnlyList<LottieClipSampling> Clips;
    public FilterMode FilterMode;
    public TextureWrapMode WrapMode;
    public bool MakeNoLongerReadable;
    public LottieAlphaMode AlphaMode;
}

public sealed class LottieFrameCache : IDisposable
{
    public bool Ready { get; }
    public int CachedFrameCount { get; }
    public long EstimatedPixelBytes { get; }

    public bool WarmStep(int maxFrames = 1);
    public Texture Sample(string marker, double seconds);
    public Texture SampleNormalized(string marker, float normalized);
}
```

Names and exact signatures are flexible; the behavioral contract is not.

### Behavioral requirements

- parse the Lottie source once per cache;
- render only requested marker clips;
- sample at configurable rates instead of assuming source FPS;
- exact one-shot endpoint inclusion must be configurable;
- loops must not cache a redundant duplicate seam frame;
- cached frames are immutable from the caller's perspective;
- native/source renderer can be released after warmup;
- multiple scene objects can reference the same cached frame;
- memory estimate is exposed;
- disposal destroys generated textures;
- warmup can be incremental to avoid a long startup spike;
- no requirement for one renderer per visual instance.

### Straight vs premultiplied alpha

MrSquare currently converts rlottie premultiplied output to straight alpha before storing immutable plant `Texture2D` snapshots because UI Toolkit composites the cached images as regular textures.

Do not hide this conversion inside game code if the package frame-cache feature can define it generically.

Add an explicit alpha-output option and test transparent colored edges.

### Do not over-promise the word "atlas"

There are two possible meanings:

1. marker timeline atlas — multiple clips in one Lottie file;
2. packed texture atlas — many raster frames packed into fewer GPU textures.

The first implementation may keep one `Texture2D` per cached frame if that is the lowest-risk API. Document it as a shared frame cache.

A later optimization can pack frames into:

- texture sheets plus UV rectangles; or
- `Texture2DArray` plus slice indices.

That second phase needs renderer/UI integration and should not block the initial safe generalization.

---

# MrSquare integration after plugin implementation

Do not change MrSquare first. Implement and validate the plugin capabilities, then integrate.

## 1. Update package dependency

After the `unity-rlottie` branch is validated, make MrSquare consume the validated commit/tag rather than silently depending on an unreviewed local package.

## 2. Remove `GardenNativeSurface`

Delete:

```text
Assets/Scripts/Garden/GardenNativeSurface.cs
```

only after all call sites and diagnostics are supported through package APIs.

## 3. Replace game-local marker parsing

`GardenLottieAtlas` should either disappear or become a very thin game-semantic wrapper around the plugin marker API.

Do not keep a second generic `{cm, tm, dr}` parser in MrSquare once the package owns that capability.

## 4. Live gardener: use native `LottieAnimation`

The gardener must use:

```text
LottieAnimation + native-preferred/default upload
```

Do **not** set `UseManagedTextureUpload = true` on supported devices.

The diagnostics should record `TextureUploadBackend` so tests can prove Android Vulkan/GLES and other supported platforms are not silently falling back to `ManagedTextureUpload`.

Keep the existing changed-frame optimization: do not render the same frame twice.

Keep distance-controlled gait sampling; this refactor must not return to wall-clock walking.

## 5. UI Toolkit presentation

`GardenCharacterView` currently calls `RuntimePanelUtils.SetTextureDirty(...)` and `MarkDirtyRepaint()` after a new frame because UI Toolkit may maintain its own dynamic-atlas copy.

Native texture upload is render-thread scheduled, so do not assume this interaction is automatically correct.

Add an explicit integration regression with a real `UIDocument`:

- a configuration where the live texture is excluded from UI Toolkit dynamic atlas;
- a forced configuration where it is admitted;
- one-shot frame changes;
- continuous motion;
- two simultaneous actors;
- capture the actual panel pixels, not only source texture counters.

If immediate dirtying races with native GPU upload, solve the ordering deliberately. Possible solutions include excluding live animated textures from the UI dynamic atlas, adding an appropriate presentation notification, or synchronizing the cache refresh with the upload lifecycle. Do not reintroduce per-frame CPU readback simply to make the test pass.

The current production-like panel uses a 64-pixel max subtexture size, so a 192-pixel actor is excluded today. Tests should still cover a larger threshold because future adaptive resolution/settings can change this assumption.

## 6. Plants: use package shared frame cache / CPU baking

Replace `GardenPlantFrameCache` internals with the generalized package utility or a thin aggregation layer.

MrSquare still owns:

- species selection;
- active palette selection;
- plant lifecycle state;
- pack/biome semantics;
- which markers are required;
- per-plant phase/start time;
- garden LOD policy.

The rlottie package owns:

- marker parsing;
- clip-to-frame mapping;
- safe CPU rasterization;
- generic sampled-frame caching;
- texture/pixel ownership;
- generic cache memory accounting.

## 7. Add the new `enter` marker lifecycle

Update botanical assets/generator contract from:

```text
dormant.loop -> grow -> grown.loop
```

toward:

```text
enter -> dormant.loop -> grow -> grown.loop
```

`enter` is a one-shot animation from seed/soil/tiny shoot into the canonical dormant seedling pose.

Requirements:

- final `enter` frame equals first/canonical dormant frame;
- dormant seam remains exact;
- dormant-to-grow seam remains exact;
- grow-to-grown seam remains exact;
- generated sources remain deterministic;
- source validation checks every boundary;
- frame-cache tests cover the additional clip.

Do not force every future Lottie user to use these marker names; they are a MrSquare content convention built on the generic plugin marker API.

---

# Large-board requirements

The intended game may contain hundreds of plants on a large board.

The integration must demonstrate that:

- 100 / 250 / 500 plant instances do not create 100 / 250 / 500 native rlottie renderers;
- cache memory depends on active source/clip quality, not plant instance count;
- a mature board can have zero live plant Lottie renderers after cache warmup;
- plant images are shared;
- off-screen / LOD presentation can later be layered on top without changing animation source ownership.

Add stress fixtures in MrSquare even if full board LOD/batching is a later task.

Remember: after Lottie rasterization is optimized, hundreds of UI Toolkit elements may become the bottleneck. Measure that independently.

---

# Adaptive resolution hooks

Do not hardcode the generalized cache to 96 x 96.

The package API must accept arbitrary valid output dimensions.

MrSquare will later choose quality based on device/display/memory budget. The current seven-species/903-frame raw RGBA32 cost is approximately:

```text
64 px   -> 14.11 MiB
80 px   -> 22.05 MiB
96 px   -> 31.75 MiB
128 px  -> 56.44 MiB
160 px  -> 88.18 MiB
192 px  -> 126.98 MiB
```

The plugin should expose enough frame-count and byte diagnostics for a game to make this decision before or during warmup.

Do not build device-tier policy into `unity-rlottie`; that belongs to the consuming game.

---

# Testing requirements — unity-rlottie

Do not consider the task complete with only Editor unit tests.

## Marker tests

Cover:

- marker list parsing;
- missing markers;
- duplicate marker policy;
- non-zero marker starts;
- single-frame markers;
- normalized 0 and 1;
- exact inclusive last frame;
- fractional Lottie marker values if supported;
- unrelated normal JSON fields;
- malformed marker data;
- documented frame-rate fallback/error behavior.

## Stable CPU buffer tests

Reproduce the class of failure that caused `GardenNativeSurface`:

- render frame A;
- mutate/upload the Unity texture;
- inspect/read texture data;
- reacquire texture raw data where APIs permit;
- render frame B;
- confirm native output remains valid and independent;
- repeat thousands of times;
- dispose;
- repeat with two independent animations;
- force GC between operations;
- exercise Editor reload/disposal hooks.

No test should rely only on `CurrentFrame` changing. Validate pixel hashes.

## Managed upload tests

- stable pixel parity after many draws;
- no use-after-free during dispose;
- no per-frame allocations beyond explicitly accepted engine internals;
- correct BGRA/RGBA channels;
- correct alpha;
- fallback after native registration failure.

## Native upload tests

On supported rendered players assert:

```text
TextureUploadBackend != ManagedTextureUpload
```

and validate actual changing GPU-visible pixels.

Exercise:

- one-shot first frame;
- repeated same frame;
- fast frame changes;
- multiple animations;
- async draw;
- pause/resume;
- teardown while uploads have been queued;
- application exit;
- scene changes;
- graphics-device/lifecycle conditions already covered by the project.

## Frame-cache tests

- exact cached frame count;
- loop endpoint not duplicated;
- one-shot endpoint included when requested;
- immutable snapshots after source renderer advances;
- source renderer can be disposed after warmup;
- sampled frame still works after renderer disposal;
- texture memory estimate;
- configurable resolution;
- configurable sample rate;
- straight-alpha conversion if exposed;
- no frame cache allocation growth after warmup;
- two consumers reuse the same cached texture references when sharing one cache.

## Native C++ tests

Preserve and extend `tests/native/NativeAbiTests.cpp` where the new behavior affects the ABI.

If no new ABI is necessary, avoid adding one only for test convenience.

---

# Rendered-player platform matrix

At minimum validate the paths available in the existing plugin infrastructure.

Priority:

1. Windows development/player path used by local visual reviews.
2. Android OpenGL ES on a physical device.
3. Android Vulkan on a physical device.
4. WebGL current `dev` path, because current `dev` has changes beyond the MrSquare pinned tag.
5. macOS Metal if the development environment is available.
6. iOS Metal before declaring the feature production-ready for iOS.

For every supported native backend record:

- selected `TextureUploadBackend`;
- pixel correctness;
- frame pacing;
- allocations;
- teardown result;
- fallback warnings;
- native upload/raster profiler markers where available.

Do not treat a screenshot alone as performance evidence.

---

# Performance acceptance criteria

The goal is to improve safety without throwing away the existing native-upload optimizations.

### Live animation

On a platform where native upload is supported:

- no per-frame `Texture2D.Apply()`;
- backend reports native upload;
- no per-frame managed `Color32[]` or equivalent full-frame allocation;
- stable frame/pixel changes;
- no regression in existing package benchmark beyond a small explainable margin;
- no shutdown crash;
- no stale frame after repeated one-shot draws.

### Frame baking

- no GPU readback in the normal bake pipeline;
- one stable CPU raster buffer per active baker/source, or an equivalently bounded design;
- incremental warmup supported;
- source renderer/baker releasable after cache is complete;
- no live renderer per consuming visual instance.

### Stress

Benchmark at least:

- 1 live animation;
- multiple live animations;
- shared cached animation shown by 10 / 100 / 500 visual instances;
- 64 / 96 / 128 output resolutions;
- one and several simultaneous source caches.

Separate Lottie cost from consumer UI/layout cost in reporting.

---

# Cross-repository execution sequence for the AI agent

## Phase 0 — baseline and evidence

1. Checkout current `unity-rlottie/dev`.
2. Record the current commit SHA.
3. Run existing native/unit/runtime tests available in the environment.
4. Run an existing benchmark/smoke case for at least one native path and one managed path.
5. Record baseline backend selection and allocation/performance evidence.
6. Inspect current `dev` changes relative to `0.5.0-dev.238`; do not accidentally revert newer WebGL/native work.

## Phase 1 — marker API

1. Implement generic marker parsing and frame mapping.
2. Add tests.
3. Document public usage.
4. Do not change upload behavior yet.

## Phase 2 — safe managed/staging ownership

1. Replace unsafe long-lived Unity texture raw-pointer ownership where applicable with a stable independently owned buffer.
2. Preserve native upload paths.
3. Add lifetime/pixel/allocation regressions.
4. Verify managed fallback on rendered players where possible.

## Phase 3 — CPU bake API and generic frame cache

1. Add deterministic render-to-buffer/bake support.
2. Implement optional sampled shared frame cache.
3. Add alpha-mode support required by UI consumers.
4. Add memory/frame diagnostics.
5. Add docs and samples.

## Phase 4 — native live regression

1. Verify default `LottieAnimation` still selects native upload on supported APIs.
2. Run benchmark comparison against Phase 0.
3. Fix any upload/presentation regression before touching MrSquare integration.

## Phase 5 — MrSquare integration

1. Point MrSquare to the validated plugin commit/tag.
2. Replace game-local marker parsing with package API.
3. Replace live gardener `GardenNativeSurface` with `LottieAnimation` native-preferred path.
4. Replace plant cache internals with generalized package frame cache/baker.
5. Preserve existing UI Toolkit texture-dirty logic until tests prove whether it can be simplified.
6. Delete `GardenNativeSurface` only after all relevant tests pass.
7. Add `enter` plant animation contract and regenerate botanical sources.
8. Run all existing garden EditMode/PlayMode reviews.
9. Run Windows release-player visual review.
10. Run physical Android review on GLES/Vulkan where available.
11. Add 100/250/500-plant stress fixtures and record scaling behavior.

## Phase 6 — documentation

Update:

- plugin README/API docs;
- upload-path/platform documentation if ownership or fallback behavior changed;
- a marker/frame-cache sample;
- MrSquare garden animation docs;
- performance evidence with exact device/API/backend information.

---

# Documentation example for plugin users

The final package docs should include a simple scenario such as:

```text
One vector Lottie file contains markers:
idle
spawn
grow
mature.loop

A game wants 200 flowers on screen.

Instead of creating 200 LottieAnimation instances:
- build one shared frame cache at the desired resolution;
- pre-sample the named clips;
- display the cached textures from each flower instance;
- dispose the baker after warmup.
```

Also document the alternative:

```text
For a small number of important continuously animated characters,
use normal LottieAnimation so native texture upload can remain active.
```

This distinction is central to the API story.

---

# Non-goals for the first implementation

Do not block this work on:

- a production world/pack system;
- fully packed sprite sheets;
- `Texture2DArray` rendering;
- custom UI Toolkit shaders;
- device quality heuristics;
- full large-board LOD;
- remote content delivery;
- changing rlottie source format.

Those are future layers. First make the reusable animation ownership, marker, native-live, and shared-frame-cache architecture correct and thoroughly validated.

---

# Definition of done

This task is complete only when all of the following are true:

1. MrSquare no longer requires `GardenNativeSurface`.
2. Live gardener animation uses `LottieAnimation` and native upload on supported targets.
3. The package managed/fallback path has a tested safe ownership contract.
4. Standard Lottie markers are available through a reusable plugin API.
5. A generic optional sampled frame cache exists or an equivalent package-level reusable abstraction is implemented.
6. Plant instances can share cached frames without one native animator per plant.
7. The plant cache can be configured for different resolutions/sample rates.
8. Existing and new pixel/lifetime/allocation tests pass.
9. Rendered-player native backend tests prove actual GPU-visible frame changes.
10. Android physical-device review succeeds without stale frames or teardown crashes.
11. Native-upload performance is not replaced by per-frame `Apply()` on supported platforms.
12. Public plugin documentation explains when to use live `LottieAnimation` versus a shared cached atlas.

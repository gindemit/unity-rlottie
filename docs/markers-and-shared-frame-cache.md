# Markers, CPU rasterization, and shared frame caches

The runtime has two deliberately different rendering models:

- Use `LottieAnimation` for a small number of live animations. It keeps the
  optimized native texture-upload backend on supported graphics APIs.
- Use `LottieFrameCache` when many consumers show the same source animation.
  It rasterizes selected marker clips once and lets every consumer reference
  the same immutable `Texture2D` frames.

For example, a single flower document can contain `idle`, `spawn`, `grow`, and
`mature.loop` markers. Two hundred flowers should normally share one warmed
cache instead of creating two hundred live `LottieAnimation` instances. A
continuously animated hero or other small set of prominent objects should use
live `LottieAnimation` instead.

## Standard Lottie markers

Every loaded `LottieAnimation` and `LottieCpuRasterizer` exposes a `Markers`
property. A marker contains its original timeline start and duration plus its
inclusive, zero-based rlottie render-frame range:

```csharp
LottieMarker marker = animation.Markers.Get("grow");
int first = marker.FirstRenderFrame;
int last = marker.LastRenderFrameInclusive;

animation.DrawMarkerFrame("grow", 0.5f);
```

`LottieMarkerSet.Parse(json, totalFramesCount)` is also available when marker
metadata is needed without retaining an animation-owned marker set. Parsing is
ordinal and rejects missing/invalid `fr`, `ip`, or `op`, empty or duplicate
marker names, non-finite values, negative durations, and positive marker
intervals that contain no integral frame. There is no implicit frame-rate
fallback.

Lottie markers use composition timeline values. rlottie renders zero-based
indices and internally applies the rounded composition `ip`, so this API
subtracts the rlottie-rounded `ip` and clamps to
`[0, TotalFramesCount - 1]`. Marker intervals are half-open
`[tm, tm + dr)`: the first timeline frame is `ceil(tm)` and the last is
`ceil(tm + dr) - 1`. A zero-duration point marker maps to `ceil(tm)`.

`Frame(name, normalized)` clamps finite input to `[0, 1]` and uses
`floor(first + normalized * frameCount)`, clamped to the inclusive last frame.
Consequently, both endpoints are exact and `1.0` never enters the next marker.
`Get` throws for a missing name; `TryGet` provides the non-throwing alternative.
An animation- or rasterizer-owned marker set throws `ObjectDisposedException`
after its owner is disposed.

## Synchronous CPU rasterization

`LottieCpuRasterizer` renders a frame without creating a GPU texture or doing a
GPU readback:

```csharp
using Unity.Collections;
using LottiePlugin;

using (LottieCpuRasterizer rasterizer =
       LottieCpuRasterizer.LoadFromJsonData(json, resourcesPath, 96, 96))
using (var pixels = new NativeArray<byte>(rasterizer.ByteCount, Allocator.Persistent))
{
    rasterizer.RenderMarkerFrame("grow", 0.5f, pixels);
    // Consume or copy pixels before disposing caller-owned `pixels`.
}
```

The caller owns the writable `NativeArray<byte>`. It must be created and have
exactly `Width * Height * 4` bytes. Rendering completes synchronously before the
method returns; calls on one rasterizer are serialized. The rasterizer keeps
its own persistent staging allocation and copies the completed frame into the
caller buffer, so it never retains the caller's pointer.

The output is tightly packed premultiplied BGRA8 with `Stride == Width * 4`.
Rows use rlottie's native surface order: byte zero is the bottom-left texture
pixel when passed directly to Unity's `LoadRawTextureData`. Dispose the
rasterizer explicitly. The runtime also disposes surviving rasterizers on
application quit and before an Editor assembly reload.

## Building and sharing a frame cache

```csharp
var options = new LottieFrameCacheOptions
{
    Width = 96,
    Height = 96,
    Clips = new[]
    {
        new LottieClipSampling("idle", 12, loop: true),
        new LottieClipSampling("grow", 24, loop: false,
            includeExactEndFrame: true),
        new LottieClipSampling("mature.loop", 12, loop: true)
    },
    AlphaMode = LottieAlphaMode.PremultipliedBgra,
    MaximumRawPixelBytes = 32L * 1024 * 1024
};

using (var cache = new LottieFrameCache(json, resourcesPath, options))
{
    while (!cache.WarmStep(maxFrames: 4))
    {
        // Yield until a later Unity frame.
    }

    Texture2D shared = cache.Sample("idle", elapsedSeconds);
    Texture2D alsoShared = cache.SampleNormalized("grow", normalizedProgress);
}
```

Construction validates dimensions, clips, sampling rates, marker names,
checked arithmetic, and the byte budget before allocating cached textures.
Inspect `Preflight`, `CachedFrameCount`, and `EstimatedRawPixelBytes` to report
the plan. `CachedFrameCount` is the final planned frame count, while
`WarmedFrameCount` reports how many immutable snapshots have actually completed
during incremental warmup. `DurationSeconds(markerName)` exposes the retained
clip duration before and after warmup without keeping the source rasterizer
alive. The estimate is exactly `frame count * width * height * 4`; it does not
include `Texture2D` objects, allocator metadata, driver copies, or other GPU
overhead. `MaximumRawPixelBytes` limits this raw-pixel estimate, not total
process residency.

`WarmStep(maxFrames)` performs bounded incremental work and returns `true` once
all frames are ready. It must run on the same thread that constructed the cache
because it creates and uploads Unity textures. Create, sample, and dispose the
cache on Unity's main thread. On completion, the source CPU rasterizer and its
staging buffer are released; the cached textures remain usable until the cache
is disposed.

Loop clips omit the duplicated endpoint and wrap both elapsed time and
normalized `1.0` to the first cached frame. Non-loop clips clamp at their ends;
set `IncludeExactEndFrame` to include the marker's final displayed frame.
Adjacent samples that resolve to the same source frame are deduplicated.
Sampling selects the most recent sampled source position, preserving the
original timing after deduplication.

Cached textures are immutable snapshots and are owned by the cache. Multiple
consumers receive the same texture references; they must not modify or destroy
them. Disposing the cache releases partially or fully warmed textures and makes
all subsequent public calls fail with `ObjectDisposedException`.

`PremultipliedBgra` stores rlottie's bytes directly in a `BGRA32` texture.
`StraightRgba` unpremultiplies color channels into cache-owned staging storage,
swizzles BGRA to RGBA, and creates an `RGBA32` texture. Fully transparent pixels
become transparent black. Choose the mode expected by the consumer's blend and
shader setup; conversion does not change row orientation.

## Live upload and managed fallback ownership

`LottieAnimation` still selects native upload by default. Inspect
`TextureUploadBackend` rather than assuming a path. Supported native backends
rasterize into plugin-owned mailbox slots and upload on Unity's render thread;
the external managed render pointer is null and there is no per-frame
`Texture2D.Apply()`.

`LottieAnimationOptions.UseManagedTextureUpload` remains supported for explicit
fallback/testing. The managed path now owns a persistent `NativeArray<byte>`
separate from Unity texture storage. rlottie renders into that stable buffer,
then the runtime calls `LoadRawTextureData` and `Apply(false, false)`. It never
retains a pointer returned by `Texture2D.GetRawTextureData`, so texture uploads
or raw-data inspection cannot invalidate the native render target. A failed
native registration or upload also transitions to this managed path and
rerenders the current frame. This preserves existing API behavior while keeping
native upload as the normal fast path.

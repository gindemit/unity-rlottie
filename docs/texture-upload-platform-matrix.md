# Texture upload paths and Vulkan native-upload analysis

## Two-slot CPU render mailbox and explicit-API upload rings (implemented)

Native texture paths use two preallocated, cacheable CPU render slots. Each
slot moves through `Free -> Rendering -> Ready -> Uploading -> Free`; rlottie
always rasterizes into normal CPU memory. Publication changes only metadata,
so no completed frame is copied into a separate publication vector and no
publisher can overwrite bytes still owned by an upload.

- D3D11, OpenGL, and Metal consume the selected CPU slot synchronously. The
  slot becomes free after `Map`/row copy/`Unmap`, `glTexSubImage2D`, or
  `replaceRegion` returns.
- D3D12 owns a separate three-region, persistently mapped upload heap. The
  render thread performs one sequential row copy from the selected cacheable
  CPU slot into a fence-available region, releases the CPU slot immediately,
  and retires the upload region with Unity's frame fence.
- Vulkan owns a separate three-slot mapped transfer-buffer ring. The render
  thread performs one sequential copy from the selected cacheable CPU slot,
  flushes non-coherent memory when required, releases the CPU slot immediately,
  and retires the transfer slot using Unity's `safeFrameNumber`.
- Android OpenGL ES uses a Unity-owned RGBA texture. rlottie still produces
  BGRA, so the render-thread upload converts into one persistent RGBA scratch
  buffer before `glTexSubImage2D`. This avoids both the former dummy external
  texture handle and Mali's rejected BGRA upload into a Unity RGBA texture.
- Managed `Texture2D.Apply` fallback and WebGL continue rendering into their
  external Unity-owned buffers and do not acquire native mailbox slots. Native
  WebGL upload snapshots each completed frame into one per-instance upload
  buffer that its render-thread `glTexSubImage2D` callback consumes.

When both CPU slots are busy, a frame is skipped instead of allocating or
blocking. When multiple completed frames are ready, the newest is uploaded and
older ready frames are coalesced. If an explicit API has no available upload
region or command context, the CPU slot returns to `Ready`; its version is not
reported as uploaded, and a later render event retries it. The queue tail clears
`uploadQueued` and then rechecks the published version, closing the former
lost-wakeup window. Registry removal first prevents new lookups, then waits for
active render/upload ownership before resetting backend resources.

The rejected three-slot design rasterized rlottie directly into persistently
mapped D3D12/Vulkan upload memory. On the tested systems that memory is
write-combined or otherwise uncached for CPU read-modify-write access. rlottie's
rasterizer performs destination reads and blends, so performance collapsed:
Android Vulkan 1024 x 1024 rose from roughly 11-15 ms to 165-200 ms per frame,
and Windows D3D12 fell from about 1393 FPS to about 1.2 FPS. The implemented
architecture restores cacheable rasterization and pays one predictable,
sequential CPU-to-upload-ring copy on explicit APIs.

## Scope and snapshot

This document describes the implementation on the canonical `dev` branch. Every
non-WebGL path was audited and validated on 2026-08-13. The WebGL statements
were re-audited from source at the `dev` tip on 2026-09-01, after
`Add native WebGL texture uploads with fallback` (2026-08-26) and
`Support Unity 6000.5 WebGL exception ABI` (2026-08-27) landed.

The WebGL native-upload path is **described from code, not test-validated**. The
most recent test evidence on this repository is from 2026-08-14 and therefore
predates that implementation, so no statement about WebGL native upload below
should be read as a browser or device validation result.

In this document, **Apply** specifically means a call to `Texture2D.Apply()`.
The synchronous and asynchronous draw paths make the same upload choice.

## Current platform and graphics API matrix

| Platform | Graphics API or output mode | Apply used? | Current upload path |
|---|---|---:|---|
| Windows | Direct3D 11 | No | Native external texture and render-thread upload |
| Windows | Direct3D 12 | No | Native external texture and render-thread upload |
| Windows | OpenGL Core | No | Native external GL texture and render-thread upload |
| Windows | Vulkan | No when native Vulkan is available; otherwise Yes | Unity-owned `Texture2D` with native render-thread upload; guarded `Texture2D.Apply()` fallback |
| Linux Editor or Standalone | OpenGL Core | No when native OpenGL upload is available; otherwise Yes | Unity-owned `Texture2D` with native render-thread upload; guarded `Texture2D.Apply()` fallback |
| Linux Editor or Standalone | Vulkan | No when native Vulkan is available; otherwise Yes | Unity-owned `Texture2D` with native render-thread upload; guarded `Texture2D.Apply()` fallback |
| macOS | Metal | No | Native external Metal texture and native upload |
| macOS | OpenGL Core | Yes | No Apple OpenGL native backend; automatic managed-upload fallback |
| iOS | Metal | No | Native external Metal texture and native upload |
| Android | OpenGL ES 2 or 3 | No | Unity-owned RGBA texture; native render-thread BGRA-to-RGBA conversion and upload |
| Android | Vulkan | No when native Vulkan is available; otherwise Yes | Unity-owned `Texture2D` with native render-thread upload; guarded `Texture2D.Apply()` fallback |
| WebGL | WebGL 1 or 2, reported as `OpenGLES2` or `OpenGLES3` | No when native WebGL upload is available; otherwise Yes | Unity-owned RGBA `Texture2D` with native BGRA-to-RGBA conversion and render-thread `glTexSubImage2D`; guarded `Texture2D.Apply()` fallback |
| WebGL | Managed upload requested, or a renderer outside `OpenGLES2`/`OpenGLES3` such as WebGPU; shader conversion (default) | Yes | Apply to the source `Texture2D`, then blit into the exposed `RenderTexture` |
| WebGL | Managed WebGL path with native conversion selected | Yes | Apply converted pixels to the exposed `Texture2D` |
| WebGL | Managed WebGL path with the conversion shader unavailable | Yes | Automatic fallback to the native-conversion path |

The Built-in, URP, and HDRP render pipelines do not select the upload path.
The active graphics API and platform compile symbols select it. Consequently:

- Windows Built-in, URP, and HDRP use Apply on Vulkan only when the native
  capability check or upload fails;
- macOS Built-in, URP, and HDRP do not use Apply when running Metal;
- Android Built-in and URP use the native path on supported Vulkan devices and
  Apply only as a runtime fallback; OpenGL ES retains its native upload path;
- iOS Built-in and URP do not use Apply on Metal; and
- WebGL Built-in and URP use the native WebGL upload path on WebGL 1/2
  renderers and Apply only when managed upload is requested, when the reported
  renderer is not a WebGL 1/2 renderer, or as a runtime fallback. HDRP is not a
  targeted WebGL, Android, or iOS configuration in the project matrix.

The relevant managed decisions are:

1. WebGL player builds attempt native upload first. They create a Unity-owned
   single-mip RGBA `Texture2D`, allocate it with one initialization-time Apply,
   and register its cached native texture name. They set
   `_usesCPURendering = true` only when managed upload was requested, when the
   reported renderer is not `OpenGLES2`/`OpenGLES3`, or when registration or a
   later upload request fails.
2. Vulkan creates a Unity-owned `Texture2D`, registers its cached native handle,
   and selects native upload only when the native capability check succeeds.
3. Linux OpenGL and Vulkan use Unity-owned textures with native render-thread
   uploads when their capability checks succeed. Android OpenGL ES also uses a
   Unity-owned RGBA texture and a persistent native conversion scratch buffer.
4. Managed-upload branches call Apply after a synchronous result or a completed
   asynchronous result.
5. Other branches call `RequestTextureUpload()`, which queues a native upload
   consumed by `GL.IssuePluginEvent` on Unity's render thread.

In WebGL shader mode, Apply is called on the intermediate source `Texture2D`,
not on the exposed output `RenderTexture`. Native WebGL upload disables shader
conversion, so neither that intermediate texture nor the blit exists on the
native path; its guarded managed fallback keeps the same RGBA texture and native
channel conversion and only resumes calling Apply.

`AnimatedButton` and `AnimatedImage` now make the same upload-path selection.
The former `AnimatedButton` managed-upload override was removed after the
render-thread pump was changed to initialize before the component queues its
one-shot first frame. Both components use native upload when it is available.

## Unsupported, fallback-only, and currently failing APIs

Here, **unsupported** means the plugin has no native texture backend for the
renderer reported by Unity. It does not mean that Unity itself cannot use that
graphics API. `RendererCommon::ToRenderer()` currently recognizes only D3D11,
D3D12, OpenGL Core, OpenGL ES 2/3, Metal, and Vulkan. Any other renderer is
mapped to `Renderer::Unknown`, and native texture creation returns no texture.

Native texture creation and external-texture wrapping failures now fall back to
a managed BGRA `Texture2D` instead of failing animation construction. This makes
unsupported renderers and transient native initialization failures functional,
but they use `Texture2D.Apply()` and emit a warning identifying the renderer or
wrapper failure. It does not turn those renderers into native-upload backends.

The following current Unity renderer values are therefore unsupported by this
plugin:

| Platform family | Unity renderer value | Status |
|---|---|---|
| PlayStation | `kUnityGfxRendererPS4` | Unsupported; no PlayStation texture backend |
| PlayStation | `kUnityGfxRendererPS5`, `kUnityGfxRendererPS5NGGC` | Unsupported; no PlayStation texture backend |
| Xbox | `kUnityGfxRendererXboxOne`, `kUnityGfxRendererXboxOneD3D12` | Unsupported; the Windows D3D backends are not Xbox backends |
| Xbox/GameCore | `kUnityGfxRendererGameCoreXboxOne`, `kUnityGfxRendererGameCoreXboxSeries` | Unsupported; no GameCore texture backend |
| Nintendo Switch | `kUnityGfxRendererNvn` | Unsupported; no NVN texture backend |
| Headless/batch mode | `kUnityGfxRendererNull` | No GPU texture backend; rendered-player validation is not available |
| macOS | OpenGL Core | No native backend in the Apple build; managed-upload fallback remains functional |

Legacy Direct3D 9, legacy desktop OpenGL, and PlayStation Vita GXM are removed
from the bundled Unity native-plugin interface and are not targets.

The following paths are not classified as unsupported:

- Linux OpenGL Core uses a Unity-owned texture. The scripting thread only
  registers Unity's cached native texture handle; extension detection and
  `glTexSubImage2D()` run from `GL.IssuePluginEvent` with Unity's render-thread
  context. Registration or upload failures retain or restore
  `Texture2D.Apply()`.
- WebGL has a native WebGL 1/2 texture backend with a guarded managed fallback,
  so it is no longer managed-upload only. Renderers that WebGL does not report
  as `OpenGLES2` or `OpenGLES3`, such as WebGPU, and explicitly requested
  managed upload retain `Texture2D.Apply()`.
- Vulkan has a guarded managed fallback when its Unity interface or an upload
  operation is unavailable.

Android Vulkan is implemented and passes Unity 6000.5.3f1 validation. On
2026-08-10, the same physical Samsung SM-N975F (Mali-G76, Android 12) produced
this matrix:

| Pipeline | API | Selected upload backend | Rendered-player result |
|---|---|---|---|
| Built-in | OpenGL ES 3 | `NativeExternalTexture` | Passed all checks |
| URP | OpenGL ES 3 | `NativeExternalTexture` | Passed all checks |
| Built-in | Vulkan | `NativeVulkan` | Passed all checks |
| URP | Vulkan | `NativeVulkan` | Passed all checks |

The original Vulkan runs reported an unchanged sampled pixel hash even though
device screenshots showed the native-uploaded animation changing. The smoke
test used an immediate `Graphics.Blit` plus `ReadPixels`, which returned stale
contents for this Unity-owned texture after a native Vulkan write. Validation
now uses `AsyncGPUReadback` for `NativeVulkan` textures and waits for a changed
GPU-visible signature. Built-in and URP both pass with distinct frame hashes;
the native upload implementation did not require a fallback or backend change.

A separate Mali-G76 issue affected textures at 1024 x 1024 and larger when
they were minified. Unity had allocated a full mip chain, but the native Vulkan
backend updates only mip level 0, so sampling could select stale lower mip
levels. Unity-owned native textures now contain exactly one mip level. The
temporary Mali-G76 managed-upload denylist for textures at or above 4 MiB has
been removed because it predated and masked that fix.

## Vulkan without Apply: implementation status

Native Vulkan upload without a per-frame Apply call is implemented for Windows,
Linux, and Android. The backend acquires `IUnityGraphicsVulkanV2` with a v1
fallback, registers a Unity-owned texture, records an outside-render-pass copy
from mapped staging memory, and uses Unity resource access and safe-frame data
for synchronization and retirement.

The path is capability guarded. If the Unity Vulkan interface, texture
registration, required Vulkan functions, or an upload operation is unavailable,
managed code retains or returns to `Texture2D.Apply()` instead of exposing a
blank texture. The runtime emits one of these diagnostic messages:

- `[LottiePlugin] Vulkan native upload enabled`
- `[LottiePlugin] Vulkan native upload unavailable; using Texture2D.Apply fallback`

### Implemented ownership model

Use a **Unity-owned sampled texture with plugin-owned Vulkan staging buffers**.
This is preferable to making the plugin own the sampled `VkImage` because Unity
continues to control the texture allocation and lifetime, while
`IUnityGraphicsVulkan::AccessTexture` coordinates image access and pipeline
barriers with Unity.

The intended frame flow is:

1. Managed code creates the output `Texture2D` once and calls
   `GetNativeTexturePtr()` once during initialization.
2. Managed code registers that opaque texture handle with the native animation
   state. It does not call Apply and does not reacquire the pointer every frame.
3. Native rendering acquires one of two cacheable CPU mailbox slots and rlottie
   produces BGRA pixels there. Publication changes the slot from `Rendering`
   to `Ready` without copying its pixels.
4. The render thread claims the newest ready CPU slot and selects one of three
   safe mapped Vulkan transfer slots. It performs one sequential memory copy,
   then releases the CPU mailbox slot immediately.
5. A Unity plugin event runs on the render thread outside a render pass.
6. The Vulkan backend calls `AccessTexture()` for transfer-destination access,
   obtains Unity's current command buffer, copies the selected mapped transfer
   slot into the Unity image with `vkCmdCopyBufferToImage`, and leaves the
   resource in a state Unity can transition for shader sampling. The transfer
   slot remains unavailable until Unity's safe-frame number retires it.
7. Unity samples the same `Texture2D`; no `Texture2D.Apply()` occurs per frame.

Unity documents that `Texture.GetNativeTexturePtr()` returns the underlying
Vulkan image handle and recommends caching it because retrieving it can
synchronize with the render thread. Unity's official native rendering plugin
sample also demonstrates updating Unity textures through native pointers on
Vulkan. Relevant primary references:

- [Texture.GetNativeTexturePtr](https://docs.unity3d.com/ScriptReference/Texture.GetNativeTexturePtr.html)
- [Texture2D.CreateExternalTexture](https://docs.unity3d.com/ScriptReference/Texture2D.CreateExternalTexture.html)
- [Unity NativeRenderingPlugin sample](https://github.com/Unity-Technologies/NativeRenderingPlugin)

### Implemented components

#### Managed runtime

- Split the overloaded `_usesCPURendering` concept into independent decisions:
  CPU pixel rendering and managed `Texture2D.Apply()` upload. rlottie always
  produces CPU pixels, but Vulkan should use a native GPU upload.
- Add a Vulkan initialization path that creates a Unity-owned BGRA `Texture2D`,
  obtains its native handle once, and registers it with the plugin.
- Retain the Unity raw-data view for runtime managed-fallback continuity, but
  redirect native Vulkan rasterization into the two-slot cacheable CPU mailbox
  and route completed frames through `RequestTextureUpload()`.
- Track whether the output texture is Unity-owned or plugin-owned. Do not call
  `UpdateExternalTexture()` for a Unity-owned Vulkan texture.
- Keep the Vulkan and Linux OpenGL capability checks independent so either
  backend can return to managed upload without masking the other.
- Keep WebGL unchanged by this Vulkan work. WebGL later received its own
  Unity-owned native upload path; see "WebGL native upload: implementation
  status".

#### Native bridge and instance state

- Add a registration entry point accepting the animation, Unity texture handle,
  width, height, and expected pixel format.
- Store explicit texture ownership in `InstanceState`. Vulkan reset must release
  plugin-owned staging resources but must never destroy Unity's image.
- Preserve the opaque handle exactly as returned by Unity. Do not infer ownership
  from whether `nativeTex` is non-null.

#### Unity/Vulkan integration

- Acquire and cache `IUnityGraphicsVulkan` (with a compatible-interface fallback
  if required by the supported Unity versions) during `UnityPluginLoad`.
- Load required Vulkan device functions from Unity's
  `UnityVulkanInstance::getInstanceProcAddr`; this avoids depending on direct
  queue submission or a separately initialized Vulkan device.
- Configure one stable upload event ID with
  `kUnityVulkanRenderPass_EnsureOutside`. The current pump emits different event
  IDs for its per-frame budget even though the native callback ignores them;
  using one configured ID repeatedly is simpler and less error-prone.
- Use `AccessTexture(..., VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
  VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
  kUnityVulkanResourceAccess_PipelineBarrier, ...)` before copying.
- Request `CommandRecordingState()` after `AccessTexture()`, because the Unity
  interface documents that resource-access calls invalidate a previously
  obtained recording state.

#### Vulkan staging resources

- Keep exactly three per-instance mapped `VkBuffer`/`VkDeviceMemory` upload
  slots, independent of the two cacheable CPU render-mailbox slots.
- Select host-visible memory and prefer host-coherent memory. If coherent memory
  is unavailable, flush the written range with the required non-coherent atom
  alignment.
- Respect `ctx.stride` through `VkBufferImageCopy::bufferRowLength` or repack rows
  when Vulkan's copy requirements cannot express the incoming stride.
- Validate the `UnityVulkanImage` extent, usage flags, aspect, and format before
  recording the copy. The image must support transfer destination usage.
- Confirm BGRA format mapping in Gamma and Linear projects. If Unity exposes an
  RGBA image instead, add native channel conversion or select a matching Unity
  texture format rather than silently copying swapped channels.
- Release buffers and memory only after Unity's `safeFrameNumber` says their last
  use is complete, or retain them until device shutdown. Never wait for the
  graphics queue on every animation frame.

#### Build system

- Make Vulkan headers available on Windows, Linux, and Android. Android NDK
  builds provide them; desktop builds need a reproducible header source or SDK
  discovery.
- Compile the real backend only where Vulkan and the Unity Vulkan interface are
  available, while preserving non-Vulkan builds.
- Prefer device function pointers supplied by Unity over linking directly to a
  platform Vulkan loader.

### Principal risks

| Risk | Required mitigation |
|---|---|
| Copy recorded inside an active render pass | Configure a stable plugin event to require outside-render-pass execution |
| Unity image layout or synchronization corruption | Use `AccessTexture` and Unity's current command buffer; do not submit independently to Unity's queue |
| Texture handle changes | Cache after initialization, re-register after resize/recreation, and never fetch it per frame |
| CPU overwrites data still used by GPU | Copy from an owned CPU mailbox slot into a free upload-ring slot and retire the ring slot by fence/safe frame |
| Wrong red/blue channels or color space | Validate Unity's Vulkan format and run Gamma/Linear image comparisons |
| Android device-specific memory behavior | Handle non-coherent memory correctly and test physical ARM64 devices |
| Old Unity interface compatibility | Query supported Vulkan interface versions and retain Apply as a guarded fallback where native upload is unavailable |
| Device loss or graphics API restart | Recreate staging resources and re-register the Unity texture after device initialization |

### Implementation sequence

1. Explicit upload-path and texture-ownership state.
2. Unity-owned texture registration and cached Vulkan interfaces.
3. Mapped staging buffers and render-thread copies.
4. Multiple staging slots, lifetime locking, and safe resource retirement.
5. Linux Vulkan support, followed by Unity-owned Linux OpenGL native upload.
6. Android Vulkan support and ARM64 physical-device validation.
7. Automatic capability and runtime failure checks with Apply fallback.

### Validation matrix and acceptance criteria

Validation completed for this implementation:

- Windows x64 native plugin builds with the guarded no-header fallback.
- Linux x64 native plugin builds against the system Vulkan headers. A Unity
  2022.3 Linux player loads the plugin and both sample animations without the
  former plugin-load crash. The local software Vulkan device is reported as a
  CPU device and rejected by Unity, so Linux native Vulkan execution remains a
  GPU-runner validation item.
- Android native plugins build for `arm64-v8a`, `armeabi-v7a`, `x86`, and
  `x86_64` with Unity 2022.3's NDK.
- A Vulkan-only Unity 2022.3 Android player previously passed the
  rendered-player smoke on a Samsung SM-N975F (Mali-G76). Its log selected
  Vulkan, enabled native upload, loaded both animations, and contained no Apply
  fallback or native failure.
- Unity 6000.5.3f1 Built-in and URP players pass the rendered-player smoke on
  the same device with `NativeVulkan`. The earlier dynamic-pixel failures were
  validation false negatives caused by immediate `Graphics.Blit`/`ReadPixels`
  capture of the native-written texture; asynchronous GPU readback observes
  changing frame hashes on both pipelines.
- On 2026-08-12, Unity 6000.5.3f1 Built-in was revalidated on that Mali-G76 at
  1024 x 1024 after removing the old 4 MiB denylist. The selected backend was
  `NativeVulkan`; the sampled hash changed from `a7a119b46a06c987` to
  `835277a42a36cd08`, and every rendered-player smoke check passed.

Exercise both immediate and asynchronous rendering, multiple simultaneous
animations, resize/recreation, pause/resume, disposal with queued uploads, and
graphics-device restart.

| Platform | API | Minimum pipelines | Notes |
|---|---|---|---|
| Windows x64 | Vulkan | Built-in, URP, HDRP where supported | Run Vulkan validation layers and RenderDoc capture |
| Linux x64 | Vulkan and OpenGL Core | Built-in and URP | Exercise both native paths and force each guarded Apply fallback |
| Android ARM64 | Vulkan | Built-in and URP | Test physical devices from more than one GPU vendor |

Run the supported Unity-version range, especially 2019.4, 2021.3, and current
Unity 6 branches. Test Gamma and Linear color spaces and compare rendered output
against the existing Apply path with deterministic image hashes or toleranced
pixel comparisons.

The Vulkan-native path is complete only when:

- no per-frame `Texture2D.Apply()` or `GetNativeTexturePtr()` call occurs;
- synchronous and asynchronous animations update correctly;
- there are no Vulkan validation, Unity graphics, or lifetime errors;
- color and alpha match the current path in Gamma and Linear projects;
- resize, disposal, and device restart do not leak or use freed resources; and
- unsupported Unity/device combinations fall back to the Apply path instead of
  returning a blank texture.

The remaining acceptance work is broader vendor, color-space, lifecycle, and
desktop Vulkan coverage; the tested Unity 6000.5.3f1 Android configuration no
longer has a known sampled-texture update failure.

## WebGL native upload: implementation status

Native WebGL upload without a per-frame Apply call is implemented for WebGL
player builds. Managed code creates a Unity-owned RGBA `Texture2D` with exactly
one mip level, calls Apply once so Unity allocates the GL texture object, caches
`GetNativeTexturePtr()` as that texture's WebGL name, and registers it with the
plugin. rlottie renders into Unity's raw-data view for that texture and the
native render routine converts BGRA to RGBA in place, which is why the shader
conversion blit is disabled on this path. Each completed frame is copied once
into a per-instance upload snapshot, and the `GL.IssuePluginEvent` pump runs a
render-thread callback that uploads that snapshot with `glTexSubImage2D`. The
callback saves and restores the previous 2D texture binding and unpack
alignment, and drains already-pending GL errors first so it does not attribute
another plugin's error to this upload.

The path is capability guarded. It is attempted only when managed upload was not
requested and Unity reports the renderer as `OpenGLES2` or `OpenGLES3`, which is
how WebGL 1 and WebGL 2 identify themselves; WebGPU and any future renderer
intentionally retain the managed path. If handle registration fails, if the
native side reports the upload as unavailable, or if an upload request is
rejected, managed code retries registration once and otherwise returns to
`Texture2D.Apply()` instead of exposing a stale texture. The runtime emits one of
these diagnostic messages:

- `[LottiePlugin] Unity-owned WebGL native upload enabled`
- `[LottiePlugin] WebGL native texture upload unavailable (<reason>); using Texture2D.Apply fallback`

The reported reasons are `texture registration`, `native upload failure`, and
`upload request rejected`.

WebGL still does not acquire native CPU mailbox slots and still has no
asynchronous worker render; its future API completes synchronously and the
managed asynchronous path then publishes the completed buffer. Persistent CPU
frame memory on the native path is `2F`: Unity's retained raw-data view plus the
native upload snapshot.

Unity 6000.5 and newer build WebGL with the WebAssembly exception and longjmp
ABI, so a build pre-process step selects the matching prebuilt native archive
variant (`WasmExceptions` for Unity 6000.5 or newer, `Legacy` otherwise) and
restores the checked-in legacy pair after the build or on the next editor tick.

Two aspects are not determined by the code alone:

- The WebGL path does not call `Texture.IncrementUpdateCount()` after a native
  GPU-side write, unlike the Unity-owned Linux OpenGL path. Whether an immediate
  `Graphics.Blit` or `ReadPixels` observes a native WebGL upload without it is
  unverified; the rendered-player smoke already prefers asynchronous GPU
  readback for `NativeWebGL`, which would mask the same class of false negative
  seen earlier on Android Vulkan.
- Color correctness is reasoned from the RGBA texture format plus the native
  BGRA-to-RGBA conversion, not from a rendered comparison in Gamma and Linear
  projects.

This whole section is described from the current source. No rendered-player,
browser, or device run has yet exercised `NativeWebGL`, its managed fallback, or
the archive-variant selection, so WebGL 1 and WebGL 2 browser validation remains
open acceptance work.

## Follow-up engineering audit

### Staging-buffer ownership and the cacheable-mailbox fix

The final implementation is a bounded two-slot cacheable CPU render mailbox.
Native `lottie_render_data` temporarily redirects its surface to an acquired
CPU slot and restores the managed pointer at publication, preserving the ABI
and managed/WebGL external-buffer behavior. Publication is metadata-only.

The mailbox replaces an older `Ready` frame when possible and skips a render
when both slots are `Rendering` or `Uploading`. It never grows in response to
backpressure. D3D11, OpenGL, and Metal consume this storage immediately.
D3D12 and Vulkan copy it once, sequentially, into separate three-slot mapped
GPU upload rings. This deliberately restores the second explicit-API copy: it
is cheap compared with asking rlottie's blending rasterizer to work in uncached
or write-combined mapped memory.

No mailbox mutex is held during backend upload. Ownership prevents publication
or rendering from touching the selected CPU slot until the immediate API has
consumed it or the explicit backend has finished its CPU copy. D3D12/Vulkan GPU
lifetime belongs only to the independent upload-ring slot, not the CPU mailbox.

### Persistent frame-memory formulas

Let `F = width * height * 4`, `P = Align(width * 4, 256) * height` for one
D3D12 footprint, and `V` be one Vulkan transfer allocation (at least the copied
frame bytes, possibly larger because of Vulkan allocation requirements).
Sampled GPU texture storage, driver-private copies, mipmaps, and allocator
metadata are excluded.

| Path | Before direct rendering | Rejected direct-mapped pool | Implemented cacheable mailbox |
|---|---:|---:|---:|
| D3D11 / plugin-owned OpenGL / Metal | `2F` | `3F` | `2F` |
| D3D12 | `2F + 3P` | `3P` | `2F + 3P` |
| Unity-owned Vulkan | `2F + 3V` | `F + 3V` | `3F + 3V` |
| Linux Unity-owned OpenGL | `2F` plus optional conversion scratch | `4F` plus optional conversion scratch | `3F` plus optional conversion scratch |
| Android OpenGL ES | `2F` | `3F` | `4F` (`F` Unity raw view + `2F` mailbox + `F` RGBA scratch) |
| Managed Apply / WebGL managed CPU frame | `F` | `F` | `F` |
| Unity-owned WebGL (native upload) | not implemented then | not implemented then | `2F` (`F` Unity raw view + `F` upload snapshot) |

The Unity-owned Vulkan formulas include the readable Unity raw-data view kept
for live fallback continuity. The rejected direct-mapped column therefore has
`F + 3V`, not merely `3V`. If a platform can later recreate the texture when
falling back, that retained `F` could be removed independently of this design.

### Related correctness work

The pool change includes these adjacent fixes:

- close the upload-queue lost-wakeup window by rechecking for a newer ready
  version after an upload clears its queued flag and requeueing when necessary;
- keep version comparisons and state transitions synchronized so a retry never
  advances `uploadedVersion` before a backend has accepted the frame;
- protect publication, queued work, rendering, and removal with explicit
  instance lifetime ownership;
- D3D12 mapped-region reuse is gated by Unity's frame fence, and Vulkan mapped
  buffer reuse is gated by `safeFrameNumber`;
- plugin-owned native texture paths no longer allocate a managed frame buffer.
  Unity-owned OpenGL/Vulkan paths retain their raw-data view only for live
  fallback continuity, but native rendering redirects away from it.

### Validation of the final architecture

- Windows Unity 2022.3 rendered-player smoke passed all 14 checks on D3D11,
  D3D12, OpenGL Core, and real native Vulkan. Both controls used native upload.
- Sustained uncapped Windows runs had post-warmup median FPS of 3065.7 (D3D11),
  1515.3 (D3D12), 965.3 (OpenGL Core), and 9155.8 (Vulkan). These are stall
  indicators from a hidden uncapped player, not display-refresh benchmarks.
- Android Vulkan on the Samsung SM-N975F passed all 14 checks at a sustained
  29-30 FPS (about 33.4-34.5 ms), with no mailbox or upload-ring exhaustion.
- Unity 2022.3 PlayMode tests passed 8/8.
- OpenGL Core's one startup `GL_INVALID_ENUM` and first-button readback anomaly
  reproduce with the pre-change native binary and are not mailbox regressions.

### Additional improvements and validation

- Evaluate `Texture.IncrementUpdateCount()` after Vulkan GPU-side writes, as is
  already done for Unity-owned OpenGL textures. Validate immediate blits and
  asynchronous readback before making it unconditional.
- WebGL upload through a render-thread `glTexSubImage2D` path is implemented and
  is no longer a proposal; `Apply()` on WebGL is now a guarded fallback rather
  than the only path. WebGL 1 and WebGL 2 browser validation of that path is
  still outstanding, so it remains code-reviewed only.
- Add native mailbox stress tests covering concurrent publish/upload, newest-
  frame coalescing, queue saturation, disposal during queued work, resize, and
  graphics-device restart. Run them with native sanitizers where possible.
- Expand rendered-player coverage to WebGL browsers, Apple Metal, multiple
  Android Vulkan GPU vendors, Gamma and Linear color spaces, and the supported
  Unity 2019.4, 2021.3, and Unity 6 branches.

The Windows player produced by the normal CI matrix now has a mandatory hosted
rendered-player smoke job which rejects managed upload for both `AnimatedImage`
and `AnimatedButton`. The runtime tests also explicitly exercise the managed
fallback and verify that it produces visible pixels.

# Texture upload paths and Vulkan native-upload analysis

## Scope and snapshot

This document describes the implementation inspected on 2026-08-10 at commit
`4dc22680668674a935d1902aeb0484362dff50b3` (`4dc2268`, "Rebuild Android
plugins with Unity exports"). At that snapshot:

- `origin/codex/remove-texture.apply-and-move-updates-to-native` and
  `origin/main` resolve to the same commit;
- therefore `main` contains every committed change reachable from that feature
  branch at this snapshot; and
- this is a point-in-time statement. The branches can diverge after either one
  receives another commit.

In this document, **Apply** specifically means a call to `Texture2D.Apply()`.
The synchronous and asynchronous draw paths make the same upload choice.

## Current platform and graphics API matrix

| Platform | Graphics API or output mode | Apply used? | Current upload path |
|---|---|---:|---|
| Windows | Direct3D 11 | No | Native external texture and render-thread upload |
| Windows | Direct3D 12 | No | Native external texture and render-thread upload |
| Windows | OpenGL Core | No | Native external GL texture and render-thread upload |
| Windows | Vulkan | Yes | CPU-backed Unity `Texture2D`, followed by `Texture.Apply()` |
| Linux Editor or Standalone | OpenGL Core | Yes | Linux is explicitly forced to the managed CPU-texture path |
| Linux Editor or Standalone | Vulkan | Yes | Linux is explicitly forced to the managed CPU-texture path |
| macOS | Metal | No | Native external Metal texture and native upload |
| macOS | OpenGL Core | Unsupported | The Apple native build implements Metal only; native texture creation fails instead of falling back to Apply |
| iOS | Metal | No | Native external Metal texture and native upload |
| Android | OpenGL ES 2 or 3 | No | Native external GL texture and render-thread upload |
| Android | Vulkan | Yes | CPU-backed Unity `Texture2D`, followed by `Texture.Apply()` |
| WebGL | Shader conversion (default) | Yes | Apply to the source `Texture2D`, then blit into the exposed `RenderTexture` |
| WebGL | Native conversion | Yes | Apply converted pixels to the exposed `Texture2D` |
| WebGL | Shader unavailable | Yes | Automatic fallback to the native-conversion path |

The Built-in, URP, and HDRP render pipelines do not select the upload path.
The active graphics API and platform compile symbols select it. Consequently:

- Windows Built-in, URP, and HDRP use Apply only when the active API is Vulkan;
- macOS Built-in, URP, and HDRP do not use Apply when running Metal;
- Android Built-in and URP use Apply on Vulkan but not OpenGL ES;
- iOS Built-in and URP do not use Apply on Metal; and
- WebGL Built-in and URP always use Apply. HDRP is not a targeted WebGL,
  Android, or iOS configuration in the project matrix.

The relevant managed decisions in the analyzed commit are:

1. WebGL player builds force `_usesCPURendering = true`.
2. All Vulkan devices set `_usesCPURendering = true`.
3. Linux Editor and Linux Standalone builds overwrite the decision and force
   `_usesCPURendering = true` for every graphics API.
4. CPU-rendering branches call Apply after a synchronous result or a completed
   asynchronous result.
5. Other branches call `RequestTextureUpload()`, which queues a native upload
   consumed by `GL.IssuePluginEvent` on Unity's render thread.

In WebGL shader mode, Apply is called on the intermediate source `Texture2D`,
not on the exposed output `RenderTexture`.

## Vulkan without Apply: feasibility result

Native Vulkan upload without a per-frame Apply call is feasible, but the
current Vulkan backend is not an implementation of it. `VulkanBackend.cpp` is a
stub: it does not acquire `IUnityGraphicsVulkan`, create upload buffers, access a
Unity texture, or record a copy. Its log messages say that Unity performs the
upload, while the managed code actually performs that upload through Apply.

Simply changing `_usesCPURendering` to `false` is unsafe. The current native
Vulkan `EnsureTextureVulkan()` reports success without setting a valid native
texture pointer, and `UploadVulkan()` deliberately performs no work. That change
would produce a null or stale texture and no visible frame.

### Recommended ownership model

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
3. rlottie continues rendering BGRA pixels into the existing native/managed
   CPU buffer.
4. `PublishUpload()` copies the completed frame into the instance staging data
   and queues an upload, as it already does for other native backends.
5. A Unity plugin event runs on the render thread outside a render pass.
6. The Vulkan backend calls `AccessTexture()` for transfer-destination access,
   obtains Unity's current command buffer, copies a mapped staging buffer into
   the Unity image with `vkCmdCopyBufferToImage`, and leaves the resource in a
   state Unity can transition for shader sampling.
7. Unity samples the same `Texture2D`; no `Texture2D.Apply()` occurs per frame.

Unity documents that `Texture.GetNativeTexturePtr()` returns the underlying
Vulkan image handle and recommends caching it because retrieving it can
synchronize with the render thread. Unity's official native rendering plugin
sample also demonstrates updating Unity textures through native pointers on
Vulkan. Relevant primary references:

- [Texture.GetNativeTexturePtr](https://docs.unity3d.com/ScriptReference/Texture.GetNativeTexturePtr.html)
- [Texture2D.CreateExternalTexture](https://docs.unity3d.com/ScriptReference/Texture2D.CreateExternalTexture.html)
- [Unity NativeRenderingPlugin sample](https://github.com/Unity-Technologies/NativeRenderingPlugin)

### Required code changes

#### Managed runtime

- Split the overloaded `_usesCPURendering` concept into independent decisions:
  CPU pixel rendering and managed `Texture2D.Apply()` upload. rlottie always
  produces CPU pixels, but Vulkan should use a native GPU upload.
- Add a Vulkan initialization path that creates a Unity-owned BGRA `Texture2D`,
  obtains its native handle once, and registers it with the plugin.
- Keep the existing render buffer allocation so rlottie has writable CPU
  memory, but route completed Vulkan frames through `RequestTextureUpload()`.
- Track whether the output texture is Unity-owned or plugin-owned. Do not call
  `UpdateExternalTexture()` for a Unity-owned Vulkan texture.
- Change the Linux override so it forces Apply only for the Linux OpenGL path;
  otherwise it would continue to mask the new Linux Vulkan path.
- Keep WebGL unchanged because it cannot use this native plugin path.

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

- Add per-instance mapped `VkBuffer`/`VkDeviceMemory` upload slots. At least
  double buffering is required; three slots match the existing D3D12 strategy
  and reduce reuse hazards.
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
| CPU overwrites staging data still used by GPU | Use multiple upload slots and frame-safe retirement |
| Wrong red/blue channels or color space | Validate Unity's Vulkan format and run Gamma/Linear image comparisons |
| Android device-specific memory behavior | Handle non-coherent memory correctly and test physical ARM64 devices |
| Old Unity interface compatibility | Query supported Vulkan interface versions and retain Apply as a guarded fallback where native upload is unavailable |
| Device loss or graphics API restart | Recreate staging resources and re-register the Unity texture after device initialization |

### Suggested implementation sequence

1. Introduce explicit upload-path and texture-ownership state without changing
   existing behavior.
2. Add Unity-owned texture registration and cache the Vulkan interface.
3. Implement a single mapped staging buffer and render-thread copy for desktop
   Vulkan.
4. Add multiple staging slots and safe resource retirement.
5. Enable Linux Vulkan without removing the Linux OpenGL Apply fallback.
6. Enable Android Vulkan after ARM64 device validation.
7. Add an automatic capability check. Fall back to Apply, with a warning, when
   the Vulkan interface, transfer-destination texture usage, or required device
   functions are unavailable.

### Validation matrix and acceptance criteria

Exercise both immediate and asynchronous rendering, multiple simultaneous
animations, resize/recreation, pause/resume, disposal with queued uploads, and
graphics-device restart.

| Platform | API | Minimum pipelines | Notes |
|---|---|---|---|
| Windows x64 | Vulkan | Built-in, URP, HDRP where supported | Run Vulkan validation layers and RenderDoc capture |
| Linux x64 | Vulkan | Built-in and URP | Prove the Linux OpenGL fallback is unchanged |
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

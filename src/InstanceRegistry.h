#ifndef INSTANCE_REGISTRY_H
#define INSTANCE_REGISTRY_H

#include "LottiePlugin.h"
#include "RendererCommon.h"
#include <atomic>
#include <array>
#include <condition_variable>
#include <cstdint>
#include <mutex>
#include <vector>

#if !defined(__EMSCRIPTEN__)

// Forward declarations for platform-specific types
#if defined(_WIN32)
struct ID3D12Resource;
struct ID3D12Fence;
struct ID3D11Texture2D;
struct D3D12_PLACED_SUBRESOURCE_FOOTPRINT;
#endif

// Per-animation instance state for texture management
struct InstanceState
{
    enum class SlotOwner : uint8_t
    {
        Free,
        Rendering,
        Ready,
        Uploading,
    };

    struct RenderSlot
    {
        SlotOwner owner = SlotOwner::Free;
        std::vector<uint8_t> storage;
        uint8_t* data = nullptr;
        uint32_t width = 0;
        uint32_t height = 0;
        uint32_t stride = 0;
        uint64_t version = 0;
        uint64_t gpuUseToken = 0;
    };

    static constexpr int kRenderSlotCount = 3;
    std::mutex renderPoolMutex;
    std::condition_variable renderPoolChanged;
    std::array<RenderSlot, kRenderSlotCount> renderSlots{};
    std::mutex renderLifetimeMutex;
    std::condition_variable renderLifetimeChanged;
    unsigned int activeRenders = 0;
    std::mutex uploadMutex;
    std::mutex lifetimeMutex;
    UploadContext uploadCtx{};
    std::atomic<uint64_t> uploadVersion{0};
    std::atomic<uint64_t> requestedVersion{0};
    std::atomic<uint64_t> uploadedVersion{0};
    std::atomic<bool> uploadQueued{false};
    int texW = 0;
    int texH = 0;
    void* nativeTex = nullptr;
    bool preferSRGBSampling = false;

    // Platform-specific texture data
    struct D3D12Data
    {
#if defined(_WIN32)
        ID3D12Resource* tex = nullptr;
        ID3D12Resource* upload = nullptr;
        void* uploadMapped = nullptr;
        unsigned int uploadSlotCount = 3;
        unsigned long long uploadSlotBytes = 0;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT* footprint = nullptr;
        int texState = 0; // D3D12_RESOURCE_STATE_COMMON
        ID3D12Fence* frameFence = nullptr;
#endif
    } d3d12;

    struct D3D11Data
    {
#if defined(_WIN32)
        ID3D11Texture2D* tex = nullptr;
#endif
    } d3d11;

    struct GLData
    {
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
        unsigned int glTex = 0;
        std::vector<uint8_t> rgbaScratch;
        bool unityOwnedTexture = false;
        std::atomic<bool> uploadAvailable{false};
#endif
    } gl;

    struct MetalData
    {
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        void* metalTex = nullptr; // id<MTLTexture> stored as void* to avoid ObjC in header
#endif
    } metal;

    struct VulkanData
    {
        void* backend = nullptr;
        bool unityOwnedTexture = false;
        std::atomic<bool> uploadAvailable{false};
    } vulkan;
};

// Redirect a render into an owned native-upload slot. Returns false for managed
// Apply/WebGL-style external buffers, which remain owned by the caller.
enum class RenderSlotAcquireResult
{
    ExternalBuffer,
    Acquired,
    NativeBackpressure,
};

RenderSlotAcquireResult AcquireRenderSlot(
    lottie_animation_wrapper* animation,
    lottie_render_data* renderData,
    bool waitForSlot);

// Publish a completed slot without copying its pixels.
void PublishRenderSlot(
    lottie_animation_wrapper* animation,
    lottie_render_data* renderData);
void CancelRenderSlot(lottie_render_data* renderData);

// Select the newest ready slot and transfer it to the upload thread.
bool AcquireNewestReadySlot(InstanceState* state, UploadContext& context, uint64_t& version);

// Return a slot after CPU consumption, or after a backend completion signal.
void ReleaseUploadSlot(InstanceState* state, int slotIndex);

// Wait until no rlottie Surface can still reference pool storage.
void WaitForActiveRenders(InstanceState* state);


// Get the instance state for an animation, optionally creating it if it doesn't exist
InstanceState* GetState(lottie_animation_wrapper* animation, bool create = true);

// Acquire an instance and its upload lock atomically with respect to removal.
// The returned lock keeps the state alive until the render-thread upload ends.
bool LockStateForUpload(
    lottie_animation_wrapper* animation,
    InstanceState*& state,
    std::unique_lock<std::mutex>& lifetimeLock,
    bool create = false);

// Reset texture state for an instance (releases GPU resources)
void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state, bool lifetimeAlreadyLocked = false);

// Remove an instance from the registry
void RemoveInstance(lottie_animation_wrapper* animation);

// Clear all instances on shutdown
void ClearAllInstances();

#endif // !defined(__EMSCRIPTEN__)

#endif // INSTANCE_REGISTRY_H

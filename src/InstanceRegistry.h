#ifndef INSTANCE_REGISTRY_H
#define INSTANCE_REGISTRY_H

#include "LottiePlugin.h"
#include "RendererCommon.h"
#include <atomic>
#include <cstdint>
#include <mutex>
#include <vector>

#if !defined(__EMSCRIPTEN__)

// Forward declarations for platform-specific types
#if defined(_WIN32)
struct ID3D12Resource;
struct ID3D11Texture2D;
struct D3D12_PLACED_SUBRESOURCE_FOOTPRINT;
#endif

// Per-animation instance state for texture management
struct InstanceState
{
    std::mutex uploadMutex;
    UploadContext uploadCtx{};
    std::vector<uint8_t> stagingBuffer;  // CPU-side copy of pixel data to decouple rlottie render thread from GPU upload
    std::atomic<uint64_t> uploadVersion{0};
    std::atomic<uint64_t> requestedVersion{0};
    uint64_t uploadedVersion = 0;
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
        unsigned int uploadWriteIdx = 0;
        unsigned long long uploadSlotBytes = 0;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT* footprint = nullptr;
        int texState = 0; // D3D12_RESOURCE_STATE_COMMON
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
#endif
    } gl;

    struct MetalData
    {
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        void* metalTex = nullptr; // id<MTLTexture> stored as void* to avoid ObjC in header
#endif
    } metal;
};

// Get the instance state for an animation, optionally creating it if it doesn't exist
InstanceState* GetState(lottie_animation_wrapper* animation, bool create = true);

// Reset texture state for an instance (releases GPU resources)
void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state);

// Remove an instance from the registry
void RemoveInstance(lottie_animation_wrapper* animation);

// Clear all instances on shutdown
void ClearAllInstances();

#endif // !defined(__EMSCRIPTEN__)

#endif // INSTANCE_REGISTRY_H

#pragma once

#include "LottiePlugin.h"
#include "UploadQueue.h"
#include "RendererCommon.h"

#include <atomic>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>

#if defined(_WIN32)
#include <d3d11.h>
#include <d3d12.h>
#endif
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#import <Metal/Metal.h>
#endif

struct InstanceState
{
    std::mutex uploadMutex;
    UploadContext uploadCtx{};
    std::atomic<uint64_t> uploadVersion{0};
    std::atomic<uint64_t> requestedVersion{0};
    uint64_t uploadedVersion = 0;
    std::atomic<bool> uploadQueued{false};

    int texW = 0;
    int texH = 0;
    void* nativeTex = nullptr;

#if defined(_WIN32)
    struct D3D12Data
    {
        ID3D12Resource* tex = nullptr;

        ID3D12Resource* upload = nullptr;
        void* uploadMapped = nullptr;
        UINT uploadSlotCount = 3;
        UINT uploadWriteIdx = 0;
        UINT64 uploadSlotBytes = 0;
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};
        D3D12_RESOURCE_STATES texState = D3D12_RESOURCE_STATE_COMMON;
    } d3d12;

    struct D3D11Data
    {
        ID3D11Texture2D* tex = nullptr;
    } d3d11;
#endif

#if !defined(__EMSCRIPTEN__)
    struct GLData
    {
        uint32_t glTex = 0;
        std::vector<uint8_t> rgbaScratch;
    } gl;
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    struct MetalData
    {
        id<MTLTexture> metalTex = nil;
    } metal;
#endif
};

#if !defined(__EMSCRIPTEN__)
extern std::mutex gInstancesMutex;
extern std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> gInstances;

InstanceState* GetState(lottie_animation_wrapper* animation, bool create = true);
void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state);
void ClearInstances();
#endif

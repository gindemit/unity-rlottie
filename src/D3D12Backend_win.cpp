#if defined(_WIN32)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")

#include "D3D12Backend_win.h"
#include "D3D12Texture_win.h"
#include "ImageBufferUtils.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include <cstdio>
#include <utility>

#include "IUnityInterface.h"
#include "IUnityGraphicsD3D12.h"

namespace
{
    ID3D12Device* gD3D12Device = nullptr;
    IUnityGraphicsD3D12v8* sD3D12v8 = nullptr;
}

void SetD3D12Device(ID3D12Device* device)
{
    gD3D12Device = device;
}

void SetD3D12Interface(IUnityGraphicsD3D12v8* v8)
{
    sD3D12v8 = v8;
}

void ConfigureD3D12UploadEvent()
{
    UnityD3D12PluginEventConfig config{};
    config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
    config.flags = kUnityD3D12EventConfigFlag_SyncWorkerThreads |
        kUnityD3D12EventConfigFlag_ModifiesCommandBuffersState |
        kUnityD3D12EventConfigFlag_EnsurePreviousFrameSubmission;
    config.ensureActiveRenderTextureIsBound = false;
    if (sD3D12v8 != nullptr) sD3D12v8->ConfigureEvent(1, &config);
}

void ResetTextureD3D12(InstanceState* state)
{
    D3D12Texture::Retire(std::exchange(state->d3d12, nullptr));
}

bool EnsureTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    if (sD3D12v8 == nullptr)
    {
        // Older interfaces cannot synchronize Unity's external-texture state
        // tracker with commands recorded into the active list. Prefer the
        // managed BGRA path to stale-state barriers or unsafe private queues.
        LottieLogWarning(animation, "[Lottie] D3D12 v8 interface unavailable; using managed texture upload");
        return false;
    }
    if (!gD3D12Device)
    {
        LottieLogError(animation, "[Lottie] D3D12 device is null");
        return false;
    }

    const bool useSrgb = state->preferSRGBSampling;
    D3D12_RESOURCE_DESC texDesc{};
    texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Alignment = 0;
    texDesc.Width = static_cast<UINT64>(width);
    texDesc.Height = static_cast<UINT>(height);
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels = 1;
    texDesc.Format = useSrgb ? DXGI_FORMAT_B8G8R8A8_UNORM_SRGB : DXGI_FORMAT_B8G8R8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.SampleDesc.Quality = 0;
    texDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    texDesc.Flags = D3D12_RESOURCE_FLAG_NONE;

    D3D12_HEAP_PROPERTIES heapDefault{ D3D12_HEAP_TYPE_DEFAULT };
    heapDefault.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
    heapDefault.MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN;
    heapDefault.CreationNodeMask = 1;
    heapDefault.VisibleNodeMask = 1;

    auto texture = D3D12Texture::Create();
    if (!texture)
        return false;
    HRESULT hr = gD3D12Device->CreateCommittedResource(
        &heapDefault, D3D12_HEAP_FLAG_NONE, &texDesc,
        D3D12_RESOURCE_STATE_COMMON, nullptr,
        IID_PPV_ARGS(texture->tex.GetAddressOf()));
    if (FAILED(hr))
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to create D3D12 texture resource. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        return false;
    }

    UINT64 totalBytes = 0;
    gD3D12Device->GetCopyableFootprints(&texDesc, 0, 1, 0, &texture->footprint, nullptr, nullptr, &totalBytes);

    // GetCopyableFootprints returns the space required by a single texture.
    // Each copy in the ring needs a separately aligned placement offset.
    constexpr UINT64 alignment = D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT;
    constexpr UINT64 slotCount = D3D12Texture::kUploadSlotCount;
    constexpr UINT64 maxSlotBytes = (UINT64_MAX / slotCount) & ~(alignment - 1);
    if (totalBytes == 0 || totalBytes > maxSlotBytes)
    {
        return false;
    }
    const UINT64 slotBytes = (totalBytes + alignment - 1) & ~(alignment - 1);
    const UINT64 uploadBytes = slotBytes * slotCount;

    D3D12_HEAP_PROPERTIES heapUpload{ D3D12_HEAP_TYPE_UPLOAD };
    heapUpload.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
    heapUpload.MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN;
    heapUpload.CreationNodeMask = 1;
    heapUpload.VisibleNodeMask = 1;

    D3D12_RESOURCE_DESC uploadDesc{};
    uploadDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    uploadDesc.Alignment = 0;
    uploadDesc.Width = uploadBytes;
    uploadDesc.Height = 1;
    uploadDesc.DepthOrArraySize = 1;
    uploadDesc.MipLevels = 1;
    uploadDesc.Format = DXGI_FORMAT_UNKNOWN;
    uploadDesc.SampleDesc.Count = 1;
    uploadDesc.SampleDesc.Quality = 0;
    uploadDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    uploadDesc.Flags = D3D12_RESOURCE_FLAG_NONE;

    hr = gD3D12Device->CreateCommittedResource(
        &heapUpload, D3D12_HEAP_FLAG_NONE, &uploadDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
        IID_PPV_ARGS(texture->upload.GetAddressOf()));
    if (FAILED(hr))
    {
        char errorMsg[256];
        snprintf(
            errorMsg, sizeof(errorMsg),
            "[Lottie] Failed to create D3D12 upload buffer. HRESULT: 0x%08X, uploadBytes: %llu",
            hr, static_cast<unsigned long long>(uploadBytes));
        LottieLogError(animation, errorMsg);

        return false;
    }

    D3D12_RANGE noCpuReads{};
    hr = texture->upload->Map(0, &noCpuReads, &texture->uploadMapped);
    if (FAILED(hr))
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to map D3D12 upload buffer. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        return false;
    }

    texture->uploadSlotBytes = slotBytes;
    state->nativeTex = texture->tex.Get();
    state->d3d12 = texture.release();
    state->texW = width;
    state->texH = height;
    LottieLogInfo(
        animation,
        "[Lottie] D3D12 texture created successfully (format=%s)",
        useSrgb ? "B8G8R8A8_UNORM_SRGB" : "B8G8R8A8_UNORM");
    return true;
}

bool PrepareRenderSlotD3D12(
    InstanceState* state,
    int slotIndex,
    uint32_t width,
    uint32_t height,
    uint8_t*& data,
    uint32_t& stride)
{
    if (state == nullptr || slotIndex < 0 || slotIndex >= InstanceState::kRenderSlotCount ||
        state->d3d12 == nullptr ||
        state->texW != static_cast<int>(width) || state->texH != static_cast<int>(height))
    {
        return false;
    }
    // rlottie performs read-modify-write blending and must rasterize into normal
    // cacheable CPU memory. Upload-heap memory is write-combined on D3D12 and is
    // prohibitively slow for that access pattern. Leaving data null asks the
    // common mailbox to provide its cacheable vector.
    data = nullptr;
    stride = width * 4;
    return true;
}

UploadResult UploadD3D12(InstanceState* state, const UploadContext& ctx)
{
    if (!state || !state->d3d12 || !ctx.data || !sD3D12v8)
        return UploadResult::Failed;

    auto& texture = *state->d3d12;
    ID3D12Fence* frameFence = sD3D12v8->GetFrameFence();
    if (!frameFence)
        return UploadResult::Retry;
    if (texture.frameFence && frameFence != texture.frameFence.Get())
    {
        // Retire the old timeline before comparing ring slots with a new one.
        const UINT64 previousCompleted = texture.frameFence->GetCompletedValue();
        if (previousCompleted == UINT64_MAX)
            return UploadResult::Failed;
        if (previousCompleted < texture.LastUploadFenceValue())
            return UploadResult::Retry;
        texture.uploadSlotFenceValues.fill(0);
        texture.frameFence.Reset();
    }
    const UINT64 completedFence = frameFence->GetCompletedValue();
    if (completedFence == UINT64_MAX)
        return UploadResult::Failed;

    unsigned int uploadSlot = D3D12Texture::kUploadSlotCount;
    for (unsigned int offset = 0; offset < D3D12Texture::kUploadSlotCount; ++offset)
    {
        const unsigned int candidate =
            (texture.nextUploadSlot + offset) % D3D12Texture::kUploadSlotCount;
        const UINT64 useFence = texture.uploadSlotFenceValues[candidate];
        if (useFence <= completedFence)
        {
            uploadSlot = candidate;
            break;
        }
    }
    if (uploadSlot == D3D12Texture::kUploadSlotCount)
    {
        return UploadResult::Retry;
    }

    const UINT64 fenceValue = sD3D12v8->GetNextFrameFenceValue();
    if (fenceValue <= completedFence || fenceValue == UINT64_MAX)
    {
        return UploadResult::Retry;
    }

    // Retry must not modify mapped upload memory or Unity's resource-state
    // tracker. Validate the active recording context before either operation.
    UnityGraphicsD3D12RecordingState recording{};
    if (!sD3D12v8->CommandRecordingState(&recording) || !recording.commandList)
    {
        return UploadResult::Retry;
    }

    ID3D12GraphicsCommandList* cmd = recording.commandList;
    uint8_t* uploadData = reinterpret_cast<uint8_t*>(texture.uploadMapped)
        + texture.uploadSlotBytes * static_cast<UINT64>(uploadSlot)
        + texture.footprint.Offset;
    const UINT uploadStride = texture.footprint.Footprint.RowPitch;
    if (!CopyImageRows(
            uploadData,
            uploadStride,
            ctx.data,
            ctx.stride,
            ctx.width,
            ctx.height))
    {
        return UploadResult::Failed;
    }
    // Unity may have sampled the external texture since our last upload.
    sD3D12v8->RequestResourceState(texture.tex.Get(), D3D12_RESOURCE_STATE_COPY_DEST);

    D3D12_TEXTURE_COPY_LOCATION dst{};
    dst.pResource = texture.tex.Get();
    dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dst.SubresourceIndex = 0;

    D3D12_TEXTURE_COPY_LOCATION src{};
    src.pResource = texture.upload.Get();
    src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    src.PlacedFootprint = texture.footprint;
    src.PlacedFootprint.Offset += texture.uploadSlotBytes * static_cast<UINT64>(uploadSlot);

    cmd->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);

    D3D12_RESOURCE_BARRIER toSRV{};
    toSRV.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    toSRV.Transition.pResource = texture.tex.Get();
    toSRV.Transition.Subresource = 0;
    toSRV.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    toSRV.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    cmd->ResourceBarrier(1, &toSRV);

    sD3D12v8->NotifyResourceState(texture.tex.Get(), D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, false);

    if (!texture.frameFence)
        texture.frameFence = frameFence;
    texture.uploadSlotFenceValues[uploadSlot] = fenceValue;
    texture.nextUploadSlot = (uploadSlot + 1) % D3D12Texture::kUploadSlotCount;
    return UploadResult::Submitted;
}

#endif // defined(_WIN32)

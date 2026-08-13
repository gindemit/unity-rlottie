#if defined(_WIN32)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")

#include "D3D12Backend_win.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include <algorithm>
#include <cstdio>
#include <cstring>

#include "IUnityInterface.h"
#include "IUnityGraphicsD3D12.h"

namespace
{
    ID3D12Device* gD3D12Device = nullptr;
    ID3D12CommandQueue* gD3D12Queue = nullptr;
    
    IUnityGraphicsD3D12v8* sD3D12v8 = nullptr;
    IUnityGraphicsD3D12v7* sD3D12 = nullptr;
    IUnityGraphicsD3D12v6* sD3D12v6 = nullptr;
    IUnityGraphicsD3D12v5* sD3D12v5 = nullptr;
    
    ID3D12CommandAllocator* sD3D12Allocator = nullptr;
    ID3D12GraphicsCommandList* sD3D12CmdList = nullptr;

    ID3D12Fence* GetUnityFrameFence();
    UINT64 GetUnityNextFrameFenceValue();
}

void SetD3D12Device(ID3D12Device* device)
{
    gD3D12Device = device;
}

ID3D12Device* GetD3D12Device()
{
    return gD3D12Device;
}

void SetD3D12Queue(ID3D12CommandQueue* queue)
{
    gD3D12Queue = queue;
}

ID3D12CommandQueue* GetD3D12Queue()
{
    return gD3D12Queue;
}

void SetD3D12Interfaces(IUnityGraphicsD3D12v8* v8, IUnityGraphicsD3D12v7* v7,
                         IUnityGraphicsD3D12v6* v6, IUnityGraphicsD3D12v5* v5)
{
    sD3D12v8 = v8;
    sD3D12 = v7;
    sD3D12v6 = v6;
    sD3D12v5 = v5;
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
    else if (sD3D12 != nullptr) sD3D12->ConfigureEvent(1, &config);
    else if (sD3D12v6 != nullptr) sD3D12v6->ConfigureEvent(1, &config);
}

void ReleaseOwnedD3D12CommandList()
{
    if (sD3D12CmdList != nullptr)
    {
        sD3D12CmdList->Release();
        sD3D12CmdList = nullptr;
    }

    if (sD3D12Allocator != nullptr)
    {
        sD3D12Allocator->Release();
        sD3D12Allocator = nullptr;
    }
}

ID3D12GraphicsCommandList* AcquireUnityD3D12CommandList()
{
    if (sD3D12v8 != nullptr)
    {
        UnityGraphicsD3D12RecordingState recordingState{};
        if (sD3D12v8->CommandRecordingState(&recordingState))
        {
            return recordingState.commandList;
        }
    }

    if (sD3D12 != nullptr)
    {
        UnityGraphicsD3D12RecordingState recordingState{};
        if (sD3D12->CommandRecordingState(&recordingState))
        {
            return recordingState.commandList;
        }
    }

    if (sD3D12v6 != nullptr)
    {
        UnityGraphicsD3D12RecordingState recordingState{};
        if (sD3D12v6->CommandRecordingState(&recordingState))
        {
            return recordingState.commandList;
        }
    }

    return nullptr;
}

D3D12CommandContext AcquireD3D12CommandContext()
{
    D3D12CommandContext ctx{};
    ctx.cmd = AcquireUnityD3D12CommandList();
    // Never reset/reuse a plugin-owned allocator here: there is no completion
    // fence proving Unity has finished executing it. Plugin events must record
    // into Unity's active command list or retry on a later event.
    return ctx;
}

void SubmitD3D12CommandContext(const D3D12CommandContext& ctx)
{
    if (!ctx.ownsCommandList || ctx.cmd == nullptr || gD3D12Queue == nullptr)
    {
        return;
    }

    ctx.cmd->Close();
    ID3D12CommandList* lists[] = { ctx.cmd };
    gD3D12Queue->ExecuteCommandLists(1, lists);
}

void NotifyD3D12ResourceState(void* resource, int state, bool isAfterState)
{
    if (sD3D12v8 != nullptr && resource != nullptr)
    {
        sD3D12v8->NotifyResourceState(reinterpret_cast<ID3D12Resource*>(resource),
                                       static_cast<D3D12_RESOURCE_STATES>(state), isAfterState);
    }
}

void ResetTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    if (state->d3d12.upload)
    {
        bool safeToRelease = true;
        UINT64 lastUse = 0;
        for (const unsigned long long fenceValue : state->d3d12.uploadSlotFenceValues)
        {
            lastUse = (std::max)(lastUse, static_cast<UINT64>(fenceValue));
        }
        ID3D12Fence* frameFence = state->d3d12.frameFence;
        if (frameFence != nullptr && lastUse != 0 && frameFence->GetCompletedValue() < lastUse)
        {
            HANDLE completionEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
            if (completionEvent != nullptr)
            {
                if (SUCCEEDED(frameFence->SetEventOnCompletion(lastUse, completionEvent)))
                {
                    safeToRelease = WaitForSingleObject(completionEvent, 2000) == WAIT_OBJECT_0;
                }
                CloseHandle(completionEvent);
            }
        }
        if (!safeToRelease)
        {
            // Device loss can leave Unity's frame fence permanently unsignaled.
            // Leaking these device-owned objects is preferable to either hanging
            // the scripting thread or freeing memory still referenced by a GPU.
            LottieLogWarning(animation, "[Lottie] D3D12 fence timeout during reset; retaining GPU resources");
            state->d3d12.upload = nullptr;
            state->d3d12.uploadMapped = nullptr;
            state->d3d12.tex = nullptr;
            state->d3d12.frameFence = nullptr;
            return;
        }
        if (state->d3d12.uploadMapped)
        {
            state->d3d12.upload->Unmap(0, nullptr);
            state->d3d12.uploadMapped = nullptr;
        }
        state->d3d12.upload->Release();
        state->d3d12.upload = nullptr;
    }
    if (state->d3d12.frameFence != nullptr)
    {
        state->d3d12.frameFence->Release();
        state->d3d12.frameFence = nullptr;
    }
    if (state->d3d12.tex)
    {
        state->d3d12.tex->Release();
        state->d3d12.tex = nullptr;
    }
    if (state->d3d12.footprint)
    {
        delete state->d3d12.footprint;
        state->d3d12.footprint = nullptr;
    }
    state->d3d12.uploadSlotBytes = 0;
    state->d3d12.uploadSlotFenceValues.fill(0);
    state->d3d12.nextUploadSlot = 0;
    state->d3d12.texState = D3D12_RESOURCE_STATE_COMMON;
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

    ID3D12Resource* texture = nullptr;
    HRESULT hr = gD3D12Device->CreateCommittedResource(
        &heapDefault, D3D12_HEAP_FLAG_NONE, &texDesc,
        D3D12_RESOURCE_STATE_COMMON, nullptr,
        IID_PPV_ARGS(&texture));
    if (FAILED(hr) || !texture)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to create D3D12 texture resource. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        if (texture)
        {
            texture->Release();
        }
        return false;
    }

    // Create footprint
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT* footprint = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT{};
    UINT64 totalBytes = 0;
    gD3D12Device->GetCopyableFootprints(&texDesc, 0, 1, 0, footprint, nullptr, nullptr, &totalBytes);

    state->d3d12.uploadSlotBytes = totalBytes;
    const UINT64 uploadBytes = totalBytes * InstanceState::D3D12Data::kUploadSlotCount;

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

    ID3D12Resource* upload = nullptr;
    hr = gD3D12Device->CreateCommittedResource(
        &heapUpload, D3D12_HEAP_FLAG_NONE, &uploadDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
        IID_PPV_ARGS(&upload));
    if (FAILED(hr) || !upload)
    {
        char errorMsg[256];
        snprintf(
            errorMsg, sizeof(errorMsg),
            "[Lottie] Failed to create D3D12 upload buffer. HRESULT: 0x%08X, uploadBytes: %llu",
            hr, static_cast<unsigned long long>(uploadBytes));
        LottieLogError(animation, errorMsg);

        if (upload)
        {
            upload->Release();
        }
        texture->Release();
        delete footprint;
        state->d3d12.uploadSlotBytes = 0;
        return false;
    }

    void* mapped = nullptr;
    hr = upload->Map(0, nullptr, &mapped);
    if (FAILED(hr) || !mapped)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to map D3D12 upload buffer. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        upload->Release();
        texture->Release();
        delete footprint;
        state->d3d12.uploadSlotBytes = 0;
        return false;
    }

    state->d3d12.tex = texture;
    state->d3d12.upload = upload;
    state->d3d12.uploadMapped = mapped;
    state->d3d12.uploadSlotFenceValues.fill(0);
    state->d3d12.nextUploadSlot = 0;
    state->d3d12.texState = D3D12_RESOURCE_STATE_COMMON;
    state->d3d12.footprint = footprint;
    state->nativeTex = texture;
    state->texW = width;
    state->texH = height;
    LottieLogInfo(
        animation,
        "[Lottie] D3D12 texture created successfully (format=%s)",
        useSrgb ? "B8G8R8A8_UNORM_SRGB" : "B8G8R8A8_UNORM");
    return true;
}

namespace
{
ID3D12Fence* GetUnityFrameFence()
{
    if (sD3D12v8 != nullptr) return sD3D12v8->GetFrameFence();
    if (sD3D12 != nullptr) return sD3D12->GetFrameFence();
    if (sD3D12v6 != nullptr) return sD3D12v6->GetFrameFence();
    if (sD3D12v5 != nullptr) return sD3D12v5->GetFrameFence();
    return nullptr;
}

UINT64 GetUnityNextFrameFenceValue()
{
    if (sD3D12v8 != nullptr) return sD3D12v8->GetNextFrameFenceValue();
    if (sD3D12 != nullptr) return sD3D12->GetNextFrameFenceValue();
    if (sD3D12v6 != nullptr) return sD3D12v6->GetNextFrameFenceValue();
    if (sD3D12v5 != nullptr) return sD3D12v5->GetNextFrameFenceValue();
    return 0;
}
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
        state->d3d12.uploadMapped == nullptr || state->d3d12.footprint == nullptr ||
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

void RefreshCompletedRenderSlotsD3D12(InstanceState* state)
{
    ID3D12Fence* fence = state != nullptr ? state->d3d12.frameFence : nullptr;
    if (state == nullptr || fence == nullptr)
    {
        return;
    }
    const UINT64 completed = fence->GetCompletedValue();
    for (unsigned long long& fenceValue : state->d3d12.uploadSlotFenceValues)
    {
        if (fenceValue != 0 && fenceValue <= completed)
        {
            fenceValue = 0;
        }
    }
}

UploadResult UploadD3D12(InstanceState* state, const UploadContext& ctx)
{
    if (!state || !state->d3d12.tex || !state->d3d12.upload || !ctx.data || !state->d3d12.footprint)
    {
        return UploadResult::Failed;
    }

    ID3D12Fence* frameFence = GetUnityFrameFence();
    if (frameFence == nullptr)
    {
        return UploadResult::Retry;
    }
    if (frameFence != nullptr && state->d3d12.frameFence == nullptr)
    {
        frameFence->AddRef();
        state->d3d12.frameFence = frameFence;
    }

    const UINT64 completedFence = frameFence != nullptr ? frameFence->GetCompletedValue() : 0;
    unsigned int uploadSlot = InstanceState::D3D12Data::kUploadSlotCount;
    for (unsigned int offset = 0; offset < InstanceState::D3D12Data::kUploadSlotCount; ++offset)
    {
        const unsigned int candidate =
            (state->d3d12.nextUploadSlot + offset) % InstanceState::D3D12Data::kUploadSlotCount;
        const UINT64 useFence = state->d3d12.uploadSlotFenceValues[candidate];
        if (useFence == 0 || (frameFence != nullptr && useFence <= completedFence))
        {
            uploadSlot = candidate;
            break;
        }
    }
    if (uploadSlot == InstanceState::D3D12Data::kUploadSlotCount)
    {
        return UploadResult::Retry;
    }

    // Retry must not modify mapped upload memory or Unity's resource-state
    // tracker. Validate the active recording context before either operation.
    D3D12CommandContext ctxWrapper = AcquireD3D12CommandContext();
    ID3D12GraphicsCommandList* cmd = ctxWrapper.cmd;
    if (cmd == nullptr)
    {
        return UploadResult::Retry;
    }

    uint8_t* uploadData = reinterpret_cast<uint8_t*>(state->d3d12.uploadMapped)
        + state->d3d12.uploadSlotBytes * static_cast<UINT64>(uploadSlot)
        + state->d3d12.footprint->Offset;
    const UINT uploadStride = state->d3d12.footprint->Footprint.RowPitch;
    const size_t rowBytes = static_cast<size_t>(ctx.width) * 4;
    for (uint32_t row = 0; row < ctx.height; ++row)
    {
        std::memcpy(uploadData + static_cast<size_t>(row) * uploadStride,
                    ctx.data + static_cast<size_t>(row) * ctx.stride,
                    rowBytes);
    }
    if (sD3D12v8 != nullptr)
    {
        // Unity may have sampled this external texture since our last upload.
        // Ask its state tracker to make COPY_DEST current on the active list;
        // the plugin's cached state is not authoritative across frames.
        sD3D12v8->RequestResourceState(state->d3d12.tex, D3D12_RESOURCE_STATE_COPY_DEST);
    }
    if (sD3D12v8 == nullptr && state->d3d12.texState != D3D12_RESOURCE_STATE_COPY_DEST)
    {
        D3D12_RESOURCE_BARRIER b{};
        b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        b.Transition.pResource = state->d3d12.tex;
        b.Transition.Subresource = 0;
        b.Transition.StateBefore = static_cast<D3D12_RESOURCE_STATES>(state->d3d12.texState);
        b.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
        cmd->ResourceBarrier(1, &b);
    }

    D3D12_TEXTURE_COPY_LOCATION dst{};
    dst.pResource = state->d3d12.tex;
    dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dst.SubresourceIndex = 0;

    D3D12_TEXTURE_COPY_LOCATION src{};
    src.pResource = state->d3d12.upload;
    src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    src.PlacedFootprint = *state->d3d12.footprint;
    src.PlacedFootprint.Offset += state->d3d12.uploadSlotBytes * static_cast<UINT64>(uploadSlot);

    cmd->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);

    D3D12_RESOURCE_BARRIER toSRV{};
    toSRV.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    toSRV.Transition.pResource = state->d3d12.tex;
    toSRV.Transition.Subresource = 0;
    toSRV.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    toSRV.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    cmd->ResourceBarrier(1, &toSRV);

    state->d3d12.texState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

    if (sD3D12v8)
    {
        sD3D12v8->NotifyResourceState(state->d3d12.tex, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, false);
    }

    const UINT64 fenceValue = GetUnityNextFrameFenceValue();
    state->d3d12.uploadSlotFenceValues[uploadSlot] = fenceValue != 0 ? fenceValue : UINT64_MAX;
    state->d3d12.nextUploadSlot = (uploadSlot + 1) % InstanceState::D3D12Data::kUploadSlotCount;
    SubmitD3D12CommandContext(ctxWrapper);
    if (fenceValue == 0)
    {
        // Without Unity's frame fence there is no safe signal for overwriting
        // mapped upload memory. Keep the slot owned; the remaining two slots
        // allow bounded progress without unsafe reuse.
        LottieLogWarning(nullptr, "[Lottie] D3D12 frame fence unavailable; upload slot retained");
    }
    return UploadResult::Submitted;
}

#endif // defined(_WIN32)

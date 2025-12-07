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
    if (ctx.cmd != nullptr)
    {
        return ctx;
    }

    if (sD3D12v5 == nullptr || gD3D12Device == nullptr || gD3D12Queue == nullptr)
    {
        return ctx;
    }

    if (sD3D12Allocator == nullptr)
    {
        if (FAILED(gD3D12Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&sD3D12Allocator))))
        {
            ReleaseOwnedD3D12CommandList();
            return ctx;
        }
    }
    else
    {
        sD3D12Allocator->Reset();
    }

    if (sD3D12CmdList == nullptr)
    {
        if (FAILED(gD3D12Device->CreateCommandList(
                0,
                D3D12_COMMAND_LIST_TYPE_DIRECT,
                sD3D12Allocator,
                nullptr,
                IID_PPV_ARGS(&sD3D12CmdList))))
        {
            ReleaseOwnedD3D12CommandList();
            return ctx;
        }
    }
    else
    {
        sD3D12CmdList->Reset(sD3D12Allocator, nullptr);
    }

    ctx.cmd = sD3D12CmdList;
    ctx.ownsCommandList = true;
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
        if (state->d3d12.uploadMapped)
        {
            state->d3d12.upload->Unmap(0, nullptr);
            state->d3d12.uploadMapped = nullptr;
        }
        state->d3d12.upload->Release();
        state->d3d12.upload = nullptr;
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
    state->d3d12.uploadWriteIdx = 0;
    state->d3d12.texState = D3D12_RESOURCE_STATE_COMMON;
}

bool EnsureTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    if (!gD3D12Device)
    {
        LottieLogError(animation, "[Lottie] D3D12 device is null");
        return false;
    }

    D3D12_RESOURCE_DESC texDesc{};
    texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Alignment = 0;
    texDesc.Width = static_cast<UINT64>(width);
    texDesc.Height = static_cast<UINT>(height);
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels = 1;
    texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
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
    const UINT64 uploadBytes = totalBytes * state->d3d12.uploadSlotCount;

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
    state->d3d12.uploadWriteIdx = 0;
    state->d3d12.texState = D3D12_RESOURCE_STATE_COMMON;
    state->d3d12.footprint = footprint;
    state->nativeTex = texture;
    state->texW = width;
    state->texH = height;
    LottieLogInfo(animation, "[Lottie] D3D12 texture created successfully");
    return true;
}

static void D3D12StageBGRAUpload(InstanceState* state, const UploadContext& ctx)
{
    if (!state || !state->d3d12.upload || !state->d3d12.uploadMapped || !ctx.data || !state->d3d12.footprint)
    {
        return;
    }

    const UINT64 slotBase = state->d3d12.uploadSlotBytes * state->d3d12.uploadWriteIdx;
    const UINT rowPitch = state->d3d12.footprint->Footprint.RowPitch;

    uint8_t* dstBase = reinterpret_cast<uint8_t*>(state->d3d12.uploadMapped)
        + slotBase
        + state->d3d12.footprint->Offset;

    const uint8_t* src = ctx.data;
    for (uint32_t y = 0; y < ctx.height; ++y)
    {
        std::memcpy(
            dstBase + static_cast<size_t>(y) * rowPitch,
            src + static_cast<size_t>(y) * ctx.stride,
            ctx.stride);
    }
}

void UploadD3D12(InstanceState* state, const UploadContext& ctx)
{
    if (!state || !state->d3d12.tex || !state->d3d12.upload || !ctx.data || !state->d3d12.footprint)
    {
        return;
    }

    D3D12StageBGRAUpload(state, ctx);

    D3D12CommandContext ctxWrapper = AcquireD3D12CommandContext();
    ID3D12GraphicsCommandList* cmd = ctxWrapper.cmd;
    if (cmd == nullptr)
    {
        return;
    }

    if (state->d3d12.texState != D3D12_RESOURCE_STATE_COPY_DEST)
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
    src.PlacedFootprint.Offset += state->d3d12.uploadSlotBytes * state->d3d12.uploadWriteIdx;

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

    state->d3d12.uploadWriteIdx = (state->d3d12.uploadWriteIdx + 1) % state->d3d12.uploadSlotCount;
    SubmitD3D12CommandContext(ctxWrapper);
}

#endif // defined(_WIN32)

#include "D3D12Backend_win.h"

#if defined(_WIN32)

#include "LottieLogger.h"
#include "RendererCommon.h"

#include <cstring>
#include <dxgi1_6.h>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")

static ID3D12Device* gD3D12Device = nullptr;
static ID3D12CommandQueue* gD3D12Queue = nullptr;
static IUnityGraphicsD3D12v8* sD3D12v8 = nullptr;
static IUnityGraphicsD3D12v7* sD3D12 = nullptr;
static IUnityGraphicsD3D12v6* sD3D12v6 = nullptr;
static IUnityGraphicsD3D12v5* sD3D12v5 = nullptr;
static ID3D12CommandAllocator* sD3D12Allocator = nullptr;
static ID3D12GraphicsCommandList* sD3D12CmdList = nullptr;

struct D3D12CommandContext
{
    ID3D12GraphicsCommandList* cmd = nullptr;
    bool ownsCommandList = false;
};

void SetD3D12Interfaces(ID3D12Device* device,
                        ID3D12CommandQueue* queue,
                        IUnityGraphicsD3D12v5* v5,
                        IUnityGraphicsD3D12v6* v6,
                        IUnityGraphicsD3D12v7* v7,
                        IUnityGraphicsD3D12v8* v8)
{
    gD3D12Device = device;
    gD3D12Queue = queue;
    sD3D12v5 = v5;
    sD3D12v6 = v6;
    sD3D12 = v7;
    sD3D12v8 = v8;
}

void ClearD3D12Interfaces()
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

    gD3D12Queue = nullptr;
    gD3D12Device = nullptr;
    sD3D12v8 = nullptr;
    sD3D12 = nullptr;
    sD3D12v6 = nullptr;
    sD3D12v5 = nullptr;
}

static ID3D12GraphicsCommandList* AcquireUnityD3D12CommandList()
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

static D3D12CommandContext AcquireD3D12CommandContext()
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
            ClearD3D12Interfaces();
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
            ClearD3D12Interfaces();
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

static void SubmitD3D12CommandContext(const D3D12CommandContext& ctx)
{
    if (!ctx.ownsCommandList || ctx.cmd == nullptr || gD3D12Queue == nullptr)
    {
        return;
    }

    ctx.cmd->Close();
    ID3D12CommandList* lists[] = { ctx.cmd };
    gD3D12Queue->ExecuteCommandLists(1, lists);
}

static void D3D12StageBGRAUpload(InstanceState* state, const UploadContext& ctx)
{
    if (state == nullptr || state->d3d12.upload == nullptr || state->d3d12.uploadMapped == nullptr)
    {
        return;
    }

    const UINT64 offset = state->d3d12.uploadSlotBytes * state->d3d12.uploadWriteIdx;
    const uint8_t* src = ctx.data;
    uint8_t* dst = reinterpret_cast<uint8_t*>(state->d3d12.uploadMapped) + offset;
    const UINT64 dstPitch = state->d3d12.footprint.Footprint.RowPitch;
    const UINT dstHeight = state->d3d12.footprint.Footprint.Height;

    for (UINT row = 0; row < dstHeight; ++row)
    {
        std::memcpy(dst + row * dstPitch, src + row * ctx.stride, ctx.width * 4u);
    }
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

    UINT64 totalBytes = 0;
    gD3D12Device->GetCopyableFootprints(
        &texDesc, 0, 1, 0, &state->d3d12.footprint, nullptr, nullptr, &totalBytes);

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

    ID3D12Resource* uploadBuffer = nullptr;
    hr = gD3D12Device->CreateCommittedResource(
        &heapUpload, D3D12_HEAP_FLAG_NONE,
        &uploadDesc, D3D12_RESOURCE_STATE_GENERIC_READ,
        nullptr, IID_PPV_ARGS(&uploadBuffer));
    if (FAILED(hr) || !uploadBuffer)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to create D3D12 upload buffer. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        if (uploadBuffer)
        {
            uploadBuffer->Release();
        }
        texture->Release();
        return false;
    }

    void* mappedData = nullptr;
    hr = uploadBuffer->Map(0, nullptr, &mappedData);
    if (FAILED(hr) || mappedData == nullptr)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to map D3D12 upload buffer. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        uploadBuffer->Release();
        texture->Release();
        return false;
    }

    state->d3d12.tex = texture;
    state->d3d12.upload = uploadBuffer;
    state->d3d12.uploadMapped = mappedData;
    state->d3d12.uploadWriteIdx = 0;
    state->d3d12.texState = D3D12_RESOURCE_STATE_COMMON;

    state->nativeTex = texture;
    state->texW = width;
    state->texH = height;

    return true;
}

void UploadD3D12(InstanceState* state, const UploadContext& ctx)
{
    if (gD3D12Device == nullptr || gD3D12Queue == nullptr || state == nullptr || state->d3d12.tex == nullptr || state->d3d12.upload == nullptr)
    {
        return;
    }

    D3D12StageBGRAUpload(state, ctx);

    D3D12CommandContext commandContext = AcquireD3D12CommandContext();
    ID3D12GraphicsCommandList* cmdList = commandContext.cmd;
    if (cmdList == nullptr)
    {
        return;
    }

    const UINT64 uploadOffset = state->d3d12.uploadSlotBytes * state->d3d12.uploadWriteIdx;
    const UINT uploadSlice = state->d3d12.uploadWriteIdx;
    state->d3d12.uploadWriteIdx = (state->d3d12.uploadWriteIdx + 1) % state->d3d12.uploadSlotCount;

    if (state->d3d12.texState != D3D12_RESOURCE_STATE_COPY_DEST)
    {
        D3D12_RESOURCE_BARRIER barrier{};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
        barrier.Transition.pResource = state->d3d12.tex;
        barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        barrier.Transition.StateBefore = state->d3d12.texState;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
        cmdList->ResourceBarrier(1, &barrier);
        state->d3d12.texState = D3D12_RESOURCE_STATE_COPY_DEST;
    }

    D3D12_TEXTURE_COPY_LOCATION dst{};
    dst.pResource = state->d3d12.tex;
    dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dst.SubresourceIndex = 0;

    D3D12_TEXTURE_COPY_LOCATION src{};
    src.pResource = state->d3d12.upload;
    src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    src.PlacedFootprint = state->d3d12.footprint;
    src.PlacedFootprint.Offset = uploadOffset;

    cmdList->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);

    if (state->d3d12.texState != D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE)
    {
        D3D12_RESOURCE_BARRIER barrier{};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
        barrier.Transition.pResource = state->d3d12.tex;
        barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        barrier.Transition.StateBefore = state->d3d12.texState;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        cmdList->ResourceBarrier(1, &barrier);
        state->d3d12.texState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    if (sD3D12v8 != nullptr)
    {
        sD3D12v8->NotifyResourceState(state->d3d12.tex, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, false);
    }

    SubmitD3D12CommandContext(commandContext);
    (void)uploadSlice;
}

void ResetTextureD3D12(InstanceState* state)
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
    state->d3d12.footprint = {};
    state->d3d12.uploadSlotBytes = 0;
    state->d3d12.uploadWriteIdx = 0;
    state->d3d12.texState = D3D12_RESOURCE_STATE_COMMON;
}

#endif

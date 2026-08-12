#ifndef D3D12_BACKEND_WIN_H
#define D3D12_BACKEND_WIN_H

#if defined(_WIN32)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;
struct ID3D12Device;
struct ID3D12CommandQueue;
struct ID3D12GraphicsCommandList;
struct IUnityGraphicsD3D12v8;
struct IUnityGraphicsD3D12v7;
struct IUnityGraphicsD3D12v6;
struct IUnityGraphicsD3D12v5;

// D3D12 command context for managing command lists
struct D3D12CommandContext
{
    ID3D12GraphicsCommandList* cmd = nullptr;
    bool ownsCommandList = false;
};

// D3D12 device/interface accessors
void SetD3D12Device(ID3D12Device* device);
ID3D12Device* GetD3D12Device();
void SetD3D12Queue(ID3D12CommandQueue* queue);
ID3D12CommandQueue* GetD3D12Queue();

// Set Unity D3D12 interfaces
void SetD3D12Interfaces(IUnityGraphicsD3D12v8* v8, IUnityGraphicsD3D12v7* v7, 
                         IUnityGraphicsD3D12v6* v6, IUnityGraphicsD3D12v5* v5);

// D3D12 texture operations
bool EnsureTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadD3D12(InstanceState* state, const UploadContext& ctx);
void ResetTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state);
bool PrepareRenderSlotD3D12(InstanceState* state, int slotIndex, uint32_t width, uint32_t height, uint8_t*& data, uint32_t& stride);
void RefreshCompletedRenderSlotsD3D12(InstanceState* state);

// D3D12 command list helpers
void ReleaseOwnedD3D12CommandList();
ID3D12GraphicsCommandList* AcquireUnityD3D12CommandList();
D3D12CommandContext AcquireD3D12CommandContext();
void SubmitD3D12CommandContext(const D3D12CommandContext& ctx);

// Notify Unity about resource state (for D3D12v8)
void NotifyD3D12ResourceState(void* resource, int state, bool isAfterState);

#endif // defined(_WIN32)

#endif // D3D12_BACKEND_WIN_H

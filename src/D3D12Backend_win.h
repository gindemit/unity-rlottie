#ifndef D3D12_BACKEND_WIN_H
#define D3D12_BACKEND_WIN_H

#if defined(_WIN32)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;
struct ID3D12Device;
struct IUnityGraphicsD3D12v8;

void SetD3D12Device(ID3D12Device* device);
void SetD3D12Interface(IUnityGraphicsD3D12v8* v8);
void ConfigureD3D12UploadEvent();

// D3D12 texture operations
bool EnsureTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
UploadResult UploadD3D12(InstanceState* state, const UploadContext& ctx);
void ResetTextureD3D12(InstanceState* state);
bool PrepareRenderSlotD3D12(InstanceState* state, int slotIndex, uint32_t width, uint32_t height, uint8_t*& data, uint32_t& stride);

#endif // defined(_WIN32)

#endif // D3D12_BACKEND_WIN_H

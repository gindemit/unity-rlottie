#pragma once

#if defined(_WIN32)

#include "InstanceRegistry.h"
#include "UploadQueue.h"

struct IUnityGraphicsD3D12v5;
struct IUnityGraphicsD3D12v6;
struct IUnityGraphicsD3D12v7;
struct IUnityGraphicsD3D12v8;

void SetD3D12Interfaces(ID3D12Device* device,
                        ID3D12CommandQueue* queue,
                        IUnityGraphicsD3D12v5* v5,
                        IUnityGraphicsD3D12v6* v6,
                        IUnityGraphicsD3D12v7* v7,
                        IUnityGraphicsD3D12v8* v8);
void ClearD3D12Interfaces();

bool EnsureTextureD3D12(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadD3D12(InstanceState* state, const UploadContext& ctx);
void ResetTextureD3D12(InstanceState* state);

#endif

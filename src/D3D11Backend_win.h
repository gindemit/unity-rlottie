#pragma once

#if defined(_WIN32)

#include "InstanceRegistry.h"
#include "UploadQueue.h"

void SetD3D11Interfaces(ID3D11Device* device, ID3D11DeviceContext* context);
void ClearD3D11Interfaces();

bool EnsureTextureD3D11(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadD3D11(InstanceState* state, const UploadContext& ctx);
void ResetTextureD3D11(InstanceState* state);

#endif

#pragma once

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)

#include "InstanceRegistry.h"
#include "UploadQueue.h"

@class MTLDevice;

void SetMetalDevice(id<MTLDevice> device);
void ClearMetalDevice();

bool EnsureTextureMetal(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadMetal(InstanceState* state, const UploadContext& ctx);
void ResetTextureMetal(InstanceState* state);

#endif

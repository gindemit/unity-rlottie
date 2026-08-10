#ifndef TEXTURE_BACKEND_H
#define TEXTURE_BACKEND_H

#include "LottiePlugin.h"
#include "RendererCommon.h"

#if !defined(__EMSCRIPTEN__)

// Forward declaration
struct InstanceState;

// Ensure texture exists for the current renderer (dispatches to backend-specific implementation)
bool EnsureTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);

// Upload texture data for the current renderer (dispatches to backend-specific implementation)
void UploadForRenderer(InstanceState* state, const UploadContext& ctx);

// Reset texture state for the current renderer (dispatches to backend-specific implementation)
void ResetTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state);

#endif // !defined(__EMSCRIPTEN__)

#endif // TEXTURE_BACKEND_H

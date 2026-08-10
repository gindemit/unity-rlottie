#ifndef VULKAN_BACKEND_H
#define VULKAN_BACKEND_H

#if !defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;

// Vulkan texture operations (mostly stubs - Unity handles texture upload for Vulkan)
bool EnsureTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadVulkan(InstanceState* state, const UploadContext& ctx);
void ResetTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state);

#endif // !defined(__EMSCRIPTEN__)

#endif // VULKAN_BACKEND_H

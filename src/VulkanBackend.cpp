#if !defined(__EMSCRIPTEN__)

#include "VulkanBackend.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"

void ResetTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state)
{
    // Vulkan: No native texture resources to release - Unity manages textures
    (void)animation;
    (void)state;
}

bool EnsureTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    // For Vulkan, Unity manages textures internally.
    // We render to CPU buffer and Unity handles GPU upload via Texture2D.LoadRawTextureData
    // No native texture pointer needed - C# uses regular Texture2D like WebGL
    LottieLogInfo(animation, "[Lottie] Vulkan: Using CPU-side rendering, Unity handles GPU upload");
    state->texW = width;
    state->texH = height;
    return true;
}

void UploadVulkan(InstanceState* state, const UploadContext& ctx)
{
    // Vulkan: No direct texture upload needed here.
    // Unity's Texture2D.LoadRawTextureData + Apply handles the GPU upload.
    LottieLogInfo(nullptr, "[Lottie] Vulkan: Skipping direct upload, Unity handles it");
    (void)state;
    (void)ctx;
}

#endif // !defined(__EMSCRIPTEN__)

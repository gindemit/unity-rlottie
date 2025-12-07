#include "VulkanBackend.h"

bool EnsureTextureVulkan(InstanceState* state, int width, int height)
{
    if (state == nullptr)
    {
        return false;
    }
    state->texW = width;
    state->texH = height;
    return true;
}

void UploadVulkan(InstanceState* state, const UploadContext& ctx)
{
    (void)state;
    (void)ctx;
}

void ResetTextureVulkan(InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }
    state->texW = 0;
    state->texH = 0;
    state->nativeTex = nullptr;
}

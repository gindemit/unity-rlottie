#pragma once

#include "InstanceRegistry.h"
#include "UploadQueue.h"

bool EnsureTextureVulkan(InstanceState* state, int width, int height);
void UploadVulkan(InstanceState* state, const UploadContext& ctx);
void ResetTextureVulkan(InstanceState* state);

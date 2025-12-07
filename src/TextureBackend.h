#pragma once

#include "InstanceRegistry.h"
#include "UploadQueue.h"

struct lottie_animation_wrapper;

bool EnsureTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state, int w, int h);
void UploadForRenderer(InstanceState* state, const UploadContext& ctx);
void ResetTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state);

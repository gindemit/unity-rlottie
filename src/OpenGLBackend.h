#pragma once

#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)

#include "InstanceRegistry.h"
#include "UploadQueue.h"

void DetectGLExtensions();
bool EnsureTextureOpenGL(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadOpenGL(InstanceState* state, const UploadContext& ctx);
void ResetTextureOpenGL(InstanceState* state);

extern bool gHasBGRAExt;
extern bool gIsOpenGLES;

#endif

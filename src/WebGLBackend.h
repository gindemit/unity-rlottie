#ifndef WEBGL_BACKEND_H
#define WEBGL_BACKEND_H

#if defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"

void RegisterWebGLRenderingPlugin();
bool RegisterUnityTextureWebGL(
    lottie_animation_wrapper* animation,
    void* nativeTexture,
    int width,
    int height);
void UnregisterUnityTextureWebGL(lottie_animation_wrapper* animation);
bool RequestTextureUploadWebGL(
    lottie_animation_wrapper* animation,
    lottie_render_data* renderData);
bool IsWebGLUploadAvailable(lottie_animation_wrapper* animation);
UnityRenderingEvent GetWebGLRenderEventFunc();

#endif

#endif

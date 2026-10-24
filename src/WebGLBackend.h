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

#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
int32_t WebGLTraceArm(uint32_t abi, uint32_t headerBytes, uint32_t recordBytes, uint32_t frame);
int32_t WebGLTraceRegister(lottie_animation_wrapper* animation, void* texture, int32_t width, int32_t height,
    uint32_t animationId, uint32_t generation, uint32_t textureToken, uint32_t attempt, uint32_t frame, uint32_t origin);
int32_t WebGLTraceRequest(lottie_animation_wrapper* animation, lottie_render_data* data,
    uint32_t animationId, uint32_t generation, uint32_t sequence, uint32_t frame);
int32_t WebGLTraceIssue(uint32_t token, uint32_t frame);
int32_t WebGLTraceCopy(uint32_t abi, uint32_t headerBytes, uint32_t recordBytes,
    void* header, uint32_t headerCapacityBytes, void* records, uint32_t capacity, uint32_t capacityBytes);
#endif

#endif

#endif

#ifndef OPENGL_BACKEND_H
#define OPENGL_BACKEND_H

// OpenGL backend is used on Windows, Android, and Linux (but not WebGL/Emscripten or Apple Metal platforms)
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;

// OpenGL extension detection
void DetectGLExtensions();
bool HasBGRAExtension();
bool IsOpenGLES();
void ResetGLExtensionState();

// OpenGL texture operations
bool EnsureTextureOpenGL(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
UploadResult UploadOpenGL(InstanceState* state, const UploadContext& ctx);
void ResetTextureOpenGL(lottie_animation_wrapper* animation, InstanceState* state);
bool RegisterUnityTextureOpenGL(
    lottie_animation_wrapper* animation,
    void* nativeTexture,
    int width,
    int height);
bool IsOpenGLUploadAvailable(lottie_animation_wrapper* animation);

// OpenGL error checking helper
bool CheckGLError(lottie_animation_wrapper* animation, const char* operation);

#endif // !defined(__EMSCRIPTEN__) && !defined(__APPLE__)

#endif // OPENGL_BACKEND_H

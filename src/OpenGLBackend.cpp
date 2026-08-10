#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <GL/gl.h>
#pragma comment(lib, "opengl32.lib")
#elif defined(__ANDROID__)
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#else
#include <GL/gl.h>
#endif

#include "OpenGLBackend.h"
#include "InstanceRegistry.h"
#include "PixelFormatUtils.h"
#include "LottieLogger.h"
#include <cstring>

// Define GL constants if not available
#ifndef GL_BGRA
#define GL_BGRA 0x80E1
#endif
#ifndef GL_CLAMP_TO_EDGE
#define GL_CLAMP_TO_EDGE 0x812F
#endif
#ifndef GL_RGBA8
#define GL_RGBA8 0x8058
#endif
#ifndef GL_RGBA
#define GL_RGBA 0x1908
#endif

#if defined(__ANDROID__)
// Define GL_BGRA as an alias to GL_BGRA_EXT for consistency
#ifndef GL_BGRA
#define GL_BGRA GL_BGRA_EXT
#endif
#endif

namespace
{
    bool gHasBGRAExt = false;
    bool gIsOpenGLES = false;
}

void DetectGLExtensions()
{
    gHasBGRAExt = false;
    gIsOpenGLES = false;

    // Check if we're running OpenGL ES (ANGLE or other ES implementation)
    const char* version = reinterpret_cast<const char*>(glGetString(GL_VERSION));
    if (version != nullptr)
    {
        gIsOpenGLES = (std::strstr(version, "OpenGL ES") != nullptr);
        LottieLogInfo(nullptr, "[Lottie] OpenGL version: %s, isOpenGLES: %s", version, gIsOpenGLES ? "true" : "false");
    }

    const char* ext = reinterpret_cast<const char*>(glGetString(GL_EXTENSIONS));
    if (ext != nullptr)
    {
        if (std::strstr(ext, "GL_EXT_texture_format_BGRA8888") != nullptr)
        {
            gHasBGRAExt = true;
        }
        // Desktop OpenGL always supports GL_BGRA via core
        if (!gIsOpenGLES)
        {
            gHasBGRAExt = true;
        }
    }
    else if (!gIsOpenGLES)
    {
        // Desktop OpenGL supports BGRA in core
        gHasBGRAExt = true;
    }
    LottieLogInfo(nullptr, "[Lottie] BGRA extension available: %s", gHasBGRAExt ? "true" : "false");
}

bool HasBGRAExtension()
{
    return gHasBGRAExt;
}

bool IsOpenGLES()
{
    return gIsOpenGLES;
}

void ResetGLExtensionState()
{
    gHasBGRAExt = false;
    gIsOpenGLES = false;
}

void CheckGLError(lottie_animation_wrapper* animation, const char* operation)
{
    GLenum err = glGetError();
    if (err != GL_NO_ERROR)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] OpenGL error after %s: 0x%04X", operation, err);
        LottieLogError(animation, errorMsg);
    }
}

void ResetTextureOpenGL(lottie_animation_wrapper* animation, InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    if (state->gl.glTex != 0)
    {
        glDeleteTextures(1, &state->gl.glTex);
        state->gl.glTex = 0;
    }
}

bool EnsureTextureOpenGL(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    // Clear any existing OpenGL errors
    while (glGetError() != GL_NO_ERROR) {}

    // Deferred OpenGL creation starts with a dummy pointer until the render event.
    if (state->gl.glTex == 0 &&
        state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(kDeferredGLTexDummy)))
    {
        LottieLogInfo(animation, "[Lottie] EnsureTexture: performing deferred OpenGL texture creation");
        state->nativeTex = nullptr; // clear dummy
    }

    if (state->gl.glTex == 0)
    {
        LottieLogInfo(animation, "[Lottie] EnsureTexture: glGenTextures");
        glGenTextures(1, &state->gl.glTex);
        CheckGLError(animation, "glGenTextures");
    }

    if (state->gl.glTex == 0)
    {
        LottieLogError(animation, "[Lottie] EnsureTexture: glTex is still 0 after glGenTextures");
        CheckGLError(animation, "texture generation check");
        return false;
    }

    glBindTexture(GL_TEXTURE_2D, state->gl.glTex);
    CheckGLError(animation, "glBindTexture");

    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    CheckGLError(animation, "glTexParameteri");

    const bool useBGRA = gHasBGRAExt;
    // For OpenGL ES with BGRA extension, use GL_BGRA for both internal and upload format
    // For OpenGL ES without BGRA, use GL_RGBA for both
    // For desktop OpenGL (non-ES), use GL_RGBA8 internal format with GL_BGRA upload format
    GLint internalFormat;
    GLenum uploadFormat;
    if (gIsOpenGLES)
    {
        // OpenGL ES requires matching internal and upload formats
        if (useBGRA)
        {
            internalFormat = GL_BGRA;
            uploadFormat = GL_BGRA;
        }
        else
        {
            internalFormat = GL_RGBA;
            uploadFormat = GL_RGBA;
        }
    }
    else
    {
        // Desktop OpenGL supports GL_RGBA8 with GL_BGRA upload
        internalFormat = GL_RGBA8;
        uploadFormat = useBGRA ? GL_BGRA : GL_RGBA;
    }

    LottieLogInfo(animation, "[Lottie] EnsureTexture: isOpenGLES=%s, useBGRA=%s, internalFormat=0x%04X, uploadFormat=0x%04X",
                  gIsOpenGLES ? "true" : "false", useBGRA ? "true" : "false", internalFormat, uploadFormat);
    glTexImage2D(GL_TEXTURE_2D, 0, internalFormat, width, height, 0, uploadFormat, GL_UNSIGNED_BYTE, nullptr);
    CheckGLError(animation, "glTexImage2D");

    state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(state->gl.glTex));
    state->texW = width;
    state->texH = height;

    LottieLogInfo(animation, "[Lottie] EnsureTexture: OpenGL texture created successfully, glTex=%u, nativeTex=%p",
                  state->gl.glTex, state->nativeTex);
    return true;
}

void UploadOpenGL(InstanceState* state, const UploadContext& ctx)
{
    if (state == nullptr || state->gl.glTex == 0 || ctx.data == nullptr)
    {
        LottieLogWarning(nullptr, "[Lottie] UploadOpenGL: Invalid parameters (state=%p, glTex=%u, data=%p)",
                       state, state ? state->gl.glTex : 0, ctx.data);
        return;
    }

    LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Uploading to texture %u, size=%ux%u, stride=%u",
                  state->gl.glTex, ctx.width, ctx.height, ctx.stride);

    // Clear any existing errors
    while (glGetError() != GL_NO_ERROR) {}

    glBindTexture(GL_TEXTURE_2D, state->gl.glTex);
    CheckGLError(nullptr, "glBindTexture in UploadOpenGL");

    glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
    CheckGLError(nullptr, "glPixelStorei in UploadOpenGL");

    if (gHasBGRAExt)
    {
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
    }
    else
    {
        LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Converting BGRA to RGBA (no BGRA extension)");
        ConvertBGRAtoRGBA(state->gl.rgbaScratch, ctx);
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_RGBA, GL_UNSIGNED_BYTE, state->gl.rgbaScratch.data());
    }
    CheckGLError(nullptr, "glTexSubImage2D in UploadOpenGL");

    // Ensure OpenGL commands are executed
    glFlush();
    CheckGLError(nullptr, "glFlush in UploadOpenGL");

    LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Upload completed successfully");
}

#endif // !defined(__EMSCRIPTEN__) && !defined(__APPLE__)

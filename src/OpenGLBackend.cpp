#include "OpenGLBackend.h"

#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)

#include "LottieLogger.h"
#include "PixelFormatUtils.h"
#include "RendererCommon.h"

#include <cstring>

#if defined(_WIN32)
#include <windows.h>
#include <GL/gl.h>
#pragma comment(lib, "opengl32.lib")
#elif defined(__ANDROID__)
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#ifndef GL_BGRA
#define GL_BGRA GL_BGRA_EXT
#endif
#else
#include <GL/gl.h>
#ifndef GL_BGRA
#define GL_BGRA 0x80E1
#endif
#endif

#if defined(_WIN32)
#ifndef GL_CLAMP_TO_EDGE
#define GL_CLAMP_TO_EDGE 0x812F
#endif
#ifndef GL_RGBA8
#define GL_RGBA8 0x8058
#endif
#ifndef GL_RGBA
#define GL_RGBA 0x1908
#endif
#endif

bool gHasBGRAExt = false;
bool gIsOpenGLES = false;

static void CheckGLError(lottie_animation_wrapper* animation, const char* operation)
{
    GLenum err = glGetError();
    if (err != GL_NO_ERROR)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] OpenGL error after %s: 0x%04X", operation, err);
        LottieLogError(animation, errorMsg);
    }
}

void DetectGLExtensions()
{
    gHasBGRAExt = false;
    gIsOpenGLES = false;

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
        if (!gIsOpenGLES)
        {
            gHasBGRAExt = true;
        }
    }
    else if (!gIsOpenGLES)
    {
        gHasBGRAExt = true;
    }
    LottieLogInfo(nullptr, "[Lottie] BGRA extension available: %s", gHasBGRAExt ? "true" : "false");
}

bool EnsureTextureOpenGL(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    if (state->gl.glTex == 0 &&
        state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(0x1)))
    {
        LottieLogInfo(animation,
            "[Lottie] EnsureTexture: performing deferred OpenGL texture creation");
        state->nativeTex = nullptr;
    }

    if (state->gl.glTex == 0)
    {
        LottieLogInfo(animation, "[Lottie] EnsureTexture: glGenTextures");
        glGenTextures(1, reinterpret_cast<GLuint*>(&state->gl.glTex));
#if defined(_WIN32) || defined(__ANDROID__)
        CheckGLError(animation, "glGenTextures");
#endif
    }

    if (state->gl.glTex == 0)
    {
        LottieLogError(animation,
            "[Lottie] EnsureTexture: glTex is still 0 after glGenTextures");
#if defined(_WIN32) || defined(__ANDROID__)
        CheckGLError(animation, "texture generation check");
#endif
        return false;
    }
    glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(state->gl.glTex));
#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(animation, "glBindTexture");
#endif
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(animation, "glTexParameteri");
#endif
#if defined(__ANDROID__) || defined(_WIN32)
    const bool useBGRA = gHasBGRAExt;
    GLint internalFormat;
    GLenum uploadFormat;
    if (gIsOpenGLES)
    {
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
        internalFormat = GL_RGBA8;
        uploadFormat = useBGRA ? GL_BGRA : GL_RGBA;
    }
    LottieLogInfo(animation, "[Lottie] EnsureTexture: isOpenGLES=%s, useBGRA=%s, internalFormat=0x%04X, uploadFormat=0x%04X",
                  gIsOpenGLES ? "true" : "false", useBGRA ? "true" : "false", internalFormat, uploadFormat);
    glTexImage2D(
        GL_TEXTURE_2D, 0, internalFormat, width, height, 0, uploadFormat, GL_UNSIGNED_BYTE, nullptr);
#else
    const GLint internalFormat = GL_RGBA8;
    glTexImage2D(
        GL_TEXTURE_2D, 0, internalFormat, width, height, 0, GL_BGRA, GL_UNSIGNED_BYTE, nullptr);
#endif
#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(animation, "glTexImage2D");
#endif

    state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(state->gl.glTex));
    state->texW = width;
    state->texH = height;

    LottieLogInfo(animation,
        "[Lottie] EnsureTexture: OpenGL texture created successfully, glTex=%u, nativeTex=%p",
        state->gl.glTex, state->nativeTex);
    return true;
}

void UploadOpenGL(InstanceState* state, const UploadContext& ctx)
{
    if (state == nullptr || state->gl.glTex == 0)
    {
        return;
    }

    glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(state->gl.glTex));
#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(nullptr, "glBindTexture in UploadOpenGL");
#endif

    glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(nullptr, "glPixelStorei in UploadOpenGL");
#endif

#if defined(__ANDROID__) || defined(_WIN32)
    if (gHasBGRAExt)
    {
        glTexSubImage2D(
            GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
    }
    else
    {
        LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Converting BGRA to RGBA (no BGRA extension)");
        ConvertBGRAtoRGBA(state->gl.rgbaScratch, ctx);
        glTexSubImage2D(
            GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_RGBA, GL_UNSIGNED_BYTE, state->gl.rgbaScratch.data());
    }
#else
    glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
#endif

#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(nullptr, "glTexSubImage2D in UploadOpenGL");
#endif

    glFlush();
#if defined(_WIN32) || defined(__ANDROID__)
    CheckGLError(nullptr, "glFlush in UploadOpenGL");
#endif

    LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Upload completed successfully");
}

void ResetTextureOpenGL(InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    if (state->gl.glTex != 0)
    {
        GLuint tex = static_cast<GLuint>(state->gl.glTex);
        glDeleteTextures(1, &tex);
        state->gl.glTex = 0;
    }
    state->gl.rgbaScratch.clear();
}

#endif

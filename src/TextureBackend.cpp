#include "TextureBackend.h"

#include "RendererCommon.h"

#if defined(_WIN32)
#include "D3D11Backend_win.h"
#include "D3D12Backend_win.h"
#endif
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#include "MetalBackend_apple.h"
#endif
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
#include "OpenGLBackend.h"
#endif
#include "VulkanBackend.h"

bool EnsureTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state, int w, int h)
{
    if (state == nullptr || w <= 0 || h <= 0)
    {
        LottieLogWarning(animation, "[Lottie] EnsureTexture: invalid parameters");
        return false;
    }

    const bool isDummyPointer = (state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(0x1)));
    if (state->texW == w && state->texH == h && state->nativeTex != nullptr && !isDummyPointer)
    {
        LottieLogInfo(animation, "[Lottie] EnsureTexture: texture already exists with matching dimensions");
        return true;
    }

    LottieLogInfo(animation, "[Lottie] EnsureTexture: creating new texture %dx%d", w, h);
    ResetTextureForRenderer(animation, state);

    switch (gRenderer)
    {
        case Renderer::D3D12:
#if defined(_WIN32)
            return EnsureTextureD3D12(animation, state, w, h);
#else
            return false;
#endif
        case Renderer::D3D11:
#if defined(_WIN32)
            return EnsureTextureD3D11(animation, state, w, h);
#else
            return false;
#endif
        case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            return EnsureTextureMetal(animation, state, w, h);
#else
            return false;
#endif
        case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
            return EnsureTextureOpenGL(animation, state, w, h);
#else
            return false;
#endif
        case Renderer::Vulkan:
            state->texW = w;
            state->texH = h;
            return true;
        case Renderer::Unknown:
        default:
            LottieLogError(animation, "[Lottie] EnsureTexture: Unknown or unsupported renderer");
            return false;
    }
}

void UploadForRenderer(InstanceState* state, const UploadContext& ctx)
{
    switch (gRenderer)
    {
        case Renderer::D3D12:
#if defined(_WIN32)
            UploadD3D12(state, ctx);
#endif
            break;
        case Renderer::D3D11:
#if defined(_WIN32)
            UploadD3D11(state, ctx);
#endif
            break;
        case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            UploadMetal(state, ctx);
#endif
            break;
        case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
            UploadOpenGL(state, ctx);
#endif
            break;
        case Renderer::Vulkan:
            break;
        case Renderer::Unknown:
        default:
            break;
    }
}

void ResetTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state)
{
    (void)animation;
    if (state == nullptr)
    {
        return;
    }

    switch (gRenderer)
    {
        case Renderer::D3D12:
#if defined(_WIN32)
            ResetTextureD3D12(state);
#endif
            break;
        case Renderer::D3D11:
#if defined(_WIN32)
            ResetTextureD3D11(state);
#endif
            break;
        case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            ResetTextureMetal(state);
#endif
            break;
        case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
            ResetTextureOpenGL(state);
#endif
            break;
        case Renderer::Vulkan:
            ResetTextureVulkan(state);
            break;
        case Renderer::Unknown:
        default:
            break;
    }
}

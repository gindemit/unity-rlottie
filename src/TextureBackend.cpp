#include "TextureBackend.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"

#if !defined(__EMSCRIPTEN__)

#if defined(_WIN32)
#include "D3D12Backend_win.h"
#include "D3D11Backend_win.h"
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#include "MetalBackend_apple.h"
#endif

#if !defined(__APPLE__)
#include "OpenGLBackend.h"
#endif

#include "VulkanBackend.h"

bool EnsureTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    if (state == nullptr || width <= 0 || height <= 0)
    {
        LottieLogWarning(animation, "[Lottie] EnsureTexture: invalid parameters");
        return false;
    }

    Renderer renderer = GetCurrentRenderer();

    // Check if texture already exists with matching dimensions
    // Exclude the dummy pointer used for deferred OpenGL texture creation on Windows
    const bool isDummyPointer = (state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(kDeferredGLTexDummy)));
    if (state->texW == width && state->texH == height && state->nativeTex != nullptr && !isDummyPointer)
    {
        LottieLogInfo(animation, "[Lottie] EnsureTexture: texture already exists with matching dimensions");
        return true;
    }

    LottieLogInfo(animation, "[Lottie] EnsureTexture: creating new texture %dx%d", width, height);
    ResetTextureForRenderer(animation, state);

    switch (renderer)
    {
        case Renderer::D3D12:
#if defined(_WIN32)
            return EnsureTextureD3D12(animation, state, width, height);
#else
            return false;
#endif
        case Renderer::D3D11:
#if defined(_WIN32)
            return EnsureTextureD3D11(animation, state, width, height);
#else
            return false;
#endif
        case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            return EnsureTextureMetal(animation, state, width, height);
#else
            return false;
#endif
        case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
            return EnsureTextureOpenGL(animation, state, width, height);
#else
            return false;
#endif
        case Renderer::Vulkan:
            return EnsureTextureVulkan(animation, state, width, height);
        case Renderer::Unknown:
        default:
            LottieLogError(animation, "[Lottie] EnsureTexture: Unknown or unsupported renderer");
            return false;
    }
}

void UploadForRenderer(InstanceState* state, const UploadContext& ctx)
{
    Renderer renderer = GetCurrentRenderer();

    switch (renderer)
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
            UploadVulkan(state, ctx);
            break;
        case Renderer::Unknown:
        default:
            break;
    }
}

void ResetTextureForRenderer(lottie_animation_wrapper* animation, InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    Renderer renderer = GetCurrentRenderer();

    switch (renderer)
    {
        case Renderer::D3D12:
#if defined(_WIN32)
            ResetTextureD3D12(animation, state);
#endif
            break;
        case Renderer::D3D11:
#if defined(_WIN32)
            ResetTextureD3D11(animation, state);
#endif
            break;
        case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            ResetTextureMetal(animation, state);
#endif
            break;
        case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
            ResetTextureOpenGL(animation, state);
#endif
            break;
        case Renderer::Vulkan:
            ResetTextureVulkan(animation, state);
            break;
        case Renderer::Unknown:
        default:
            // Try to reset all if renderer is unknown (e.g., during shutdown)
#if defined(_WIN32)
            ResetTextureD3D12(animation, state);
            ResetTextureD3D11(animation, state);
            ResetTextureOpenGL(animation, state);
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            ResetTextureMetal(animation, state);
#elif !defined(__EMSCRIPTEN__)
            ResetTextureOpenGL(animation, state);
#endif
            ResetTextureVulkan(animation, state);
            break;
    }
}

#endif // !defined(__EMSCRIPTEN__)

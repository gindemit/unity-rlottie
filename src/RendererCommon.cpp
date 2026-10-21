#include "RendererCommon.h"

namespace
{
    Renderer gRenderer = Renderer::Unknown;
    void* gDevice = nullptr;
}

Renderer ToRenderer(int deviceType)
{
    Renderer result;
    switch (deviceType)
    {
        case kUnityGfxRendererD3D11:
            result = Renderer::D3D11;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: D3D11");
            break;
        case kUnityGfxRendererD3D12:
            result = Renderer::D3D12;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: D3D12");
            break;
        case kUnityGfxRendererOpenGLCore:
        case kUnityGfxRendererOpenGLES20:
        case kUnityGfxRendererOpenGLES30:
            result = Renderer::OpenGL;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: OpenGL/GLES");
            break;
        case kUnityGfxRendererMetal:
            result = Renderer::Metal;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: Metal");
            break;
        case kUnityGfxRendererVulkan:
            result = Renderer::Vulkan;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: Vulkan");
            break;
        default:
            result = Renderer::Unknown;
            LottieLogWarning(nullptr, "[Lottie] Unknown graphics device type");
            break;
    }
    return result;
}

Renderer GetCurrentRenderer()
{
    return gRenderer;
}

void SetCurrentRenderer(Renderer renderer)
{
    gRenderer = renderer;
}

void* GetCurrentDevice()
{
    return gDevice;
}

void SetCurrentDevice(void* device)
{
    gDevice = device;
}

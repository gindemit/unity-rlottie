#include "RendererCommon.h"

#include "LottieLogger.h"

Renderer gRenderer = Renderer::Unknown;
void* gDevice = nullptr;

Renderer ToRenderer(int deviceType)
{
    Renderer result;
    switch (deviceType)
    {
        case 2: // kUnityGfxRendererD3D11
            result = Renderer::D3D11;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: D3D11");
            break;
        case 18: // kUnityGfxRendererD3D12
            result = Renderer::D3D12;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: D3D12");
            break;
        case 0: // kUnityGfxRendererOpenGL
        case 17: // kUnityGfxRendererOpenGLCore
        case 8:  // kUnityGfxRendererOpenGLES20
        case 11: // kUnityGfxRendererOpenGLES30
            result = Renderer::OpenGL;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: OpenGL/GLES");
            break;
        case 16: // kUnityGfxRendererMetal
            result = Renderer::Metal;
            LottieLogInfo(nullptr, "[Lottie] Graphics device type: Metal");
            break;
        case 21: // kUnityGfxRendererVulkan
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

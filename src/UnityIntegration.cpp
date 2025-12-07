#include "UnityIntegration.h"

#if !defined(__EMSCRIPTEN__)

#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include "RendererCommon.h"
#include "UploadQueue.h"

#if defined(_WIN32)
#include "D3D11Backend_win.h"
#include "D3D12Backend_win.h"
#include "IUnityGraphicsD3D12.h"
#endif
#if defined(__APPLE__)
#include <TargetConditionals.h>
#endif
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#include "IUnityGraphicsMetal.h"
#include "MetalBackend_apple.h"
#endif
#if !defined(__APPLE__)
#include "OpenGLBackend.h"
#endif
#include "UploadPipeline.h"
#include "TextureBackend.h"

IUnityGraphics* sUnityGraphics = nullptr;
IUnityProfiler* sProfiler = nullptr;
const UnityProfilerMarkerDesc* sMkGetResult = nullptr;
const UnityProfilerMarkerDesc* sMkPublish = nullptr;
const UnityProfilerMarkerDesc* sMkUpload = nullptr;

static IUnityGraphicsD3D12v8* sD3D12v8 = nullptr;
static IUnityGraphicsD3D12v7* sD3D12 = nullptr;
static IUnityGraphicsD3D12v6* sD3D12v6 = nullptr;
static IUnityGraphicsD3D12v5* sD3D12v5 = nullptr;

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
static IUnityGraphicsMetalV2* sMetalV2 = nullptr;
static IUnityGraphicsMetalV1* sMetalV1 = nullptr;
#endif

void ProfBegin(const UnityProfilerMarkerDesc* d)
{
    if (sProfiler != nullptr && sProfiler->IsAvailable() && d != nullptr)
    {
        sProfiler->BeginSample(d);
    }
}

void ProfEnd(const UnityProfilerMarkerDesc* d)
{
    if (sProfiler != nullptr && sProfiler->IsAvailable() && d != nullptr)
    {
        sProfiler->EndSample(d);
    }
}

extern "C" void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    LottieLoggerSetUnityLog(unityInterfaces != nullptr ? unityInterfaces->Get<IUnityLog>() : nullptr);
    LottieLogInfo(nullptr, "[Lottie] Plugin loading...");
    sProfiler = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityProfiler>() : nullptr;
    if (sProfiler != nullptr && sProfiler->IsAvailable())
    {
        sProfiler->CreateMarker(&sMkGetResult, "Lottie/GetFutureResult", kUnityProfilerCategoryScripts, kUnityProfilerMarkerFlagDefault, 0);
        sProfiler->CreateMarker(&sMkPublish, "Lottie/PublishUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
        sProfiler->CreateMarker(&sMkUpload, "Lottie/PerformUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
    }

#if defined(_WIN32)
    sD3D12v8 = nullptr;
    sD3D12 = nullptr;
    sD3D12v6 = nullptr;
    sD3D12v5 = nullptr;
    if (unityInterfaces != nullptr)
    {
        sD3D12v8 = unityInterfaces->Get<IUnityGraphicsD3D12v8>();
        if (sD3D12v8 == nullptr)
        {
            sD3D12 = unityInterfaces->Get<IUnityGraphicsD3D12v7>();
        }
        if (sD3D12 == nullptr)
        {
            sD3D12v6 = unityInterfaces->Get<IUnityGraphicsD3D12v6>();
            if (sD3D12v6 == nullptr)
            {
                sD3D12v5 = unityInterfaces->Get<IUnityGraphicsD3D12v5>();
            }
        }
    }
#endif
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    sMetalV2 = nullptr;
    sMetalV1 = nullptr;
    if (unityInterfaces != nullptr)
    {
        sMetalV2 = unityInterfaces->Get<IUnityGraphicsMetalV2>();
        LottieLogInfo(nullptr, "[Lottie] IUnityGraphicsMetalV2: %s", sMetalV2 != nullptr ? "available" : "NOT available");
        if (sMetalV2 == nullptr)
        {
            sMetalV1 = unityInterfaces->Get<IUnityGraphicsMetalV1>();
            LottieLogInfo(nullptr, "[Lottie] IUnityGraphicsMetalV1: %s", sMetalV1 != nullptr ? "available" : "NOT available");
        }

        if (sMetalV2 != nullptr)
        {
            id<MTLDevice> device = sMetalV2->MetalDevice();
            LottieLogInfo(nullptr, "[Lottie] MetalV2->MetalDevice(): %s", device != nil ? "valid" : "nil");
        }
        else if (sMetalV1 != nullptr)
        {
            id<MTLDevice> device = sMetalV1->MetalDevice();
            LottieLogInfo(nullptr, "[Lottie] MetalV1->MetalDevice(): %s", device != nil ? "valid" : "nil");
        }
    }
    else
    {
        LottieLogWarning(nullptr, "[Lottie] unityInterfaces is null, cannot get Metal interface");
    }
#endif

    sUnityGraphics = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityGraphics>() : nullptr;
    LottieLogInfo(nullptr, "[Lottie] IUnityGraphics: %s", sUnityGraphics != nullptr ? "available" : "NOT available");

    if (sUnityGraphics != nullptr)
    {
        sUnityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
        LottieLogInfo(nullptr, "[Lottie] Registered graphics device event callback");

        ::UnityGfxRenderer currentRenderer = sUnityGraphics->GetRenderer();
        LottieLogInfo(nullptr, "[Lottie] Current renderer from sUnityGraphics->GetRenderer(): %d", (int)currentRenderer);

        if (currentRenderer != ::kUnityGfxRendererNull && gRenderer == Renderer::Unknown)
        {
            LottieLogInfo(nullptr, "[Lottie] Graphics device already initialized (renderer=%d), triggering init event", (int)currentRenderer);
            OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
            LottieLogInfo(nullptr, "[Lottie] After init event: gRenderer=%d", (int)gRenderer);
        }
        else if (currentRenderer == ::kUnityGfxRendererNull)
        {
            LottieLogInfo(nullptr, "[Lottie] Graphics device not yet initialized (renderer is null)");
        }
    }
    else
    {
        LottieLogWarning(nullptr, "[Lottie] Failed to get IUnityGraphics interface");
    }

    LottieLogInfo(nullptr, "[Lottie] Plugin loaded successfully (gRenderer=%d)", (int)gRenderer);
}

extern "C" void UNITY_INTERFACE_API UnityPluginUnload()
{
    LottieLogInfo(nullptr, "[Lottie] Plugin unloading...");

    if (sUnityGraphics != nullptr)
    {
        sUnityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);
        sUnityGraphics = nullptr;
    }

#if !defined(__EMSCRIPTEN__)
    ClearInstances();
    ClearPendingUploads();
#endif

    gDevice = nullptr;
    gRenderer = Renderer::Unknown;

    sProfiler = nullptr;
    sMkGetResult = nullptr;
    sMkPublish = nullptr;
    sMkUpload = nullptr;
#if defined(_WIN32)
    ClearD3D12Interfaces();
    ClearD3D11Interfaces();
#endif
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    ClearMetalDevice();
#endif
    LottieLogInfo(nullptr, "[Lottie] Plugin unloaded successfully");
    LottieLoggerSetUnityLog(nullptr);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
{
    if (eventType == kUnityGfxDeviceEventInitialize)
    {
        LottieLogInfo(nullptr, "[Lottie] OnGraphicsDeviceEvent: Initialize");

        if (sUnityGraphics != nullptr)
        {
            ::UnityGfxRenderer currentRenderer = sUnityGraphics->GetRenderer();
            gRenderer = ToRenderer(static_cast<int>(currentRenderer));
            LottieLogInfo(nullptr, "[Lottie] Graphics device type determined: %d", static_cast<int>(gRenderer));

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            if (gRenderer == Renderer::Metal)
            {
                if (sMetalV2 != nullptr)
                {
                    SetMetalDevice(sMetalV2->MetalDevice());
                    LottieLogInfo(nullptr, "[Lottie] Metal device set from IUnityGraphicsMetalV2");
                }
                else if (sMetalV1 != nullptr)
                {
                    SetMetalDevice(sMetalV1->MetalDevice());
                    LottieLogInfo(nullptr, "[Lottie] Metal device set from IUnityGraphicsMetalV1");
                }
            }
#endif
        }

        switch (gRenderer)
        {
            case Renderer::D3D12:
#if defined(_WIN32)
            {
                ID3D12Device* d3d12Device = reinterpret_cast<ID3D12Device*>(gDevice);
                ID3D12CommandQueue* queue = nullptr;
                if (sD3D12v8 != nullptr)
                {
                    queue = sD3D12v8->GetCommandQueue();
                }
                else if (sD3D12 != nullptr)
                {
                    queue = sD3D12->GetCommandQueue();
                }
                else if (sD3D12v6 != nullptr)
                {
                    queue = sD3D12v6->GetCommandQueue();
                }
                else if (sD3D12v5 != nullptr)
                {
                    queue = sD3D12v5->GetCommandQueue();
                }
                SetD3D12Interfaces(d3d12Device, queue, sD3D12v5, sD3D12v6, sD3D12, sD3D12v8);
            }
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                SetD3D11Interfaces(reinterpret_cast<ID3D11Device*>(gDevice), nullptr);
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                if (gDevice != nullptr && sMetalV2 == nullptr && sMetalV1 == nullptr)
                {
                    SetMetalDevice((__bridge id<MTLDevice>)gDevice);
                }
#endif
                break;
            case Renderer::OpenGL:
#if !defined(__APPLE__)
                DetectGLExtensions();
#endif
                break;
            case Renderer::Vulkan:
                LottieLogInfo(nullptr, "[Lottie] Vulkan device initialized");
                break;
            default:
#if !defined(__APPLE__)
                DetectGLExtensions();
#endif
                break;
        }
    }
    else if (eventType == kUnityGfxDeviceEventShutdown)
    {
        LottieLogInfo(nullptr, "[Lottie] Graphics device shutting down");
        gDevice = nullptr;
        switch (gRenderer)
        {
            case Renderer::D3D12:
#if defined(_WIN32)
                ClearD3D12Interfaces();
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                ClearD3D11Interfaces();
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                ClearMetalDevice();
#endif
                break;
            case Renderer::Vulkan:
                LottieLogInfo(nullptr, "[Lottie] Vulkan device shutdown");
                break;
            default:
                break;
        }
#if !defined(__APPLE__)
        gHasBGRAExt = false;
        gIsOpenGLES = false;
#endif
        ClearInstances();
        ClearPendingUploads();
        gRenderer = Renderer::Unknown;
    }
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnitySetGraphicsDevice(void* device, int deviceType, int eventType)
{
    LottieLogInfo(nullptr,
        "[Lottie] UnitySetGraphicsDevice: eventType=%d, deviceType=%d, device=%p",
        eventType, deviceType, device);

    if (eventType == ::kUnityGfxDeviceEventInitialize)
    {
        LottieLogInfo(nullptr, "[Lottie] Graphics device initializing");
        gRenderer = ToRenderer(deviceType);
        gDevice = device;
        switch (gRenderer)
        {
            case Renderer::D3D12:
#if defined(_WIN32)
            {
                ID3D12CommandQueue* queue = nullptr;
                if (sD3D12v8 != nullptr)
                {
                    queue = sD3D12v8->GetCommandQueue();
                }
                else if (sD3D12 != nullptr)
                {
                    queue = sD3D12->GetCommandQueue();
                }
                else if (sD3D12v6 != nullptr)
                {
                    queue = sD3D12v6->GetCommandQueue();
                }
                else if (sD3D12v5 != nullptr)
                {
                    queue = sD3D12v5->GetCommandQueue();
                }
                SetD3D12Interfaces(reinterpret_cast<ID3D12Device*>(device), queue, sD3D12v5, sD3D12v6, sD3D12, sD3D12v8);
            }
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                SetD3D11Interfaces(reinterpret_cast<ID3D11Device*>(device), nullptr);
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                if (sMetalV2 != nullptr)
                {
                    SetMetalDevice(sMetalV2->MetalDevice());
                }
                else if (sMetalV1 != nullptr)
                {
                    SetMetalDevice(sMetalV1->MetalDevice());
                }
                else if (device != nullptr)
                {
                    SetMetalDevice((__bridge id<MTLDevice>)device);
                }
#endif
                break;
            case Renderer::OpenGL:
#if !defined(__APPLE__)
                DetectGLExtensions();
#endif
                break;
            case Renderer::Vulkan:
                LottieLogInfo(nullptr, "[Lottie] Vulkan device initialized");
                break;
            default:
#if !defined(__APPLE__)
                DetectGLExtensions();
#endif
                break;
        }
    }
    else if (eventType == ::kUnityGfxDeviceEventShutdown)
    {
        LottieLogInfo(nullptr, "[Lottie] Graphics device shutting down");
        gDevice = nullptr;
        switch (gRenderer)
        {
            case Renderer::D3D12:
#if defined(_WIN32)
                ClearD3D12Interfaces();
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                ClearD3D11Interfaces();
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                ClearMetalDevice();
#endif
                break;
            case Renderer::Vulkan:
                LottieLogInfo(nullptr, "[Lottie] Vulkan device shutdown");
                break;
            default:
                break;
        }
#if !defined(__APPLE__)
        gHasBGRAExt = false;
        gIsOpenGLES = false;
#endif
        ClearInstances();
        ClearPendingUploads();
        gRenderer = Renderer::Unknown;
    }
}

#endif

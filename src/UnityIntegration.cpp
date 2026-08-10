#if !defined(__EMSCRIPTEN__)

// Platform headers must come first for Unity D3D12 headers
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#endif

#include "UnityIntegration.h"
#include "RendererCommon.h"
#include "InstanceRegistry.h"
#include "UploadQueue.h"
#include "LottieLogger.h"

#include "IUnityInterface.h"
#include "IUnityProfiler.h"
#include "IUnityGraphics.h"
#include "IUnityLog.h"
#include "VulkanBackend.h"

#if defined(_WIN32)
#include "D3D12Backend_win.h"
#include "D3D11Backend_win.h"
#include "OpenGLBackend.h"
#include "IUnityGraphicsD3D12.h"
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#include "MetalBackend_apple.h"
// Note: IUnityGraphicsMetal.h is only included in .mm files (MetalBackend_apple.mm)
// as it requires Objective-C compilation
#endif

#if defined(__ANDROID__)
#include "OpenGLBackend.h"
#endif

#if !defined(__APPLE__) && !defined(_WIN32) && !defined(__ANDROID__)
#include "OpenGLBackend.h"
#endif

namespace
{
    IUnityGraphics* sUnityGraphics = nullptr;
    IUnityProfiler* sProfiler = nullptr;
    const UnityProfilerMarkerDesc* sMkGetResult = nullptr;
    const UnityProfilerMarkerDesc* sMkPublish = nullptr;
    const UnityProfilerMarkerDesc* sMkUpload = nullptr;
}

IUnityGraphics* GetUnityGraphics()
{
    return sUnityGraphics;
}

const UnityProfilerMarkerDesc* GetProfilerMarkerGetResult()
{
    return sMkGetResult;
}

const UnityProfilerMarkerDesc* GetProfilerMarkerPublish()
{
    return sMkPublish;
}

const UnityProfilerMarkerDesc* GetProfilerMarkerUpload()
{
    return sMkUpload;
}

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

// Internal callback handler for graphics device events
static void UNITY_INTERFACE_API OnGraphicsDeviceEventInternal(UnityGfxDeviceEventType eventType);

void OnGraphicsDeviceEvent(int eventType)
{
    OnGraphicsDeviceEventInternal(static_cast<UnityGfxDeviceEventType>(eventType));
}

static void UNITY_INTERFACE_API OnGraphicsDeviceEventInternal(UnityGfxDeviceEventType eventType)
{
    if (eventType == kUnityGfxDeviceEventInitialize)
    {
        LottieLogInfo(nullptr, "[Lottie] OnGraphicsDeviceEvent: Initialize");

        // Get renderer type from IUnityGraphics
        if (sUnityGraphics != nullptr)
        {
            UnityGfxRenderer currentRenderer = sUnityGraphics->GetRenderer();
            SetCurrentRenderer(ToRenderer(static_cast<int>(currentRenderer)));
            LottieLogInfo(nullptr, "[Lottie] Graphics device type determined: %d", static_cast<int>(GetCurrentRenderer()));

            if (GetCurrentRenderer() == Renderer::Vulkan)
            {
                ConfigureVulkanUploadEvent();
            }

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            if (GetCurrentRenderer() == Renderer::Metal)
            {
                // Try to acquire Metal device (interfaces were set during UnityPluginLoad)
                TryAcquireMetalDevice();
                if (GetMetalDevice() == nullptr)
                {
                    LottieLogError(nullptr, "[Lottie] Failed to acquire Metal device");
                }
            }
#endif

#if defined(__ANDROID__) || defined(_WIN32)
            if (GetCurrentRenderer() == Renderer::OpenGL)
            {
                DetectGLExtensions();
            }
#endif
        }
    }
    else if (eventType == kUnityGfxDeviceEventShutdown)
    {
        LottieLogInfo(nullptr, "[Lottie] OnGraphicsDeviceEvent: Shutdown");
        SetCurrentDevice(nullptr);

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        ShutdownMetal();
#endif

#if defined(_WIN32)
        SetD3D12Queue(nullptr);
        SetD3D12Device(nullptr);
        ReleaseOwnedD3D12CommandList();
        ReleaseD3D11Context();
#endif

        SetCurrentRenderer(Renderer::Unknown);

#if defined(__ANDROID__) || defined(_WIN32)
        ResetGLExtensionState();
#endif

        ClearAllInstances();
        ClearUploadQueue();
        ShutdownVulkan();
    }
}

extern "C" EXPORT_API void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    // Early logging before Unity's logger is set (uses printf on iOS)
#if defined(__APPLE__)
    printf("[Lottie] UnityPluginLoad called (unityInterfaces=%p)\n", (void*)unityInterfaces);
    fflush(stdout);
#endif
    LottieLoggerSetUnityLog(unityInterfaces != nullptr ? unityInterfaces->Get<IUnityLog>() : nullptr);
    LottieLogInfo(nullptr, "[Lottie] Plugin loading...");
    sProfiler = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityProfiler>() : nullptr;
    InitializeVulkanFromUnity(unityInterfaces);
    if (sProfiler != nullptr && sProfiler->IsAvailable())
    {
        sProfiler->CreateMarker(&sMkGetResult, "Lottie/GetFutureResult", kUnityProfilerCategoryScripts, kUnityProfilerMarkerFlagDefault, 0);
        sProfiler->CreateMarker(&sMkPublish, "Lottie/PublishUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
        sProfiler->CreateMarker(&sMkUpload, "Lottie/PerformUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
    }

#if defined(_WIN32)
    if (unityInterfaces != nullptr)
    {
        IUnityGraphicsD3D12v8* d3d12v8 = unityInterfaces->Get<IUnityGraphicsD3D12v8>();
        IUnityGraphicsD3D12v7* d3d12v7 = nullptr;
        IUnityGraphicsD3D12v6* d3d12v6 = nullptr;
        IUnityGraphicsD3D12v5* d3d12v5 = nullptr;

        if (d3d12v8 == nullptr)
        {
            d3d12v7 = unityInterfaces->Get<IUnityGraphicsD3D12v7>();
        }
        if (d3d12v7 == nullptr)
        {
            d3d12v6 = unityInterfaces->Get<IUnityGraphicsD3D12v6>();
            if (d3d12v6 == nullptr)
            {
                d3d12v5 = unityInterfaces->Get<IUnityGraphicsD3D12v5>();
            }
        }
        SetD3D12Interfaces(d3d12v8, d3d12v7, d3d12v6, d3d12v5);
    }
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    // Initialize Metal backend - this gets the Metal interface from Unity
    // and acquires the device
    InitializeMetalFromUnity(unityInterfaces);
#endif

    // Get the IUnityGraphics interface and register for device event callbacks
    sUnityGraphics = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityGraphics>() : nullptr;
    LottieLogInfo(nullptr, "[Lottie] IUnityGraphics: %s", sUnityGraphics != nullptr ? "available" : "NOT available");

    if (sUnityGraphics != nullptr)
    {
        sUnityGraphics->RegisterDeviceEventCallback(OnGraphicsDeviceEventInternal);
        LottieLogInfo(nullptr, "[Lottie] Registered graphics device event callback");

        // Check if the graphics device is already initialized (we may have missed the init event)
        UnityGfxRenderer currentRenderer = sUnityGraphics->GetRenderer();
        LottieLogInfo(nullptr, "[Lottie] Current renderer from sUnityGraphics->GetRenderer(): %d", (int)currentRenderer);

        if (currentRenderer != kUnityGfxRendererNull && GetCurrentRenderer() == Renderer::Unknown)
        {
            LottieLogInfo(nullptr, "[Lottie] Graphics device already initialized (renderer=%d), triggering init event", (int)currentRenderer);
            OnGraphicsDeviceEventInternal(kUnityGfxDeviceEventInitialize);
            LottieLogInfo(nullptr, "[Lottie] After init event: gRenderer=%d", (int)GetCurrentRenderer());
        }
        else if (currentRenderer == kUnityGfxRendererNull)
        {
            LottieLogInfo(nullptr, "[Lottie] Graphics device not yet initialized (renderer is null)");
        }
    }
    else
    {
        LottieLogWarning(nullptr, "[Lottie] Failed to get IUnityGraphics interface");
    }

    LottieLogInfo(nullptr, "[Lottie] Plugin loaded successfully (gRenderer=%d)", (int)GetCurrentRenderer());
}

extern "C" EXPORT_API void UNITY_INTERFACE_API UnityPluginUnload()
{
    LottieLogInfo(nullptr, "[Lottie] Plugin unloading...");

    // Unregister graphics device event callback
    if (sUnityGraphics != nullptr)
    {
        sUnityGraphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEventInternal);
        sUnityGraphics = nullptr;
    }

    SetCurrentDevice(nullptr);
    SetCurrentRenderer(Renderer::Unknown);

#if defined(_WIN32)
    SetD3D12Queue(nullptr);
    SetD3D12Device(nullptr);
    ReleaseOwnedD3D12CommandList();
    ReleaseD3D11Context();
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    SetMetalDevice(nullptr);
#endif

    ClearAllInstances();
    ClearUploadQueue();
    ShutdownVulkan();

    sProfiler = nullptr;
    sMkGetResult = nullptr;
    sMkPublish = nullptr;
    sMkUpload = nullptr;

#if defined(_WIN32)
    SetD3D12Interfaces(nullptr, nullptr, nullptr, nullptr);
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    ShutdownMetal();
#endif

    LottieLogInfo(nullptr, "[Lottie] Plugin unloaded successfully");
    LottieLoggerSetUnityLog(nullptr);
}

#endif // !defined(__EMSCRIPTEN__)

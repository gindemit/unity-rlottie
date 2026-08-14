#if !defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"
#include "RendererCommon.h"
#include "InstanceRegistry.h"
#include "UploadQueue.h"
#include "UploadPipeline.h"
#include "UnityIntegration.h"
#include "LottieLogger.h"
#include "VulkanBackend.h"

#include "IUnityInterface.h"
#include "IUnityGraphics.h"

#if defined(_WIN32)
#include "D3D12Backend_win.h"
#include "D3D11Backend_win.h"
#include "OpenGLBackend.h"
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#include "MetalBackend_apple.h"
#endif

#if defined(__ANDROID__)
#include "OpenGLBackend.h"
#endif

#if !defined(__APPLE__) && !defined(_WIN32) && !defined(__ANDROID__)
#include "OpenGLBackend.h"
#endif

// Render event callback - called on the render thread
static void UNITY_INTERFACE_API OnRenderEvent(int /*eventID*/)
{
    lottie_animation_wrapper* animation = DequeueUpload();

    if (animation != nullptr)
    {
        // Treat the dequeued address as an opaque registry key until
        // PerformUploadFor acquires the instance lifetime lock.
        ProfBegin(GetProfilerMarkerUpload());
        PerformUploadFor(animation);
        ProfEnd(GetProfilerMarkerUpload());
    }
}

extern "C" {

EXPORT_API UnityRenderingEvent lottie_get_render_event_func(void)
{
    return OnRenderEvent;
}

// iOS static library registration helpers
// On iOS with IL2CPP, static libraries must be explicitly registered via UnityRegisterRenderingPluginV5
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
typedef void (UNITY_INTERFACE_API *UnityPluginLoadFunc)(IUnityInterfaces* unityInterfaces);
typedef void (UNITY_INTERFACE_API *UnityPluginUnloadFunc)(void);

UNITY_INTERFACE_EXPORT UnityPluginLoadFunc UNITY_INTERFACE_API lottie_get_plugin_load_func()
{
    printf("[Lottie] lottie_get_plugin_load_func called, returning UnityPluginLoad=%p\n", (void*)&UnityPluginLoad);
    fflush(stdout);
    return &UnityPluginLoad;
}

UNITY_INTERFACE_EXPORT UnityPluginUnloadFunc UNITY_INTERFACE_API lottie_get_plugin_unload_func()
{
    printf("[Lottie] lottie_get_plugin_unload_func called, returning UnityPluginUnload=%p\n", (void*)&UnityPluginUnload);
    fflush(stdout);
    return &UnityPluginUnload;
}
#endif

EXPORT_API void UNITY_INTERFACE_API UnitySetGraphicsDevice(void* device, int deviceType, int eventType)
{
    LottieLogInfo(nullptr,
        "[Lottie] UnitySetGraphicsDevice: eventType=%d, deviceType=%d, device=%p",
        eventType, deviceType, device);

    if (eventType == kUnityGfxDeviceEventInitialize)
    {
        LottieLogInfo(nullptr, "[Lottie] Graphics device initializing");
        SetCurrentRenderer(ToRenderer(deviceType));
        SetCurrentDevice(device);

        switch (GetCurrentRenderer())
        {
            case Renderer::D3D12:
#if defined(_WIN32)
                SetD3D12Device(reinterpret_cast<ID3D12Device*>(device));
                // Get command queue from D3D12 interface
                {
                    IUnityGraphicsD3D12v8* d3d12v8 = nullptr;
                    IUnityGraphicsD3D12v7* d3d12v7 = nullptr;
                    IUnityGraphicsD3D12v6* d3d12v6 = nullptr;
                    IUnityGraphicsD3D12v5* d3d12v5 = nullptr;
                    // These are cached from UnityPluginLoad, access them through extern functions
                    // For simplicity, we'll get the queue when needed
                }
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                {
                    ID3D11Device* d3dDevice = reinterpret_cast<ID3D11Device*>(device);
                    SetD3D11Device(d3dDevice);
                    if (d3dDevice)
                    {
                        ID3D11DeviceContext* context = nullptr;
                        d3dDevice->GetImmediateContext(&context);
                        SetD3D11Context(context);
                    }
                }
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                // Try to get Metal device from Unity's Metal interface (interfaces cached during UnityPluginLoad)
                {
                    void* metalDevice = TryAcquireMetalDevice();
                    if (metalDevice == nullptr && device != nullptr)
                    {
                        // Fallback to the device pointer passed by Unity
                        SetMetalDevice(device);
                        LottieLogInfo(nullptr, "[Lottie] Got Metal device from bridged pointer");
                    }

                    if (GetMetalDevice() == nullptr)
                    {
                        LottieLogError(nullptr, "[Lottie] Failed to acquire Metal device");
                    }
                }
#endif
                break;
            case Renderer::OpenGL:
#if defined(_WIN32) || defined(__ANDROID__)
                DetectGLExtensions();
#endif
                break;
            case Renderer::Vulkan:
                // ConfigureEvent is installed from Unity's graphics-device
                // initialize callback. Device entry points are acquired lazily.
                LottieLogInfo(nullptr, "[Lottie] Vulkan device initialized");
                break;
            default:
                // For unknown renderers, try to detect GL extensions in case it's OpenGL-based
#if defined(__ANDROID__) || defined(_WIN32)
                DetectGLExtensions();
#endif
                break;
        }
    }
    else if (eventType == kUnityGfxDeviceEventShutdown)
    {
        LottieLogInfo(nullptr, "[Lottie] Graphics device shutting down");
        SetCurrentDevice(nullptr);

        switch (GetCurrentRenderer())
        {
            case Renderer::D3D12:
#if defined(_WIN32)
                SetD3D12Queue(nullptr);
                SetD3D12Device(nullptr);
                ReleaseOwnedD3D12CommandList();
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                ReleaseD3D11Context();
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                SetMetalDevice(nullptr);
#endif
                break;
            case Renderer::Vulkan:
                LottieLogInfo(nullptr, "[Lottie] Vulkan device shutdown");
                break;
            default:
                break;
        }

        SetCurrentRenderer(Renderer::Unknown);

#if !defined(__APPLE__)
        ResetGLExtensionState();
#endif

        ClearAllInstances();
        ClearUploadQueue();
        ShutdownVulkan();
    }
}

EXPORT_API void UNITY_INTERFACE_API UnityRenderEvent(int eventID)
{
    OnRenderEvent(eventID);
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API lottie_unity_plugin_load(IUnityInterfaces* ifaces)
{
    UnityPluginLoad(ifaces);
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API lottie_unity_plugin_unload()
{
    UnityPluginUnload();
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API lottie_unity_set_graphics_device(void* device, int deviceType, int eventType)
{
    UnitySetGraphicsDevice(device, deviceType, eventType);
}

UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API lottie_unity_render_event(int eventID)
{
    UnityRenderEvent(eventID);
}

} // extern "C"

#endif // !defined(__EMSCRIPTEN__)

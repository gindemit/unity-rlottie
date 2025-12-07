#include "LottiePlugin.h"

#if !defined(__EMSCRIPTEN__)

#include "UnityIntegration.h"
#include "UploadPipeline.h"

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityRenderEvent(int eventID)
{
    OnRenderEvent(eventID);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_plugin_load(IUnityInterfaces* ifaces)
{
    UnityPluginLoad(ifaces);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_plugin_unload()
{
    UnityPluginUnload();
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_set_graphics_device(void* device, int deviceType, int eventType)
{
    UnitySetGraphicsDevice(device, deviceType, eventType);
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_render_event(int eventID)
{
    UnityRenderEvent(eventID);
}

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
typedef void (UNITY_INTERFACE_API *UnityPluginLoadFunc)(IUnityInterfaces* unityInterfaces);
typedef void (UNITY_INTERFACE_API *UnityPluginUnloadFunc)(void);

extern "C" UnityPluginLoadFunc UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_get_plugin_load_func()
{
    printf("[Lottie] lottie_get_plugin_load_func called, returning UnityPluginLoad=%p\n", (void*)&UnityPluginLoad);
    fflush(stdout);
    return &UnityPluginLoad;
}

extern "C" UnityPluginUnloadFunc UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_get_plugin_unload_func()
{
    printf("[Lottie] lottie_get_plugin_unload_func called, returning UnityPluginUnload=%p\n", (void*)&UnityPluginUnload);
    fflush(stdout);
    return &UnityPluginUnload;
}
#endif

#endif

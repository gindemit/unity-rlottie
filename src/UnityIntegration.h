#pragma once

#include "ExportApi.h"
#include "RendererCommon.h"

#if !defined(__EMSCRIPTEN__)
#include "IUnityInterface.h"
#include "IUnityProfiler.h"
#include "IUnityGraphics.h"

extern IUnityGraphics* sUnityGraphics;
extern IUnityProfiler* sProfiler;
extern const UnityProfilerMarkerDesc* sMkGetResult;
extern const UnityProfilerMarkerDesc* sMkPublish;
extern const UnityProfilerMarkerDesc* sMkUpload;

void ProfBegin(const UnityProfilerMarkerDesc* d);
void ProfEnd(const UnityProfilerMarkerDesc* d);

extern "C" void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces);
extern "C" void UNITY_INTERFACE_API UnityPluginUnload();
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnitySetGraphicsDevice(void* device, int deviceType, int eventType);
extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);
#endif

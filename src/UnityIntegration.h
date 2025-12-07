#ifndef UNITY_INTEGRATION_H
#define UNITY_INTEGRATION_H

#if !defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"

// Forward declarations
struct IUnityInterfaces;
struct IUnityGraphics;
struct IUnityProfiler;
struct UnityProfilerMarkerDesc;

// Unity plugin lifecycle
void UnityPluginLoad(IUnityInterfaces* unityInterfaces);
void UnityPluginUnload();

// Graphics device event callback
void OnGraphicsDeviceEvent(int eventType);

// Unity graphics interface accessor
IUnityGraphics* GetUnityGraphics();

// Profiler marker accessors
const UnityProfilerMarkerDesc* GetProfilerMarkerGetResult();
const UnityProfilerMarkerDesc* GetProfilerMarkerPublish();
const UnityProfilerMarkerDesc* GetProfilerMarkerUpload();

// Profiler helpers
void ProfBegin(const UnityProfilerMarkerDesc* d);
void ProfEnd(const UnityProfilerMarkerDesc* d);

#endif // !defined(__EMSCRIPTEN__)

#endif // UNITY_INTEGRATION_H

#ifndef UNITY_INTEGRATION_H
#define UNITY_INTEGRATION_H

#if !defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"
#include "IUnityInterface.h"

// Forward declarations
struct IUnityGraphics;
struct IUnityProfiler;
struct UnityProfilerMarkerDesc;

// Unity plugin lifecycle - declarations are already in IUnityInterface.h
// We just provide the implementation in UnityIntegration.cpp

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

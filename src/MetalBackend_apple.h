#ifndef METAL_BACKEND_APPLE_H
#define METAL_BACKEND_APPLE_H

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;
struct IUnityInterfaces;

// Metal device accessors (device is stored as void* to avoid ObjC in header)
void SetMetalDevice(void* device);
void* GetMetalDevice();

// Initialize Metal from Unity interfaces (called during plugin load)
// This handles acquiring the Metal device from IUnityGraphicsMetal interfaces
void InitializeMetalFromUnity(IUnityInterfaces* unityInterfaces);

// Try to acquire Metal device from cached Unity interfaces
void* TryAcquireMetalDevice();

// Reset Metal state (called during shutdown)
void ShutdownMetal();

// Metal texture operations
bool EnsureTextureMetal(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadMetal(InstanceState* state, const UploadContext& ctx);
void ResetTextureMetal(lottie_animation_wrapper* animation, InstanceState* state);

#endif // defined(__APPLE__) && !defined(__EMSCRIPTEN__)

#endif // METAL_BACKEND_APPLE_H

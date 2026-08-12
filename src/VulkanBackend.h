#ifndef VULKAN_BACKEND_H
#define VULKAN_BACKEND_H

#if !defined(__EMSCRIPTEN__)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;
struct IUnityInterfaces;

// Vulkan lifecycle and Unity-owned texture registration.
void InitializeVulkanFromUnity(IUnityInterfaces* unityInterfaces);
void ConfigureVulkanUploadEvent();
void ShutdownVulkan();
bool IsNativeVulkanUploadSupported();
bool RegisterUnityTextureVulkan(lottie_animation_wrapper* animation, void* nativeTexture, int width, int height);
bool IsVulkanUploadAvailable(lottie_animation_wrapper* animation);

// Vulkan texture operations.
bool EnsureTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
void UploadVulkan(InstanceState* state, const UploadContext& ctx);
bool PrepareRenderSlotVulkan(InstanceState* state, int slotIndex, uint32_t width, uint32_t height, uint8_t*& data, uint32_t& stride);
void RefreshCompletedRenderSlotsVulkan(InstanceState* state);
void BeginUploadEventVulkan(InstanceState* state);
void ResetTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state);

#endif // !defined(__EMSCRIPTEN__)

#endif // VULKAN_BACKEND_H

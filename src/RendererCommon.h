#ifndef RENDERER_COMMON_H
#define RENDERER_COMMON_H

#include "LottieLogger.h"
#include "IUnityGraphics.h"
#include <cstdint>

// Renderer enum for identifying graphics backend
enum class Renderer
{
    Unknown,
    D3D12,
    D3D11,
    Metal,
    OpenGL,
    Vulkan,
};

// Constant for deferred OpenGL texture creation on Windows
constexpr uintptr_t kDeferredGLTexDummy = 0x1;

// Convert Unity device type to Renderer enum
Renderer ToRenderer(int deviceType);

#if !defined(__EMSCRIPTEN__)
// Upload context for texture data transfer
struct UploadContext
{
    const uint8_t* data = nullptr;
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t stride = 0;
};
#endif

// Global renderer state accessors
Renderer GetCurrentRenderer();
void SetCurrentRenderer(Renderer renderer);
void* GetCurrentDevice();
void SetCurrentDevice(void* device);

#endif // RENDERER_COMMON_H

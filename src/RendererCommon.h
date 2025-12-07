#pragma once

#include "LottieLogger.h"

#include <cstdint>

enum class Renderer
{
    Unknown,
    D3D12,
    D3D11,
    Metal,
    OpenGL,
    Vulkan,
};

Renderer ToRenderer(int deviceType);

extern Renderer gRenderer;
extern void* gDevice;

#ifndef PIXEL_FORMAT_UTILS_H
#define PIXEL_FORMAT_UTILS_H

#include "RendererCommon.h"
#include <cstdint>
#include <cstddef>
#include <utility>
#include <vector>

#if !defined(__EMSCRIPTEN__)
// Convert BGRA to RGBA for OpenGL ES without BGRA extension support
void ConvertBGRAtoRGBA(std::vector<uint8_t>& buffer, const UploadContext& ctx);
#endif

// The validated 32-bit surface may have padding at the end of each row.
inline void ConvertBGRAtoRGBA(uint32_t* buffer, uint32_t width, uint32_t height, uint32_t stride) noexcept
{
    auto* bytes = reinterpret_cast<uint8_t*>(buffer);
    for (uint32_t y = 0; y < height; ++y)
    {
        uint8_t* pixel = bytes + static_cast<std::size_t>(y) * stride;
        for (uint32_t x = 0; x < width; ++x, pixel += 4)
        {
            std::swap(pixel[0], pixel[2]);
        }
    }
}

#endif // PIXEL_FORMAT_UTILS_H

#ifndef PIXEL_FORMAT_UTILS_H
#define PIXEL_FORMAT_UTILS_H

#include "RendererCommon.h"
#include <cstdint>
#include <vector>

#if !defined(__EMSCRIPTEN__)
// Convert BGRA to RGBA for OpenGL ES without BGRA extension support
void ConvertBGRAtoRGBA(std::vector<uint8_t>& buffer, const UploadContext& ctx);
#endif

#if defined(__EMSCRIPTEN__)
// Convert BGRA to RGBA in-place for WebGL (which doesn't support BGRA textures)
void ConvertBGRAtoRGBA(uint32_t* buffer, uint32_t width, uint32_t height);
#endif

#endif // PIXEL_FORMAT_UTILS_H

#pragma once

#include <cstdint>

#if defined(__EMSCRIPTEN__)
void ConvertBGRAtoRGBA(uint32_t* buffer, uint32_t width, uint32_t height);
#endif

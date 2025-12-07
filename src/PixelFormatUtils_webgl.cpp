#include "PixelFormatUtils_webgl.h"

#include "LottiePlugin.h"

#if defined(__EMSCRIPTEN__)

void ConvertBGRAtoRGBA(uint32_t* buffer, uint32_t width, uint32_t height)
{
    uint32_t pixelCount = width * height;
    for (uint32_t i = 0; i < pixelCount; ++i)
    {
        uint32_t pixel = buffer[i];
        uint8_t b = (pixel >> 0) & 0xFF;
        uint8_t g = (pixel >> 8) & 0xFF;
        uint8_t r = (pixel >> 16) & 0xFF;
        uint8_t a = (pixel >> 24) & 0xFF;
        buffer[i] = (a << 24) | (b << 16) | (g << 8) | r;
    }
}

#endif

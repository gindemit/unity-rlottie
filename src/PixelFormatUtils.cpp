#include "PixelFormatUtils.h"

#if defined(__ANDROID__) || defined(_WIN32) || (!defined(__EMSCRIPTEN__) && !defined(__APPLE__))
void ConvertBGRAtoRGBA(std::vector<uint8_t>& buffer, const UploadContext& ctx)
{
    buffer.resize(static_cast<size_t>(ctx.width) * static_cast<size_t>(ctx.height) * 4u);
    const uint8_t* src = ctx.data;
    uint8_t* dst = buffer.data();
    for (uint32_t y = 0; y < ctx.height; ++y)
    {
        for (uint32_t x = 0; x < ctx.width; ++x)
        {
            const size_t srcOffset = static_cast<size_t>(y) * ctx.stride + static_cast<size_t>(x) * 4u;
            const size_t dstOffset = (static_cast<size_t>(y) * ctx.width + x) * 4u;
            dst[dstOffset + 0] = src[srcOffset + 2];
            dst[dstOffset + 1] = src[srcOffset + 1];
            dst[dstOffset + 2] = src[srcOffset + 0];
            dst[dstOffset + 3] = src[srcOffset + 3];
        }
    }
}
#endif

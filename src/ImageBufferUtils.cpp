#include "ImageBufferUtils.h"

#include <cstring>
#include <limits>

bool TryGetImageBufferSize(
    uint32_t width,
    uint32_t height,
    uint32_t stride,
    ImageBufferSize& size) noexcept
{
    size = {};
    if (width == 0 || height == 0)
    {
        return false;
    }

    constexpr size_t bytesPerPixel = sizeof(uint32_t);
    if (static_cast<size_t>(width) > std::numeric_limits<size_t>::max() / bytesPerPixel)
    {
        return false;
    }

    const size_t rowBytes = static_cast<size_t>(width) * bytesPerPixel;
    if (static_cast<size_t>(stride) < rowBytes ||
        static_cast<size_t>(height) > std::numeric_limits<size_t>::max() / stride)
    {
        return false;
    }

    size.rowBytes = rowBytes;
    size.totalBytes = static_cast<size_t>(stride) * height;
    return true;
}

bool IsValidImageBuffer(
    const void* buffer,
    uint32_t width,
    uint32_t height,
    uint32_t stride) noexcept
{
    ImageBufferSize size{};
    return buffer != nullptr && TryGetImageBufferSize(width, height, stride, size);
}

bool CopyImageRows(
    void* destination,
    size_t destinationStride,
    const void* source,
    size_t sourceStride,
    uint32_t width,
    uint32_t height) noexcept
{
    if (destination == nullptr || source == nullptr || width == 0 || height == 0 ||
        static_cast<size_t>(width) > std::numeric_limits<size_t>::max() / sizeof(uint32_t))
    {
        return false;
    }

    const size_t rowBytes = static_cast<size_t>(width) * sizeof(uint32_t);
    if (destinationStride < rowBytes || sourceStride < rowBytes ||
        static_cast<size_t>(height) > std::numeric_limits<size_t>::max() / destinationStride ||
        static_cast<size_t>(height) > std::numeric_limits<size_t>::max() / sourceStride)
    {
        return false;
    }

    auto* dst = static_cast<uint8_t*>(destination);
    const auto* src = static_cast<const uint8_t*>(source);
    for (uint32_t row = 0; row < height; ++row)
    {
        std::memcpy(dst + static_cast<size_t>(row) * destinationStride,
                    src + static_cast<size_t>(row) * sourceStride,
                    rowBytes);
    }
    return true;
}

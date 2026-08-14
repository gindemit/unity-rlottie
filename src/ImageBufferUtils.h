#ifndef _IMAGE_BUFFER_UTILS_H_
#define _IMAGE_BUFFER_UTILS_H_

#include <cstddef>
#include <cstdint>

struct ImageBufferSize
{
    size_t rowBytes;
    size_t totalBytes;
};

// Validates a tightly packed 32-bit pixel row against the supplied stride and
// calculates sizes without allowing integer wraparound.
bool TryGetImageBufferSize(
    uint32_t width,
    uint32_t height,
    uint32_t stride,
    ImageBufferSize& size) noexcept;

bool IsValidImageBuffer(
    const void* buffer,
    uint32_t width,
    uint32_t height,
    uint32_t stride) noexcept;

// Copies only visible pixels. Padding in either row is intentionally ignored.
bool CopyImageRows(
    void* destination,
    size_t destinationStride,
    const void* source,
    size_t sourceStride,
    uint32_t width,
    uint32_t height) noexcept;

#endif // _IMAGE_BUFFER_UTILS_H_

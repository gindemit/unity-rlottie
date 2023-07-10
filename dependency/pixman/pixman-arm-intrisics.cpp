#include <arm_neon.h>

extern "C" void pixman_composite_src_n_8888_asm_neon(int32_t w, int32_t h,
                                                     uint32_t *dst,
                                                     int32_t   dst_stride,
                                                     uint32_t  src)
{
    // Create a 4-element vector with the same value
    uint32x4_t value = vdupq_n_u32(src);

    // calculate total length
    int total_len = w * h;

    // Perform the operation on blocks of 4 32-bit integers
    for (int i = 0; i < total_len; i += 4)
    {
        vst1q_u32(dst + i, value); // Store the vector to memory
    }

    // If the total length is not a multiple of 4, we need to finish the rest
    for (int i = total_len & ~3; i < total_len; ++i)
    {
        dst[i] = src;
    }
}

extern "C" void pixman_composite_over_n_8888_asm_neon(int32_t w, int32_t h,
                                                      uint32_t *dst,
                                                      int32_t   dst_stride,
                                                      uint32_t  color)
{
    // Calculate alpha
    int alpha = color >> 24;
    int ialpha = 255 - alpha;

    // Calculate total length
    int total_len = w * h;

    // Create 4-element vectors
    uint32x4_t colorVector = vdupq_n_u32(color);
    uint32x4_t ialphaVector = vdupq_n_u32(ialpha);

    // Perform the operation on blocks of 4 32-bit integers
    for (int i = 0; i < total_len; i += 4)
    {
        uint32x4_t dstVector = vld1q_u32(dst + i); // Load 4 integers from dst

        // Multiply dst[i] by ialpha and divide by 255
        uint32x4_t mulVector = vmulq_n_u32(dstVector, ialpha);
        mulVector = vaddq_u32(mulVector, vdupq_n_u32(255));
        mulVector = vshrq_n_u32(mulVector, 8); // Equivalent to / 255

        // Add color and store the result
        uint32x4_t resultVector = vaddq_u32(colorVector, mulVector);
        vst1q_u32(dst + i, resultVector); // Store the vector to memory
    }

    // If the total length is not a multiple of 4, we need to finish the rest
    for (int i = total_len & ~3; i < total_len; ++i)
    {
        dst[i] = color + ((dst[i] * ialpha + 255) / 255);
    }
}
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
                                                      uint32_t  src) {
    // Since neon intrinsics handle 128 bits at a time, or 4 pixels of 32 bits each,
    // we need to adjust the width accordingly.
    int adjusted_w = w / 4;
    for (int j = 0; j < h; ++j) {
        for (int i = 0; i < adjusted_w; ++i) {
            // Initialization. It seems to just duplicate the source value across vectors
            uint32x4_t v0 = vdupq_n_u32(src);
            uint32x4_t v1 = vdupq_n_u32(src);
            uint32x4_t v2 = vdupq_n_u32(src);
            uint32x4_t v3 = vdupq_n_u32(src);

            // Storing to destination
            vst1q_u32(dst + i * 4, v0);
            vst1q_u32(dst + i * 4 + 4, v1);
            vst1q_u32(dst + i * 4 + 8, v2);
            vst1q_u32(dst + i * 4 + 12, v3);
        }
        dst += dst_stride;
    }
}
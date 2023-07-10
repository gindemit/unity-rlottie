#include <arm_neon.h>

extern "C" void pixman_composite_src_n_8888_asm_neon(int32_t w, int32_t h,
                                                     uint32_t *dst,
                                                     int32_t   dst_stride,
                                                     uint32_t  src)
{
    uint8x8_t v_src = vdup_n_u8(src);

    for (int32_t y = 0; y < h; y++)
    {
        for (int32_t x = 0; x < w; x += 8)
        {
            vst1_u8((uint8_t *)(dst + x), v_src);
        }
        dst += dst_stride;
    }
}

extern "C" void pixman_composite_over_n_8888_asm_neon(int32_t w, int32_t h,
                                                      uint32_t *dst,
                                                      int32_t   dst_stride,
                                                      uint32_t  src)
{
    // Extract the source alpha and replicate it to all 16 lanes of a NEON vector
    uint8x16_t v_src_alpha = vdupq_n_u8(src >> 24);
    // Extract the source color and replicate it to all 16 lanes of a NEON vector
    uint8x16_t v_src_color = vdupq_n_u8(src);

    for (int32_t y = 0; y < h; y++)
    {
        for (int32_t x = 0; x < w; x += 16)  // Changed to 16 to match uint8x16_t
        {
            // Load 16 destination pixels
            uint8x16_t v_dst_color = vld1q_u8((uint8_t *)(dst + x));

            // Calculate the result color = source color * source alpha + destination color * (1 - source alpha)
            // Note that we need to shift right by 8 because the alpha blending operation can result in values greater than 255
            uint8x16_t v_res_color = vshrq_n_u8(vmlaq_u8(vmlsq_u8(v_dst_color, v_dst_color, v_src_alpha), v_src_color, v_src_alpha), 8);

            // Store the result to memory
            vst1q_u8((uint8_t *)(dst + x), v_res_color);
        }
        dst += dst_stride;
    }
}
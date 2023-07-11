#include <arm_neon.h>
#include <memory>

#define BYTE_MUL(c, a)                                  \
    ((((((c) >> 8) & 0x00ff00ff) * (a)) & 0xff00ff00) + \
     (((((c)&0x00ff00ff) * (a)) >> 8) & 0x00ff00ff))

extern "C" void pixman_composite_src_n_8888_asm_neon(int32_t w, int32_t h,
                                                     uint32_t *dst,
                                                     int32_t   dst_stride,
                                                     uint32_t  src)
{
    int total_len = w * h;
    memset(dst, src, total_len);
    // // Create a 4-element vector with the same value
    // uint32x4_t value = vdupq_n_u32(src);

    // // calculate total length
    // int total_len = w * h;

    // // Perform the operation on blocks of 4 32-bit integers
    // for (int i = 0; i < total_len; i += 4)
    // {
    //     vst1q_u32(dst + i, value); // Store the vector to memory
    // }

    // // If the total length is not a multiple of 4, we need to finish the rest
    // for (int i = total_len & ~3; i < total_len; ++i)
    // {
    //     dst[i] = src;
    // }
}

inline constexpr int vAlpha(uint32_t c)
{
    return c >> 24;
}

extern "C" void pixman_composite_over_n_8888_asm_neon(int32_t w, int32_t h,
                                                      uint32_t *dst,
                                                      int32_t   dst_stride,
                                                      uint32_t  color) {
    
    int total_len = w * h;
    int ialpha = 255 - vAlpha(color);
    for (int i = 0; i < total_len; ++i)
    {
      dst[i] = color + BYTE_MUL(dst[i], ialpha);
    }
    // // Calculate alpha
    // int alpha = color >> 24;
    // int ialpha = 255 - alpha;

    // // Calculate total length
    // int total_len = w * h;

    // // create a 4 element vector with the source color
    // uint32x4_t v_color = vdupq_n_u32(color);
    
    // // create a 4 element vector with the inverted alpha value
    // uint8x16_t v_ialpha = vdupq_n_u8(ialpha);
    
    // // iterate over the array 4 elements at a time
    // for (int i = 0; i < total_len; i += 4)
    // {
    //     // load 4 elements from the destination array
    //     uint32x4_t v_dest = vld1q_u32(&dst[i]);
        
    //     // unpack the destination colors into 8-bit elements
    //     uint8x16_t v_dest8 = vreinterpretq_u8_u32(v_dest);

    //     // multiply the destination color by the inverted alpha
    //     uint8x16_t v_dest_scaled = vmulq_u8(v_dest8, v_ialpha);
        
    //     // repack the result into 32-bit elements
    //     uint32x4_t v_dest_scaled32 = vreinterpretq_u32_u8(v_dest_scaled);
        
    //     // add the source color to the result
    //     uint32x4_t v_result = vaddq_u32(v_color, v_dest_scaled32);
        
    //     // store the result back into the destination array
    //     vst1q_u32(&dst[i], v_result);
    // }

    // // If the total length is not a multiple of 4, we need to finish the rest
    // for (int i = total_len & ~3; i < total_len; ++i)
    // {
    //     dst[i] = color + BYTE_MUL(dst[i], ialpha);
    // }
}
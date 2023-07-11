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
}
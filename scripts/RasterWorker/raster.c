#include <stdint.h>
#include <stddef.h>

typedef struct Lottie_Animation_S Animation;
extern Animation *lottie_animation_from_data(const char *, const char *, const char *);
extern void lottie_animation_destroy(Animation *);
extern size_t lottie_animation_get_totalframe(const Animation *);
extern void lottie_animation_render(Animation *, size_t, uint32_t *, size_t, size_t, size_t);

void *lottie_worker_create(const char *json) {
    // rlottie's C API caches by key. Each JSON content must identify its own model.
    return lottie_animation_from_data(json, json, "");
}
void lottie_worker_destroy(Animation *animation) { lottie_animation_destroy(animation); }
int lottie_worker_render(Animation *animation, int frame, uint8_t *pixels, int width, int height) {
    if (!animation || !pixels || frame < 0 || (size_t)frame >= lottie_animation_get_totalframe(animation) ||
        width < 1 || height < 1 || width > 4096 || height > 4096) return 0;
    // Return canonical premultiplied BGRA; the existing cache owns alpha conversion.
    lottie_animation_render(animation, frame, (uint32_t *)pixels, width, height, (size_t)width * 4);
    return 1;
}

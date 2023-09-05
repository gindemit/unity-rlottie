#ifndef _VORBIS_PLUGIN_H_
#define _VORBIS_PLUGIN_H_

#include "ExportApi.h"
#include <stdio.h>
#include <stdlib.h>
#include <rlottie.h>

typedef struct lottie_animation_wrapper {
    lottie_animation_wrapper *self;
    std::unique_ptr<rlottie::Animation> animation;
    double frameRate;
    int64_t totalFrame;
    double duration;
    int64_t width;
    int64_t height;
} lottie_animation_wrapper;

typedef struct lottie_render_data {
    uint32_t *buffer;
    uint32_t width;
    uint32_t height;
    uint32_t bytesPerLine;
    std::future<rlottie::Surface> render_future;
} lottie_render_data;

extern "C" {
    EXPORT_API int32_t lottie_load_from_data(
        const char* json_data,
        const char* resource_path,
        lottie_animation_wrapper** animation_wrapper);
    EXPORT_API int32_t lottie_load_from_file(
        const char *file_path,
        lottie_animation_wrapper **animation_wrapper);
    EXPORT_API int32_t lottie_dispose_wrapper(lottie_animation_wrapper **animation_wrapper);
    EXPORT_API int32_t lottie_render_immediately(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio);

    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio);
    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data);

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data);
    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data);

    EXPORT_API int32_t initialize_logger(
        const char* log_dir_path,
        const char* log_file_name,
        int32_t log_file_roll_size_mb);
}

#endif // !_VORBIS_PLUGIN_H_
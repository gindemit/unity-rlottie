#ifndef _VORBIS_PLUGIN_H_
#define _VORBIS_PLUGIN_H_

#include "ExportApi.h"
#include <stdio.h>
#include <stdlib.h>
#include <future>
#include <rlottie.h>

// Provide Unity macro fallbacks only when the Unity PluginAPI is unavailable.
#if !defined(HAVE_UNITY_PLUGINAPI)
#   ifndef UNITY_INTERFACE_API
#       if defined(_MSC_VER)
#           define UNITY_INTERFACE_API __stdcall
#       else
#           define UNITY_INTERFACE_API
#       endif
#   endif

#   ifndef UNITY_INTERFACE_EXPORT
#       if defined(_MSC_VER)
#           define UNITY_INTERFACE_EXPORT __declspec(dllexport)
#       else
#           define UNITY_INTERFACE_EXPORT __attribute__((visibility("default")))
#       endif
#   endif
#endif

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
#if !defined(__EMSCRIPTEN__)
    std::future<rlottie::Surface> render_future;
#endif
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
    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        int32_t* ready);
    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data);

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data);
    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data);

    EXPORT_API int32_t lottie_initialize_logger(
        const char* log_dir_path,
        const char* log_file_name,
        int32_t log_file_roll_size_mb);

    // GPU texture upload helpers
    typedef void (UNITY_INTERFACE_API *UnityRenderingEvent)(int eventID);

    EXPORT_API void* lottie_create_texture(int width, int height);
    EXPORT_API void  lottie_destroy_texture(void* tex);
    EXPORT_API void* lottie_get_native_texture_ptr(void);
    EXPORT_API int   lottie_bind_lottie_instance(lottie_animation_wrapper* animation_wrapper);
    EXPORT_API void  lottie_update_texture(void);
    EXPORT_API UnityRenderingEvent lottie_get_render_event_func(void);
}

#endif // !_VORBIS_PLUGIN_H_

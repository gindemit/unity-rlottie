#ifndef _LOTTIE_PLUGIN_H_
#define _LOTTIE_PLUGIN_H_

#include "ExportApi.h"
#include <stdio.h>
#include <stdlib.h>
#include <future>
#include <rlottie.h>

// --- Unity PluginAPI integration -------------------------------------------
// If we build with the official Unity PluginAPI headers, include them here so
// UNITY_INTERFACE_API is defined BEFORE we typedef UnityRenderingEvent.
#if !defined(__EMSCRIPTEN__)
#   include "IUnityInterface.h"
#endif

// Provide portable fallbacks when PluginAPI headers are not available.
#if !defined(UNITY_INTERFACE_API)
#   if defined(_MSC_VER)
#       define UNITY_INTERFACE_API __stdcall
#   else
#       define UNITY_INTERFACE_API
#   endif
#endif

#if !defined(UNITY_INTERFACE_EXPORT)
#   if defined(_MSC_VER)
#       define UNITY_INTERFACE_EXPORT __declspec(dllexport)
#   else
#       define UNITY_INTERFACE_EXPORT __attribute__((visibility("default")))
#   endif
#endif
// --------------------------------------------------------------------------

// Log level enum matching industry standards
typedef enum {
    LOTTIE_LOG_NONE = 0,
    LOTTIE_LOG_ERROR = 1,
    LOTTIE_LOG_WARNING = 2,
    LOTTIE_LOG_INFO = 3
} LottieLogLevel;

typedef struct lottie_animation_wrapper {
    lottie_animation_wrapper *self;
    std::unique_ptr<rlottie::Animation> animation;
    double frameRate;
    int64_t totalFrame;
    double duration;
    int64_t width;
    int64_t height;
    LottieLogLevel logLevel;
} lottie_animation_wrapper;

typedef struct lottie_render_data {
    uint32_t *buffer;
    uint32_t width;
    uint32_t height;
    uint32_t bytesPerLine;
#if !defined(__EMSCRIPTEN__)
    std::future<rlottie::Surface> render_future;
    lottie_animation_wrapper* render_pool_owner = nullptr;
    int render_pool_slot = -1;
    uint32_t* external_buffer = nullptr;
    uint32_t external_bytes_per_line = 0;
    bool render_skipped = false;
    void* render_pool_state = nullptr;
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
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba);

    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba);
    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        int32_t* ready);
    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data);

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data);
    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data);
    
    EXPORT_API int32_t lottie_set_log_level(
        lottie_animation_wrapper* animation_wrapper,
        LottieLogLevel log_level);
    
    EXPORT_API int32_t lottie_set_global_log_level(LottieLogLevel log_level);

    // Match Unity's expected render-event callback signature. Now that
    // UNITY_INTERFACE_API is guaranteed to be defined, this typedef parses cleanly
    // on all compilers (MSVC/Clang/GCC/ObjC++).
    typedef void (UNITY_INTERFACE_API *UnityRenderingEvent)(int eventID);

    EXPORT_API void* lottie_create_texture(lottie_animation_wrapper* animation, int width, int height);
    EXPORT_API void* lottie_create_texture_with_color_space(
        lottie_animation_wrapper* animation,
        int width,
        int height,
        bool prefer_srgb_sampling);
    EXPORT_API void  lottie_destroy_texture(lottie_animation_wrapper* animation, void* tex);
    EXPORT_API void* lottie_get_native_texture_ptr(lottie_animation_wrapper* animation);
    EXPORT_API void  lottie_update_texture(lottie_animation_wrapper* animation);
    EXPORT_API int32_t lottie_is_native_vulkan_backend_compiled(void);
    EXPORT_API int32_t lottie_supports_native_vulkan_upload(void);
    EXPORT_API int32_t lottie_register_unity_vulkan_texture(
        lottie_animation_wrapper* animation,
        void* native_texture,
        int width,
        int height);
    EXPORT_API int32_t lottie_is_vulkan_upload_available(lottie_animation_wrapper* animation);
    EXPORT_API int32_t lottie_register_unity_opengl_texture(
        lottie_animation_wrapper* animation,
        void* native_texture,
        int width,
        int height);
    EXPORT_API int32_t lottie_is_opengl_upload_available(lottie_animation_wrapper* animation);
    EXPORT_API UnityRenderingEvent lottie_get_render_event_func(void);
}

#endif // !_LOTTIE_PLUGIN_H_

#include "LottiePlugin.h"

extern "C" {

    static lottie_animation_wrapper* create_animation_wrapper(std::unique_ptr<rlottie::Animation>& animation)
    {
        lottie_animation_wrapper *animation_wrapper = new lottie_animation_wrapper();
        animation_wrapper->self = animation_wrapper;
        animation_wrapper->frameRate = animation->frameRate();
        animation_wrapper->totalFrame = animation->totalFrame();
        animation_wrapper->duration = animation->duration();
        size_t width = 0;
        size_t height = 0;
        animation->size(width, height);
        animation_wrapper->width = width;
        animation_wrapper->height = height;
        animation_wrapper->animation = std::move(animation);
        return animation_wrapper;
    }

    EXPORT_API int32_t lottie_load_from_data(const char* json_data, const char* resource_path, lottie_animation_wrapper** animation_wrapper) {
        const std::function<void(float& r, float& g, float& b)>& null_func = nullptr;
        auto animation = rlottie::Animation::loadFromData(std::string(json_data), std::string(resource_path), null_func);
        *animation_wrapper = create_animation_wrapper(animation);
        return 0;
    }
    EXPORT_API int32_t lottie_load_from_file(const char* file_path, lottie_animation_wrapper** animation_wrapper) {
        auto animation = rlottie::Animation::loadFromFile(std::string(file_path));
        *animation_wrapper = create_animation_wrapper(animation);
        return 0;
    }
    EXPORT_API int32_t lottie_dispose_wrapper(lottie_animation_wrapper** animation_wrapper) {
        delete (*animation_wrapper);
        *animation_wrapper = nullptr;
        return 0;
    }

    EXPORT_API int32_t lottie_render_immediately(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio) {
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
        return 0;
    }


    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio) {
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        render_data->render_future = animation_wrapper->animation->render(frame_number, surface, keep_aspect_ratio);
        return 0;
    }
    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data) {
        render_data->render_future.get();
        return 0;
    }

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data) {
        *render_data = new lottie_render_data();
        return 0;
    }
    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data) {
        delete (*render_data);
        *render_data = nullptr;
        return 0;
    }
}
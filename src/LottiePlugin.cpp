#include "LottiePlugin.h"

extern "C" {
    EXPORT_API int32_t lottie_load_from_file(const char* file_path, lottie_animation_wrapper** animation_wrapper) {
        *animation_wrapper = new lottie_animation_wrapper();
        (*animation_wrapper)->self = *animation_wrapper;
        auto animation = rlottie::Animation::loadFromFile(std::string(file_path));
        double frameRate = animation->frameRate();
        size_t totalFrame = animation->totalFrame();
        double duration = animation->duration();
        (*animation_wrapper)->animation = std::move(animation);
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
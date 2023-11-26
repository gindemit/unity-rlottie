#include "LottiePlugin.h"
#include "vdebug.h"

extern "C" {

    static lottie_animation_wrapper* create_animation_wrapper(std::unique_ptr<rlottie::Animation>& animation)
    {
        lottie_animation_wrapper *animation_wrapper = new lottie_animation_wrapper();

        if (animation_wrapper == nullptr){

            fprintf(stderr, "Couldnt allocate lottie_animation_wrapper!");
            return nullptr;
        }

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

    EXPORT_API int32_t lottie_load_from_data(
      const char* json_data,
      const char* resource_path,
      lottie_animation_wrapper** animation_wrapper) {
        const std::function<void(float& r, float& g, float& b)>& null_func = nullptr;
        auto animation = rlottie::Animation::loadFromData(std::string(json_data), std::string(resource_path), null_func);
        if(!animation) {
            fprintf(stderr, "Couldnt load from data '%s'.", resource_path);
            return -1;
        }
        *animation_wrapper = create_animation_wrapper(animation);
        return *animation_wrapper == nullptr ? -1 : 0;
    }
    EXPORT_API int32_t lottie_load_from_file(
      const char* file_path,
      lottie_animation_wrapper** animation_wrapper) {
        auto animation = rlottie::Animation::loadFromFile(std::string(file_path));

        if(!animation) {
            fprintf(stderr, "Couldnt load from file '%s'.", file_path);
            return -1;
        }

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
         if (*render_data == nullptr){

            fprintf(stderr, "Couldnt allocate lottie_render_data!");
            return -1;
        }
        return 0;
    }
    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data) {
        delete (*render_data);
        *render_data = nullptr;
        return 0;
    }
    EXPORT_API int32_t initialize_logger(const char* log_dir_path, const char* log_file_name, int32_t log_file_roll_size_mb) {
        fprintf(stderr, "Initializing logger (stderr)\n");
        // print the paths
        fprintf(stderr, "log_dir_path: %s\n", log_dir_path);
        fprintf(stderr, "log_file_name: %s\n", log_file_name);
        fprintf(stderr, "log_file_roll_size_mb: %d\n", log_file_roll_size_mb);
        fprintf(stdout, "Initializing logger (stdout)\n");
        initialize(GuaranteedLogger(), std::string(log_dir_path), std::string(log_file_name), log_file_roll_size_mb);
        set_log_level(LogLevel::INFO);

        vDebug << "Initialized logger (debug) test message";
        vWarning << "Initialized logger (warning) test message";
        vCritical << "Initialized logger (critical) test message";
        // print the paths
        vDebug << "log_dir_path: " << log_dir_path;
        vDebug << "log_file_name: " << log_file_name;
        vDebug << "log_file_roll_size_mb: " << log_file_roll_size_mb;
        return 0;
    }
}

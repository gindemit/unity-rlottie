#include "LottiePlugin.h"

#include "LottieLogger.h"
#include "RendererCommon.h"
#include "InstanceRegistry.h"
#include "UploadPipeline.h"
#include "UnityIntegration.h"
#include "TextureBackend.h"
#include "UploadQueue.h"
#include "PixelFormatUtils.h"
#include "PixelFormatUtils_webgl.h"
#include "vdebug.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

#if defined(__APPLE__)
#    include <TargetConditionals.h>
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#    import <Metal/Metal.h>
#endif

static lottie_animation_wrapper* create_animation_wrapper(std::unique_ptr<rlottie::Animation>& animation)
{
    lottie_animation_wrapper* animation_wrapper = new lottie_animation_wrapper();

    if (animation_wrapper == nullptr)
    {
        fprintf(stderr, "Couldnt allocate lottie_animation_wrapper!");
        LottieLogError(nullptr, "[Lottie] Failed to allocate lottie_animation_wrapper");
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
    animation_wrapper->logLevel = LottieGetGlobalLogLevel();
    LottieLogInfo(animation_wrapper, "[Lottie] Created animation wrapper: width=%lld, height=%lld, fps=%.2f, frames=%lld, duration=%.2fs",
                 (long long)animation_wrapper->width, (long long)animation_wrapper->height,
                 animation_wrapper->frameRate, (long long)animation_wrapper->totalFrame,
                 animation_wrapper->duration);
    return animation_wrapper;
}

extern "C"
{
    EXPORT_API int32_t lottie_load_from_data(
        const char* json_data,
        const char* resource_path,
        lottie_animation_wrapper** animation_wrapper)
    {
        const char* path_display = (resource_path == nullptr) ? "(null)" :
                                    (resource_path[0] == '\0') ? "(empty)" : resource_path;
        LottieLogInfo(nullptr, "[Lottie] Loading animation from data, resource_path='%s'", path_display);
        const std::function<void(float& r, float& g, float& b)>& null_func = nullptr;
        auto animation = rlottie::Animation::loadFromData(std::string(json_data), std::string(resource_path), null_func);
        if (!animation)
        {
            fprintf(stderr, "Couldnt load from data '%s'.", resource_path);
            LottieLogError(nullptr, "[Lottie] Failed to load animation from data");
            return -1;
        }
        *animation_wrapper = create_animation_wrapper(animation);
        LottieLogInfo(*animation_wrapper, "[Lottie] Successfully loaded animation from data");
        return *animation_wrapper == nullptr ? -1 : 0;
    }

    EXPORT_API int32_t lottie_load_from_file(
        const char* file_path,
        lottie_animation_wrapper** animation_wrapper)
    {
        LottieLogInfo(nullptr, "[Lottie] Loading animation from file: %s", file_path ? file_path : "(null)");
        auto animation = rlottie::Animation::loadFromFile(std::string(file_path));

        if (!animation)
        {
            fprintf(stderr, "Couldnt load from file '%s'.", file_path);
            LottieLogError(nullptr, "[Lottie] Failed to load animation from file");
            return -1;
        }

        *animation_wrapper = create_animation_wrapper(animation);
        LottieLogInfo(*animation_wrapper, "[Lottie] Successfully loaded animation from file");
        return 0;
    }

    EXPORT_API int32_t lottie_dispose_wrapper(lottie_animation_wrapper** animation_wrapper)
    {
        LottieLogInfo(animation_wrapper ? *animation_wrapper : nullptr, "[Lottie] Disposing animation wrapper");
#if !defined(__EMSCRIPTEN__)
        if (animation_wrapper != nullptr && *animation_wrapper != nullptr)
        {
            {
                std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
                auto it = gInstances.find(*animation_wrapper);
                if (it != gInstances.end())
                {
                    ResetTextureState(*animation_wrapper, it->second.get());
                    gInstances.erase(it);
                }
            }
        }
#endif
        delete (*animation_wrapper);
        *animation_wrapper = nullptr;
        LottieLogInfo(nullptr, "[Lottie] Animation wrapper disposed successfully");
        return 0;
    }

#if defined(__EMSCRIPTEN__)
    EXPORT_API int32_t lottie_render_immediately(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] lottie_render_immediately called for frame %u", frame_number);
        LottieLogInfo(animation_wrapper, "[WebGL] render_data: buffer=%p, width=%u, height=%u, bytesPerLine=%u",
            render_data->buffer, render_data->width, render_data->height, render_data->bytesPerLine);

        if (render_data->buffer == nullptr)
        {
            LottieLogError(animation_wrapper, "[WebGL] ERROR: render_data->buffer is NULL!");
            return -1;
        }
        if (animation_wrapper == nullptr || animation_wrapper->animation == nullptr)
        {
            LottieLogError(animation_wrapper, "[WebGL] ERROR: animation_wrapper or animation is NULL!");
            return -1;
        }

        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);

        if (convert_bgra_to_rgba)
        {
            LottieLogInfo(animation_wrapper, "[WebGL] renderSync completed, converting BGRA to RGBA...");
            ConvertBGRAtoRGBA(render_data->buffer, render_data->width, render_data->height);
            LottieLogInfo(animation_wrapper, "[WebGL] lottie_render_immediately complete");
        }
        else
        {
            LottieLogInfo(animation_wrapper, "[WebGL] renderSync completed, skipping BGRA to RGBA conversion (shader mode)");
        }
        LottieLogInfo(animation_wrapper, "[Lottie] Frame rendered successfully");
        return 0;
    }

    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        LottieLogInfo(animation_wrapper, "[WebGL] lottie_render_create_future_async called for frame %u", frame_number);
        (void)convert_bgra_to_rgba;

        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        render_data->render_future = animation_wrapper->animation->render(frame_number, surface, keep_aspect_ratio);
        LottieLogInfo(animation_wrapper, "[WebGL] Async render future created");
        return 0;
    }

    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        int32_t* ready)
    {
        if (render_data == nullptr || ready == nullptr)
        {
            LottieLogWarning(animation_wrapper, "[Lottie] try_get_future_result called with null parameters");
            return -1;
        }

        *ready = 0;

        if (!render_data->render_future.valid() ||
            render_data->render_future.wait_for(std::chrono::milliseconds(0)) != std::future_status::ready)
        {
            return 0;
        }

        LottieLogInfo(animation_wrapper, "[Lottie] Render future ready, getting result");
        render_data->render_future.get();

        *ready = 1;
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and published");
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] Waiting for render future result");
        render_data->render_future.get();
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and uploaded");
        return 0;
    }
#else
    EXPORT_API int32_t lottie_render_immediately(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        (void)convert_bgra_to_rgba;
        LottieLogInfo(animation_wrapper, "[Lottie] lottie_render_immediately called for frame %u", frame_number);
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
        PublishUpload(animation_wrapper, render_data);
        LottieLogInfo(animation_wrapper, "[Lottie] Frame rendered successfully");
        return 0;
    }

    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        (void)convert_bgra_to_rgba;
        LottieLogInfo(animation_wrapper, "[Lottie] Creating async render future for frame %u", frame_number);
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        render_data->render_future = animation_wrapper->animation->render(frame_number, surface, keep_aspect_ratio);
        LottieLogInfo(animation_wrapper, "[Lottie] Async render future created");
        return 0;
    }

    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        int32_t* ready)
    {
        if (render_data == nullptr || ready == nullptr)
        {
            LottieLogWarning(animation_wrapper, "[Lottie] try_get_future_result called with null parameters");
            return -1;
        }

        *ready = 0;

        if (!render_data->render_future.valid() ||
            render_data->render_future.wait_for(std::chrono::milliseconds(0)) != std::future_status::ready)
        {
            return 0;
        }

        LottieLogInfo(animation_wrapper, "[Lottie] Render future ready, getting result");
        render_data->render_future.get();

        ProfBegin(sMkPublish);
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(sMkPublish);

        *ready = 1;
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and published");
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] Waiting for render future result");
        ProfBegin(sMkGetResult);
        render_data->render_future.get();
        ProfEnd(sMkGetResult);

        ProfBegin(sMkPublish);
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(sMkPublish);
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and uploaded");
        return 0;
    }
#endif

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data)
    {
        LottieLogInfo(nullptr, "[Lottie] Allocating render data");
        *render_data = new lottie_render_data();
        if (*render_data == nullptr)
        {
            fprintf(stderr, "Couldnt allocate lottie_render_data!");
            LottieLogError(nullptr, "[Lottie] Failed to allocate render data");
            return -1;
        }
        LottieLogInfo(nullptr, "[Lottie] Render data allocated successfully");
        return 0;
    }

    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data)
    {
        LottieLogInfo(nullptr, "[Lottie] Disposing render data");
        delete (*render_data);
        *render_data = nullptr;
        return 0;
    }

    EXPORT_API int32_t lottie_set_log_level(
        lottie_animation_wrapper* animation_wrapper,
        LottieLogLevel log_level)
    {
        if (animation_wrapper != nullptr)
        {
            animation_wrapper->logLevel = log_level;
        }
        else
        {
            LottieSetGlobalLogLevel(log_level);
        }
        return 0;
    }

    EXPORT_API int32_t lottie_set_global_log_level(LottieLogLevel log_level)
    {
        LottieSetGlobalLogLevel(log_level);
        LottieLogInfo(nullptr, "[Lottie] Global log level changed to %d", (int)log_level);
        return 0;
    }

#if !defined(__EMSCRIPTEN__)
    EXPORT_API void* lottie_create_texture(lottie_animation_wrapper* animation, int width, int height)
    {
#if defined(__APPLE__)
        printf("[Lottie] lottie_create_texture called: width=%d, height=%d, renderer=%d, sUnityGraphics=%p\n",
               width, height, (int)gRenderer, (void*)sUnityGraphics);
        fflush(stdout);
#endif

        LottieLogInfo(animation,
            "[Lottie] Creating texture: width=%d, height=%d (renderer=%d, sUnityGraphics=%p)",
            width, height, (int)gRenderer, (void*)sUnityGraphics);

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        printf("[Lottie] Metal state: device pointer=%p\n", (__bridge void*)nullptr);
        fflush(stdout);
#endif

        InstanceState* state = GetState(animation);

        if (gRenderer == Renderer::Unknown && sUnityGraphics != nullptr)
        {
            LottieLogInfo(animation, "[Lottie] Attempting lazy graphics device initialization");
            ::UnityGfxRenderer currentRenderer = sUnityGraphics->GetRenderer();
            LottieLogInfo(animation, "[Lottie] sUnityGraphics->GetRenderer() = %d", (int)currentRenderer);
            if (currentRenderer != ::kUnityGfxRendererNull)
            {
                OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
                LottieLogInfo(animation, "[Lottie] Lazy initialization completed, renderer=%d", (int)gRenderer);
            }
            else
            {
                LottieLogWarning(animation, "[Lottie] sUnityGraphics->GetRenderer() returned kUnityGfxRendererNull");
            }
        }
        else if (gRenderer == Renderer::Unknown)
        {
            LottieLogWarning(animation, "[Lottie] gRenderer is Unknown and sUnityGraphics is null");
        }

        if (gRenderer == Renderer::Unknown)
        {
            LottieLogWarning(animation,
                "[Lottie] lottie_create_texture called before graphics device is initialized");
            return nullptr;
        }

        if (gRenderer == Renderer::OpenGL)
        {
#if defined(_WIN32)
            state->texW = width;
            state->texH = height;
            state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(0x1));

            LottieLogInfo(animation,
                "[Lottie] OpenGL (Windows): deferring texture creation; dummy nativeTex=%p",
                state->nativeTex);

            return state->nativeTex;
#else
            if (!EnsureTextureForRenderer(animation, state, width, height))
            {
                LottieLogError(animation,
                    "[Lottie] OpenGL non-Windows: EnsureTexture failed in lottie_create_texture");
                return nullptr;
            }

            LottieLogInfo(animation,
                "[Lottie] OpenGL non-Windows: Texture created immediately, nativeTex=%p",
                state ? state->nativeTex : nullptr);

            return state != nullptr ? state->nativeTex : nullptr;
#endif
        }

        if (!EnsureTextureForRenderer(animation, state, width, height))
        {
            LottieLogError(animation,
                "[Lottie] lottie_create_texture: EnsureTexture failed for renderer=%d",
                (int)gRenderer);
            return nullptr;
        }

        LottieLogInfo(animation,
            "[Lottie] lottie_create_texture: texture ready, nativeTex=%p, texW=%d, texH=%d",
            state ? state->nativeTex : nullptr,
            state ? state->texW : 0,
            state ? state->texH : 0);

        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void lottie_destroy_texture(lottie_animation_wrapper* animation, void* /*tex*/)
    {
        LottieLogInfo(animation, "[Lottie] Destroying texture");
        InstanceState* state = GetState(animation, /*create=*/false);
        ResetTextureState(animation, state);
    }

    EXPORT_API void* lottie_get_native_texture_ptr(lottie_animation_wrapper* animation)
    {
        InstanceState* state = GetState(animation, /*create=*/false);
        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void lottie_update_texture(lottie_animation_wrapper* animation)
    {
        if (animation == nullptr)
        {
            LottieLogWarning(nullptr, "[Lottie] update_texture: animation is null");
            return;
        }

        InstanceState* state = GetState(animation, /*create=*/false);
        if (state == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] update_texture: no instance state");
            return;
        }

        state->requestedVersion.fetch_add(1, std::memory_order_release);

        bool expected = false;
        if (state->uploadQueued.compare_exchange_strong(expected, true, std::memory_order_acq_rel))
        {
            EnqueuePendingUpload(animation);
        }
    }

    EXPORT_API UnityRenderingEvent lottie_get_render_event_func(void)
    {
        return OnRenderEvent;
    }
#endif
}

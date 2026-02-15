#include "LottiePlugin.h"
#include "LottieLogger.h"
#include "RendererCommon.h"

#if !defined(__EMSCRIPTEN__)
#include "InstanceRegistry.h"
#include "UploadQueue.h"
#include "UploadPipeline.h"
#include "TextureBackend.h"
#include "UnityIntegration.h"
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#include "MetalBackend_apple.h"
#endif

#if defined(__EMSCRIPTEN__)
#include "PixelFormatUtils.h"
#endif

#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <string>

namespace
{
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
            RemoveInstance(*animation_wrapper);
            RemoveFromUploadQueue(*animation_wrapper);
        }
#endif
        delete (*animation_wrapper);
        *animation_wrapper = nullptr;
        LottieLogInfo(nullptr, "[Lottie] Animation wrapper disposed successfully");
        return 0;
    }

    EXPORT_API int32_t lottie_render_immediately(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] lottie_render_immediately called for frame %u", frame_number);
#if defined(__EMSCRIPTEN__)
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
#endif
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
#if defined(__EMSCRIPTEN__)
        // Convert BGRA to RGBA for WebGL only if requested (when not using shader conversion)
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
#else
        (void)convert_bgra_to_rgba; // Unused on non-WebGL platforms
        PublishUpload(animation_wrapper, render_data);
#endif
        LottieLogInfo(animation_wrapper, "[Lottie] Frame rendered successfully");
        return 0;
    }

#if defined(__EMSCRIPTEN__)
    // WebGL single-thread fallback: do sync render instead of futures
    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        LottieLogInfo(animation_wrapper, "[WebGL] lottie_render_create_future_async called for frame %u", frame_number);
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
        LottieLogInfo(animation_wrapper, "[WebGL] Surface created, calling renderSync...");
        // WebGL single-thread fallback: do sync render instead of futures
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
        // Convert BGRA to RGBA for WebGL only if requested (when not using shader conversion)
        if (convert_bgra_to_rgba)
        {
            LottieLogInfo(animation_wrapper, "[WebGL] renderSync completed, converting BGRA to RGBA...");
            ConvertBGRAtoRGBA(render_data->buffer, render_data->width, render_data->height);
            LottieLogInfo(animation_wrapper, "[WebGL] BGRA to RGBA conversion complete");
        }
        else
        {
            LottieLogInfo(animation_wrapper, "[WebGL] renderSync completed, skipping BGRA to RGBA conversion (shader mode)");
        }
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* /*animation_wrapper*/,
        lottie_render_data* /*render_data*/)
    {
        // WebGL single-thread fallback: nothing to do here
        return 0;
    }

    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* /*animation_wrapper*/,
        lottie_render_data* /*render_data*/,
        int32_t* ready)
    {
        // WebGL single-thread fallback: render was done synchronously, always ready
        if (ready != nullptr)
        {
            *ready = 1;
        }
        return 0;
    }
#else
    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio,
        bool convert_bgra_to_rgba)
    {
        (void)convert_bgra_to_rgba; // Unused on non-WebGL platforms
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

        ProfBegin(GetProfilerMarkerPublish());
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(GetProfilerMarkerPublish());

        *ready = 1;
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and published");
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] Waiting for render future result");
        ProfBegin(GetProfilerMarkerGetResult());
        render_data->render_future.get();
        ProfEnd(GetProfilerMarkerGetResult());

        ProfBegin(GetProfilerMarkerPublish());
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(GetProfilerMarkerPublish());
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
            // Set global log level if no specific animation wrapper
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
    EXPORT_API void* lottie_create_texture_with_color_space(
        lottie_animation_wrapper* animation,
        int width,
        int height,
        bool prefer_srgb_sampling)
    {
        // Early printf logging for iOS debugging
#if defined(__APPLE__)
        printf("[Lottie] lottie_create_texture called: width=%d, height=%d, renderer=%d, sUnityGraphics=%p\n",
               width, height, (int)GetCurrentRenderer(), (void*)GetUnityGraphics());
        fflush(stdout);
#endif

        LottieLogInfo(animation,
            "[Lottie] Creating texture: width=%d, height=%d, prefer_srgb_sampling=%s (renderer=%d, sUnityGraphics=%p)",
            width,
            height,
            prefer_srgb_sampling ? "true" : "false",
            (int)GetCurrentRenderer(),
            (void*)GetUnityGraphics());

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        printf("[Lottie] Metal state: gMetalDevice=%p\n", GetMetalDevice());
        fflush(stdout);

        LottieLogInfo(animation, "[Lottie] Metal state: gMetalDevice=%p", GetMetalDevice());
#endif

        InstanceState* state = GetState(animation);
        if (state != nullptr)
        {
            state->preferSRGBSampling = prefer_srgb_sampling;
        }

        // If Unity hasn't initialized the graphics device yet, try to initialize it now.
        // This can happen if the plugin missed the kUnityGfxDeviceEventInitialize event.
        if (GetCurrentRenderer() == Renderer::Unknown && GetUnityGraphics() != nullptr)
        {
            LottieLogInfo(animation, "[Lottie] Attempting lazy graphics device initialization");
            IUnityGraphics* unityGraphics = GetUnityGraphics();
            UnityGfxRenderer currentRenderer = unityGraphics->GetRenderer();
            LottieLogInfo(animation, "[Lottie] sUnityGraphics->GetRenderer() = %d", (int)currentRenderer);
            if (currentRenderer != kUnityGfxRendererNull)
            {
                // Trigger initialization
                OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);
                LottieLogInfo(animation, "[Lottie] Lazy initialization completed, renderer=%d", (int)GetCurrentRenderer());
            }
            else
            {
                LottieLogWarning(animation, "[Lottie] sUnityGraphics->GetRenderer() returned kUnityGfxRendererNull");
            }
        }
        else if (GetCurrentRenderer() == Renderer::Unknown)
        {
            LottieLogWarning(animation, "[Lottie] gRenderer is Unknown and sUnityGraphics is null");
        }

        // If still unknown after lazy init attempt, bail out
        if (GetCurrentRenderer() == Renderer::Unknown)
        {
            LottieLogWarning(animation,
                "[Lottie] lottie_create_texture called before graphics device is initialized");
            return nullptr;
        }

        // SPECIAL CASE: OpenGL on Windows – we MUST avoid GL calls from this thread.
        if (GetCurrentRenderer() == Renderer::OpenGL)
        {
#if defined(_WIN32)
            // Defer actual GL texture creation to the render thread (OnRenderEvent).
            state->texW = width;
            state->texH = height;
            // Dummy non-null pointer so C# knows we are in "native texture" mode.
            state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(kDeferredGLTexDummy));

            LottieLogInfo(animation,
                "[Lottie] OpenGL (Windows): deferring texture creation; dummy nativeTex=%p",
                state->nativeTex);

            return state->nativeTex;
#else
            // Non-Windows OpenGL (e.g. Linux): create immediately as before.
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

        // D3D11, D3D12, Metal, Vulkan: create immediately.
        if (!EnsureTextureForRenderer(animation, state, width, height))
        {
            LottieLogError(animation,
                "[Lottie] lottie_create_texture: EnsureTexture failed for renderer=%d",
                (int)GetCurrentRenderer());
            return nullptr;
        }

        LottieLogInfo(animation,
            "[Lottie] lottie_create_texture: texture ready, nativeTex=%p, texW=%d, texH=%d",
            state ? state->nativeTex : nullptr,
            state ? state->texW : 0,
            state ? state->texH : 0);

        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void* lottie_create_texture(lottie_animation_wrapper* animation, int width, int height)
    {
        // Backward compatible entry point for older managed clients.
        return lottie_create_texture_with_color_space(animation, width, height, false);
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

        EnqueueUpload(animation);
    }
#endif // !defined(__EMSCRIPTEN__)
}

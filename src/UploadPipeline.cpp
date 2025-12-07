#include "UploadPipeline.h"
#include "InstanceRegistry.h"
#include "TextureBackend.h"
#include "RendererCommon.h"
#include "LottieLogger.h"

#if !defined(__EMSCRIPTEN__)

void PerformUploadFor(lottie_animation_wrapper* animation)
{
    if (animation == nullptr)
    {
        LottieLogWarning(nullptr, "[Lottie] PerformUploadFor: animation is null");
        return;
    }

    Renderer renderer = GetCurrentRenderer();
    LottieLogInfo(animation, "[Lottie] PerformUploadFor: renderer=%d", (int)renderer);

    InstanceState* state = GetState(animation, /*create=*/false);
    if (state == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PerformUploadFor: state is null");
        return;
    }

    const uint64_t requested = state->requestedVersion.load(std::memory_order_acquire);
    if (requested == 0 || requested == state->uploadedVersion)
    {
        LottieLogInfo(animation, "[Lottie] PerformUploadFor: no new data to upload");
        state->uploadQueued.store(false, std::memory_order_release);
        return;
    }

    UploadContext ctx;
    {
        std::lock_guard<std::mutex> lock(state->uploadMutex);
        ctx = state->uploadCtx;
    }

    if (ctx.data == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PerformUploadFor: upload context data is null");
        state->uploadQueued.store(false, std::memory_order_release);
        return;
    }

    if (!EnsureTextureForRenderer(animation, state, static_cast<int>(ctx.width), static_cast<int>(ctx.height)))
    {
        LottieLogError(animation, "[Lottie] PerformUploadFor: EnsureTexture failed");
        state->uploadQueued.store(false, std::memory_order_release);
        return;
    }

    LottieLogInfo(animation, "[Lottie] PerformUploadFor: uploading texture data");
    UploadForRenderer(state, ctx);

    state->uploadedVersion = requested;
    state->uploadQueued.store(false, std::memory_order_release);
    LottieLogInfo(animation, "[Lottie] PerformUploadFor: upload completed successfully");
}

void PublishUpload(lottie_animation_wrapper* animation, const lottie_render_data* render_data)
{
    if (animation == nullptr || render_data == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PublishUpload: null animation or render_data");
        return;
    }

    InstanceState* state = GetState(animation);
    if (state == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PublishUpload: could not get state");
        return;
    }

    UploadContext ctx;
    ctx.data = reinterpret_cast<const uint8_t*>(render_data->buffer);
    ctx.width = render_data->width;
    ctx.height = render_data->height;
    ctx.stride = render_data->bytesPerLine;

    {
        std::lock_guard<std::mutex> lock(state->uploadMutex);
        state->uploadCtx = ctx;
    }

    state->uploadVersion.fetch_add(1, std::memory_order_release);
    LottieLogInfo(animation, "[Lottie] PublishUpload: upload published, version incremented");
}

#endif // !defined(__EMSCRIPTEN__)

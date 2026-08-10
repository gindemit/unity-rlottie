#include "UploadPipeline.h"
#include "InstanceRegistry.h"
#include "TextureBackend.h"
#include "RendererCommon.h"
#include "LottieLogger.h"
#include <cstring>

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

    InstanceState* state = nullptr;
    std::unique_lock<std::mutex> stateLifetimeLock;
    if (!LockStateForUpload(animation, state, stateLifetimeLock))
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

    // Calculate buffer size
    size_t bufferSize = static_cast<size_t>(render_data->bytesPerLine) * render_data->height;
    const uint8_t* srcData = reinterpret_cast<const uint8_t*>(render_data->buffer);

    {
        std::lock_guard<std::mutex> lock(state->uploadMutex);
        
        // Resize staging buffer if needed and copy the pixel data
        // This creates an owned copy so rlottie can start rendering the next frame
        // without corrupting the data we're about to upload to the GPU
        if (state->stagingBuffer.size() != bufferSize)
        {
            state->stagingBuffer.resize(bufferSize);
        }
        std::memcpy(state->stagingBuffer.data(), srcData, bufferSize);
        
        // Update upload context to point to our staging buffer copy
        state->uploadCtx.data = state->stagingBuffer.data();
        state->uploadCtx.width = render_data->width;
        state->uploadCtx.height = render_data->height;
        state->uploadCtx.stride = render_data->bytesPerLine;
    }

    state->uploadVersion.fetch_add(1, std::memory_order_release);
    LottieLogInfo(animation, "[Lottie] PublishUpload: data copied to staging buffer, version incremented");
}

#endif // !defined(__EMSCRIPTEN__)

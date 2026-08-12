#include "UploadPipeline.h"
#include "InstanceRegistry.h"
#include "TextureBackend.h"
#include "RendererCommon.h"
#include "LottieLogger.h"
#include "UploadQueue.h"

#if !defined(__EMSCRIPTEN__)

namespace
{
void FinishQueueTurn(lottie_animation_wrapper* animation, InstanceState* state)
{
    state->uploadQueued.store(false, std::memory_order_release);
    if (state->uploadVersion.load(std::memory_order_acquire) >
        state->uploadedVersion.load(std::memory_order_acquire))
    {
        EnqueueUpload(animation);
    }
}
}

void PerformUploadFor(lottie_animation_wrapper* animation)
{
    if (animation == nullptr)
    {
        LottieLogWarning(nullptr, "[Lottie] PerformUploadFor: animation is null");
        return;
    }

    InstanceState* state = nullptr;
    std::unique_lock<std::mutex> stateLifetimeLock;
    if (!LockStateForUpload(animation, state, stateLifetimeLock))
    {
        return;
    }

    Renderer renderer = GetCurrentRenderer();
    LottieLogInfo(animation, "[Lottie] PerformUploadFor: renderer=%d", (int)renderer);
    BeginUploadEventForRenderer(state);

    UploadContext ctx;
    uint64_t requested = 0;
    if (!AcquireNewestReadySlot(state, ctx, requested))
    {
        LottieLogInfo(animation, "[Lottie] PerformUploadFor: no new data to upload");
        state->uploadedVersion.store(
            state->uploadVersion.load(std::memory_order_acquire),
            std::memory_order_release);
        FinishQueueTurn(animation, state);
        return;
    }

    if (ctx.data == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PerformUploadFor: upload context data is null");
        ReleaseUploadSlot(state, ctx.slotIndex);
        state->uploadedVersion.store(requested, std::memory_order_release);
        FinishQueueTurn(animation, state);
        return;
    }

    if (!EnsureTextureForRenderer(animation, state, static_cast<int>(ctx.width), static_cast<int>(ctx.height)))
    {
        LottieLogError(animation, "[Lottie] PerformUploadFor: EnsureTexture failed");
        ReleaseUploadSlot(state, ctx.slotIndex);
        state->uploadedVersion.store(requested, std::memory_order_release);
        FinishQueueTurn(animation, state);
        return;
    }

    LottieLogInfo(animation, "[Lottie] PerformUploadFor: uploading texture data");
    UploadForRenderer(state, ctx);

    // D3D12 and Vulkan keep Uploading ownership until their frame completion
    // signal says mapped memory is safe. Immediate APIs have consumed the CPU
    // pointer before returning.
    if (renderer != Renderer::D3D12 && renderer != Renderer::Vulkan)
    {
        ReleaseUploadSlot(state, ctx.slotIndex);
    }

    state->uploadedVersion.store(requested, std::memory_order_release);
    // Close the clear-vs-publish lost-wakeup window. A publisher racing the
    // upload tail either observes uploadQueued=true, or is re-enqueued here.
    FinishQueueTurn(animation, state);
    LottieLogInfo(animation, "[Lottie] PerformUploadFor: upload completed successfully");
}

void PublishUpload(lottie_animation_wrapper* animation, const lottie_render_data* render_data)
{
    if (animation == nullptr || render_data == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PublishUpload: null animation or render_data");
        return;
    }

    PublishRenderSlot(animation, const_cast<lottie_render_data*>(render_data));
    LottieLogInfo(animation, "[Lottie] PublishUpload: render slot published without a frame copy");
}

#endif // !defined(__EMSCRIPTEN__)

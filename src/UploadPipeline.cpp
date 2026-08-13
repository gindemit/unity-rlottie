#include "UploadPipeline.h"
#include "InstanceRegistry.h"
#include "TextureBackend.h"
#include "RendererCommon.h"
#include "LottieLogger.h"
#include "UploadQueue.h"

#if !defined(__EMSCRIPTEN__)

namespace
{
lottie_animation_wrapper* FinishQueueTurn(lottie_animation_wrapper* animation, InstanceState* state)
{
    state->uploadQueued.store(false, std::memory_order_release);
    if (state->uploadVersion.load(std::memory_order_acquire) >
        state->uploadedVersion.load(std::memory_order_acquire))
    {
        // PerformUploadFor already owns state->lifetimeMutex. Requeue directly
        // instead of recursively acquiring the same non-recursive mutex.
        return EnqueueUploadWithLifetimeLocked(animation, state);
    }
    return nullptr;
}

void FinishQueueTurnAfterUnlock(
    std::unique_lock<std::mutex>& lifetimeLock,
    lottie_animation_wrapper* dropped,
    lottie_animation_wrapper* current)
{
    lifetimeLock.unlock();
    FinishDroppedUpload(dropped, current);
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
        lottie_animation_wrapper* dropped = FinishQueueTurn(animation, state);
        FinishQueueTurnAfterUnlock(stateLifetimeLock, dropped, animation);
        return;
    }

    if (ctx.data == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] PerformUploadFor: upload context data is null");
        ReleaseUploadSlot(state, ctx.slotIndex);
        state->uploadedVersion.store(requested, std::memory_order_release);
        lottie_animation_wrapper* dropped = FinishQueueTurn(animation, state);
        FinishQueueTurnAfterUnlock(stateLifetimeLock, dropped, animation);
        return;
    }

    if (!EnsureTextureForRenderer(animation, state, static_cast<int>(ctx.width), static_cast<int>(ctx.height)))
    {
        LottieLogError(animation, "[Lottie] PerformUploadFor: EnsureTexture failed");
        ReleaseUploadSlot(state, ctx.slotIndex);
        state->uploadedVersion.store(requested, std::memory_order_release);
        lottie_animation_wrapper* dropped = FinishQueueTurn(animation, state);
        FinishQueueTurnAfterUnlock(stateLifetimeLock, dropped, animation);
        return;
    }

    LottieLogInfo(animation, "[Lottie] PerformUploadFor: uploading texture data");
    const UploadResult uploadResult = UploadForRenderer(state, ctx);

    if (uploadResult == UploadResult::Retry)
    {
        // The render event did not have a usable command context/upload-ring
        // slot. Keep the immutable CPU frame ready and schedule another bounded
        // pump turn; do not report the version as uploaded.
        RestoreUploadSlotToReady(state, ctx.slotIndex);
        lottie_animation_wrapper* dropped = FinishQueueTurn(animation, state);
        FinishQueueTurnAfterUnlock(stateLifetimeLock, dropped, animation);
        return;
    }

    if (uploadResult == UploadResult::Failed)
    {
        // A terminal backend failure did not submit this frame. Backends that
        // can fall back mark native upload unavailable; consume this mailbox
        // version without claiming success or retry-spinning indefinitely.
        ReleaseUploadSlot(state, ctx.slotIndex);
        state->uploadedVersion.store(requested, std::memory_order_release);
        lottie_animation_wrapper* dropped = FinishQueueTurn(animation, state);
        LottieLogError(animation, "[Lottie] PerformUploadFor: upload failed");
        FinishQueueTurnAfterUnlock(stateLifetimeLock, dropped, animation);
        return;
    }

    // Every backend has consumed or copied the cacheable CPU mailbox pointer
    // before returning. Explicit APIs retain their independently-owned mapped
    // upload slots until their fence/safe-frame completion signals.
    ReleaseUploadSlot(state, ctx.slotIndex);

    state->uploadedVersion.store(requested, std::memory_order_release);
    // Close the clear-vs-publish lost-wakeup window. A publisher racing the
    // upload tail either observes uploadQueued=true, or is re-enqueued here.
    lottie_animation_wrapper* dropped = FinishQueueTurn(animation, state);
    LottieLogInfo(animation, "[Lottie] PerformUploadFor: upload completed successfully");
    FinishQueueTurnAfterUnlock(stateLifetimeLock, dropped, animation);
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

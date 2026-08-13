#include "UploadQueue.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include <mutex>
#include <queue>

#if !defined(__EMSCRIPTEN__)

namespace
{
    std::mutex gPendingUploadsMutex;
    std::queue<lottie_animation_wrapper*> gPendingUploads;

    lottie_animation_wrapper* EnqueueUploadStateLocked(
        lottie_animation_wrapper* animation,
        InstanceState* state)
    {
        const uint64_t latest = state->uploadVersion.load(std::memory_order_acquire);
        if (latest == 0 || latest == state->uploadedVersion.load(std::memory_order_acquire))
        {
            return nullptr;
        }

        state->requestedVersion.store(latest, std::memory_order_release);
        const bool enqueue = !state->uploadQueued.exchange(true, std::memory_order_acq_rel);
        if (!enqueue)
        {
            LottieLogInfo(animation, "[Lottie] Upload already queued, skipping");
            return nullptr;
        }
        LottieLogInfo(animation, "[Lottie] Queueing texture upload");

        lottie_animation_wrapper* dropped = nullptr;
        std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
        if (gPendingUploads.size() >= kMaxPendingUploads)
        {
            dropped = gPendingUploads.front();
            gPendingUploads.pop();
        }
        gPendingUploads.push(animation);
        return dropped;
    }
}

void EnqueueUpload(lottie_animation_wrapper* animation)
{
    if (animation == nullptr)
    {
        return;
    }

    InstanceState* state = nullptr;
    std::unique_lock<std::mutex> lifetimeLock;
    if (!LockStateForUpload(animation, state, lifetimeLock))
    {
        LottieLogWarning(animation, "[Lottie] EnqueueUpload: no instance state");
        return;
    }

    lottie_animation_wrapper* dropped = EnqueueUploadStateLocked(animation, state);
    lifetimeLock.unlock();

    FinishDroppedUpload(dropped, animation);
}

lottie_animation_wrapper* EnqueueUploadWithLifetimeLocked(
    lottie_animation_wrapper* animation,
    InstanceState* state)
{
    if (animation == nullptr || state == nullptr)
    {
        return nullptr;
    }

    // The upload event owns this instance's lifetime lock, so it is safe to
    // mutate its queue flags directly. Never call LockStateForUpload for the
    // same instance here: lifetimeMutex is deliberately non-recursive.
    return EnqueueUploadStateLocked(animation, state);
}

void FinishDroppedUpload(
    lottie_animation_wrapper* dropped,
    lottie_animation_wrapper* current)
{
    if (dropped != nullptr && dropped != current)
    {
        InstanceState* droppedState = nullptr;
        std::unique_lock<std::mutex> droppedLifetimeLock;
        if (LockStateForUpload(dropped, droppedState, droppedLifetimeLock))
        {
            // Logging dereferences the wrapper to read its log level, so it
            // must happen only after disposal is excluded by lifetimeMutex.
            LottieLogWarning(dropped, "[Lottie] Upload queue full, dropping oldest upload");
            droppedState->uploadQueued.store(false, std::memory_order_release);
        }
    }
}

lottie_animation_wrapper* DequeueUpload()
{
    std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
    if (gPendingUploads.empty())
    {
        return nullptr;
    }

    lottie_animation_wrapper* animation = gPendingUploads.front();
    gPendingUploads.pop();
    return animation;
}

void RemoveFromUploadQueue(lottie_animation_wrapper* animation)
{
    std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
    std::queue<lottie_animation_wrapper*> filtered;
    while (!gPendingUploads.empty())
    {
        lottie_animation_wrapper* candidate = gPendingUploads.front();
        gPendingUploads.pop();
        if (candidate != animation)
        {
            filtered.push(candidate);
        }
    }
    std::swap(gPendingUploads, filtered);
}

void ClearUploadQueue()
{
    std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
    std::queue<lottie_animation_wrapper*> empty;
    std::swap(gPendingUploads, empty);
}

#endif // !defined(__EMSCRIPTEN__)

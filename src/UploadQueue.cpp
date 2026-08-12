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

    const uint64_t latest = state->uploadVersion.load(std::memory_order_acquire);
    if (latest == 0 || latest == state->uploadedVersion.load(std::memory_order_acquire))
    {
        return;
    }

    state->requestedVersion.store(latest, std::memory_order_release);
    const bool enqueue = !state->uploadQueued.exchange(true, std::memory_order_acq_rel);
    if (!enqueue)
    {
        LottieLogInfo(animation, "[Lottie] Upload already queued, skipping");
        return;
    }
    LottieLogInfo(animation, "[Lottie] Queueing texture upload");

    lottie_animation_wrapper* dropped = nullptr;
    {
        std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
        if (gPendingUploads.size() >= kMaxPendingUploads)
        {
            dropped = gPendingUploads.front();
            gPendingUploads.pop();
        }

        gPendingUploads.push(animation);
    }
    lifetimeLock.unlock();

    if (dropped != nullptr && dropped != animation)
    {
        LottieLogWarning(dropped, "[Lottie] Upload queue full, dropping oldest upload");
        InstanceState* droppedState = nullptr;
        std::unique_lock<std::mutex> droppedLifetimeLock;
        if (LockStateForUpload(dropped, droppedState, droppedLifetimeLock))
        {
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

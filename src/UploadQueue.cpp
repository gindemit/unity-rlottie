#include "UploadQueue.h"

#if !defined(__EMSCRIPTEN__)

#include "InstanceRegistry.h"
#include "LottieLogger.h"

std::mutex gPendingUploadsMutex;
std::queue<lottie_animation_wrapper*> gPendingUploads;

void EnqueuePendingUpload(lottie_animation_wrapper* animation)
{
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

    if (dropped != nullptr && dropped != animation)
    {
        LottieLogWarning(dropped, "[Lottie] Upload queue full, dropping oldest upload");
        InstanceState* droppedState = GetState(dropped, /*create=*/false);
        if (droppedState != nullptr)
        {
            droppedState->uploadQueued.store(false, std::memory_order_release);
        }
    }
}

lottie_animation_wrapper* PopPendingUpload()
{
    lottie_animation_wrapper* animation = nullptr;
    {
        std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
        if (!gPendingUploads.empty())
        {
            animation = gPendingUploads.front();
            gPendingUploads.pop();
        }
    }
    return animation;
}

void ClearPendingUploads()
{
    std::lock_guard<std::mutex> queueLock(gPendingUploadsMutex);
    std::queue<lottie_animation_wrapper*> empty;
    std::swap(gPendingUploads, empty);
}

#endif

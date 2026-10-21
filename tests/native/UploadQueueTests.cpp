// Exercise the production queue with a synthetic lifetime registry. No backend
// or GPU is present: upload success/retry decisions are outside this test.
#include "UploadQueue.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include <cstdio>
#include <memory>
#include <thread>
#include <unordered_map>

namespace
{
    std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> states;
    int failures = 0;
    void Expect(bool condition, const char* message)
    {
        if (!condition) { std::fprintf(stderr, "FAILED: %s\n", message); ++failures; }
    }
    lottie_animation_wrapper* AddState(uintptr_t key)
    {
        auto* animation = reinterpret_cast<lottie_animation_wrapper*>(key);
        states[animation] = std::make_unique<InstanceState>();
        states[animation]->uploadVersion.store(1);
        return animation;
    }
}

// Queue dependencies only; keys remain opaque and no wrapper is dereferenced.
bool LockStateForUpload(lottie_animation_wrapper* animation, InstanceState*& state,
                        std::unique_lock<std::mutex>& lifetimeLock, bool)
{
    auto found = states.find(animation);
    if (found == states.end()) { return false; }
    state = found->second.get();
    lifetimeLock = std::unique_lock<std::mutex>(state->lifetimeMutex);
    return true;
}
void LottieLogInfo(lottie_animation_wrapper*, const char*, ...) { }
void LottieLogWarning(lottie_animation_wrapper*, const char*, ...) { }

int main()
{
    Expect(lottie_get_pending_upload_count() == 0, "empty queue");
    for (uintptr_t key = 1; key <= 130; ++key)
    {
        auto* animation = AddState(key);
        EnqueueUpload(animation);
        EnqueueUpload(animation);
        Expect(lottie_get_pending_upload_count() == static_cast<int>(key), "deduplicated burst");
    }
    Expect(lottie_get_pending_upload_count() == 130, "snapshot does not consume work");
    auto* first = reinterpret_cast<lottie_animation_wrapper*>(1);
    RemoveFromUploadQueue(first);
    states.erase(first);
    Expect(lottie_get_pending_upload_count() == 129, "removal after snapshot");
    auto* next = DequeueUpload();
    Expect(next == reinterpret_cast<lottie_animation_wrapper*>(2), "FIFO survives removal");
    Expect(lottie_get_pending_upload_count() == 128, "dequeue updates count");
    // Model the pipeline's retry tail using the real lifetime-locked requeue.
    {
        InstanceState* state = states[next].get();
        std::unique_lock<std::mutex> lock(state->lifetimeMutex);
        state->uploadQueued.store(false);
        Expect(EnqueueUploadWithLifetimeLocked(next, state) == nullptr, "retry needs no eviction");
    }
    Expect(lottie_get_pending_upload_count() == 129, "retry appears in next snapshot");
    ClearUploadQueue();
    Expect(lottie_get_pending_upload_count() == 0, "clear reflected in snapshot");
    Expect(DequeueUpload() == nullptr, "stale event after clear is safe");
    states.clear();
    // A publication after an empty snapshot is visible at the next poll.
    auto* late = AddState(1000);
    Expect(lottie_get_pending_upload_count() == 0, "empty before late publication");
    std::thread producer([&] { for (int i = 0; i < 10000; ++i) { EnqueueUpload(late); } });
    for (int i = 0; i < 10000; ++i)
    {
        int count = lottie_get_pending_upload_count();
        Expect(count == 0 || count == 1, "concurrent snapshot stays bounded");
    }
    producer.join();
    Expect(lottie_get_pending_upload_count() == 1, "late publication remains visible");
    ClearUploadQueue();
    states.clear();
    if (failures == 0) { std::puts("Upload queue tests passed."); }
    return failures == 0 ? 0 : 1;
}

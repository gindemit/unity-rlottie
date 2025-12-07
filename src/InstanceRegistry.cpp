#include "InstanceRegistry.h"

#if !defined(__EMSCRIPTEN__)

#include "TextureBackend.h"
#include "LottieLogger.h"

std::mutex gInstancesMutex;
std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> gInstances;

InstanceState* GetState(lottie_animation_wrapper* animation, bool create)
{
    if (animation == nullptr)
    {
        LottieLogWarning(nullptr, "[Lottie] GetState called with null animation");
        return nullptr;
    }

    std::lock_guard<std::mutex> lock(gInstancesMutex);
    auto it = gInstances.find(animation);
    if (it != gInstances.end())
    {
        LottieLogInfo(animation, "[Lottie] Found existing instance state");
        return it->second.get();
    }

    if (!create)
    {
        LottieLogInfo(animation, "[Lottie] Instance state not found, create=false");
        return nullptr;
    }

    auto instance = std::make_unique<InstanceState>();
    InstanceState* raw = instance.get();
    gInstances.emplace(animation, std::move(instance));
    LottieLogInfo(animation, "[Lottie] Created new instance state");
    return raw;
}

void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state)
{
    if (state == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] ResetTextureState called with null state");
        return;
    }

    LottieLogInfo(animation, "[Lottie] Resetting texture state");
    ResetTextureForRenderer(animation, state);

    state->nativeTex = nullptr;
    state->texW = 0;
    state->texH = 0;
    state->uploadCtx = {};
    state->uploadVersion.store(0, std::memory_order_relaxed);
    state->requestedVersion.store(0, std::memory_order_relaxed);
    state->uploadedVersion = 0;
    state->uploadQueued.store(false, std::memory_order_release);
}

void ClearInstances()
{
    std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
    for (auto& entry : gInstances)
    {
        ResetTextureState(entry.first, entry.second.get());
    }
    gInstances.clear();
}

#endif

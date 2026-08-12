#include "InstanceRegistry.h"
#include "TextureBackend.h"
#include <chrono>
#include <memory>
#include <unordered_map>

#if !defined(__EMSCRIPTEN__)

namespace
{
    std::mutex gInstancesMutex;
    std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> gInstances;
}

namespace
{
bool UsesNativeRenderPool(InstanceState* state)
{
    if (state == nullptr || state->nativeTex == nullptr)
    {
        return false;
    }

    switch (GetCurrentRenderer())
    {
        case Renderer::Vulkan:
            return state->vulkan.unityOwnedTexture &&
                state->vulkan.uploadAvailable.load(std::memory_order_acquire);
#if !defined(__APPLE__)
        case Renderer::OpenGL:
            // A deferred plugin-owned OpenGL texture starts with the dummy
            // pointer and becomes uploadable on the first render event.
            return state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(kDeferredGLTexDummy)) ||
                state->gl.uploadAvailable.load(std::memory_order_acquire);
#endif
        case Renderer::D3D11:
        case Renderer::D3D12:
        case Renderer::Metal:
            return true;
        default:
            return false;
    }
}
}

RenderSlotAcquireResult AcquireRenderSlot(
    lottie_animation_wrapper* animation,
    lottie_render_data* renderData,
    bool waitForSlot)
{
    if (animation == nullptr || renderData == nullptr || renderData->width == 0 || renderData->height == 0)
    {
        return RenderSlotAcquireResult::ExternalBuffer;
    }

    InstanceState* state = nullptr;
    {
        std::lock_guard<std::mutex> registryLock(gInstancesMutex);
        auto it = gInstances.find(animation);
        if (it == gInstances.end())
        {
            return RenderSlotAcquireResult::ExternalBuffer;
        }
        state = it->second.get();
        std::lock_guard<std::mutex> renderLifetimeLock(state->renderLifetimeMutex);
        ++state->activeRenders;
    }
    if (!UsesNativeRenderPool(state))
    {
        std::lock_guard<std::mutex> renderLifetimeLock(state->renderLifetimeMutex);
        --state->activeRenders;
        state->renderLifetimeChanged.notify_all();
        return RenderSlotAcquireResult::ExternalBuffer;
    }

    std::unique_lock<std::mutex> lock(state->renderPoolMutex);

    int selected = -1;
    uint64_t oldestReady = UINT64_MAX;
    for (int i = 0; i < InstanceState::kRenderSlotCount; ++i)
    {
        const InstanceState::RenderSlot& slot = state->renderSlots[i];
        if (slot.owner == InstanceState::SlotOwner::Free)
        {
            selected = i;
            break;
        }
        if (slot.owner == InstanceState::SlotOwner::Ready && slot.version < oldestReady)
        {
            // Async backpressure coalesces an unconsumed older frame instead of
            // allocating a fourth buffer or blocking the scripting thread.
            selected = i;
            oldestReady = slot.version;
        }
    }
    if (selected < 0)
    {
        if (waitForSlot)
        {
            state->renderPoolChanged.wait_for(lock, std::chrono::milliseconds(16));
            for (int i = 0; i < InstanceState::kRenderSlotCount; ++i)
            {
                if (state->renderSlots[i].owner == InstanceState::SlotOwner::Free)
                {
                    selected = i;
                    break;
                }
            }
        }
    }
    if (selected < 0)
    {
        LottieLogWarning(animation, "[Lottie] Render pool exhausted; frame skipped");
        state->uploadVersion.fetch_add(1, std::memory_order_acq_rel);
        std::lock_guard<std::mutex> renderLifetimeLock(state->renderLifetimeMutex);
        --state->activeRenders;
        state->renderLifetimeChanged.notify_all();
        return RenderSlotAcquireResult::NativeBackpressure;
    }

    InstanceState::RenderSlot& slot = state->renderSlots[selected];
    uint8_t* data = nullptr;
    uint32_t stride = renderData->width * 4;
    if (!PrepareRenderSlotForRenderer(
            state, selected, renderData->width, renderData->height, data, stride))
    {
        std::lock_guard<std::mutex> renderLifetimeLock(state->renderLifetimeMutex);
        --state->activeRenders;
        state->renderLifetimeChanged.notify_all();
        return RenderSlotAcquireResult::NativeBackpressure;
    }
    if (data == nullptr)
    {
        const size_t bytes = static_cast<size_t>(stride) * renderData->height;
        if (slot.storage.size() != bytes)
        {
            slot.storage.resize(bytes);
        }
        data = slot.storage.data();
    }

    slot.owner = InstanceState::SlotOwner::Rendering;
    slot.data = data;
    slot.width = renderData->width;
    slot.height = renderData->height;
    slot.stride = stride;
    slot.gpuUseToken = 0;

    renderData->render_pool_owner = animation;
    renderData->render_pool_slot = selected;
    renderData->render_pool_state = state;
    renderData->external_buffer = renderData->buffer;
    renderData->external_bytes_per_line = renderData->bytesPerLine;
    renderData->buffer = reinterpret_cast<uint32_t*>(data);
    renderData->bytesPerLine = stride;
    return RenderSlotAcquireResult::Acquired;
}

void PublishRenderSlot(lottie_animation_wrapper* animation, lottie_render_data* renderData)
{
    if (renderData == nullptr || renderData->render_pool_slot < 0)
    {
        return;
    }

    InstanceState* state = static_cast<InstanceState*>(renderData->render_pool_state);
    const int slotIndex = renderData->render_pool_slot;
    if (state != nullptr && slotIndex < InstanceState::kRenderSlotCount)
    {
        std::lock_guard<std::mutex> lock(state->renderPoolMutex);
        InstanceState::RenderSlot& slot = state->renderSlots[slotIndex];
        if (slot.owner == InstanceState::SlotOwner::Rendering)
        {
            slot.version = state->uploadVersion.fetch_add(1, std::memory_order_acq_rel) + 1;
            slot.owner = InstanceState::SlotOwner::Ready;
        }
    }

    renderData->buffer = renderData->external_buffer;
    renderData->bytesPerLine = renderData->external_bytes_per_line;
    renderData->external_buffer = nullptr;
    renderData->external_bytes_per_line = 0;
    renderData->render_pool_owner = nullptr;
    renderData->render_pool_slot = -1;
    renderData->render_pool_state = nullptr;
    if (state != nullptr)
    {
        std::lock_guard<std::mutex> renderLifetimeLock(state->renderLifetimeMutex);
        --state->activeRenders;
        state->renderLifetimeChanged.notify_all();
    }
}

void CancelRenderSlot(lottie_render_data* renderData)
{
    if (renderData == nullptr || renderData->render_pool_slot < 0)
    {
        return;
    }
    InstanceState* state = static_cast<InstanceState*>(renderData->render_pool_state);
    if (state != nullptr)
    {
        std::lock_guard<std::mutex> poolLock(state->renderPoolMutex);
        InstanceState::RenderSlot& slot = state->renderSlots[renderData->render_pool_slot];
        if (slot.owner == InstanceState::SlotOwner::Rendering)
        {
            slot.owner = InstanceState::SlotOwner::Free;
            state->renderPoolChanged.notify_all();
        }
    }
    renderData->buffer = renderData->external_buffer;
    renderData->bytesPerLine = renderData->external_bytes_per_line;
    renderData->external_buffer = nullptr;
    renderData->external_bytes_per_line = 0;
    renderData->render_pool_owner = nullptr;
    renderData->render_pool_state = nullptr;
    renderData->render_pool_slot = -1;
    if (state != nullptr)
    {
        std::lock_guard<std::mutex> renderLifetimeLock(state->renderLifetimeMutex);
        --state->activeRenders;
        state->renderLifetimeChanged.notify_all();
    }
}

bool AcquireNewestReadySlot(InstanceState* state, UploadContext& context, uint64_t& version)
{
    if (state == nullptr)
    {
        return false;
    }
    std::lock_guard<std::mutex> lock(state->renderPoolMutex);
    int newest = -1;
    version = 0;
    for (int i = 0; i < InstanceState::kRenderSlotCount; ++i)
    {
        InstanceState::RenderSlot& slot = state->renderSlots[i];
        if (slot.owner == InstanceState::SlotOwner::Ready && slot.version >= version)
        {
            newest = i;
            version = slot.version;
        }
    }
    if (newest < 0)
    {
        return false;
    }
    for (int i = 0; i < InstanceState::kRenderSlotCount; ++i)
    {
        InstanceState::RenderSlot& slot = state->renderSlots[i];
        if (i != newest && slot.owner == InstanceState::SlotOwner::Ready)
        {
            slot.owner = InstanceState::SlotOwner::Free;
            state->renderPoolChanged.notify_all();
        }
    }
    InstanceState::RenderSlot& slot = state->renderSlots[newest];
    slot.owner = InstanceState::SlotOwner::Uploading;
    context = {slot.data, slot.width, slot.height, slot.stride, newest};
    return true;
}

void ReleaseUploadSlot(InstanceState* state, int slotIndex)
{
    if (state == nullptr || slotIndex < 0 || slotIndex >= InstanceState::kRenderSlotCount)
    {
        return;
    }
    std::lock_guard<std::mutex> lock(state->renderPoolMutex);
    InstanceState::RenderSlot& slot = state->renderSlots[slotIndex];
    if (slot.owner == InstanceState::SlotOwner::Uploading)
    {
        slot.owner = InstanceState::SlotOwner::Free;
        slot.gpuUseToken = 0;
        state->renderPoolChanged.notify_all();
    }
}

void WaitForActiveRenders(InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }
    std::unique_lock<std::mutex> lock(state->renderLifetimeMutex);
    state->renderLifetimeChanged.wait(lock, [state]() { return state->activeRenders == 0; });
}

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

bool LockStateForUpload(
    lottie_animation_wrapper* animation,
    InstanceState*& state,
    std::unique_lock<std::mutex>& lifetimeLock,
    bool create)
{
    state = nullptr;
    std::unique_lock<std::mutex> registryLock(gInstancesMutex);
    auto it = gInstances.find(animation);
    if (it == gInstances.end())
    {
        if (!create || animation == nullptr)
        {
            return false;
        }
        auto instance = std::make_unique<InstanceState>();
        state = instance.get();
        it = gInstances.emplace(animation, std::move(instance)).first;
    }

    state = it->second.get();
    lifetimeLock = std::unique_lock<std::mutex>(state->lifetimeMutex);
    registryLock.unlock();
    return true;
}

void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state, bool lifetimeAlreadyLocked)
{
    if (state == nullptr)
    {
        LottieLogWarning(animation, "[Lottie] ResetTextureState called with null state");
        return;
    }
    LottieLogInfo(animation, "[Lottie] Resetting texture state");
    std::unique_lock<std::mutex> lifetimeLock(state->lifetimeMutex, std::defer_lock);
    if (!lifetimeAlreadyLocked)
    {
        lifetimeLock.lock();
    }
    WaitForActiveRenders(state);

    // Delegate to backend-specific reset
    ResetTextureForRenderer(animation, state);

    state->nativeTex = nullptr;
    state->texW = 0;
    state->texH = 0;
    state->uploadCtx = {};
    state->uploadVersion.store(0, std::memory_order_relaxed);
    state->requestedVersion.store(0, std::memory_order_relaxed);
    state->uploadedVersion.store(0, std::memory_order_relaxed);
    state->uploadQueued.store(false, std::memory_order_release);
    {
        std::lock_guard<std::mutex> poolLock(state->renderPoolMutex);
        for (InstanceState::RenderSlot& slot : state->renderSlots)
        {
            slot = {};
        }
    }
}

void RemoveInstance(lottie_animation_wrapper* animation)
{
    if (animation == nullptr)
    {
        return;
    }

    std::unique_ptr<InstanceState> removed;
    {
        std::lock_guard<std::mutex> lock(gInstancesMutex);
        auto it = gInstances.find(animation);
        if (it != gInstances.end())
        {
            removed = std::move(it->second);
            gInstances.erase(it);
        }
    }
    if (removed != nullptr)
    {
        // Removing from the registry first prevents new render-thread users.
        // Existing users retain lifetimeMutex and finish before backend reset.
        WaitForActiveRenders(removed.get());
        ResetTextureState(animation, removed.get());
    }
}

void ClearAllInstances()
{
    std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> removed;
    {
        std::lock_guard<std::mutex> lock(gInstancesMutex);
        removed.swap(gInstances);
    }
    for (auto& entry : removed)
    {
        WaitForActiveRenders(entry.second.get());
        ResetTextureState(entry.first, entry.second.get());
    }
}

#endif // !defined(__EMSCRIPTEN__)

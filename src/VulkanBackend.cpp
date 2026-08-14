#if !defined(__EMSCRIPTEN__)

#include "VulkanBackend.h"
#include "ImageBufferUtils.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include "RendererCommon.h"

#if defined(LOTTIE_VULKAN_AVAILABLE)

#include "IUnityGraphicsVulkan.h"
#include <algorithm>
#include <atomic>
#include <cstring>
#include <mutex>
#include <vector>

namespace
{
constexpr int kLottieUploadEventId = 1;
constexpr size_t kVulkanUploadSlotCount = 3;

IUnityGraphicsVulkan* gVulkan = nullptr;
IUnityGraphicsVulkanV2* gVulkanV2 = nullptr;
UnityVulkanInstance gInstance{};

PFN_vkGetDeviceProcAddr pfnGetDeviceProcAddr = nullptr;
PFN_vkGetPhysicalDeviceMemoryProperties pfnGetPhysicalDeviceMemoryProperties = nullptr;
PFN_vkGetPhysicalDeviceProperties pfnGetPhysicalDeviceProperties = nullptr;
PFN_vkCreateBuffer pfnCreateBuffer = nullptr;
PFN_vkDestroyBuffer pfnDestroyBuffer = nullptr;
PFN_vkGetBufferMemoryRequirements pfnGetBufferMemoryRequirements = nullptr;
PFN_vkAllocateMemory pfnAllocateMemory = nullptr;
PFN_vkFreeMemory pfnFreeMemory = nullptr;
PFN_vkBindBufferMemory pfnBindBufferMemory = nullptr;
PFN_vkMapMemory pfnMapMemory = nullptr;
PFN_vkUnmapMemory pfnUnmapMemory = nullptr;
PFN_vkFlushMappedMemoryRanges pfnFlushMappedMemoryRanges = nullptr;
PFN_vkCmdCopyBufferToImage pfnCmdCopyBufferToImage = nullptr;
bool gDeviceFunctionsReady = false;
bool gUploadEventConfigured = false;
bool gDeviceDetailsLogged = false;
std::atomic<uint64_t> gLastSafeFrame{0};

struct UploadSlot
{
    VkBuffer buffer = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    void* mapped = nullptr;
    VkDeviceSize size = 0;
    bool coherent = false;
    bool used = false;
    uint64_t lastUsedFrame = 0;
};

struct VulkanTextureData
{
    std::vector<UploadSlot> slots;
    size_t nextUploadSlot = 0;
    uint64_t poolExhaustionCount = 0;
    bool copyConfigurationLogged = false;
};

void DestroySlot(UploadSlot& slot);

std::mutex gRetiredMutex;
std::vector<VulkanTextureData*> gRetiredTextures;

void DestroyTextureData(VulkanTextureData* data)
{
    if (data == nullptr)
    {
        return;
    }
    for (UploadSlot& slot : data->slots)
    {
        DestroySlot(slot);
    }
    delete data;
}

void CollectRetiredTextures(uint64_t safeFrameNumber, bool deviceShutdown)
{
    std::lock_guard<std::mutex> lock(gRetiredMutex);
    auto it = gRetiredTextures.begin();
    while (it != gRetiredTextures.end())
    {
        VulkanTextureData* data = *it;
        bool safe = deviceShutdown;
        if (!safe)
        {
            safe = std::all_of(data->slots.begin(), data->slots.end(), [safeFrameNumber](const UploadSlot& slot)
            {
                return !slot.used || slot.lastUsedFrame <= safeFrameNumber;
            });
        }
        if (safe)
        {
            DestroyTextureData(data);
            it = gRetiredTextures.erase(it);
        }
        else
        {
            ++it;
        }
    }
}

UnityVulkanInstance GetInstance()
{
    if (gVulkanV2 != nullptr)
    {
        return gVulkanV2->Instance();
    }
    if (gVulkan != nullptr)
    {
        return gVulkan->Instance();
    }
    return {};
}

bool AccessTexture(void* texture, UnityVulkanImage* image)
{
    if (gVulkanV2 != nullptr)
    {
        return gVulkanV2->AccessTexture(
            texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
            kUnityVulkanResourceAccess_PipelineBarrier, image);
    }
    return gVulkan != nullptr && gVulkan->AccessTexture(
        texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
        VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT,
        kUnityVulkanResourceAccess_PipelineBarrier, image);
}

bool FinishTextureUpload(void* texture)
{
    UnityVulkanImage image{};
    if (gVulkanV2 != nullptr)
    {
        return gVulkanV2->AccessTexture(
            texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT, VK_ACCESS_SHADER_READ_BIT,
            kUnityVulkanResourceAccess_PipelineBarrier, &image);
    }
    return gVulkan != nullptr && gVulkan->AccessTexture(
        texture, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
        VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT, VK_ACCESS_SHADER_READ_BIT,
        kUnityVulkanResourceAccess_PipelineBarrier, &image);
}

bool GetRecordingState(UnityVulkanRecordingState* recording)
{
    if (gVulkanV2 != nullptr)
    {
        return gVulkanV2->CommandRecordingState(recording, kUnityVulkanGraphicsQueueAccess_DontCare);
    }
    return gVulkan != nullptr &&
        gVulkan->CommandRecordingState(recording, kUnityVulkanGraphicsQueueAccess_DontCare);
}

template <typename T>
T LoadDeviceFunction(const char* name)
{
    return reinterpret_cast<T>(pfnGetDeviceProcAddr(gInstance.device, name));
}

bool EnsureDeviceFunctions()
{
    if (gDeviceFunctionsReady)
    {
        return true;
    }

    gInstance = GetInstance();
    if (gInstance.instance == VK_NULL_HANDLE || gInstance.physicalDevice == VK_NULL_HANDLE ||
        gInstance.device == VK_NULL_HANDLE || gInstance.getInstanceProcAddr == nullptr)
    {
        return false;
    }

    pfnGetDeviceProcAddr = reinterpret_cast<PFN_vkGetDeviceProcAddr>(
        gInstance.getInstanceProcAddr(gInstance.instance, "vkGetDeviceProcAddr"));
    pfnGetPhysicalDeviceMemoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(
        gInstance.getInstanceProcAddr(gInstance.instance, "vkGetPhysicalDeviceMemoryProperties"));
    pfnGetPhysicalDeviceProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(
        gInstance.getInstanceProcAddr(gInstance.instance, "vkGetPhysicalDeviceProperties"));
    if (pfnGetDeviceProcAddr == nullptr || pfnGetPhysicalDeviceMemoryProperties == nullptr ||
        pfnGetPhysicalDeviceProperties == nullptr)
    {
        return false;
    }

    pfnCreateBuffer = LoadDeviceFunction<PFN_vkCreateBuffer>("vkCreateBuffer");
    pfnDestroyBuffer = LoadDeviceFunction<PFN_vkDestroyBuffer>("vkDestroyBuffer");
    pfnGetBufferMemoryRequirements = LoadDeviceFunction<PFN_vkGetBufferMemoryRequirements>("vkGetBufferMemoryRequirements");
    pfnAllocateMemory = LoadDeviceFunction<PFN_vkAllocateMemory>("vkAllocateMemory");
    pfnFreeMemory = LoadDeviceFunction<PFN_vkFreeMemory>("vkFreeMemory");
    pfnBindBufferMemory = LoadDeviceFunction<PFN_vkBindBufferMemory>("vkBindBufferMemory");
    pfnMapMemory = LoadDeviceFunction<PFN_vkMapMemory>("vkMapMemory");
    pfnUnmapMemory = LoadDeviceFunction<PFN_vkUnmapMemory>("vkUnmapMemory");
    pfnFlushMappedMemoryRanges = LoadDeviceFunction<PFN_vkFlushMappedMemoryRanges>("vkFlushMappedMemoryRanges");
    pfnCmdCopyBufferToImage = LoadDeviceFunction<PFN_vkCmdCopyBufferToImage>("vkCmdCopyBufferToImage");

    gDeviceFunctionsReady = pfnCreateBuffer != nullptr && pfnDestroyBuffer != nullptr &&
        pfnGetBufferMemoryRequirements != nullptr && pfnAllocateMemory != nullptr &&
        pfnFreeMemory != nullptr && pfnBindBufferMemory != nullptr && pfnMapMemory != nullptr &&
        pfnUnmapMemory != nullptr && pfnFlushMappedMemoryRanges != nullptr &&
        pfnCmdCopyBufferToImage != nullptr;
    if (gDeviceFunctionsReady && !gDeviceDetailsLogged)
    {
        VkPhysicalDeviceProperties properties{};
        pfnGetPhysicalDeviceProperties(gInstance.physicalDevice, &properties);
        LottieLogInfo(nullptr,
            "[Lottie] Vulkan upload device: name=%s vendor=0x%04x device=0x%04x driver=0x%08x api=%u.%u.%u maxImage2D=%u nonCoherentAtom=%llu",
            properties.deviceName, properties.vendorID, properties.deviceID, properties.driverVersion,
            VK_VERSION_MAJOR(properties.apiVersion), VK_VERSION_MINOR(properties.apiVersion),
            VK_VERSION_PATCH(properties.apiVersion), properties.limits.maxImageDimension2D,
            static_cast<unsigned long long>(properties.limits.nonCoherentAtomSize));
        gDeviceDetailsLogged = true;
    }
    return gDeviceFunctionsReady;
}

void DestroySlot(UploadSlot& slot)
{
    if (slot.mapped != nullptr && pfnUnmapMemory != nullptr)
    {
        pfnUnmapMemory(gInstance.device, slot.memory);
    }
    if (slot.buffer != VK_NULL_HANDLE && pfnDestroyBuffer != nullptr)
    {
        pfnDestroyBuffer(gInstance.device, slot.buffer, nullptr);
    }
    if (slot.memory != VK_NULL_HANDLE && pfnFreeMemory != nullptr)
    {
        pfnFreeMemory(gInstance.device, slot.memory, nullptr);
    }
    slot = {};
}

bool CreateUploadSlot(VkDeviceSize size, UploadSlot& slot)
{
    VkBufferCreateInfo bufferInfo{};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = size;
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (pfnCreateBuffer(gInstance.device, &bufferInfo, nullptr, &slot.buffer) != VK_SUCCESS)
    {
        return false;
    }

    VkMemoryRequirements requirements{};
    pfnGetBufferMemoryRequirements(gInstance.device, slot.buffer, &requirements);
    VkPhysicalDeviceMemoryProperties properties{};
    pfnGetPhysicalDeviceMemoryProperties(gInstance.physicalDevice, &properties);

    uint32_t memoryType = UINT32_MAX;
    bool coherent = false;
    for (uint32_t i = 0; i < properties.memoryTypeCount; ++i)
    {
        const bool allowed = (requirements.memoryTypeBits & (1u << i)) != 0;
        const VkMemoryPropertyFlags flags = properties.memoryTypes[i].propertyFlags;
        if (allowed && (flags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0)
        {
            if (memoryType == UINT32_MAX || (flags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0)
            {
                memoryType = i;
                coherent = (flags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0;
            }
            if (coherent)
            {
                break;
            }
        }
    }

    if (memoryType == UINT32_MAX)
    {
        DestroySlot(slot);
        return false;
    }

    VkMemoryAllocateInfo allocateInfo{};
    allocateInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocateInfo.allocationSize = requirements.size;
    allocateInfo.memoryTypeIndex = memoryType;
    if (pfnAllocateMemory(gInstance.device, &allocateInfo, nullptr, &slot.memory) != VK_SUCCESS ||
        pfnBindBufferMemory(gInstance.device, slot.buffer, slot.memory, 0) != VK_SUCCESS ||
        pfnMapMemory(gInstance.device, slot.memory, 0, requirements.size, 0, &slot.mapped) != VK_SUCCESS)
    {
        DestroySlot(slot);
        return false;
    }

    slot.size = requirements.size;
    slot.coherent = coherent;
    const VkMemoryPropertyFlags memoryFlags = properties.memoryTypes[memoryType].propertyFlags;
    LottieLogInfo(nullptr,
        "[Lottie] Vulkan staging slot: requested=%llu allocated=%llu alignment=%llu memoryType=%u flags=0x%x coherent=%d",
        static_cast<unsigned long long>(size), static_cast<unsigned long long>(requirements.size),
        static_cast<unsigned long long>(requirements.alignment), memoryType,
        static_cast<unsigned int>(memoryFlags), coherent ? 1 : 0);
    return true;
}

void MarkUnavailable(InstanceState* state, const char* reason)
{
    state->vulkan.uploadAvailable.store(false, std::memory_order_release);
    LottieLogWarning(nullptr, "[Lottie] Vulkan native upload failed (%s); Apply fallback will be selected", reason);
}
} // namespace

void InitializeVulkanFromUnity(IUnityInterfaces* unityInterfaces)
{
    gVulkanV2 = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityGraphicsVulkanV2>() : nullptr;
    gVulkan = gVulkanV2 == nullptr && unityInterfaces != nullptr
        ? unityInterfaces->Get<IUnityGraphicsVulkan>()
        : nullptr;

}

void ConfigureVulkanUploadEvent()
{
    if (gUploadEventConfigured || (gVulkanV2 == nullptr && gVulkan == nullptr))
    {
        return;
    }

    UnityVulkanPluginEventConfig config{};
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
    config.flags = 0;
    if (gVulkanV2 != nullptr)
    {
        gVulkanV2->ConfigureEvent(kLottieUploadEventId, &config);
        gUploadEventConfigured = true;
    }
    else if (gVulkan != nullptr)
    {
        gVulkan->ConfigureEvent(kLottieUploadEventId, &config);
        gUploadEventConfigured = true;
    }
}

void ShutdownVulkan()
{
    // Unity's graphics-device shutdown event is the final safe point for
    // resources retained after an animation was disposed between frames.
    CollectRetiredTextures(0, true);
    gDeviceFunctionsReady = false;
    gUploadEventConfigured = false;
    gDeviceDetailsLogged = false;
    gLastSafeFrame.store(0, std::memory_order_release);
    gInstance = {};
    pfnGetDeviceProcAddr = nullptr;
    pfnGetPhysicalDeviceMemoryProperties = nullptr;
    pfnGetPhysicalDeviceProperties = nullptr;
    pfnCreateBuffer = nullptr;
    pfnDestroyBuffer = nullptr;
    pfnGetBufferMemoryRequirements = nullptr;
    pfnAllocateMemory = nullptr;
    pfnFreeMemory = nullptr;
    pfnBindBufferMemory = nullptr;
    pfnMapMemory = nullptr;
    pfnUnmapMemory = nullptr;
    pfnFlushMappedMemoryRanges = nullptr;
    pfnCmdCopyBufferToImage = nullptr;
}

bool IsNativeVulkanUploadSupported()
{
    return GetCurrentRenderer() == Renderer::Vulkan && gUploadEventConfigured &&
        (gVulkanV2 != nullptr || gVulkan != nullptr) &&
        EnsureDeviceFunctions();
}

bool RegisterUnityTextureVulkan(lottie_animation_wrapper* animation, void* nativeTexture, int width, int height)
{
    if (animation == nullptr || nativeTexture == nullptr || width <= 0 || height <= 0 ||
        !IsNativeVulkanUploadSupported())
    {
        return false;
    }

    InstanceState* state = nullptr;
    std::unique_lock<std::mutex> lifetimeLock;
    if (!LockStateForUpload(animation, state, lifetimeLock, /*create=*/true))
    {
        return false;
    }
    WaitForActiveRenders(state);
    ResetTextureVulkan(animation, state);
    {
        std::lock_guard<std::mutex> poolLock(state->renderPoolMutex);
        for (InstanceState::RenderSlot& slot : state->renderSlots)
        {
            slot = {};
        }
        state->renderPoolChanged.notify_all();
    }
    state->nativeTex = nativeTexture;
    state->texW = width;
    state->texH = height;
    state->vulkan.unityOwnedTexture = true;
    state->vulkan.uploadAvailable.store(true, std::memory_order_release);
    return true;
}

bool IsVulkanUploadAvailable(lottie_animation_wrapper* animation)
{
    InstanceState* state = nullptr;
    std::unique_lock<std::mutex> lifetimeLock;
    return LockStateForUpload(animation, state, lifetimeLock) &&
        state->vulkan.uploadAvailable.load(std::memory_order_acquire) &&
        IsNativeVulkanUploadSupported();
}

void ResetTextureVulkan(lottie_animation_wrapper*, InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    VulkanTextureData* data = static_cast<VulkanTextureData*>(state->vulkan.backend);
    if (data != nullptr)
    {
        // Reset/dispose normally runs on the scripting thread. A slot may still
        // be referenced by Unity's recorded command buffer, so ownership moves
        // to a render-thread retirement list instead of being freed here.
        std::lock_guard<std::mutex> retiredLock(gRetiredMutex);
        gRetiredTextures.push_back(data);
    }
    state->vulkan.backend = nullptr;
    state->vulkan.unityOwnedTexture = false;
    state->vulkan.uploadAvailable.store(false, std::memory_order_release);
}

bool EnsureTextureVulkan(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    const bool valid = state != nullptr && state->vulkan.unityOwnedTexture &&
        state->nativeTex != nullptr && state->texW == width && state->texH == height &&
        state->vulkan.uploadAvailable.load(std::memory_order_acquire) && EnsureDeviceFunctions();
    if (!valid)
    {
        LottieLogWarning(animation, "[Lottie] Vulkan Unity texture is not registered or upload is unavailable");
    }
    return valid;
}

bool PrepareRenderSlotVulkan(
    InstanceState* state,
    int slotIndex,
    uint32_t width,
    uint32_t height,
    uint8_t*& data,
    uint32_t& stride)
{
    if (state == nullptr || slotIndex < 0 || slotIndex >= InstanceState::kRenderSlotCount ||
        !EnsureTextureVulkan(nullptr, state, static_cast<int>(width), static_cast<int>(height)))
    {
        return false;
    }

    // rlottie performs read/modify/write blending and must render into normal
    // host-cacheable memory. Leave data null so InstanceRegistry supplies its
    // persistent CPU mailbox storage. UploadVulkan performs one sequential copy
    // into a separate safe-frame-tracked mapped upload slot.
    stride = width * 4;
    data = nullptr;
    return true;
}

void RefreshCompletedRenderSlotsVulkan(InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }
    VulkanTextureData* backend = static_cast<VulkanTextureData*>(state->vulkan.backend);
    if (backend == nullptr)
    {
        return;
    }
    const uint64_t safeFrame = gLastSafeFrame.load(std::memory_order_acquire);
    for (UploadSlot& backendSlot : backend->slots)
    {
        if (backendSlot.used && backendSlot.lastUsedFrame <= safeFrame)
        {
            backendSlot.used = false;
        }
    }
}

void BeginUploadEventVulkan(InstanceState*)
{
    if (!EnsureDeviceFunctions())
    {
        return;
    }
    UnityVulkanRecordingState recording{};
    if (GetRecordingState(&recording) && recording.commandBuffer != VK_NULL_HANDLE)
    {
        gLastSafeFrame.store(recording.safeFrameNumber, std::memory_order_release);
        CollectRetiredTextures(recording.safeFrameNumber, false);
    }
}

UploadResult UploadVulkan(InstanceState* state, const UploadContext& ctx)
{
    ImageBufferSize imageSize{};
    if (state == nullptr || !IsValidImageBuffer(ctx.data, ctx.width, ctx.height, ctx.stride) ||
        !TryGetImageBufferSize(ctx.width, ctx.height, ctx.stride, imageSize) ||
        (ctx.stride % 4) != 0 || !EnsureDeviceFunctions())
    {
        if (state != nullptr)
        {
            MarkUnavailable(state, "invalid upload context");
        }
        return UploadResult::Failed;
    }

    // PerformUploadFor holds state->lifetimeMutex from the atomic registry lookup
    // through command recording, preventing reset/removal from retiring this
    // data while it is in use.

    VulkanTextureData* data = static_cast<VulkanTextureData*>(state->vulkan.backend);
    if (data == nullptr)
    {
        data = new VulkanTextureData();
        data->slots.resize(kVulkanUploadSlotCount);
        state->vulkan.backend = data;
    }

    const VkDeviceSize bytes = static_cast<VkDeviceSize>(imageSize.totalBytes);
    if (data->slots.size() != kVulkanUploadSlotCount)
    {
        MarkUnavailable(state, "upload ring slot count");
        return UploadResult::Failed;
    }

    for (UploadSlot& uploadSlot : data->slots)
    {
        if (uploadSlot.mapped == nullptr && !CreateUploadSlot(bytes, uploadSlot))
        {
            MarkUnavailable(state, "upload ring allocation");
            return UploadResult::Failed;
        }
        if (uploadSlot.size < bytes)
        {
            MarkUnavailable(state, "upload ring dimensions changed");
            return UploadResult::Failed;
        }
    }

    // Select capacity before AccessTexture records a TRANSFER_DST transition.
    // A retry must not leave Unity's image in an upload-only layout.
    RefreshCompletedRenderSlotsVulkan(state);
    UploadSlot* slot = nullptr;
    for (size_t offset = 0; offset < data->slots.size(); ++offset)
    {
        const size_t candidate = (data->nextUploadSlot + offset) % data->slots.size();
        if (!data->slots[candidate].used)
        {
            slot = &data->slots[candidate];
            data->nextUploadSlot = (candidate + 1) % data->slots.size();
            break;
        }
    }
    if (slot == nullptr)
    {
        ++data->poolExhaustionCount;
        if (data->poolExhaustionCount == 1 || data->poolExhaustionCount % 120 == 0)
        {
            LottieLogWarning(nullptr,
                "[Lottie] Vulkan upload ring exhausted; frame deferred (count=%llu)",
                static_cast<unsigned long long>(data->poolExhaustionCount));
        }
        return UploadResult::Retry;
    }

    UnityVulkanImage image{};
    if (!AccessTexture(state->nativeTex, &image))
    {
        MarkUnavailable(state, "AccessTexture");
        return UploadResult::Failed;
    }

    const bool bgra = image.format == VK_FORMAT_B8G8R8A8_UNORM || image.format == VK_FORMAT_B8G8R8A8_SRGB;
    if (!bgra || (image.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT) == 0 || image.samples != VK_SAMPLE_COUNT_1_BIT ||
        image.extent.width != ctx.width || image.extent.height != ctx.height)
    {
        FinishTextureUpload(state->nativeTex);
        MarkUnavailable(state, "unsupported Unity image format or usage");
        return UploadResult::Failed;
    }

    UnityVulkanRecordingState recording{};
    if (!GetRecordingState(&recording) || recording.commandBuffer == VK_NULL_HANDLE || recording.renderPass != VK_NULL_HANDLE)
    {
        FinishTextureUpload(state->nativeTex);
        MarkUnavailable(state, "command recording state");
        return UploadResult::Failed;
    }
    CollectRetiredTextures(recording.safeFrameNumber, false);
    gLastSafeFrame.store(recording.safeFrameNumber, std::memory_order_release);

    // This is the only post-render frame copy. The source is cacheable CPU
    // mailbox memory; the destination stays GPU-owned until safeFrame retires
    // this upload slot.
    std::memcpy(slot->mapped, ctx.data, static_cast<size_t>(bytes));
    if (!slot->coherent)
    {
        VkMappedMemoryRange range{};
        range.sType = VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE;
        range.memory = slot->memory;
        range.offset = 0;
        range.size = VK_WHOLE_SIZE;
        if (pfnFlushMappedMemoryRanges(gInstance.device, 1, &range) != VK_SUCCESS)
        {
            // AccessTexture already transitioned Unity's image to
            // TRANSFER_DST. Restore the state tracker even though no copy will
            // be recorded, otherwise managed fallback may sample an image left
            // in an upload-only layout.
            FinishTextureUpload(state->nativeTex);
            MarkUnavailable(state, "non-coherent memory flush");
            return UploadResult::Failed;
        }
    }

    VkBufferImageCopy copy{};
    copy.bufferOffset = 0;
    copy.bufferRowLength = ctx.stride == ctx.width * 4 ? 0 : ctx.stride / 4;
    copy.bufferImageHeight = 0;
    copy.imageSubresource.aspectMask = image.aspect & VK_IMAGE_ASPECT_COLOR_BIT;
    copy.imageSubresource.mipLevel = 0;
    copy.imageSubresource.baseArrayLayer = 0;
    copy.imageSubresource.layerCount = 1;
    copy.imageExtent = {ctx.width, ctx.height, 1};
    if (!data->copyConfigurationLogged)
    {
        LottieLogInfo(nullptr,
            "[Lottie] Vulkan copy configuration: image=%ux%u stride=%u bytes=%llu format=%d usage=0x%x postLayout=%d postStage=0x%x postAccess=0x%x",
            ctx.width, ctx.height, ctx.stride, static_cast<unsigned long long>(bytes),
            static_cast<int>(image.format), static_cast<unsigned int>(image.usage),
            static_cast<int>(VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL),
            static_cast<unsigned int>(VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT),
            static_cast<unsigned int>(VK_ACCESS_SHADER_READ_BIT));
        data->copyConfigurationLogged = true;
    }
    pfnCmdCopyBufferToImage(
        recording.commandBuffer, slot->buffer, image.image,
        VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
    // From this point the command buffer references the mapped slot. Even if
    // finalizing Unity's image access fails, safeFrame must gate its reuse.
    slot->lastUsedFrame = recording.currentFrameNumber;
    slot->used = true;
    // AccessTexture above transitions into TRANSFER_DST and makes earlier use
    // visible to the copy. Finalize the access through Unity as well so it
    // records the transfer-write -> shader-read dependency and keeps its
    // resource-layout tracker consistent with the image's actual layout.
    if (!FinishTextureUpload(state->nativeTex))
    {
        MarkUnavailable(state, "post-upload shader-read transition");
        return UploadResult::Failed;
    }
    return UploadResult::Submitted;
}

#else

void InitializeVulkanFromUnity(IUnityInterfaces*) {}
void ConfigureVulkanUploadEvent() {}
void ShutdownVulkan() {}
bool IsNativeVulkanUploadSupported() { return false; }
bool RegisterUnityTextureVulkan(lottie_animation_wrapper*, void*, int, int) { return false; }
bool IsVulkanUploadAvailable(lottie_animation_wrapper*) { return false; }

void ResetTextureVulkan(lottie_animation_wrapper*, InstanceState* state)
{
    if (state != nullptr)
    {
        state->vulkan.backend = nullptr;
        state->vulkan.unityOwnedTexture = false;
        state->vulkan.uploadAvailable.store(false, std::memory_order_release);
    }
}

bool EnsureTextureVulkan(lottie_animation_wrapper*, InstanceState*, int, int) { return false; }
UploadResult UploadVulkan(InstanceState*, const UploadContext&) { return UploadResult::Failed; }
bool PrepareRenderSlotVulkan(InstanceState*, int, uint32_t, uint32_t, uint8_t*&, uint32_t&) { return false; }
void RefreshCompletedRenderSlotsVulkan(InstanceState*) {}
void BeginUploadEventVulkan(InstanceState*) {}

#endif // LOTTIE_VULKAN_AVAILABLE
#endif // !defined(__EMSCRIPTEN__)

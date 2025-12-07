#pragma once

#include "LottiePlugin.h"

#include <cstdint>
#include <mutex>
#include <queue>

auto constexpr kMaxPendingUploads = static_cast<size_t>(1024);

struct UploadContext
{
    const uint8_t* data = nullptr;
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t stride = 0;
};

#if !defined(__EMSCRIPTEN__)
extern std::mutex gPendingUploadsMutex;
extern std::queue<lottie_animation_wrapper*> gPendingUploads;

void EnqueuePendingUpload(lottie_animation_wrapper* animation);
lottie_animation_wrapper* PopPendingUpload();
void ClearPendingUploads();
#endif

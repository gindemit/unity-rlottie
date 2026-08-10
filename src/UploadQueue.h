#ifndef UPLOAD_QUEUE_H
#define UPLOAD_QUEUE_H

#include "LottiePlugin.h"
#include <cstddef>

#if !defined(__EMSCRIPTEN__)

// Maximum number of pending uploads in the queue
constexpr size_t kMaxPendingUploads = 1024;

// Enqueue an animation for texture upload
void EnqueueUpload(lottie_animation_wrapper* animation);

// Dequeue an animation from the upload queue
lottie_animation_wrapper* DequeueUpload();

// Remove an animation from the pending uploads queue
void RemoveFromUploadQueue(lottie_animation_wrapper* animation);

// Clear the upload queue
void ClearUploadQueue();

#endif // !defined(__EMSCRIPTEN__)

#endif // UPLOAD_QUEUE_H

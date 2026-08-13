#ifndef UPLOAD_QUEUE_H
#define UPLOAD_QUEUE_H

#include "LottiePlugin.h"
#include <cstddef>

#if !defined(__EMSCRIPTEN__)

struct InstanceState;

// Maximum number of pending uploads in the queue
constexpr size_t kMaxPendingUploads = 1024;

// Enqueue an animation for texture upload
void EnqueueUpload(lottie_animation_wrapper* animation);

// Requeue while the caller already owns state->lifetimeMutex. This avoids
// recursively acquiring that non-recursive mutex from an upload-event tail.
lottie_animation_wrapper* EnqueueUploadWithLifetimeLocked(
    lottie_animation_wrapper* animation,
    InstanceState* state);

// Clear the queue flag of an evicted instance after the current instance's
// lifetime lock has been released.
void FinishDroppedUpload(
    lottie_animation_wrapper* dropped,
    lottie_animation_wrapper* current);

// Dequeue an animation from the upload queue
lottie_animation_wrapper* DequeueUpload();

// Remove an animation from the pending uploads queue
void RemoveFromUploadQueue(lottie_animation_wrapper* animation);

// Clear the upload queue
void ClearUploadQueue();

#endif // !defined(__EMSCRIPTEN__)

#endif // UPLOAD_QUEUE_H

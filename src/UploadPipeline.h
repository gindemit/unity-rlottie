#ifndef UPLOAD_PIPELINE_H
#define UPLOAD_PIPELINE_H

#include "LottiePlugin.h"

#if !defined(__EMSCRIPTEN__)

// Forward declaration
struct lottie_render_data;

// Publish render data for upload (called after rendering completes)
void PublishUpload(lottie_animation_wrapper* animation, const lottie_render_data* render_data);

// Perform GPU upload for an animation (called on render thread)
void PerformUploadFor(lottie_animation_wrapper* animation);

#endif // !defined(__EMSCRIPTEN__)

#endif // UPLOAD_PIPELINE_H

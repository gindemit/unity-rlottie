#pragma once

#include "UploadQueue.h"

struct lottie_render_data;
struct lottie_animation_wrapper;
struct InstanceState;

#if !defined(__EMSCRIPTEN__)
void PublishUpload(lottie_animation_wrapper* animation, const lottie_render_data* render_data);
void PerformUploadFor(lottie_animation_wrapper* animation);
void OnRenderEvent(int eventID);
#endif

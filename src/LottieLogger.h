#ifndef LOTTIE_LOGGER_H
#define LOTTIE_LOGGER_H

#include "LottiePlugin.h"

struct IUnityLog;

void LottieLoggerSetUnityLog(IUnityLog* log);
LottieLogLevel LottieGetGlobalLogLevel();
void LottieSetGlobalLogLevel(LottieLogLevel level);

void LottieLogInfo(lottie_animation_wrapper* animation, const char* format, ...);
void LottieLogWarning(lottie_animation_wrapper* animation, const char* format, ...);
void LottieLogError(lottie_animation_wrapper* animation, const char* format, ...);

#endif // LOTTIE_LOGGER_H

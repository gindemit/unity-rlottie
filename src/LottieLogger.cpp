#include "LottieLogger.h"

#include <atomic>
#include <cstdarg>
#include <cstdio>

#if !defined(__EMSCRIPTEN__)
#include "IUnityLog.h"

#if defined(__ANDROID__)
#include <android/log.h>
#define LOTTIE_ANDROID_LOG_TAG "LottiePlugin"
#endif

static std::atomic<LottieLogLevel> sGlobalLogLevel(LOTTIE_LOG_INFO);
static IUnityLog* sLog = nullptr;

void LottieLoggerSetUnityLog(IUnityLog* log)
{
    sLog = log;
}

LottieLogLevel LottieGetGlobalLogLevel()
{
    return sGlobalLogLevel.load();
}

void LottieSetGlobalLogLevel(LottieLogLevel level)
{
    sGlobalLogLevel.store(level);
}

void LottieLogInfo(lottie_animation_wrapper* animation, const char* format, ...)
{
    LottieLogLevel level = animation ? animation->logLevel : sGlobalLogLevel.load();
    if (level >= LOTTIE_LOG_INFO)
    {
        char buffer[512];
        va_list args;
        va_start(args, format);
        vsnprintf(buffer, sizeof(buffer), format, args);
        va_end(args);
#if defined(__ANDROID__)
        __android_log_print(ANDROID_LOG_INFO, LOTTIE_ANDROID_LOG_TAG, "%s", buffer);
#endif
        if (sLog)
        {
            UNITY_LOG(sLog, buffer);
        }
    }
}

void LottieLogWarning(lottie_animation_wrapper* animation, const char* format, ...)
{
    LottieLogLevel level = animation ? animation->logLevel : sGlobalLogLevel.load();
    if (level >= LOTTIE_LOG_WARNING)
    {
        char buffer[512];
        va_list args;
        va_start(args, format);
        vsnprintf(buffer, sizeof(buffer), format, args);
        va_end(args);
#if defined(__ANDROID__)
        __android_log_print(ANDROID_LOG_WARN, LOTTIE_ANDROID_LOG_TAG, "%s", buffer);
#endif
        if (sLog)
        {
            UNITY_LOG_WARNING(sLog, buffer);
        }
    }
}

void LottieLogError(lottie_animation_wrapper* animation, const char* format, ...)
{
    LottieLogLevel level = animation ? animation->logLevel : sGlobalLogLevel.load();
    if (level >= LOTTIE_LOG_ERROR)
    {
        char buffer[512];
        va_list args;
        va_start(args, format);
        vsnprintf(buffer, sizeof(buffer), format, args);
        va_end(args);
#if defined(__ANDROID__)
        __android_log_print(ANDROID_LOG_ERROR, LOTTIE_ANDROID_LOG_TAG, "%s", buffer);
#endif
        if (sLog)
        {
            UNITY_LOG_ERROR(sLog, buffer);
        }
    }
}

#else

static std::atomic<LottieLogLevel> sGlobalLogLevel(LOTTIE_LOG_INFO);

void LottieLoggerSetUnityLog(IUnityLog*) {}

LottieLogLevel LottieGetGlobalLogLevel()
{
    return sGlobalLogLevel.load();
}

void LottieSetGlobalLogLevel(LottieLogLevel level)
{
    sGlobalLogLevel.store(level);
}

// WebGL logging - output to browser console via printf
void LottieLogInfo(lottie_animation_wrapper*, const char* format, ...)
{
    if (sGlobalLogLevel.load() < LOTTIE_LOG_INFO) return;
    va_list args;
    va_start(args, format);
    printf("[Lottie INFO] ");
    vprintf(format, args);
    printf("\n");
    va_end(args);
}

void LottieLogWarning(lottie_animation_wrapper*, const char* format, ...)
{
    if (sGlobalLogLevel.load() < LOTTIE_LOG_WARNING) return;
    va_list args;
    va_start(args, format);
    printf("[Lottie WARNING] ");
    vprintf(format, args);
    printf("\n");
    va_end(args);
}

void LottieLogError(lottie_animation_wrapper*, const char* format, ...)
{
    if (sGlobalLogLevel.load() < LOTTIE_LOG_ERROR) return;
    va_list args;
    va_start(args, format);
    printf("[Lottie ERROR] ");
    vprintf(format, args);
    printf("\n");
    va_end(args);
}

#endif

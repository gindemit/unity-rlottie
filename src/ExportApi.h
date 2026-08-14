#ifndef _EXPORT_API_H_
#define _EXPORT_API_H_

#if defined(__EMSCRIPTEN__)
  #include <emscripten/emscripten.h>
  #define EXPORT_API EMSCRIPTEN_KEEPALIVE
#elif defined(_MSC_VER)
  #if defined(LOTTIE_PLUGIN_EXPORTS)
    #define EXPORT_API __declspec(dllexport)
  #else
    #define EXPORT_API __declspec(dllimport)
  #endif
#else
  #define EXPORT_API __attribute__((visibility("default")))
#endif

#endif // !_EXPORT_API_H_

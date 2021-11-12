#ifndef _EXPORT_API_H_
#define _EXPORT_API_H_

#if _MSC_VER // this is defined when compiling with Visual Studio
#define EXPORT_API __declspec(dllexport) // Visual Studio needs annotating exported functions with this
#else
#define EXPORT_API // XCode or Android Studio does not need annotating exported functions, so define is empty
#endif

#endif // !_EXPORT_API_H_
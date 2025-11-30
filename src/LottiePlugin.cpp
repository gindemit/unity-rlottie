#include "LottiePlugin.h"
#include "vdebug.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <functional>
#include <memory>
#include <mutex>
#include <queue>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#if defined(__ANDROID__)
#    include <android/log.h>
#    define LOTTIE_ANDROID_LOG_TAG "LottiePlugin"
#endif

// --- Platform GPU headers FIRST ---
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <GL/gl.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "opengl32.lib")
#endif
// ----------------------------------

#if !defined(__EMSCRIPTEN__)
#include "IUnityInterface.h"
#include "IUnityProfiler.h"
#include "IUnityGraphics.h"
#if defined(_WIN32)
#include "IUnityGraphicsD3D12.h"
#endif
#include "IUnityLog.h"


static IUnityProfiler* sProfiler = nullptr;
static IUnityLog* sLog = nullptr;
static const UnityProfilerMarkerDesc* sMkGetResult = nullptr;
static const UnityProfilerMarkerDesc* sMkPublish = nullptr;
static const UnityProfilerMarkerDesc* sMkUpload = nullptr;
#    if defined(_WIN32)
static IUnityGraphicsD3D12v8* sD3D12v8 = nullptr;
static IUnityGraphicsD3D12v7* sD3D12 = nullptr;
static IUnityGraphicsD3D12v6* sD3D12v6 = nullptr;
static IUnityGraphicsD3D12v5* sD3D12v5 = nullptr;
static ID3D12CommandAllocator* sD3D12Allocator = nullptr;
static ID3D12GraphicsCommandList* sD3D12CmdList = nullptr;
#    endif

static inline void ProfBegin(const UnityProfilerMarkerDesc* d)
{
    if (sProfiler != nullptr && sProfiler->IsAvailable() && d != nullptr)
    {
        sProfiler->BeginSample(d);
    }
}

static inline void ProfEnd(const UnityProfilerMarkerDesc* d)
{
    if (sProfiler != nullptr && sProfiler->IsAvailable() && d != nullptr)
    {
        sProfiler->EndSample(d);
    }
}

// Global log level (default: Info – verbose logging)
static std::atomic<LottieLogLevel> sGlobalLogLevel(LOTTIE_LOG_INFO);

static inline void LottieLogInfo(lottie_animation_wrapper* animation, const char* format, ...)
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

static inline void LottieLogWarning(lottie_animation_wrapper* animation, const char* format, ...)
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

static inline void LottieLogError(lottie_animation_wrapper* animation, const char* format, ...)
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
struct IUnityInterfaces;
struct UnityProfilerMarkerDesc;

static inline void ProfBegin(const UnityProfilerMarkerDesc*) {}
static inline void ProfEnd(const UnityProfilerMarkerDesc*) {}

static const UnityProfilerMarkerDesc* sMkGetResult = nullptr;
static const UnityProfilerMarkerDesc* sMkPublish = nullptr;
static const UnityProfilerMarkerDesc* sMkUpload = nullptr;

// WebGL stubs for logging
static std::atomic<LottieLogLevel> sGlobalLogLevel(LOTTIE_LOG_INFO);

static inline void LottieLogInfo(lottie_animation_wrapper*, const char*, ...) {}
static inline void LottieLogWarning(lottie_animation_wrapper*, const char*, ...) {}
static inline void LottieLogError(lottie_animation_wrapper*, const char*, ...) {}
#endif

#if defined(__APPLE__)
#    include <TargetConditionals.h>
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#    import <Metal/Metal.h>
#endif

#if defined(__ANDROID__)
#    include <GLES3/gl3.h>
#    include <GLES2/gl2ext.h>
// Define GL_BGRA as an alias to GL_BGRA_EXT for consistency
#    ifndef GL_BGRA
#        define GL_BGRA GL_BGRA_EXT
#    endif
// On Apple platforms we rely on Metal; avoid desktop OpenGL headers there.
#elif !defined(__EMSCRIPTEN__) && !defined(__APPLE__) && !defined(_WIN32)
#    include <GL/gl.h>
#    ifndef GL_BGRA
#        define GL_BGRA 0x80E1
#    endif
#endif

// Define OpenGL constants for Windows if needed
#if defined(_WIN32)
#    ifndef GL_BGRA
#        define GL_BGRA 0x80E1
#    endif
#    ifndef GL_CLAMP_TO_EDGE
#        define GL_CLAMP_TO_EDGE 0x812F
#    endif
#    ifndef GL_RGBA8
#        define GL_RGBA8 0x8058
#    endif
#    ifndef GL_RGBA
#        define GL_RGBA 0x1908
#    endif
#endif

namespace
{
#if !defined(__EMSCRIPTEN__)
  // Forward declarations so Clang/GCC know these names before use
  struct InstanceState; // forward declare the struct used in maps

  void PerformUploadFor(lottie_animation_wrapper* animation);
  void PublishUpload(lottie_animation_wrapper* animation,
                      const lottie_render_data* render_data);
#endif
#if defined(__ANDROID__) || defined(_WIN32)
    static bool gHasBGRAExt = false;
    static bool gIsOpenGLES = false;

    static void DetectGLExtensions()
    {
        gHasBGRAExt = false;
        gIsOpenGLES = false;

        // Check if we're running OpenGL ES (ANGLE or other ES implementation)
        const char* version = reinterpret_cast<const char*>(glGetString(GL_VERSION));
        if (version != nullptr)
        {
            gIsOpenGLES = (std::strstr(version, "OpenGL ES") != nullptr);
            LottieLogInfo(nullptr, "[Lottie] OpenGL version: %s, isOpenGLES: %s", version, gIsOpenGLES ? "true" : "false");
        }

        const char* ext = reinterpret_cast<const char*>(glGetString(GL_EXTENSIONS));
        if (ext != nullptr)
        {
            if (std::strstr(ext, "GL_EXT_texture_format_BGRA8888") != nullptr)
            {
                gHasBGRAExt = true;
            }
            // Desktop OpenGL always supports GL_BGRA via core
            if (!gIsOpenGLES)
            {
                gHasBGRAExt = true;
            }
        }
        else if (!gIsOpenGLES)
        {
            // Desktop OpenGL supports BGRA in core
            gHasBGRAExt = true;
        }
        LottieLogInfo(nullptr, "[Lottie] BGRA extension available: %s", gHasBGRAExt ? "true" : "false");
    }
#endif
#if !defined(__EMSCRIPTEN__)
    struct UploadContext
    {
        const uint8_t* data = nullptr;
        uint32_t width = 0;
        uint32_t height = 0;
        uint32_t stride = 0;
    };

    struct InstanceState
    {
        std::mutex uploadMutex;
        UploadContext uploadCtx{};
        std::atomic<uint64_t> uploadVersion{0};
        std::atomic<uint64_t> requestedVersion{0};
        uint64_t uploadedVersion = 0;
        std::atomic<bool> uploadQueued{false};
        int texW = 0;
        int texH = 0;
        void* nativeTex = nullptr;
#if defined(_WIN32)
        ID3D12Resource* d3d12Tex = nullptr;

        // Upload ring
        ID3D12Resource* d3d12Upload = nullptr;
        void* d3d12UploadMapped = nullptr;
        UINT d3d12UploadSlotCount = 3;
        UINT d3d12UploadWriteIdx = 0;
        UINT64 d3d12UploadSlotBytes = 0;

        // Footprint for a single subresource copy
        D3D12_PLACED_SUBRESOURCE_FOOTPRINT d3d12Footprint{};
        D3D12_RESOURCE_STATES d3d12TexState = D3D12_RESOURCE_STATE_COMMON;

        // D3D11
        ID3D11Texture2D* d3dTex = nullptr;
        
        // OpenGL (for OpenGLCore on Windows)
        GLuint glTex = 0;
        std::vector<uint8_t> rgbaScratch;
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        id<MTLTexture> metalTex = nil;
#elif !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
        GLuint glTex = 0;
        std::vector<uint8_t> rgbaScratch;
#endif
    };

#endif // !defined(__EMSCRIPTEN__)

    enum class Renderer
    {
        Unknown,
        D3D12,
        D3D11,
        Metal,
        OpenGL,
        Vulkan,
    };

#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__) && (defined(_WIN32) || defined(__ANDROID__))
    // Helper function to check and log OpenGL errors
    static void CheckGLError(lottie_animation_wrapper* animation, const char* operation)
    {
#if defined(_WIN32)
        GLenum err = glGetError();
        if (err != GL_NO_ERROR)
        {
            char errorMsg[256];
            snprintf(errorMsg, sizeof(errorMsg), "[Lottie] OpenGL error after %s: 0x%04X", operation, err);
            LottieLogError(animation, errorMsg);
        }
#elif defined(__ANDROID__)
        GLenum err = glGetError();
        if (err != GL_NO_ERROR)
        {
            char errorMsg[256];
            snprintf(errorMsg, sizeof(errorMsg), "[Lottie] OpenGL error after %s: 0x%04X", operation, err);
            LottieLogError(animation, errorMsg);
        }
#endif
    }
#endif

    Renderer gRenderer = Renderer::Unknown;
    void* gDevice = nullptr;

#if defined(_WIN32)
    ID3D12Device* gD3D12Device = nullptr;
    ID3D12CommandQueue* gD3D12Queue = nullptr;
    ID3D11Device* gD3DDevice = nullptr;
    ID3D11DeviceContext* gD3DContext = nullptr;
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    id<MTLDevice> gMetalDevice = nil;
#endif

#if !defined(__EMSCRIPTEN__)
    std::mutex gInstancesMutex;
    std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> gInstances;

    std::mutex gPendingUploadsMutex;
    std::queue<lottie_animation_wrapper*> gPendingUploads;
    constexpr size_t kMaxPendingUploads = 1024;

    // Forward declarations for functions used within this block
    InstanceState* GetState(lottie_animation_wrapper* animation, bool create = true);
    void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state);
    bool EnsureTexture(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
#endif

    enum UnityGfxRenderer
    {
        kUnityGfxRendererOpenGL = 0,
        kUnityGfxRendererD3D9 = 1,
        kUnityGfxRendererD3D11 = 2,
        kUnityGfxRendererGCM = 3,
        kUnityGfxRendererNull = 4,
        kUnityGfxRendererXenon = 6,
        kUnityGfxRendererOpenGLES20 = 8,
        kUnityGfxRendererOpenGLES30 = 11,
        kUnityGfxRendererMetal = 16,
        kUnityGfxRendererOpenGLCore = 17,
        kUnityGfxRendererD3D12 = 18,
        kUnityGfxRendererVulkan = 21
    };

    Renderer ToRenderer(int deviceType)
    {
        Renderer result;
        switch (deviceType)
        {
            case kUnityGfxRendererD3D11:
                result = Renderer::D3D11;
                LottieLogInfo(nullptr, "[Lottie] Graphics device type: D3D11");
                break;
            case kUnityGfxRendererD3D12:
                result = Renderer::D3D12;
                LottieLogInfo(nullptr, "[Lottie] Graphics device type: D3D12");
                break;
            case kUnityGfxRendererOpenGL:
            case kUnityGfxRendererOpenGLCore:
            case kUnityGfxRendererOpenGLES20:
            case kUnityGfxRendererOpenGLES30:
                result = Renderer::OpenGL;
                LottieLogInfo(nullptr, "[Lottie] Graphics device type: OpenGL/GLES");
                break;
            case kUnityGfxRendererMetal:
                result = Renderer::Metal;
                LottieLogInfo(nullptr, "[Lottie] Graphics device type: Metal");
                break;
            case kUnityGfxRendererVulkan:
                result = Renderer::Vulkan;
                LottieLogInfo(nullptr, "[Lottie] Graphics device type: Vulkan");
                break;
            default:
                result = Renderer::Unknown;
                LottieLogWarning(nullptr, "[Lottie] Unknown graphics device type");
                break;
        }
        return result;
    }

#if !defined(__EMSCRIPTEN__)
    InstanceState* GetState(lottie_animation_wrapper* animation, bool create)
    {
        if (animation == nullptr)
        {
            LottieLogWarning(nullptr, "[Lottie] GetState called with null animation");
            return nullptr;
        }

        std::lock_guard<std::mutex> lock(gInstancesMutex);
        auto it = gInstances.find(animation);
        if (it != gInstances.end())
        {
            LottieLogInfo(animation, "[Lottie] Found existing instance state");
            return it->second.get();
        }

        if (!create)
        {
            LottieLogInfo(animation, "[Lottie] Instance state not found, create=false");
            return nullptr;
        }

        auto instance = std::make_unique<InstanceState>();
        InstanceState* raw = instance.get();
        gInstances.emplace(animation, std::move(instance));
        LottieLogInfo(animation, "[Lottie] Created new instance state");
        return raw;
    }

    void ResetTextureState(lottie_animation_wrapper* animation, InstanceState* state)
    {
        if (state == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] ResetTextureState called with null state");
            return;
        }
        LottieLogInfo(animation, "[Lottie] Resetting texture state");

#if defined(_WIN32)
        if (state->d3d12Upload)
        {
            if (state->d3d12UploadMapped)
            {
                state->d3d12Upload->Unmap(0, nullptr);
                state->d3d12UploadMapped = nullptr;
            }
            state->d3d12Upload->Release();
            state->d3d12Upload = nullptr;
        }
        if (state->d3d12Tex)
        {
            state->d3d12Tex->Release();
            state->d3d12Tex = nullptr;
        }
        state->d3d12Footprint = {};
        state->d3d12UploadSlotBytes = 0;
        state->d3d12UploadWriteIdx = 0;
        state->d3d12TexState = D3D12_RESOURCE_STATE_COMMON;
        if (state->d3dTex)
        {
            state->d3dTex->Release();
            state->d3dTex = nullptr;
        }
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        state->metalTex = nil;
#elif !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
        if (state->glTex != 0)
        {
            glDeleteTextures(1, &state->glTex);
            state->glTex = 0;
        }
#endif

        state->nativeTex = nullptr;
        state->texW = 0;
        state->texH = 0;
        state->uploadCtx = {};
        state->uploadVersion.store(0, std::memory_order_relaxed);
        state->requestedVersion.store(0, std::memory_order_relaxed);
        state->uploadedVersion = 0;
        state->uploadQueued.store(false, std::memory_order_release);
    }

    bool EnsureTexture(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
    {
        if (state == nullptr || width <= 0 || height <= 0)
        {
            LottieLogWarning(animation, "[Lottie] EnsureTexture: invalid parameters");
            return false;
        }

        // Check if texture already exists with matching dimensions
        // Exclude the dummy pointer (0x1) used for deferred OpenGL texture creation on Windows
        const bool isDummyPointer = (state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(0x1)));
        if (state->texW == width && state->texH == height && state->nativeTex != nullptr && !isDummyPointer)
        {
            LottieLogInfo(animation, "[Lottie] EnsureTexture: texture already exists with matching dimensions");
            return true;
        }

        LottieLogInfo(animation, "[Lottie] EnsureTexture: creating new texture %dx%d", width, height);
        ResetTextureState(animation, state);

        switch (gRenderer)
        {
            case Renderer::D3D12:
#if defined(_WIN32)
            {
                if (!gD3D12Device)
                {
                    LottieLogError(animation, "[Lottie] D3D12 device is null");
                    return false;
                }

                D3D12_RESOURCE_DESC texDesc{};
                texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
                texDesc.Alignment = 0;
                texDesc.Width = static_cast<UINT64>(width);
                texDesc.Height = static_cast<UINT>(height);
                texDesc.DepthOrArraySize = 1;
                texDesc.MipLevels = 1;
                texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
                texDesc.SampleDesc.Count = 1;
                texDesc.SampleDesc.Quality = 0;
                texDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
                texDesc.Flags = D3D12_RESOURCE_FLAG_NONE;

                D3D12_HEAP_PROPERTIES heapDefault{ D3D12_HEAP_TYPE_DEFAULT };
                heapDefault.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
                heapDefault.MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN;
                heapDefault.CreationNodeMask = 1;
                heapDefault.VisibleNodeMask = 1;
                
                ID3D12Resource* texture = nullptr;
                HRESULT hr = gD3D12Device->CreateCommittedResource(
                    &heapDefault, D3D12_HEAP_FLAG_NONE, &texDesc,
                    D3D12_RESOURCE_STATE_COMMON, nullptr,
                    IID_PPV_ARGS(&texture));
                if (FAILED(hr) || !texture)
                {
                    char errorMsg[256];
                    snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to create D3D12 texture resource. HRESULT: 0x%08X", hr);
                    LottieLogError(animation, errorMsg);
                    if (texture)
                    {
                        texture->Release();
                    }
                    return false;
                }

                UINT64 totalBytes = 0;
                gD3D12Device->GetCopyableFootprints(
                    &texDesc, 0, 1, 0, &state->d3d12Footprint, nullptr, nullptr, &totalBytes);

                state->d3d12UploadSlotBytes = totalBytes;
                const UINT64 uploadBytes = totalBytes * state->d3d12UploadSlotCount;

                D3D12_HEAP_PROPERTIES heapUpload{ D3D12_HEAP_TYPE_UPLOAD };
                heapUpload.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
                heapUpload.MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN;
                heapUpload.CreationNodeMask = 1;
                heapUpload.VisibleNodeMask = 1;
                
                D3D12_RESOURCE_DESC uploadDesc{};
                uploadDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
                uploadDesc.Alignment = 0;
                uploadDesc.Width = uploadBytes;
                uploadDesc.Height = 1;
                uploadDesc.DepthOrArraySize = 1;
                uploadDesc.MipLevels = 1;
                uploadDesc.Format = DXGI_FORMAT_UNKNOWN;
                uploadDesc.SampleDesc.Count = 1;
                uploadDesc.SampleDesc.Quality = 0;
                uploadDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
                uploadDesc.Flags = D3D12_RESOURCE_FLAG_NONE;

                ID3D12Resource* upload = nullptr;
                hr = gD3D12Device->CreateCommittedResource(
                    &heapUpload, D3D12_HEAP_FLAG_NONE, &uploadDesc,
                    D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                    IID_PPV_ARGS(&upload));
                if (FAILED(hr) || !upload)
                {
                    char errorMsg[256];
                    snprintf(
                      errorMsg,
                      sizeof(errorMsg),
                      "[Lottie] Failed to create D3D12 upload buffer. HRESULT: 0x%08X, uploadBytes: %llu", hr, static_cast<unsigned long long>(uploadBytes));
                    LottieLogError(animation, errorMsg);

                    if (upload)
                    {
                        upload->Release();
                    }
                    texture->Release();
                    state->d3d12Footprint = {};
                    state->d3d12UploadSlotBytes = 0;
                    return false;
                }

                void* mapped = nullptr;
                hr = upload->Map(0, nullptr, &mapped);
                if (FAILED(hr) || !mapped)
                {
                    char errorMsg[256];
                    snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to map D3D12 upload buffer. HRESULT: 0x%08X", hr);
                    LottieLogError(animation, errorMsg);
                    upload->Release();
                    texture->Release();
                    state->d3d12Footprint = {};
                    state->d3d12UploadSlotBytes = 0;
                    return false;
                }

                state->d3d12Tex = texture;
                state->d3d12Upload = upload;
                state->d3d12UploadMapped = mapped;
                state->d3d12UploadWriteIdx = 0;
                state->d3d12TexState = D3D12_RESOURCE_STATE_COMMON;
                state->nativeTex = texture;
                state->texW = width;
                state->texH = height;
                LottieLogInfo(animation, "[Lottie] D3D12 texture created successfully");
                return true;
            }
#else
                return false;
#endif
            case Renderer::D3D11:
#if defined(_WIN32)
            {
                if (gD3DDevice == nullptr)
                {
                    LottieLogError(animation, "[Lottie] D3D11 device is null");
                    return false;
                }

                D3D11_TEXTURE2D_DESC desc{};
                desc.Width = static_cast<UINT>(width);
                desc.Height = static_cast<UINT>(height);
                desc.MipLevels = 1;
                desc.ArraySize = 1;
                desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
                desc.SampleDesc.Count = 1;
                desc.Usage = D3D11_USAGE_DYNAMIC;
                desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
                desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;

                ID3D11Texture2D* texture = nullptr;
                HRESULT hr = gD3DDevice->CreateTexture2D(&desc, nullptr, &texture);
                if (FAILED(hr) || texture == nullptr)
                {
                    LottieLogError(animation, "[Lottie] Failed to create D3D11 texture");
                    if (texture != nullptr)
                    {
                        texture->Release();
                    }
                    return false;
                }

                state->d3dTex = texture;
                state->nativeTex = texture;
                state->texW = width;
                state->texH = height;
                LottieLogInfo(animation, "[Lottie] D3D11 texture created successfully");
                return true;
            }
#else
                return false;
#endif
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
            {
                if (gMetalDevice == nil)
                {
                    LottieLogError(animation, "[Lottie] Metal device is nil");
                    return false;
                }

                MTLTextureDescriptor* descriptor =
                    [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
                                                                       width:width
                                                                      height:height
                                                                   mipmapped:NO];
                descriptor.usage = MTLTextureUsageShaderRead | MTLTextureUsageRenderTarget;

                id<MTLTexture> texture = [gMetalDevice newTextureWithDescriptor:descriptor];
                if (texture == nil)
                {
                    LottieLogError(animation, "[Lottie] Failed to create Metal texture");
                    return false;
                }

                state->metalTex = texture;
                state->nativeTex = (__bridge void*)texture;
                state->texW = width;
                state->texH = height;
                LottieLogInfo(animation, "[Lottie] Metal texture created successfully");
                return true;
            }
#else
                return false;
#endif
            case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
            {
                // Clear any existing OpenGL errors
#if defined(_WIN32) || defined(__ANDROID__)
                while (glGetError() != GL_NO_ERROR) {}
#endif

                // If we used deferred creation on Windows, nativeTex might be the dummy 0x1.
                if (state->glTex == 0 &&
                    state->nativeTex == reinterpret_cast<void*>(static_cast<uintptr_t>(0x1)))
                {
                    LottieLogInfo(animation,
                        "[Lottie] EnsureTexture: performing deferred OpenGL texture creation");
                    state->nativeTex = nullptr; // clear dummy
                }

                if (state->glTex == 0)
                {
                    LottieLogInfo(animation, "[Lottie] EnsureTexture: glGenTextures");
                    glGenTextures(1, &state->glTex);
#if defined(_WIN32) || defined(__ANDROID__)
                    CheckGLError(animation, "glGenTextures");
#endif
                }

                if (state->glTex == 0)
                {
                    LottieLogError(animation,
                        "[Lottie] EnsureTexture: glTex is still 0 after glGenTextures");
#if defined(_WIN32) || defined(__ANDROID__)
                    CheckGLError(animation, "texture generation check");
#endif
                    return false;
                }
                glBindTexture(GL_TEXTURE_2D, state->glTex);
#if defined(_WIN32) || defined(__ANDROID__)
                CheckGLError(animation, "glBindTexture");
#endif
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
#if defined(_WIN32) || defined(__ANDROID__)
                CheckGLError(animation, "glTexParameteri");
#endif
#    if defined(__ANDROID__) || defined(_WIN32)
                const bool useBGRA = gHasBGRAExt;
                // For OpenGL ES with BGRA extension, use GL_BGRA for both internal and upload format
                // For OpenGL ES without BGRA, use GL_RGBA for both
                // For desktop OpenGL (non-ES), use GL_RGBA8 internal format with GL_BGRA upload format
                GLint internalFormat;
                GLenum uploadFormat;
                if (gIsOpenGLES)
                {
                    // OpenGL ES requires matching internal and upload formats
                    if (useBGRA)
                    {
                        internalFormat = GL_BGRA;
                        uploadFormat = GL_BGRA;
                    }
                    else
                    {
                        internalFormat = GL_RGBA;
                        uploadFormat = GL_RGBA;
                    }
                }
                else
                {
                    // Desktop OpenGL supports GL_RGBA8 with GL_BGRA upload
                    internalFormat = GL_RGBA8;
                    uploadFormat = useBGRA ? GL_BGRA : GL_RGBA;
                }
                LottieLogInfo(animation, "[Lottie] EnsureTexture: isOpenGLES=%s, useBGRA=%s, internalFormat=0x%04X, uploadFormat=0x%04X",
                              gIsOpenGLES ? "true" : "false", useBGRA ? "true" : "false", internalFormat, uploadFormat);
                glTexImage2D(
                    GL_TEXTURE_2D, 0, internalFormat, width, height, 0, uploadFormat, GL_UNSIGNED_BYTE, nullptr);
#    else
                const GLint internalFormat = GL_RGBA8;
                glTexImage2D(
                    GL_TEXTURE_2D, 0, internalFormat, width, height, 0, GL_BGRA, GL_UNSIGNED_BYTE, nullptr);
#    endif
#if defined(_WIN32) || defined(__ANDROID__)
                CheckGLError(animation, "glTexImage2D");
#endif

                state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(state->glTex));
                state->texW = width;
                state->texH = height;

                LottieLogInfo(animation,
                    "[Lottie] EnsureTexture: OpenGL texture created successfully, glTex=%u, nativeTex=%p",
                    state->glTex, state->nativeTex);
                return true;
            }
#else
                return false;
#endif
            case Renderer::Vulkan:
                // For Vulkan, Unity manages textures internally.
                // We render to CPU buffer and Unity handles GPU upload via Texture2D.LoadRawTextureData
                // No native texture pointer needed - C# uses regular Texture2D like WebGL
                LottieLogInfo(animation, "[Lottie] Vulkan: Using CPU-side rendering, Unity handles GPU upload");
                state->texW = width;
                state->texH = height;
                return true;
            case Renderer::Unknown:
            default:
                LottieLogError(animation, "[Lottie] EnsureTexture: Unknown or unsupported renderer");
                return false;
        }
    }
#endif // !defined(__EMSCRIPTEN__)

#if defined(__ANDROID__) || defined(_WIN32) || (!defined(__EMSCRIPTEN__) && !defined(__APPLE__))
    void ConvertBGRAtoRGBA(std::vector<uint8_t>& buffer, const UploadContext& ctx)
    {
        buffer.resize(static_cast<size_t>(ctx.width) * static_cast<size_t>(ctx.height) * 4u);
        const uint8_t* src = ctx.data;
        uint8_t* dst = buffer.data();
        for (uint32_t y = 0; y < ctx.height; ++y)
        {
            for (uint32_t x = 0; x < ctx.width; ++x)
            {
                const size_t srcOffset = static_cast<size_t>(y) * ctx.stride + static_cast<size_t>(x) * 4u;
                const size_t dstOffset = (static_cast<size_t>(y) * ctx.width + x) * 4u;
                dst[dstOffset + 0] = src[srcOffset + 2];
                dst[dstOffset + 1] = src[srcOffset + 1];
                dst[dstOffset + 2] = src[srcOffset + 0];
                dst[dstOffset + 3] = src[srcOffset + 3];
            }
        }
    }
#endif

#if defined(_WIN32)
    struct D3D12CommandContext
    {
        ID3D12GraphicsCommandList* cmd = nullptr;
        bool ownsCommandList = false;
    };

    void ReleaseOwnedD3D12CommandList()
    {
        if (sD3D12CmdList != nullptr)
        {
            sD3D12CmdList->Release();
            sD3D12CmdList = nullptr;
        }

        if (sD3D12Allocator != nullptr)
        {
            sD3D12Allocator->Release();
            sD3D12Allocator = nullptr;
        }
    }

    ID3D12GraphicsCommandList* AcquireUnityD3D12CommandList()
    {
        if (sD3D12v8 != nullptr)
        {
            UnityGraphicsD3D12RecordingState recordingState{};
            if (sD3D12v8->CommandRecordingState(&recordingState))
            {
                return recordingState.commandList;
            }
        }

        if (sD3D12 != nullptr)
        {
            UnityGraphicsD3D12RecordingState recordingState{};
            if (sD3D12->CommandRecordingState(&recordingState))
            {
                return recordingState.commandList;
            }
        }

        if (sD3D12v6 != nullptr)
        {
            UnityGraphicsD3D12RecordingState recordingState{};
            if (sD3D12v6->CommandRecordingState(&recordingState))
            {
                return recordingState.commandList;
            }
        }

        return nullptr;
    }

    D3D12CommandContext AcquireD3D12CommandContext()
    {
        D3D12CommandContext ctx{};
        ctx.cmd = AcquireUnityD3D12CommandList();
        if (ctx.cmd != nullptr)
        {
            return ctx;
        }

        if (sD3D12v5 == nullptr || gD3D12Device == nullptr || gD3D12Queue == nullptr)
        {
            return ctx;
        }

        if (sD3D12Allocator == nullptr)
        {
            if (FAILED(gD3D12Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&sD3D12Allocator))))
            {
                ReleaseOwnedD3D12CommandList();
                return ctx;
            }
        }
        else
        {
            sD3D12Allocator->Reset();
        }

        if (sD3D12CmdList == nullptr)
        {
            if (FAILED(gD3D12Device->CreateCommandList(
                    0,
                    D3D12_COMMAND_LIST_TYPE_DIRECT,
                    sD3D12Allocator,
                    nullptr,
                    IID_PPV_ARGS(&sD3D12CmdList))))
            {
                ReleaseOwnedD3D12CommandList();
                return ctx;
            }
        }
        else
        {
            sD3D12CmdList->Reset(sD3D12Allocator, nullptr);
        }

        ctx.cmd = sD3D12CmdList;
        ctx.ownsCommandList = true;
        return ctx;
    }

    void SubmitD3D12CommandContext(const D3D12CommandContext& ctx)
    {
        if (!ctx.ownsCommandList || ctx.cmd == nullptr || gD3D12Queue == nullptr)
        {
            return;
        }

        ctx.cmd->Close();
        ID3D12CommandList* lists[] = { ctx.cmd };
        gD3D12Queue->ExecuteCommandLists(1, lists);
    }

    void D3D12StageBGRAUpload(InstanceState* state, const UploadContext& ctx)
    {
#if defined(_WIN32)
        if (!state || !state->d3d12Upload || !state->d3d12UploadMapped || !ctx.data)
        {
            return;
        }

        const UINT64 slotBase = state->d3d12UploadSlotBytes * state->d3d12UploadWriteIdx;
        const UINT rowPitch = state->d3d12Footprint.Footprint.RowPitch;

        uint8_t* dstBase = reinterpret_cast<uint8_t*>(state->d3d12UploadMapped)
            + slotBase
            + state->d3d12Footprint.Offset;

        const uint8_t* src = ctx.data;
        for (uint32_t y = 0; y < ctx.height; ++y)
        {
            std::memcpy(
                dstBase + static_cast<size_t>(y) * rowPitch,
                src + static_cast<size_t>(y) * ctx.stride,
                ctx.stride);
        }
#endif
    }

    void UploadD3D12(InstanceState* state, const UploadContext& ctx)
    {
#if defined(_WIN32)
        if (!state || !state->d3d12Tex || !state->d3d12Upload || !ctx.data)
        {
            return;
        }

        D3D12StageBGRAUpload(state, ctx);

        D3D12CommandContext ctxWrapper = AcquireD3D12CommandContext();
        ID3D12GraphicsCommandList* cmd = ctxWrapper.cmd;
        if (cmd == nullptr)
        {
            return;
        }

        if (state->d3d12TexState != D3D12_RESOURCE_STATE_COPY_DEST)
        {
            D3D12_RESOURCE_BARRIER b{};
            b.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            b.Transition.pResource = state->d3d12Tex;
            b.Transition.Subresource = 0;
            b.Transition.StateBefore = state->d3d12TexState;
            b.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
            cmd->ResourceBarrier(1, &b);
        }

        D3D12_TEXTURE_COPY_LOCATION dst{};
        dst.pResource = state->d3d12Tex;
        dst.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dst.SubresourceIndex = 0;

        D3D12_TEXTURE_COPY_LOCATION src{};
        src.pResource = state->d3d12Upload;
        src.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        src.PlacedFootprint = state->d3d12Footprint;
        src.PlacedFootprint.Offset += state->d3d12UploadSlotBytes * state->d3d12UploadWriteIdx;

        cmd->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);

        D3D12_RESOURCE_BARRIER toSRV{};
        toSRV.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        toSRV.Transition.pResource = state->d3d12Tex;
        toSRV.Transition.Subresource = 0;
        toSRV.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
        toSRV.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        cmd->ResourceBarrier(1, &toSRV);

        state->d3d12TexState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

        if (sD3D12v8)
        {
            sD3D12v8->NotifyResourceState(state->d3d12Tex, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, false);
        }

        state->d3d12UploadWriteIdx = (state->d3d12UploadWriteIdx + 1) % state->d3d12UploadSlotCount;
        SubmitD3D12CommandContext(ctxWrapper);
#endif
    }

    void UploadD3D11(InstanceState* state, const UploadContext& ctx)
    {
#if defined(_WIN32)
        if (state == nullptr || gD3DContext == nullptr || state->d3dTex == nullptr || ctx.data == nullptr)
        {
            return;
        }

        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(gD3DContext->Map(state->d3dTex, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped)))
        {
            return;
        }

        const uint8_t* src = ctx.data;
        uint8_t* dst = reinterpret_cast<uint8_t*>(mapped.pData);
        for (uint32_t y = 0; y < ctx.height; ++y)
        {
            std::memcpy(dst + y * mapped.RowPitch, src + y * ctx.stride, ctx.stride);
        }

        gD3DContext->Unmap(state->d3dTex, 0);
#endif
    }

#endif // _WIN32

#if !defined(__EMSCRIPTEN__)
    void UploadMetal(InstanceState* state, const UploadContext& ctx)
    {
#if defined(__APPLE__)
        if (state == nullptr || state->metalTex == nil || ctx.data == nullptr)
        {
            return;
        }

        MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
        [state->metalTex replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];
#endif
    }

    void UploadOpenGL(InstanceState* state, const UploadContext& ctx)
    {
#if !defined(__APPLE__)
        if (state == nullptr || state->glTex == 0 || ctx.data == nullptr)
        {
            LottieLogWarning(nullptr, "[Lottie] UploadOpenGL: Invalid parameters (state=%p, glTex=%u, data=%p)",
                           state, state ? state->glTex : 0, ctx.data);
            return;
        }

        LottieLogInfo(nullptr,
            "[Lottie] UploadOpenGL: Uploading to texture %u, size=%ux%u, stride=%u",
            state->glTex, ctx.width, ctx.height, ctx.stride);

#if defined(_WIN32) || defined(__ANDROID__)
        // Clear any existing errors
        while (glGetError() != GL_NO_ERROR) {}
#endif

        glBindTexture(GL_TEXTURE_2D, state->glTex);
#if defined(_WIN32) || defined(__ANDROID__)
        CheckGLError(nullptr, "glBindTexture in UploadOpenGL");
#endif

        glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
#if defined(_WIN32) || defined(__ANDROID__)
        CheckGLError(nullptr, "glPixelStorei in UploadOpenGL");
#endif

#    if defined(__ANDROID__) || defined(_WIN32)
        if (gHasBGRAExt)
        {
            glTexSubImage2D(
                GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
        }
        else
        {
            LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Converting BGRA to RGBA (no BGRA extension)");
            ConvertBGRAtoRGBA(state->rgbaScratch, ctx);
            glTexSubImage2D(
                GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_RGBA, GL_UNSIGNED_BYTE, state->rgbaScratch.data());
        }
#    else
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
#    endif

#if defined(_WIN32) || defined(__ANDROID__)
        CheckGLError(nullptr, "glTexSubImage2D in UploadOpenGL");
#endif

        // Ensure OpenGL commands are executed
        glFlush();
#if defined(_WIN32) || defined(__ANDROID__)
        CheckGLError(nullptr, "glFlush in UploadOpenGL");
#endif

        LottieLogInfo(nullptr, "[Lottie] UploadOpenGL: Upload completed successfully");
#endif
    }
#endif // !defined(__EMSCRIPTEN__)

#if !defined(__EMSCRIPTEN__)
    void PerformUploadFor(lottie_animation_wrapper* animation)
    {
        if (animation == nullptr)
        {
            LottieLogWarning(nullptr, "[Lottie] PerformUploadFor: animation is null");
            return;
        }

        LottieLogInfo(animation, "[Lottie] PerformUploadFor: renderer=%d", (int)gRenderer);

        InstanceState* state = GetState(animation, /*create=*/false);
        if (state == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] PerformUploadFor: state is null");
            return;
        }

        const uint64_t requested = state->requestedVersion.load(std::memory_order_acquire);
        if (requested == 0 || requested == state->uploadedVersion)
        {
            LottieLogInfo(animation, "[Lottie] PerformUploadFor: no new data to upload");
            state->uploadQueued.store(false, std::memory_order_release);
            return;
        }

        UploadContext ctx;
        {
            std::lock_guard<std::mutex> lock(state->uploadMutex);
            ctx = state->uploadCtx;
        }

        if (ctx.data == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] PerformUploadFor: upload context data is null");
            state->uploadQueued.store(false, std::memory_order_release);
            return;
        }

        if (!EnsureTexture(animation, state, static_cast<int>(ctx.width), static_cast<int>(ctx.height)))
        {
            LottieLogError(animation, "[Lottie] PerformUploadFor: EnsureTexture failed");
            state->uploadQueued.store(false, std::memory_order_release);
            return;
        }

        LottieLogInfo(animation, "[Lottie] PerformUploadFor: uploading texture data");
        switch (gRenderer)
        {
            case Renderer::D3D12:
#if defined(_WIN32)
                UploadD3D12(state, ctx);
#endif
                break;
            case Renderer::D3D11:
#if defined(_WIN32)
                UploadD3D11(state, ctx);
#endif
                break;
            case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                UploadMetal(state, ctx);
#endif
                break;
            case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
                UploadOpenGL(state, ctx);
#endif
                break;
            case Renderer::Vulkan:
                // Vulkan: No direct texture upload needed here.
                // Unity's Texture2D.LoadRawTextureData + Apply handles the GPU upload.
                LottieLogInfo(animation, "[Lottie] Vulkan: Skipping direct upload, Unity handles it");
                break;
            case Renderer::Unknown:
            default:
                break;
        }

        state->uploadedVersion = requested;
        state->uploadQueued.store(false, std::memory_order_release);
        LottieLogInfo(animation, "[Lottie] PerformUploadFor: upload completed successfully");
    }

    void PublishUpload(lottie_animation_wrapper* animation, const lottie_render_data* render_data)
    {
        if (animation == nullptr || render_data == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] PublishUpload: null animation or render_data");
            return;
        }

        InstanceState* state = GetState(animation);
        if (state == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] PublishUpload: could not get state");
            return;
        }

        UploadContext ctx;
        ctx.data = reinterpret_cast<const uint8_t*>(render_data->buffer);
        ctx.width = render_data->width;
        ctx.height = render_data->height;
        ctx.stride = render_data->bytesPerLine;

        {
            std::lock_guard<std::mutex> lock(state->uploadMutex);
            state->uploadCtx = ctx;
        }

        state->uploadVersion.fetch_add(1, std::memory_order_release);
        LottieLogInfo(animation, "[Lottie] PublishUpload: upload published, version incremented");
    }
#endif // !__EMSCRIPTEN__

    static lottie_animation_wrapper* create_animation_wrapper(std::unique_ptr<rlottie::Animation>& animation)
    {
        lottie_animation_wrapper* animation_wrapper = new lottie_animation_wrapper();

        if (animation_wrapper == nullptr)
        {
            fprintf(stderr, "Couldnt allocate lottie_animation_wrapper!");
            LottieLogError(nullptr, "[Lottie] Failed to allocate lottie_animation_wrapper");
            return nullptr;
        }

        animation_wrapper->self = animation_wrapper;
        animation_wrapper->frameRate = animation->frameRate();
        animation_wrapper->totalFrame = animation->totalFrame();
        animation_wrapper->duration = animation->duration();
        size_t width = 0;
        size_t height = 0;
        animation->size(width, height);
        animation_wrapper->width = width;
        animation_wrapper->height = height;
        animation_wrapper->animation = std::move(animation);
        animation_wrapper->logLevel = sGlobalLogLevel.load();
        LottieLogInfo(animation_wrapper, "[Lottie] Created animation wrapper: width=%lld, height=%lld, fps=%.2f, frames=%lld, duration=%.2fs",
                     (long long)animation_wrapper->width, (long long)animation_wrapper->height,
                     animation_wrapper->frameRate, (long long)animation_wrapper->totalFrame,
                     animation_wrapper->duration);
        return animation_wrapper;
    }
}

extern "C"
{
    EXPORT_API int32_t lottie_load_from_data(
        const char* json_data,
        const char* resource_path,
        lottie_animation_wrapper** animation_wrapper)
    {
        const char* path_display = (resource_path == nullptr) ? "(null)" : 
                                    (resource_path[0] == '\0') ? "(empty)" : resource_path;
        LottieLogInfo(nullptr, "[Lottie] Loading animation from data, resource_path='%s'", path_display);
        const std::function<void(float& r, float& g, float& b)>& null_func = nullptr;
        auto animation = rlottie::Animation::loadFromData(std::string(json_data), std::string(resource_path), null_func);
        if (!animation)
        {
            fprintf(stderr, "Couldnt load from data '%s'.", resource_path);
            LottieLogError(nullptr, "[Lottie] Failed to load animation from data");
            return -1;
        }
        *animation_wrapper = create_animation_wrapper(animation);
        LottieLogInfo(*animation_wrapper, "[Lottie] Successfully loaded animation from data");
        return *animation_wrapper == nullptr ? -1 : 0;
    }

    EXPORT_API int32_t lottie_load_from_file(
        const char* file_path,
        lottie_animation_wrapper** animation_wrapper)
    {
        LottieLogInfo(nullptr, "[Lottie] Loading animation from file: %s", file_path ? file_path : "(null)");
        auto animation = rlottie::Animation::loadFromFile(std::string(file_path));

        if (!animation)
        {
            fprintf(stderr, "Couldnt load from file '%s'.", file_path);
            LottieLogError(nullptr, "[Lottie] Failed to load animation from file");
            return -1;
        }

        *animation_wrapper = create_animation_wrapper(animation);
        LottieLogInfo(*animation_wrapper, "[Lottie] Successfully loaded animation from file");
        return 0;
    }

    EXPORT_API int32_t lottie_dispose_wrapper(lottie_animation_wrapper** animation_wrapper)
    {
        LottieLogInfo(animation_wrapper ? *animation_wrapper : nullptr, "[Lottie] Disposing animation wrapper");
#if !defined(__EMSCRIPTEN__)
        if (animation_wrapper != nullptr && *animation_wrapper != nullptr)
        {
            {
                std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
                auto it = gInstances.find(*animation_wrapper);
                if (it != gInstances.end())
                {
                    ResetTextureState(*animation_wrapper, it->second.get());
                    gInstances.erase(it);
                }
            }

            std::lock_guard<std::mutex> queueLock(gPendingUploadsMutex);
            std::queue<lottie_animation_wrapper*> filtered;
            while (!gPendingUploads.empty())
            {
                lottie_animation_wrapper* candidate = gPendingUploads.front();
                gPendingUploads.pop();
                if (candidate != *animation_wrapper)
                {
                    filtered.push(candidate);
                }
            }
            std::swap(gPendingUploads, filtered);
        }
#endif
        delete (*animation_wrapper);
        *animation_wrapper = nullptr;
        LottieLogInfo(nullptr, "[Lottie] Animation wrapper disposed successfully");
        return 0;
    }

    EXPORT_API int32_t lottie_render_immediately(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] Rendering frame %u immediately", frame_number);
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
#if !defined(__EMSCRIPTEN__)
        PublishUpload(animation_wrapper, render_data);
#endif
        LottieLogInfo(animation_wrapper, "[Lottie] Frame rendered successfully");
        return 0;
    }

#if defined(__EMSCRIPTEN__)
    // WebGL single-thread fallback: do sync render instead of futures
    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio)
    {
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        // WebGL single-thread fallback: do sync render instead of futures
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* /*animation_wrapper*/,
        lottie_render_data* /*render_data*/)
    {
        // WebGL single-thread fallback: nothing to do here
        return 0;
    }
#else
    EXPORT_API int32_t lottie_render_create_future_async(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        uint32_t frame_number,
        bool keep_aspect_ratio)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] Creating async render future for frame %u", frame_number);
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        render_data->render_future = animation_wrapper->animation->render(frame_number, surface, keep_aspect_ratio);
        LottieLogInfo(animation_wrapper, "[Lottie] Async render future created");
        return 0;
    }

    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        int32_t* ready)
    {
        if (render_data == nullptr || ready == nullptr)
        {
            LottieLogWarning(animation_wrapper, "[Lottie] try_get_future_result called with null parameters");
            return -1;
        }

        *ready = 0;

        if (!render_data->render_future.valid() ||
            render_data->render_future.wait_for(std::chrono::milliseconds(0)) != std::future_status::ready)
        {
            return 0;
        }

        LottieLogInfo(animation_wrapper, "[Lottie] Render future ready, getting result");
        render_data->render_future.get();

        ProfBegin(sMkPublish);
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(sMkPublish);

        *ready = 1;
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and published");
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data)
    {
        LottieLogInfo(animation_wrapper, "[Lottie] Waiting for render future result");
        ProfBegin(sMkGetResult);
        render_data->render_future.get();
        ProfEnd(sMkGetResult);

        ProfBegin(sMkPublish);
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(sMkPublish);
        LottieLogInfo(animation_wrapper, "[Lottie] Future result retrieved and uploaded");
        return 0;
    }
#endif

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data)
    {
        LottieLogInfo(nullptr, "[Lottie] Allocating render data");
        *render_data = new lottie_render_data();
        if (*render_data == nullptr)
        {
            fprintf(stderr, "Couldnt allocate lottie_render_data!");
            LottieLogError(nullptr, "[Lottie] Failed to allocate render data");
            return -1;
        }
        LottieLogInfo(nullptr, "[Lottie] Render data allocated successfully");
        return 0;
    }

    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data)
    {
        LottieLogInfo(nullptr, "[Lottie] Disposing render data");
        delete (*render_data);
        *render_data = nullptr;
        return 0;
    }

    EXPORT_API int32_t lottie_set_log_level(
        lottie_animation_wrapper* animation_wrapper,
        LottieLogLevel log_level)
    {
        if (animation_wrapper != nullptr)
        {
            animation_wrapper->logLevel = log_level;
        }
        else
        {
            // Set global log level if no specific animation wrapper
            sGlobalLogLevel.store(log_level);
        }
        return 0;
    }

    EXPORT_API int32_t lottie_set_global_log_level(LottieLogLevel log_level)
    {
        sGlobalLogLevel.store(log_level);
        LottieLogInfo(nullptr, "[Lottie] Global log level changed to %d", (int)log_level);
        return 0;
    }

#if !defined(__EMSCRIPTEN__)
    EXPORT_API void* lottie_create_texture(lottie_animation_wrapper* animation, int width, int height)
    {
        LottieLogInfo(animation,
            "[Lottie] Creating texture: width=%d, height=%d (renderer=%d)",
            width, height, (int)gRenderer);

        InstanceState* state = GetState(animation);

        // If Unity hasn't initialized the graphics device yet, bail out clearly.
        if (gRenderer == Renderer::Unknown)
        {
            LottieLogWarning(animation,
                "[Lottie] lottie_create_texture called before graphics device is initialized");
            return nullptr;
        }

        // SPECIAL CASE: OpenGL on Windows – we MUST avoid GL calls from this thread.
        if (gRenderer == Renderer::OpenGL)
        {
#if defined(_WIN32)
            // Defer actual GL texture creation to the render thread (OnRenderEvent).
            state->texW = width;
            state->texH = height;
            // Dummy non-null pointer so C# knows we are in "native texture" mode.
            state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(0x1));

            LottieLogInfo(animation,
                "[Lottie] OpenGL (Windows): deferring texture creation; dummy nativeTex=%p",
                state->nativeTex);

            return state->nativeTex;
#else
            // Non-Windows OpenGL (e.g. Linux): create immediately as before.
            if (!EnsureTexture(animation, state, width, height))
            {
                LottieLogError(animation,
                    "[Lottie] OpenGL non-Windows: EnsureTexture failed in lottie_create_texture");
                return nullptr;
            }

            LottieLogInfo(animation,
                "[Lottie] OpenGL non-Windows: Texture created immediately, nativeTex=%p",
                state ? state->nativeTex : nullptr);

            return state != nullptr ? state->nativeTex : nullptr;
#endif
        }

        // D3D11, D3D12, Metal, Vulkan: create immediately.
        if (!EnsureTexture(animation, state, width, height))
        {
            LottieLogError(animation,
                "[Lottie] lottie_create_texture: EnsureTexture failed for renderer=%d",
                (int)gRenderer);
            return nullptr;
        }

        LottieLogInfo(animation,
            "[Lottie] lottie_create_texture: texture ready, nativeTex=%p, texW=%d, texH=%d",
            state ? state->nativeTex : nullptr,
            state ? state->texW : 0,
            state ? state->texH : 0);

        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void lottie_destroy_texture(lottie_animation_wrapper* animation, void* /*tex*/)
    {
        LottieLogInfo(animation, "[Lottie] Destroying texture");
        InstanceState* state = GetState(animation, /*create=*/false);
        ResetTextureState(animation, state);
    }

    EXPORT_API void* lottie_get_native_texture_ptr(lottie_animation_wrapper* animation)
    {
        InstanceState* state = GetState(animation, /*create=*/false);
        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void lottie_update_texture(lottie_animation_wrapper* animation)
    {
        if (animation == nullptr)
        {
            LottieLogWarning(nullptr, "[Lottie] update_texture: animation is null");
            return;
        }

        InstanceState* state = GetState(animation, /*create=*/false);
        if (state == nullptr)
        {
            LottieLogWarning(animation, "[Lottie] update_texture: no instance state");
            return;
        }

        const uint64_t latest = state->uploadVersion.load(std::memory_order_acquire);
        if (latest == 0 || latest == state->uploadedVersion)
        {
            return;
        }

        state->requestedVersion.store(latest, std::memory_order_release);
        const bool enqueue = !state->uploadQueued.exchange(true, std::memory_order_acq_rel);
        if (!enqueue)
        {
            LottieLogInfo(animation, "[Lottie] Upload already queued, skipping");
            return;
        }
        LottieLogInfo(animation, "[Lottie] Queueing texture upload");

        lottie_animation_wrapper* dropped = nullptr;
        {
            std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
            if (gPendingUploads.size() >= kMaxPendingUploads)
            {
                dropped = gPendingUploads.front();
                gPendingUploads.pop();
            }

            gPendingUploads.push(animation);
        }

        if (dropped != nullptr && dropped != animation)
        {
            LottieLogWarning(dropped, "[Lottie] Upload queue full, dropping oldest upload");
            InstanceState* droppedState = GetState(dropped, /*create=*/false);
            if (droppedState != nullptr)
            {
                droppedState->uploadQueued.store(false, std::memory_order_release);
            }
        }
    }

    static void UNITY_INTERFACE_API OnRenderEvent(int /*eventID*/)
    {
        lottie_animation_wrapper* animation = nullptr;
        {
            std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
            if (!gPendingUploads.empty())
            {
                animation = gPendingUploads.front();
                gPendingUploads.pop();
            }
        }

        if (animation != nullptr)
        {
            LottieLogInfo(animation, "[Lottie] Performing GPU upload on render thread");
            ProfBegin(sMkUpload);
            PerformUploadFor(animation);
            ProfEnd(sMkUpload);
        }
    }

    EXPORT_API UnityRenderingEvent lottie_get_render_event_func(void)
    {
        return OnRenderEvent;
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
    {
#if !defined(__EMSCRIPTEN__)
        sLog = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityLog>() : nullptr;
        LottieLogInfo(nullptr, "[Lottie] Plugin loading...");
        sProfiler = unityInterfaces != nullptr ? unityInterfaces->Get<IUnityProfiler>() : nullptr;
        if (sProfiler != nullptr && sProfiler->IsAvailable())
        {
            sProfiler->CreateMarker(&sMkGetResult, "Lottie/GetFutureResult", kUnityProfilerCategoryScripts, kUnityProfilerMarkerFlagDefault, 0);
            sProfiler->CreateMarker(&sMkPublish, "Lottie/PublishUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
            sProfiler->CreateMarker(&sMkUpload, "Lottie/PerformUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
        }

#if defined(_WIN32)
        sD3D12v8 = nullptr;
        sD3D12 = nullptr;
        sD3D12v6 = nullptr;
        sD3D12v5 = nullptr;
        if (unityInterfaces != nullptr)
        {
            sD3D12v8 = unityInterfaces->Get<IUnityGraphicsD3D12v8>();
            if (sD3D12v8 == nullptr)
            {
                sD3D12 = unityInterfaces->Get<IUnityGraphicsD3D12v7>();
            }
            if (sD3D12 == nullptr)
            {
                sD3D12v6 = unityInterfaces->Get<IUnityGraphicsD3D12v6>();
                if (sD3D12v6 == nullptr)
                {
                    sD3D12v5 = unityInterfaces->Get<IUnityGraphicsD3D12v5>();
                }
            }
        }
        LottieLogInfo(nullptr, "[Lottie] Plugin loaded successfully");
#endif
#else
        (void)unityInterfaces;
#endif
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
    {
        LottieLogInfo(nullptr, "[Lottie] Plugin unloading...");
        gDevice = nullptr;
        gRenderer = Renderer::Unknown;
#if defined(_WIN32)
        gD3D12Queue = nullptr;
        gD3D12Device = nullptr;
        ReleaseOwnedD3D12CommandList();
        if (gD3DContext)
        {
            gD3DContext->Release();
            gD3DContext = nullptr;
        }
        gD3DDevice = nullptr;
#endif
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        gMetalDevice = nil;
#endif
#if !defined(__EMSCRIPTEN__)
        std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
        for (auto& entry : gInstances)
        {
            ResetTextureState(entry.first, entry.second.get());
        }
        gInstances.clear();

        {
            std::lock_guard<std::mutex> queueLock(gPendingUploadsMutex);
            std::queue<lottie_animation_wrapper*> empty;
            std::swap(gPendingUploads, empty);
        }
#endif

#if !defined(__EMSCRIPTEN__)
        sProfiler = nullptr;
        sMkGetResult = nullptr;
        sMkPublish = nullptr;
        sMkUpload = nullptr;
#    if defined(_WIN32)
        sD3D12v8 = nullptr;
        sD3D12 = nullptr;
        sD3D12v6 = nullptr;
        sD3D12v5 = nullptr;
#    endif
        LottieLogInfo(nullptr, "[Lottie] Plugin unloaded successfully");
        sLog = nullptr;
#endif
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnitySetGraphicsDevice(void* device, int deviceType, int eventType)
    {
        LottieLogInfo(nullptr,
            "[Lottie] UnitySetGraphicsDevice: eventType=%d, deviceType=%d, device=%p",
            eventType, deviceType, device);

        if (eventType == ::kUnityGfxDeviceEventInitialize)
        {
            LottieLogInfo(nullptr, "[Lottie] Graphics device initializing");
            gRenderer = ToRenderer(deviceType);
            gDevice = device;
            switch (gRenderer)
            {
                case Renderer::D3D12:
#if defined(_WIN32)
                    gD3D12Device = reinterpret_cast<ID3D12Device*>(device);
                    if (sD3D12v8 != nullptr)
                    {
                        gD3D12Queue = sD3D12v8->GetCommandQueue();
                    }
                    else if (sD3D12 != nullptr)
                    {
                        gD3D12Queue = sD3D12->GetCommandQueue();
                    }
                    else if (sD3D12v6 != nullptr)
                    {
                        gD3D12Queue = sD3D12v6->GetCommandQueue();
                    }
                    else if (sD3D12v5 != nullptr)
                    {
                        gD3D12Queue = sD3D12v5->GetCommandQueue();
                    }
#endif
                    break;
                case Renderer::D3D11:
#if defined(_WIN32)
                    gD3DDevice = reinterpret_cast<ID3D11Device*>(device);
                    if (gD3DDevice)
                    {
                        gD3DDevice->GetImmediateContext(&gD3DContext);
                    }
#endif
                    break;
                case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                    gMetalDevice = (__bridge id<MTLDevice>)device;
#endif
                    break;
                case Renderer::OpenGL:
#if defined(_WIN32) || defined(__ANDROID__)
                    DetectGLExtensions();
#endif
                    break;
                case Renderer::Vulkan:
                    LottieLogInfo(nullptr, "[Lottie] Vulkan device initialized");
                    break;
                default:
                    // For unknown renderers, try to detect GL extensions in case it's OpenGL-based
#if defined(__ANDROID__) || defined(_WIN32)
                    DetectGLExtensions();
#endif
                    break;
            }
        }
        else if (eventType == ::kUnityGfxDeviceEventShutdown)
        {
            LottieLogInfo(nullptr, "[Lottie] Graphics device shutting down");
            gDevice = nullptr;
            switch (gRenderer)
            {
                case Renderer::D3D12:
#if defined(_WIN32)
                    gD3D12Queue = nullptr;
                    gD3D12Device = nullptr;
                    ReleaseOwnedD3D12CommandList();
#endif
                    break;
                case Renderer::D3D11:
#if defined(_WIN32)
                    if (gD3DContext)
                    {
                        gD3DContext->Release();
                        gD3DContext = nullptr;
                    }
                    gD3DDevice = nullptr;
#endif
                    break;
                case Renderer::Metal:
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
                    gMetalDevice = nil;
#endif
                    break;
                case Renderer::Vulkan:
                    LottieLogInfo(nullptr, "[Lottie] Vulkan device shutdown");
                    break;
                default:
                    break;
            }
            gRenderer = Renderer::Unknown;
#if defined(__ANDROID__) || defined(_WIN32)
            gHasBGRAExt = false;
            gIsOpenGLES = false;
#endif
            {
                std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
                for (auto& entry : gInstances)
                {
                    ResetTextureState(entry.first, entry.second.get());
                }
            }
            {
                std::lock_guard<std::mutex> queueLock(gPendingUploadsMutex);
                std::queue<lottie_animation_wrapper*> empty;
                std::swap(gPendingUploads, empty);
            }
        }
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityRenderEvent(int eventID)
    {
        OnRenderEvent(eventID);
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_plugin_load(IUnityInterfaces* ifaces)
    {
        UnityPluginLoad(ifaces);
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_plugin_unload()
    {
        UnityPluginUnload();
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_set_graphics_device(void* device, int deviceType, int eventType)
    {
        UnitySetGraphicsDevice(device, deviceType, eventType);
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API lottie_unity_render_event(int eventID)
    {
        UnityRenderEvent(eventID);
    }
#endif // !__EMSCRIPTEN__
}

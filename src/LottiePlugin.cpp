#include "LottiePlugin.h"
#include "vdebug.h"

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <functional>
#include <memory>
#include <mutex>
#include <queue>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#if !defined(__EMSCRIPTEN__)
#    include "IUnityInterface.h"
#    include "IUnityProfiler.h"

static IUnityProfiler* sProfiler = nullptr;
static UnityProfilerMarkerDesc* sMkGetResult = nullptr;
static UnityProfilerMarkerDesc* sMkPublish = nullptr;
static UnityProfilerMarkerDesc* sMkUpload = nullptr;

static inline void ProfBegin(UnityProfilerMarkerDesc* d)
{
    if (sProfiler != nullptr && sProfiler->IsAvailable() && d != nullptr)
    {
        sProfiler->BeginSample(d);
    }
}

static inline void ProfEnd(UnityProfilerMarkerDesc* d)
{
    if (sProfiler != nullptr && sProfiler->IsAvailable() && d != nullptr)
    {
        sProfiler->EndSample(d);
    }
}
#else
struct UnityProfilerMarkerDesc;

static inline void ProfBegin(UnityProfilerMarkerDesc*) {}
static inline void ProfEnd(UnityProfilerMarkerDesc*) {}

static UnityProfilerMarkerDesc* sMkGetResult = nullptr;
static UnityProfilerMarkerDesc* sMkPublish = nullptr;
static UnityProfilerMarkerDesc* sMkUpload = nullptr;
#endif

#if defined(_WIN32)
#    include <d3d11.h>
#    pragma comment(lib, "d3d11.lib")
#endif

#if defined(__APPLE__)
#    include <TargetConditionals.h>
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
#    import <Metal/Metal.h>
#endif

#if defined(__ANDROID__)
#    include <GLES3/gl3.h>
// On Apple platforms we rely on Metal; avoid desktop OpenGL headers there.
#elif !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
#    include <GL/gl.h>
#    ifndef GL_BGRA
#        define GL_BGRA 0x80E1
#    endif
#endif

namespace
{
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
        ID3D11Texture2D* d3dTex = nullptr;
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        id<MTLTexture> metalTex = nil;
#elif !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
        GLuint glTex = 0;
#endif
#if defined(__ANDROID__)
        std::vector<uint8_t> rgbaScratch;
#endif
    };

    lottie_animation_wrapper* gBoundAnimation = nullptr;

    enum class Renderer
    {
        Unknown,
        D3D11,
        Metal,
        OpenGL,
    };

    Renderer gRenderer = Renderer::Unknown;
    void* gDevice = nullptr;

#if defined(_WIN32)
    ID3D11Device* gD3DDevice = nullptr;
    ID3D11DeviceContext* gD3DContext = nullptr;
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    id<MTLDevice> gMetalDevice = nil;
#endif

    std::mutex gInstancesMutex;
    std::unordered_map<lottie_animation_wrapper*, std::unique_ptr<InstanceState>> gInstances;

    std::mutex gPendingUploadsMutex;
    std::queue<lottie_animation_wrapper*> gPendingUploads;
    constexpr size_t kMaxPendingUploads = 32;

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
        kUnityGfxRendererD3D12 = 18,
        kUnityGfxRendererVulkan = 21
    };

    enum UnityGfxDeviceEventType
    {
        kUnityGfxDeviceEventInitialize = 0,
        kUnityGfxDeviceEventShutdown = 1,
        kUnityGfxDeviceEventBeforeReset = 2,
        kUnityGfxDeviceEventAfterReset = 3
    };

    Renderer ToRenderer(int deviceType)
    {
        switch (deviceType)
        {
            case kUnityGfxRendererD3D11:
                return Renderer::D3D11;
            case kUnityGfxRendererOpenGL:
            case kUnityGfxRendererOpenGLES20:
            case kUnityGfxRendererOpenGLES30:
                return Renderer::OpenGL;
            case kUnityGfxRendererMetal:
                return Renderer::Metal;
            default:
                return Renderer::Unknown;
        }
    }

    InstanceState* GetState(lottie_animation_wrapper* animation, bool create = true)
    {
        if (animation == nullptr)
        {
            return nullptr;
        }

        std::lock_guard<std::mutex> lock(gInstancesMutex);
        auto it = gInstances.find(animation);
        if (it != gInstances.end())
        {
            return it->second.get();
        }

        if (!create)
        {
            return nullptr;
        }

        auto instance = std::make_unique<InstanceState>();
        InstanceState* raw = instance.get();
        gInstances.emplace(animation, std::move(instance));
        return raw;
    }

    void ResetTextureState(InstanceState* state)
    {
        if (state == nullptr)
        {
            return;
        }

#if defined(_WIN32)
        if (state->d3dTex)
        {
            state->d3dTex->Release();
            state->d3dTex = nullptr;
        }
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        state->metalTex = nil;
#elif !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
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

    bool EnsureTexture(InstanceState* state, int width, int height)
    {
        if (state == nullptr || width <= 0 || height <= 0)
        {
            return false;
        }

        if (state->texW == width && state->texH == height && state->nativeTex != nullptr)
        {
            return true;
        }

        ResetTextureState(state);

        switch (gRenderer)
        {
            case Renderer::D3D11:
#if defined(_WIN32)
            {
                if (gD3DDevice == nullptr)
                {
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
                    return false;
                }

                state->metalTex = texture;
                state->nativeTex = (__bridge void*)texture;
                state->texW = width;
                state->texH = height;
                return true;
            }
#else
                return false;
#endif
            case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
            {
                if (state->glTex == 0)
                {
                    glGenTextures(1, &state->glTex);
                }
                if (state->glTex == 0)
                {
                    return false;
                }
                glBindTexture(GL_TEXTURE_2D, state->glTex);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
#    if defined(__ANDROID__)
                const GLint internalFormat = GL_RGBA;
                glTexImage2D(
                    GL_TEXTURE_2D, 0, internalFormat, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
#    else
                const GLint internalFormat = GL_RGBA8;
                glTexImage2D(
                    GL_TEXTURE_2D, 0, internalFormat, width, height, 0, GL_BGRA, GL_UNSIGNED_BYTE, nullptr);
#    endif
                state->nativeTex = reinterpret_cast<void*>(static_cast<uintptr_t>(state->glTex));
                state->texW = width;
                state->texH = height;
                return true;
            }
#else
                return false;
#endif
            case Renderer::Unknown:
            default:
                return false;
        }
    }

#if defined(__ANDROID__)
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

    void UploadMetal(InstanceState* state, const UploadContext& ctx)
    {
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
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
#if !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
        if (state == nullptr || state->glTex == 0 || ctx.data == nullptr)
        {
            return;
        }

        glBindTexture(GL_TEXTURE_2D, state->glTex);
        glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
#    if defined(__ANDROID__)
        ConvertBGRAtoRGBA(state->rgbaScratch, ctx);
        glTexSubImage2D(
            GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_RGBA, GL_UNSIGNED_BYTE, state->rgbaScratch.data());
#    else
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
#    endif
#endif
    }

    void PerformUploadFor(lottie_animation_wrapper* animation)
    {
        if (animation == nullptr)
        {
            return;
        }

        InstanceState* state = GetState(animation, /*create=*/false);
        if (state == nullptr)
        {
            return;
        }

        const uint64_t requested = state->requestedVersion.load(std::memory_order_acquire);
        if (requested == 0 || requested == state->uploadedVersion)
        {
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
            state->uploadQueued.store(false, std::memory_order_release);
            return;
        }

        if (!EnsureTexture(state, static_cast<int>(ctx.width), static_cast<int>(ctx.height)))
        {
            state->uploadQueued.store(false, std::memory_order_release);
            return;
        }

        switch (gRenderer)
        {
            case Renderer::D3D11:
                UploadD3D11(state, ctx);
                break;
            case Renderer::Metal:
                UploadMetal(state, ctx);
                break;
            case Renderer::OpenGL:
                UploadOpenGL(state, ctx);
                break;
            case Renderer::Unknown:
            default:
                break;
        }

        state->uploadedVersion = requested;
        state->uploadQueued.store(false, std::memory_order_release);
    }

    void PublishUpload(lottie_animation_wrapper* animation, const lottie_render_data* render_data)
    {
        if (animation == nullptr || render_data == nullptr)
        {
            return;
        }

        InstanceState* state = GetState(animation);
        if (state == nullptr)
        {
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
    }
#endif // !__EMSCRIPTEN__

    static lottie_animation_wrapper* create_animation_wrapper(std::unique_ptr<rlottie::Animation>& animation)
    {
        lottie_animation_wrapper* animation_wrapper = new lottie_animation_wrapper();

        if (animation_wrapper == nullptr)
        {
            fprintf(stderr, "Couldnt allocate lottie_animation_wrapper!");
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
        const std::function<void(float& r, float& g, float& b)>& null_func = nullptr;
        auto animation = rlottie::Animation::loadFromData(std::string(json_data), std::string(resource_path), null_func);
        if (!animation)
        {
            fprintf(stderr, "Couldnt load from data '%s'.", resource_path);
            return -1;
        }
        *animation_wrapper = create_animation_wrapper(animation);
        return *animation_wrapper == nullptr ? -1 : 0;
    }

    EXPORT_API int32_t lottie_load_from_file(
        const char* file_path,
        lottie_animation_wrapper** animation_wrapper)
    {
        auto animation = rlottie::Animation::loadFromFile(std::string(file_path));

        if (!animation)
        {
            fprintf(stderr, "Couldnt load from file '%s'.", file_path);
            return -1;
        }

        *animation_wrapper = create_animation_wrapper(animation);
        return 0;
    }

    EXPORT_API int32_t lottie_dispose_wrapper(lottie_animation_wrapper** animation_wrapper)
    {
#if !defined(__EMSCRIPTEN__)
        if (animation_wrapper != nullptr && *animation_wrapper != nullptr)
        {
            {
                std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
                auto it = gInstances.find(*animation_wrapper);
                if (it != gInstances.end())
                {
                    ResetTextureState(it->second.get());
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
        return 0;
    }

    EXPORT_API int32_t lottie_render_immediately(
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
        animation_wrapper->animation->renderSync(frame_number, surface, keep_aspect_ratio);
#if !defined(__EMSCRIPTEN__)
        PublishUpload(animation_wrapper, render_data);
#endif
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
        rlottie::Surface surface(
            render_data->buffer,
            render_data->width,
            render_data->height,
            render_data->bytesPerLine);
        render_data->render_future = animation_wrapper->animation->render(frame_number, surface, keep_aspect_ratio);
        return 0;
    }

    EXPORT_API int32_t lottie_render_try_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data,
        int32_t* ready)
    {
        if (render_data == nullptr || ready == nullptr)
        {
            return -1;
        }

        *ready = 0;

        if (!render_data->render_future.valid() ||
            render_data->render_future.wait_for(std::chrono::milliseconds(0)) != std::future_status::ready)
        {
            return 0;
        }

        render_data->render_future.get();

        ProfBegin(sMkPublish);
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(sMkPublish);

        *ready = 1;
        return 0;
    }

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* animation_wrapper,
        lottie_render_data* render_data)
    {
        ProfBegin(sMkGetResult);
        render_data->render_future.get();
        ProfEnd(sMkGetResult);

        ProfBegin(sMkPublish);
        PublishUpload(animation_wrapper, render_data);
        ProfEnd(sMkPublish);
        return 0;
    }
#endif

    EXPORT_API int32_t lottie_allocate_render_data(lottie_render_data** render_data)
    {
        *render_data = new lottie_render_data();
        if (*render_data == nullptr)
        {
            fprintf(stderr, "Couldnt allocate lottie_render_data!");
            return -1;
        }
        return 0;
    }

    EXPORT_API int32_t lottie_dispose_render_data(lottie_render_data** render_data)
    {
        delete (*render_data);
        *render_data = nullptr;
        return 0;
    }

    EXPORT_API int32_t lottie_initialize_logger(
        const char* log_dir_path,
        const char* log_file_name,
        int32_t log_file_roll_size_mb)
    {
        fprintf(stderr, "Initializing logger (stderr)\n");
        // print the paths
        fprintf(stderr, "log_dir_path: %s\n", log_dir_path);
        fprintf(stderr, "log_file_name: %s\n", log_file_name);
        fprintf(stderr, "log_file_roll_size_mb: %d\n", log_file_roll_size_mb);
        fprintf(stdout, "Initializing logger (stdout)\n");
        initialize(GuaranteedLogger(), std::string(log_dir_path), std::string(log_file_name), log_file_roll_size_mb);
        set_log_level(LogLevel::INFO);

        vDebug << "Initialized logger (debug) test message";
        vWarning << "Initialized logger (warning) test message";
        vCritical << "Initialized logger (critical) test message";
        // print the paths
        vDebug << "log_dir_path: " << log_dir_path;
        vDebug << "log_file_name: " << log_file_name;
        vDebug << "log_file_roll_size_mb: " << log_file_roll_size_mb;
        return 0;
    }

#if !defined(__EMSCRIPTEN__)
    EXPORT_API void* lp_create_texture(int width, int height)
    {
        InstanceState* state = GetState(gBoundAnimation);
        if (!EnsureTexture(state, width, height))
        {
            return nullptr;
        }
        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void lp_destroy_texture(void* /*tex*/)
    {
        InstanceState* state = GetState(gBoundAnimation, /*create=*/false);
        ResetTextureState(state);
    }

    EXPORT_API void* lp_get_native_texture_ptr(void)
    {
        InstanceState* state = GetState(gBoundAnimation, /*create=*/false);
        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API int lp_bind_lottie_instance(lottie_animation_wrapper* animation_wrapper)
    {
        gBoundAnimation = animation_wrapper;
        if (gBoundAnimation == nullptr)
        {
            return 0;
        }
        return GetState(gBoundAnimation) != nullptr ? 1 : 0;
    }

    EXPORT_API void lp_update_texture(void)
    {
        lottie_animation_wrapper* animation = gBoundAnimation;
        if (animation == nullptr)
        {
            return;
        }

        InstanceState* state = GetState(animation, /*create=*/false);
        if (state == nullptr)
        {
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
            return;
        }

        std::lock_guard<std::mutex> lock(gPendingUploadsMutex);
        if (gPendingUploads.size() < kMaxPendingUploads)
        {
            gPendingUploads.push(animation);
        }
        else
        {
            state->uploadQueued.store(false, std::memory_order_release);
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
            ProfBegin(sMkUpload);
            PerformUploadFor(animation);
            ProfEnd(sMkUpload);
        }
    }

    EXPORT_API UnityRenderingEvent lp_get_render_event_func(void)
    {
        return OnRenderEvent;
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(void* unityInterfaces)
    {
#if !defined(__EMSCRIPTEN__)
        auto* ifaces = static_cast<IUnityInterfaces*>(unityInterfaces);
        sProfiler = ifaces != nullptr ? ifaces->Get<IUnityProfiler>() : nullptr;
        if (sProfiler != nullptr && sProfiler->IsAvailable())
        {
            sProfiler->CreateMarker(&sMkGetResult, "Lottie/GetFutureResult", kUnityProfilerCategoryScripts, kUnityProfilerMarkerFlagDefault, 0);
            sProfiler->CreateMarker(&sMkPublish, "Lottie/PublishUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
            sProfiler->CreateMarker(&sMkUpload, "Lottie/PerformUpload", kUnityProfilerCategoryRender, kUnityProfilerMarkerFlagDefault, 0);
        }
#else
        (void)unityInterfaces;
#endif
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
    {
        gBoundAnimation = nullptr;
        gDevice = nullptr;
        gRenderer = Renderer::Unknown;
#if defined(_WIN32)
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
        std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
        for (auto& entry : gInstances)
        {
            ResetTextureState(entry.second.get());
        }
        gInstances.clear();

        {
            std::lock_guard<std::mutex> queueLock(gPendingUploadsMutex);
            std::queue<lottie_animation_wrapper*> empty;
            std::swap(gPendingUploads, empty);
        }

#if !defined(__EMSCRIPTEN__)
        sProfiler = nullptr;
        sMkGetResult = nullptr;
        sMkPublish = nullptr;
        sMkUpload = nullptr;
#endif
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnitySetGraphicsDevice(void* device, int deviceType, int eventType)
    {
        if (eventType == kUnityGfxDeviceEventInitialize)
        {
            gRenderer = ToRenderer(deviceType);
            gDevice = device;
            switch (gRenderer)
            {
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
                default:
                    break;
            }
        }
        else if (eventType == kUnityGfxDeviceEventShutdown)
        {
            gDevice = nullptr;
            switch (gRenderer)
            {
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
                default:
                    break;
            }
            gRenderer = Renderer::Unknown;
            gBoundAnimation = nullptr;
            {
                std::lock_guard<std::mutex> instanceLock(gInstancesMutex);
                for (auto& entry : gInstances)
                {
                    ResetTextureState(entry.second.get());
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
#endif // !__EMSCRIPTEN__
}

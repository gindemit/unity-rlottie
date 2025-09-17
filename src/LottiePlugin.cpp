#include "LottiePlugin.h"
#include "vdebug.h"

#include <atomic>
#include <cstdint>
#include <cstring>
#include <functional>
#include <mutex>
#include <string>
#include <vector>

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
#    ifndef GL_BGRA
#        define GL_BGRA 0x80E1
#    endif
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

    std::mutex gUploadMutex;
    UploadContext gUploadContext;
    std::atomic<uint64_t> gUploadVersion{0};
    std::atomic<uint64_t> gRequestedVersion{0};
    uint64_t gUploadedVersion = 0;

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
    void* gNativeTexture = nullptr;
    int gTextureWidth = 0;
    int gTextureHeight = 0;
    int gRequestedWidth = 0;
    int gRequestedHeight = 0;

#if defined(_WIN32)
    ID3D11Device* gD3DDevice = nullptr;
    ID3D11DeviceContext* gD3DContext = nullptr;
    ID3D11Texture2D* gD3DTexture = nullptr;
#endif

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
    id<MTLDevice> gMetalDevice = nil;
    id<MTLTexture> gMetalTexture = nil;
#endif

#if !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
    GLuint gGLTexture = 0;
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

    void ResetTextureState()
    {
        gTextureWidth = 0;
        gTextureHeight = 0;
        gNativeTexture = nullptr;
#if defined(_WIN32)
        if (gD3DTexture)
        {
            gD3DTexture->Release();
            gD3DTexture = nullptr;
        }
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        gMetalTexture = nil;
#elif !defined(__EMSCRIPTEN__) && !defined(__APPLE__)
        if (gGLTexture != 0)
        {
            glDeleteTextures(1, &gGLTexture);
            gGLTexture = 0;
        }
#endif
    }

    bool EnsureTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        if (width == gTextureWidth && height == gTextureHeight && gNativeTexture != nullptr)
        {
            return true;
        }

        ResetTextureState();

        gTextureWidth = width;
        gTextureHeight = height;

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

                HRESULT hr = gD3DDevice->CreateTexture2D(&desc, nullptr, &gD3DTexture);
                if (FAILED(hr))
                {
                    gD3DTexture = nullptr;
                    return false;
                }
                gNativeTexture = gD3DTexture;
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
                MTLTextureDescriptor* descriptor = [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
                                                                                                    width:width
                                                                                                   height:height
                                                                                                mipmapped:NO];
                descriptor.usage = MTLTextureUsageShaderRead | MTLTextureUsageRenderTarget;
                gMetalTexture = [gMetalDevice newTextureWithDescriptor:descriptor];
                gNativeTexture = (__bridge void*)gMetalTexture;
                return gMetalTexture != nil;
            }
#else
                return false;
#endif
            case Renderer::OpenGL:
#if !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
            {
                if (gGLTexture == 0)
                {
                    glGenTextures(1, &gGLTexture);
                }
                glBindTexture(GL_TEXTURE_2D, gGLTexture);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
#    if defined(__ANDROID__)
                const GLint internalFormat = GL_RGBA;
#    else
                const GLint internalFormat = GL_RGBA8;
#    endif
                glTexImage2D(GL_TEXTURE_2D, 0, internalFormat, width, height, 0, GL_BGRA, GL_UNSIGNED_BYTE, nullptr);
                gNativeTexture = reinterpret_cast<void*>(static_cast<uintptr_t>(gGLTexture));
                return gGLTexture != 0;
            }
#else
                return false;
#endif
            case Renderer::Unknown:
            default:
                return false;
        }
    }

    void UploadD3D11(const UploadContext& ctx)
    {
#if defined(_WIN32)
        if (gD3DContext == nullptr || gD3DTexture == nullptr || ctx.data == nullptr)
        {
            return;
        }
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(gD3DContext->Map(gD3DTexture, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped)))
        {
            return;
        }
        const uint8_t* src = ctx.data;
        uint8_t* dst = reinterpret_cast<uint8_t*>(mapped.pData);
        for (uint32_t y = 0; y < ctx.height; ++y)
        {
            std::memcpy(dst + y * mapped.RowPitch, src + y * ctx.stride, ctx.stride);
        }
        gD3DContext->Unmap(gD3DTexture, 0);
#endif
    }

    void UploadMetal(const UploadContext& ctx)
    {
#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        if (gMetalTexture == nil || ctx.data == nullptr)
        {
            return;
        }
        MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
        [gMetalTexture replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];
#endif
    }

    [[maybe_unused]] static void ConvertBGRAtoRGBA(std::vector<uint8_t>& buffer, const UploadContext& ctx)
    {
        buffer.resize(static_cast<size_t>(ctx.width) * static_cast<size_t>(ctx.height) * 4u);
        const uint8_t* src = ctx.data;
        uint8_t* dst = buffer.data();
        for (uint32_t y = 0; y < ctx.height; ++y)
        {
            for (uint32_t x = 0; x < ctx.width; ++x)
            {
                size_t offset = static_cast<size_t>(y) * ctx.width * 4u + x * 4u;
                size_t srcOffset = static_cast<size_t>(y) * ctx.stride + x * 4u;
                dst[offset + 0] = src[srcOffset + 2];
                dst[offset + 1] = src[srcOffset + 1];
                dst[offset + 2] = src[srcOffset + 0];
                dst[offset + 3] = src[srcOffset + 3];
            }
        }
    }

    void UploadOpenGL(const UploadContext& ctx)
    {
#if !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
        if (gGLTexture == 0 || ctx.data == nullptr)
        {
            return;
        }
        glBindTexture(GL_TEXTURE_2D, gGLTexture);
#    if defined(__ANDROID__)
#        if defined(GL_BGRA)
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
#        else
        static std::vector<uint8_t> converted;
        ConvertBGRAtoRGBA(converted, ctx);
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_RGBA, GL_UNSIGNED_BYTE, converted.data());
#        endif
#    else
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA, GL_UNSIGNED_BYTE, ctx.data);
#    endif
#endif
    }

    void PerformUpload()
    {
        uint64_t requested = gRequestedVersion.load(std::memory_order_acquire);
        if (requested == 0 || requested == gUploadedVersion)
        {
            return;
        }

        UploadContext ctx;
        {
            std::lock_guard<std::mutex> lock(gUploadMutex);
            ctx = gUploadContext;
        }

        if (ctx.data == nullptr)
        {
            return;
        }

        if (!EnsureTexture(static_cast<int>(ctx.width), static_cast<int>(ctx.height)))
        {
            return;
        }

        switch (gRenderer)
        {
            case Renderer::D3D11:
                UploadD3D11(ctx);
                break;
            case Renderer::Metal:
                UploadMetal(ctx);
                break;
            case Renderer::OpenGL:
                UploadOpenGL(ctx);
                break;
            case Renderer::Unknown:
            default:
                return;
        }

        gUploadedVersion = requested;
    }

    void PublishUpload(const lottie_render_data* render_data)
    {
        if (render_data == nullptr)
        {
            return;
        }
        UploadContext ctx;
        ctx.data = reinterpret_cast<const uint8_t*>(render_data->buffer);
        ctx.width = render_data->width;
        ctx.height = render_data->height;
        ctx.stride = render_data->bytesPerLine;
        {
            std::lock_guard<std::mutex> lock(gUploadMutex);
            gUploadContext = ctx;
        }
        gUploadVersion.fetch_add(1, std::memory_order_release);
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
        PublishUpload(render_data);
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

    EXPORT_API int32_t lottie_render_get_future_result(
        lottie_animation_wrapper* /*animation_wrapper*/,
        lottie_render_data* render_data)
    {
        render_data->render_future.get();
        PublishUpload(render_data);
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
        gRequestedWidth = width;
        gRequestedHeight = height;
        if (!EnsureTexture(width, height))
        {
            return nullptr;
        }
        return gNativeTexture;
    }

    EXPORT_API void lp_destroy_texture(void* /*tex*/)
    {
        ResetTextureState();
        gRequestedWidth = 0;
        gRequestedHeight = 0;
    }

    EXPORT_API void* lp_get_native_texture_ptr(void)
    {
        return gNativeTexture;
    }

    EXPORT_API int lp_bind_lottie_instance(lottie_animation_wrapper* animation_wrapper)
    {
        gBoundAnimation = animation_wrapper;
        return gBoundAnimation != nullptr ? 1 : 0;
    }

    EXPORT_API void lp_update_texture(void)
    {
        uint64_t latest = gUploadVersion.load(std::memory_order_acquire);
        if (latest == 0)
        {
            return;
        }
        gRequestedVersion.store(latest, std::memory_order_release);
    }

    static void UNITY_INTERFACE_API OnRenderEvent(int /*eventID*/)
    {
        PerformUpload();
    }

    EXPORT_API UnityRenderingEvent lp_get_render_event_func(void)
    {
        return OnRenderEvent;
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(void* /*unityInterfaces*/)
    {
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
    {
        ResetTextureState();
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
            if (gRequestedWidth > 0 && gRequestedHeight > 0)
            {
                EnsureTexture(gRequestedWidth, gRequestedHeight);
            }
        }
        else if (eventType == kUnityGfxDeviceEventShutdown)
        {
            ResetTextureState();
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
        }
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityRenderEvent(int eventID)
    {
        OnRenderEvent(eventID);
    }
#endif // !__EMSCRIPTEN__
}

#include "LottiePlugin.h"
#include "vdebug.h"

#include <algorithm>
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

// --- Platform GPU headers FIRST ------------------------------------------------
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3d11.lib")
#endif
// -----------------------------------------------------------------------------

#if !defined(__EMSCRIPTEN__)
#include "IUnityInterface.h"
#include "IUnityProfiler.h"
#include "IUnityGraphics.h"
#if defined(_WIN32)
#include "IUnityGraphicsD3D12.h"
#endif
#include "IUnityLog.h"


static IUnityProfiler* sProfiler = nullptr;
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
#else
struct IUnityInterfaces;
struct UnityProfilerMarkerDesc;

static inline void ProfBegin(const UnityProfilerMarkerDesc*) {}
static inline void ProfEnd(const UnityProfilerMarkerDesc*) {}

static const UnityProfilerMarkerDesc* sMkGetResult = nullptr;
static const UnityProfilerMarkerDesc* sMkPublish = nullptr;
static const UnityProfilerMarkerDesc* sMkUpload = nullptr;
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
  // Forward declarations so Clang/GCC know these names before use
  struct InstanceState; // forward declare the struct used in maps

  void PerformUploadFor(lottie_animation_wrapper* animation);
  void PublishUpload(lottie_animation_wrapper* animation,
                      const lottie_render_data* render_data);
#endif
#if defined(__ANDROID__)
    static bool gHasBGRAExt = false;

    static void DetectGLExtensions()
    {
        gHasBGRAExt = false;
        const char* ext = reinterpret_cast<const char*>(glGetString(GL_EXTENSIONS));
        if (ext != nullptr && std::strstr(ext, "GL_EXT_texture_format_BGRA8888") != nullptr)
        {
            gHasBGRAExt = true;
        }
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
#elif defined(__APPLE__) && !defined(__EMSCRIPTEN__)
        id<MTLTexture> metalTex = nil;
#elif !defined(__EMSCRIPTEN__) && !defined(_WIN32) && !defined(__APPLE__)
        GLuint glTex = 0;
#endif
#if defined(__ANDROID__)
        std::vector<uint8_t> rgbaScratch;
#endif
    };

#endif // !defined(__EMSCRIPTEN__)

    lottie_animation_wrapper* gBoundAnimation = nullptr;

    enum class Renderer
    {
        Unknown,
        D3D12,
        D3D11,
        Metal,
        OpenGL,
    };

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

    Renderer ToRenderer(int deviceType)
    {
        switch (deviceType)
        {
            case kUnityGfxRendererD3D11:
                return Renderer::D3D11;
            case kUnityGfxRendererD3D12:
                return Renderer::D3D12;
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
            case Renderer::D3D12:
#if defined(_WIN32)
            {
                if (!gD3D12Device)
                {
                    return false;
                }

                D3D12_RESOURCE_DESC texDesc{};
                texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
                texDesc.Width = static_cast<UINT64>(width);
                texDesc.Height = static_cast<UINT>(height);
                texDesc.DepthOrArraySize = 1;
                texDesc.MipLevels = 1;
                texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
                texDesc.SampleDesc.Count = 1;
                texDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;

                D3D12_HEAP_PROPERTIES heapDefault{ D3D12_HEAP_TYPE_DEFAULT };
                ID3D12Resource* texture = nullptr;
                HRESULT hr = gD3D12Device->CreateCommittedResource(
                    &heapDefault, D3D12_HEAP_FLAG_NONE, &texDesc,
                    D3D12_RESOURCE_STATE_COMMON, nullptr,
                    IID_PPV_ARGS(&texture));
                if (FAILED(hr) || !texture)
                {
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
                D3D12_RESOURCE_DESC uploadDesc{};
                uploadDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
                uploadDesc.Width = uploadBytes;
                uploadDesc.Height = 1;
                uploadDesc.DepthOrArraySize = 1;
                uploadDesc.MipLevels = 1;
                uploadDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

                ID3D12Resource* upload = nullptr;
                hr = gD3D12Device->CreateCommittedResource(
                    &heapUpload, D3D12_HEAP_FLAG_NONE, &uploadDesc,
                    D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                    IID_PPV_ARGS(&upload));
                if (FAILED(hr) || !upload)
                {
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
                const bool useBGRA = gHasBGRAExt;
                const GLint internalFormat = useBGRA ? GL_RGBA8 : GL_RGBA;
                const GLenum uploadFormat = useBGRA ? GL_BGRA_EXT : GL_RGBA;
                glTexImage2D(
                    GL_TEXTURE_2D, 0, internalFormat, width, height, 0, uploadFormat, GL_UNSIGNED_BYTE, nullptr);
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
        if (gHasBGRAExt)
        {
            glTexSubImage2D(
                GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_BGRA_EXT, GL_UNSIGNED_BYTE, ctx.data);
        }
        else
        {
            ConvertBGRAtoRGBA(state->rgbaScratch, ctx);
            glTexSubImage2D(
                GL_TEXTURE_2D, 0, 0, 0, ctx.width, ctx.height, GL_RGBA, GL_UNSIGNED_BYTE, state->rgbaScratch.data());
        }
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
            case Renderer::D3D12:
#if defined(_WIN32)
                UploadD3D12(state, ctx);
#endif
                break;
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
    EXPORT_API void* lottie_create_texture(int width, int height)
    {
        InstanceState* state = GetState(gBoundAnimation);
        if (!EnsureTexture(state, width, height))
        {
            return nullptr;
        }
        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API void lottie_destroy_texture(void* /*tex*/)
    {
        InstanceState* state = GetState(gBoundAnimation, /*create=*/false);
        ResetTextureState(state);
    }

    EXPORT_API void* lottie_get_native_texture_ptr(void)
    {
        InstanceState* state = GetState(gBoundAnimation, /*create=*/false);
        return state != nullptr ? state->nativeTex : nullptr;
    }

    EXPORT_API int lottie_bind_lottie_instance(lottie_animation_wrapper* animation_wrapper)
    {
        gBoundAnimation = animation_wrapper;
        if (gBoundAnimation == nullptr)
        {
            return 0;
        }
        return GetState(gBoundAnimation) != nullptr ? 1 : 0;
    }

    EXPORT_API void lottie_update_texture(void)
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
#endif
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
            ResetTextureState(entry.second.get());
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
#endif
    }

    extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnitySetGraphicsDevice(void* device, int deviceType, int eventType)
    {
        if (eventType == ::kUnityGfxDeviceEventInitialize)
        {
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
                default:
#if defined(__ANDROID__)
                    DetectGLExtensions();
#endif
                    break;
            }
        }
        else if (eventType == ::kUnityGfxDeviceEventShutdown)
        {
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
                default:
                    break;
            }
            gRenderer = Renderer::Unknown;
            gBoundAnimation = nullptr;
#if defined(__ANDROID__)
            gHasBGRAExt = false;
#endif
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

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)

#include <TargetConditionals.h>
#import <Foundation/Foundation.h>
#import <Metal/Metal.h>

#include "MetalBackend_apple.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"

#include "IUnityInterface.h"
#include "IUnityGraphicsMetal.h"

namespace
{
    id<MTLDevice> gMetalDevice = nil;
    IUnityGraphicsMetalV2* sMetalV2 = nullptr;
    IUnityGraphicsMetalV1* sMetalV1 = nullptr;
}

void SetMetalDevice(void* device)
{
    gMetalDevice = (__bridge id<MTLDevice>)device;
}

void* GetMetalDevice()
{
    return (__bridge void*)gMetalDevice;
}

void InitializeMetalFromUnity(IUnityInterfaces* unityInterfaces)
{
    if (unityInterfaces == nullptr)
    {
        LottieLogError(nullptr, "[Lottie] InitializeMetalFromUnity: unityInterfaces is null");
        return;
    }

    sMetalV2 = unityInterfaces->Get<IUnityGraphicsMetalV2>();
    sMetalV1 = nullptr;

    LottieLogInfo(nullptr, "[Lottie] IUnityGraphicsMetalV2: %s", sMetalV2 != nullptr ? "available" : "NOT available");

    if (sMetalV2 == nullptr)
    {
        sMetalV1 = unityInterfaces->Get<IUnityGraphicsMetalV1>();
        LottieLogInfo(nullptr, "[Lottie] IUnityGraphicsMetalV1: %s", sMetalV1 != nullptr ? "available" : "NOT available");
    }

    // Try to get Metal device immediately
    if (sMetalV2 != nullptr)
    {
        gMetalDevice = sMetalV2->MetalDevice();
        LottieLogInfo(nullptr, "[Lottie] Got Metal device from IUnityGraphicsMetalV2");
    }
    else if (sMetalV1 != nullptr)
    {
        gMetalDevice = sMetalV1->MetalDevice();
        LottieLogInfo(nullptr, "[Lottie] Got Metal device from IUnityGraphicsMetalV1");
    }

    if (gMetalDevice == nil)
    {
        LottieLogError(nullptr, "[Lottie] Failed to acquire Metal device during initialization");
    }
}

void ShutdownMetal()
{
    gMetalDevice = nil;
    sMetalV2 = nullptr;
    sMetalV1 = nullptr;
}

void* TryAcquireMetalDevice()
{
    if (gMetalDevice != nil)
    {
        return (__bridge void*)gMetalDevice;
    }

    if (sMetalV2 != nullptr)
    {
        gMetalDevice = sMetalV2->MetalDevice();
        LottieLogInfo(nullptr, "[Lottie] Got Metal device from IUnityGraphicsMetalV2 (lazy)");
    }
    else if (sMetalV1 != nullptr)
    {
        gMetalDevice = sMetalV1->MetalDevice();
        LottieLogInfo(nullptr, "[Lottie] Got Metal device from IUnityGraphicsMetalV1 (lazy)");
    }

    return (__bridge void*)gMetalDevice;
}

void ResetTextureMetal(lottie_animation_wrapper* animation, InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    if (state->metal.metalTex != nullptr)
    {
        // Release the retained texture using CFBridgingRelease
        // This transfers ownership back to ARC which will then release it
        CFBridgingRelease(state->metal.metalTex);
        state->metal.metalTex = nullptr;
    }
}

bool EnsureTextureMetal(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    // Try to acquire Metal device if not yet available
    if (gMetalDevice == nil)
    {
        LottieLogInfo(animation, "[Lottie] Metal device is nil, attempting lazy acquisition");
        TryAcquireMetalDevice();
    }

    if (gMetalDevice == nil)
    {
        LottieLogError(animation, "[Lottie] Metal device is nil, cannot create texture");
        return false;
    }

    MTLTextureDescriptor* descriptor =
        [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
                                                           width:width
                                                          height:height
                                                       mipmapped:NO];
    descriptor.usage = MTLTextureUsageShaderRead | MTLTextureUsageRenderTarget;
    // Use shared storage mode for CPU-writable textures on iOS
    // This is the default on macOS but explicit is better
#if TARGET_OS_IOS || TARGET_OS_TV
    descriptor.storageMode = MTLStorageModeShared;
#endif

    id<MTLTexture> texture = [gMetalDevice newTextureWithDescriptor:descriptor];
    if (texture == nil)
    {
        LottieLogError(animation, "[Lottie] Failed to create Metal texture");
        return false;
    }

    // Use __bridge_retained to transfer ownership to the void* storage
    // The texture is now retained and must be released with CFBridgingRelease
    state->metal.metalTex = (__bridge_retained void*)texture;
    state->nativeTex = state->metal.metalTex;
    state->texW = width;
    state->texH = height;
    LottieLogInfo(animation, "[Lottie] Metal texture created successfully (size=%dx%d)", width, height);
    return true;
}

void UploadMetal(InstanceState* state, const UploadContext& ctx)
{
    if (state == nullptr || state->metal.metalTex == nil || ctx.data == nullptr)
    {
        LottieLogWarning(nullptr, "[Lottie] UploadMetal: Invalid parameters (state=%p, metalTex=%p, data=%p)",
                       state, state ? state->metal.metalTex : nullptr, ctx.data);
        return;
    }

    LottieLogInfo(nullptr, "[Lottie] UploadMetal: Uploading to texture, size=%ux%u, stride=%u",
                 ctx.width, ctx.height, ctx.stride);

    id<MTLTexture> metalTex = (__bridge id<MTLTexture>)state->metal.metalTex;
    MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
    [metalTex replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];

    LottieLogInfo(nullptr, "[Lottie] UploadMetal: Upload completed successfully");
}

#endif // defined(__APPLE__) && !defined(__EMSCRIPTEN__)

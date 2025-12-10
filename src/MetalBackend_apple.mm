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

// Get Unity's current command buffer for synchronized GPU operations
static id<MTLCommandBuffer> GetUnityCommandBuffer()
{
    if (sMetalV2 != nullptr)
    {
        return sMetalV2->CurrentCommandBuffer();
    }
    else if (sMetalV1 != nullptr)
    {
        return sMetalV1->CurrentCommandBuffer();
    }
    return nil;
}

// End Unity's current command encoder to ensure safe texture access
// This must be called before creating our own encoder
static void EndUnityCommandEncoder()
{
    if (sMetalV2 != nullptr)
    {
        sMetalV2->EndCurrentCommandEncoder();
    }
    else if (sMetalV1 != nullptr)
    {
        sMetalV1->EndCurrentCommandEncoder();
    }
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

    // NOTE: Do NOT try to get the Metal device here during UnityPluginLoad.
    // Unity's internal Metal device (metal::g_Device) is not yet initialized at this point,
    // and calling MetalDevice() will trigger an assertion failure.
    // The device will be acquired lazily via TryAcquireMetalDevice() when
    // OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize) is fired.
    LottieLogInfo(nullptr, "[Lottie] Metal interfaces cached, device will be acquired on graphics init");
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
    
    // End Unity's current command encoder before we create our blit encoder
    EndUnityCommandEncoder();
    
    // Get Unity's current command buffer for synchronized GPU operations
    id<MTLCommandBuffer> commandBuffer = GetUnityCommandBuffer();
    
    if (commandBuffer != nil)
    {
        // Use a blit command encoder to copy data from a staging buffer to the texture.
        // This ensures the copy is synchronized with Unity's rendering pipeline.
        
        // Calculate buffer size
        NSUInteger bufferSize = ctx.stride * ctx.height;
        
        // Create a temporary staging buffer with shared storage
        id<MTLBuffer> stagingBuffer = [gMetalDevice newBufferWithBytes:ctx.data
                                                                length:bufferSize
                                                               options:MTLResourceStorageModeShared];
        
        if (stagingBuffer != nil)
        {
            // Create blit encoder and copy from buffer to texture
            id<MTLBlitCommandEncoder> blitEncoder = [commandBuffer blitCommandEncoder];
            if (blitEncoder != nil)
            {
                [blitEncoder copyFromBuffer:stagingBuffer
                               sourceOffset:0
                          sourceBytesPerRow:ctx.stride
                        sourceBytesPerImage:bufferSize
                                 sourceSize:MTLSizeMake(ctx.width, ctx.height, 1)
                                  toTexture:metalTex
                           destinationSlice:0
                           destinationLevel:0
                          destinationOrigin:MTLOriginMake(0, 0, 0)];
                [blitEncoder endEncoding];
                
                LottieLogInfo(nullptr, "[Lottie] UploadMetal: Blit copy queued successfully");
            }
            else
            {
                LottieLogWarning(nullptr, "[Lottie] UploadMetal: Failed to create blit encoder, falling back to replaceRegion");
                MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
                [metalTex replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];
            }
            // stagingBuffer will be released by ARC after the command buffer completes
        }
        else
        {
            LottieLogWarning(nullptr, "[Lottie] UploadMetal: Failed to create staging buffer, falling back to replaceRegion");
            MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
            [metalTex replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];
        }
    }
    else
    {
        // Fallback: no command buffer available, use direct replaceRegion
        LottieLogWarning(nullptr, "[Lottie] UploadMetal: No command buffer available, using direct replaceRegion");
        MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
        [metalTex replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];
    }

    LottieLogInfo(nullptr, "[Lottie] UploadMetal: Upload completed successfully");
}

#endif // defined(__APPLE__) && !defined(__EMSCRIPTEN__)

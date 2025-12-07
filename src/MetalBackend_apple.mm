#include "MetalBackend_apple.h"

#if defined(__APPLE__) && !defined(__EMSCRIPTEN__)

#import <Metal/Metal.h>
#import <TargetConditionals.h>

#include "LottieLogger.h"
#include "RendererCommon.h"

static id<MTLDevice> gMetalDevice = nil;

void SetMetalDevice(id<MTLDevice> device)
{
    gMetalDevice = device;
}

void ClearMetalDevice()
{
    gMetalDevice = nil;
}

bool EnsureTextureMetal(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
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
#if TARGET_OS_IOS || TARGET_OS_TV
    descriptor.storageMode = MTLStorageModeShared;
#endif

    id<MTLTexture> texture = [gMetalDevice newTextureWithDescriptor:descriptor];
    if (texture == nil)
    {
        LottieLogError(animation, "[Lottie] Failed to create Metal texture");
        return false;
    }

    state->metal.metalTex = texture;
    state->nativeTex = (__bridge void*)texture;
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
                       state, state ? (__bridge void*)state->metal.metalTex : nullptr, ctx.data);
        return;
    }

    LottieLogInfo(nullptr, "[Lottie] UploadMetal: Uploading to texture, size=%ux%u, stride=%u",
                 ctx.width, ctx.height, ctx.stride);

    MTLRegion region = MTLRegionMake2D(0, 0, ctx.width, ctx.height);
    [state->metal.metalTex replaceRegion:region mipmapLevel:0 withBytes:ctx.data bytesPerRow:ctx.stride];

    LottieLogInfo(nullptr, "[Lottie] UploadMetal: Upload completed successfully");
}

void ResetTextureMetal(InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    state->metal.metalTex = nil;
}

#endif

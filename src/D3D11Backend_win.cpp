#if defined(_WIN32)

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#pragma comment(lib, "d3d11.lib")

#include "D3D11Backend_win.h"
#include "InstanceRegistry.h"
#include "LottieLogger.h"
#include <cstring>

namespace
{
    ID3D11Device* gD3DDevice = nullptr;
    ID3D11DeviceContext* gD3DContext = nullptr;
}

void SetD3D11Device(ID3D11Device* device)
{
    gD3DDevice = device;
}

ID3D11Device* GetD3D11Device()
{
    return gD3DDevice;
}

void SetD3D11Context(ID3D11DeviceContext* context)
{
    gD3DContext = context;
}

ID3D11DeviceContext* GetD3D11Context()
{
    return gD3DContext;
}

void ReleaseD3D11Context()
{
    if (gD3DContext)
    {
        gD3DContext->Release();
        gD3DContext = nullptr;
    }
    gD3DDevice = nullptr;
}

void ResetTextureD3D11(lottie_animation_wrapper* animation, InstanceState* state)
{
    if (state == nullptr)
    {
        return;
    }

    if (state->d3d11.tex)
    {
        state->d3d11.tex->Release();
        state->d3d11.tex = nullptr;
    }
}

bool EnsureTextureD3D11(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
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

    state->d3d11.tex = texture;
    state->nativeTex = texture;
    state->texW = width;
    state->texH = height;
    LottieLogInfo(animation, "[Lottie] D3D11 texture created successfully");
    return true;
}

void UploadD3D11(InstanceState* state, const UploadContext& ctx)
{
    if (state == nullptr || gD3DContext == nullptr || state->d3d11.tex == nullptr || ctx.data == nullptr)
    {
        return;
    }

    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(gD3DContext->Map(state->d3d11.tex, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped)))
    {
        return;
    }

    const uint8_t* src = ctx.data;
    uint8_t* dst = reinterpret_cast<uint8_t*>(mapped.pData);
    for (uint32_t y = 0; y < ctx.height; ++y)
    {
        std::memcpy(dst + y * mapped.RowPitch, src + y * ctx.stride, ctx.stride);
    }

    gD3DContext->Unmap(state->d3d11.tex, 0);
}

#endif // defined(_WIN32)

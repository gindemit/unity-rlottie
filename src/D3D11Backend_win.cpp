#include "D3D11Backend_win.h"

#if defined(_WIN32)

#include "LottieLogger.h"
#include "RendererCommon.h"

#include <cstring>

#pragma comment(lib, "d3d11.lib")

static ID3D11Device* gD3DDevice = nullptr;
static ID3D11DeviceContext* gD3DContext = nullptr;

void SetD3D11Interfaces(ID3D11Device* device, ID3D11DeviceContext* context)
{
    gD3DDevice = device;
    gD3DContext = context;
    if (gD3DDevice != nullptr && gD3DContext == nullptr)
    {
        gD3DDevice->GetImmediateContext(&gD3DContext);
    }
}

void ClearD3D11Interfaces()
{
    if (gD3DContext)
    {
        gD3DContext->Release();
        gD3DContext = nullptr;
    }
    gD3DDevice = nullptr;
}

bool EnsureTextureD3D11(lottie_animation_wrapper* animation, InstanceState* state, int width, int height)
{
    if (!gD3DDevice)
    {
        LottieLogError(animation, "[Lottie] D3D11 device is null");
        return false;
    }

    D3D11_TEXTURE2D_DESC texDesc{};
    texDesc.Width = static_cast<UINT>(width);
    texDesc.Height = static_cast<UINT>(height);
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.SampleDesc.Quality = 0;
    texDesc.Usage = D3D11_USAGE_DYNAMIC;
    texDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    texDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    texDesc.MiscFlags = 0;

    ID3D11Texture2D* texture = nullptr;
    HRESULT hr = gD3DDevice->CreateTexture2D(&texDesc, nullptr, &texture);
    if (FAILED(hr) || !texture)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to create D3D11 texture. HRESULT: 0x%08X", hr);
        LottieLogError(animation, errorMsg);
        if (texture)
        {
            texture->Release();
        }
        return false;
    }

    state->d3d11.tex = texture;
    state->nativeTex = texture;
    state->texW = width;
    state->texH = height;
    return true;
}

void UploadD3D11(InstanceState* state, const UploadContext& ctx)
{
    if (gD3DDevice == nullptr || gD3DContext == nullptr || state == nullptr || state->d3d11.tex == nullptr)
    {
        return;
    }

    D3D11_MAPPED_SUBRESOURCE mapped{};
    HRESULT hr = gD3DContext->Map(state->d3d11.tex, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
    if (FAILED(hr) || mapped.pData == nullptr)
    {
        char errorMsg[256];
        snprintf(errorMsg, sizeof(errorMsg), "[Lottie] Failed to map D3D11 texture. HRESULT: 0x%08X", hr);
        LottieLogError(nullptr, errorMsg);
        return;
    }

    const uint8_t* src = ctx.data;
    uint8_t* dst = reinterpret_cast<uint8_t*>(mapped.pData);
    const UINT dstPitch = mapped.RowPitch;
    for (uint32_t row = 0; row < ctx.height; ++row)
    {
        std::memcpy(dst + row * dstPitch, src + row * ctx.stride, ctx.width * 4u);
    }

    gD3DContext->Unmap(state->d3d11.tex, 0);
}

void ResetTextureD3D11(InstanceState* state)
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

#endif

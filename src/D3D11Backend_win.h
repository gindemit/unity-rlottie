#ifndef D3D11_BACKEND_WIN_H
#define D3D11_BACKEND_WIN_H

#if defined(_WIN32)

#include "LottiePlugin.h"
#include "RendererCommon.h"

// Forward declarations
struct InstanceState;
struct ID3D11Device;
struct ID3D11DeviceContext;

// D3D11 device accessors
void SetD3D11Device(ID3D11Device* device);
ID3D11Device* GetD3D11Device();
void SetD3D11Context(ID3D11DeviceContext* context);
ID3D11DeviceContext* GetD3D11Context();
void ReleaseD3D11Context();

// D3D11 texture operations
bool EnsureTextureD3D11(lottie_animation_wrapper* animation, InstanceState* state, int width, int height);
UploadResult UploadD3D11(InstanceState* state, const UploadContext& ctx);
void ResetTextureD3D11(lottie_animation_wrapper* animation, InstanceState* state);

#endif // defined(_WIN32)

#endif // D3D11_BACKEND_WIN_H

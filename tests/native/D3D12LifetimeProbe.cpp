#include "D3D12Texture_win.h"

// Built into a test DLL so the host can unload its last reference while the
// production retirement callback is still waiting on a real D3D12 fence.
extern "C" __declspec(dllexport) BOOL RetireTestTexture(
    ID3D12Resource* buffer, ID3D12Fence* fence, UINT64 lastUse)
{
    auto texture = D3D12Texture::Create();
    if (!texture)
        return FALSE;

    texture->tex = buffer;
    texture->upload = buffer;
    texture->frameFence = fence;
    texture->uploadSlotFenceValues = { 0, lastUse, lastUse ? lastUse - 1 : 0 };
    D3D12_RANGE noCpuReads{};
    if (FAILED(buffer->Map(0, &noCpuReads, &texture->uploadMapped)))
        return FALSE;

    D3D12Texture::Retire(texture.release());
    return TRUE;
}

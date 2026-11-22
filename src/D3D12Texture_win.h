#pragma once

#include <windows.h>
#include <d3d12.h>
#include <wrl/client.h>
#include <array>
#include <memory>

// One owner follows the texture from creation through its last GPU upload.
struct D3D12Texture
{
    static constexpr unsigned int kUploadSlotCount = 3;
    Microsoft::WRL::ComPtr<ID3D12Resource> tex;
    Microsoft::WRL::ComPtr<ID3D12Resource> upload;
    Microsoft::WRL::ComPtr<ID3D12Fence> frameFence;
    void* uploadMapped = nullptr;
    UINT64 uploadSlotBytes = 0;
    std::array<UINT64, kUploadSlotCount> uploadSlotFenceValues{};
    unsigned int nextUploadSlot = 0;
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint{};

    static std::unique_ptr<D3D12Texture> Create();
    static void Retire(D3D12Texture* texture);
    UINT64 LastUploadFenceValue() const;
    ~D3D12Texture();

private:
    D3D12Texture() = default;
    D3D12Texture(const D3D12Texture&) = delete;
    D3D12Texture& operator=(const D3D12Texture&) = delete;

    HMODULE module = nullptr;
    HANDLE completion = nullptr;
    PTP_WAIT wait = nullptr;
    UINT64 lastUse = 0;
    static void CALLBACK CheckFence(PTP_CALLBACK_INSTANCE callback, void* context, PTP_WAIT wait, TP_WAIT_RESULT);
    void PollFenceLater() const;
};

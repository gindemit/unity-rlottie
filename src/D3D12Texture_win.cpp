#include "D3D12Texture_win.h"
#include <algorithm>
#include <new>

std::unique_ptr<D3D12Texture> D3D12Texture::Create()
{
    std::unique_ptr<D3D12Texture> texture(new (std::nothrow) D3D12Texture());
    if (!texture ||
        !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(&CheckFence), &texture->module))
    {
        return nullptr;
    }
    // Reserve the callback before publishing any GPU resources. Reset can then
    // transfer ownership even under memory pressure or during Unity shutdown.
    texture->completion = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    texture->wait = CreateThreadpoolWait(CheckFence, texture.get(), nullptr);
    if (!texture->completion || !texture->wait)
        return nullptr;
    return texture;
}

D3D12Texture::~D3D12Texture()
{
    if (uploadMapped)
        upload->Unmap(0, nullptr);
    if (wait)
        CloseThreadpoolWait(wait);
    if (completion)
        CloseHandle(completion);
    if (module)
        FreeLibrary(module);
}

UINT64 D3D12Texture::LastUploadFenceValue() const
{
    return *std::max_element(uploadSlotFenceValues.begin(), uploadSlotFenceValues.end());
}

void D3D12Texture::Retire(D3D12Texture* texture)
{
    if (!texture)
        return;

    texture->lastUse = texture->LastUploadFenceValue();
    if (!texture->frameFence || texture->frameFence->GetCompletedValue() >= texture->lastUse)
    {
        delete texture;
        return;
    }
    if (SUCCEEDED(texture->frameFence->SetEventOnCompletion(texture->lastUse, texture->completion)))
        SetThreadpoolWait(texture->wait, texture->completion, nullptr);
    else
        texture->PollFenceLater();
}

void D3D12Texture::PollFenceLater() const
{
    // Fence-event registration can fail under kernel memory pressure. Keep
    // checking the same fence without blocking the Unity thread or allocating.
    LARGE_INTEGER delay;
    delay.QuadPart = -100 * 10000LL; // Relative 100 ms in 100 ns units.
    FILETIME dueTime{delay.LowPart, static_cast<DWORD>(delay.HighPart)};
    SetThreadpoolWait(wait, completion, &dueTime);
}

void CALLBACK D3D12Texture::CheckFence(PTP_CALLBACK_INSTANCE callback, void* context, PTP_WAIT wait, TP_WAIT_RESULT)
{
    auto* texture = static_cast<D3D12Texture*>(context);
    if (texture->frameFence->GetCompletedValue() < texture->lastUse)
    {
        texture->PollFenceLater();
        return;
    }

    // A preceding callback could still be returning from rearming this wait.
    // Finish both callbacks before releasing the final DLL reference.
    DisassociateCurrentThreadFromCallback(callback);
    WaitForThreadpoolWaitCallbacks(wait, FALSE);
    FreeLibraryWhenCallbackReturns(callback, texture->module);
    texture->module = nullptr;
    delete texture;
}

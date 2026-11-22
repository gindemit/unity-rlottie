#include <windows.h>
#include <d3d12.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <wrl/implements.h>
#include <algorithm>
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <string>

using Microsoft::WRL::ComPtr;

namespace
{
void Require(bool passed, const char* message)
{
    if (!passed)
    {
        std::fprintf(stderr, "FAIL: %s\n", message);
        std::exit(EXIT_FAILURE);
    }
}

ULONG ReferenceCount(IUnknown* object)
{
    object->AddRef();
    return object->Release();
}

ComPtr<ID3D12Device> FindHardwareDevice()
{
    ComPtr<IDXGIFactory1> factory;
    Require(SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))), "enumerate graphics hardware");
    for (UINT index = 0; ; ++index)
    {
        ComPtr<IDXGIAdapter1> adapter;
        const HRESULT result = factory->EnumAdapters1(index, &adapter);
        if (result == DXGI_ERROR_NOT_FOUND)
            return nullptr;
        Require(SUCCEEDED(result), "read graphics adapter");
        DXGI_ADAPTER_DESC1 desc{};
        Require(SUCCEEDED(adapter->GetDesc1(&desc)), "read adapter capabilities");
        if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) ||
            FAILED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, __uuidof(ID3D12Device), nullptr)))
            continue;
        ComPtr<ID3D12Device> device;
        Require(SUCCEEDED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device))),
            "create D3D12 hardware device");
        return device;
    }
}

// Only event registration fails. All completion values come from the GPU.
class UnregisteredFence : public Microsoft::WRL::RuntimeClass<
    Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, ID3D12Fence>
{
public:
    explicit UnregisteredFence(ID3D12Fence* fence) : realFence(fence) {}
    std::atomic<unsigned int> reads{0};
    UINT64 STDMETHODCALLTYPE GetCompletedValue() override
    {
        const UINT64 value = realFence->GetCompletedValue();
        ++reads;
        return value;
    }
    HRESULT STDMETHODCALLTYPE SetEventOnCompletion(UINT64, HANDLE) override { return E_OUTOFMEMORY; }
    HRESULT STDMETHODCALLTYPE Signal(UINT64 value) override { return realFence->Signal(value); }
    HRESULT STDMETHODCALLTYPE GetDevice(REFIID iid, void** out) override { return realFence->GetDevice(iid, out); }
    HRESULT STDMETHODCALLTYPE GetPrivateData(REFGUID key, UINT* size, void* data) override
    { return realFence->GetPrivateData(key, size, data); }
    HRESULT STDMETHODCALLTYPE SetPrivateData(REFGUID key, UINT size, const void* data) override
    { return realFence->SetPrivateData(key, size, data); }
    HRESULT STDMETHODCALLTYPE SetPrivateDataInterface(REFGUID key, const IUnknown* data) override
    { return realFence->SetPrivateDataInterface(key, data); }
    HRESULT STDMETHODCALLTYPE SetName(LPCWSTR name) override { return realFence->SetName(name); }
private:
    ComPtr<ID3D12Fence> realFence;
};

void SubmitRetirements(const wchar_t* path, ID3D12Resource* buffer, ID3D12Fence* fence,
                       UINT64 lastUse, unsigned int count)
{
    HMODULE module = LoadLibraryW(path);
    Require(module != nullptr, "load lifetime probe");
    using Retire = BOOL (*)(ID3D12Resource*, ID3D12Fence*, UINT64);
    auto retire = reinterpret_cast<Retire>(GetProcAddress(module, "RetireTestTexture"));
    if (!retire)
        retire = reinterpret_cast<Retire>(GetProcAddress(module, "_RetireTestTexture"));
    Require(retire != nullptr, "locate lifetime probe");
    for (unsigned int n = 0; n < count; ++n)
        Require(retire(buffer, fence, lastUse) != FALSE, "retire mapped resources");
    Require(FreeLibrary(module) != FALSE, "drop host module reference");
}

void WaitForUnload(const wchar_t* path)
{
    const ULONGLONG deadline = GetTickCount64() + 10000;
    while (GetModuleHandleW(path) && GetTickCount64() < deadline)
        Sleep(10);
    Require(!GetModuleHandleW(path), "release callback and DLL after fence completion");
}
}

int wmain(int argc, wchar_t** argv)
{
    Require(argc == 2, "expected lifetime probe DLL path");
    std::wstring probePath(argv[1]);
    std::replace(probePath.begin(), probePath.end(), L'/', L'\\');
    const wchar_t* path = probePath.c_str();
    auto device = FindHardwareDevice();
    if (!device)
    {
        std::puts("SKIP: no D3D12 hardware device available");
        return 77;
    }
    ComPtr<ID3D12Fence> fence;
    Require(SUCCEEDED(device->CreateFence(1, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence))), "create frame fence");
    ComPtr<ID3D12CommandQueue> queue;
    D3D12_COMMAND_QUEUE_DESC queueDesc{};
    Require(SUCCEEDED(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue))), "create GPU queue");
    D3D12_HEAP_PROPERTIES heap{};
    heap.Type = D3D12_HEAP_TYPE_UPLOAD;
    heap.CreationNodeMask = heap.VisibleNodeMask = 1;
    D3D12_RESOURCE_DESC desc{};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = 4096;
    desc.Height = desc.DepthOrArraySize = desc.MipLevels = desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    ComPtr<ID3D12Resource> buffer;
    Require(SUCCEEDED(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
        D3D12_RESOURCE_STATE_GENERIC_READ, nullptr, IID_PPV_ARGS(&buffer))), "create mapped buffer");
    const ULONG bufferRefs = ReferenceCount(buffer.Get());
    SubmitRetirements(path, buffer.Get(), fence.Get(), 1, 1);
    Require(!GetModuleHandleW(path), "completed fence retires immediately");
    Require(ReferenceCount(buffer.Get()) == bufferRefs, "release completed resource references");

    constexpr unsigned int pending = 12;
    SubmitRetirements(path, buffer.Get(), fence.Get(), 3, pending);
    Require(GetModuleHandleW(path) != nullptr, "pending callbacks keep the DLL loaded");
    Require(SUCCEEDED(fence->Signal(2)), "advance to the earlier upload slot");
    Require(ReferenceCount(buffer.Get()) == bufferRefs + 2 * pending, "retain both resources for every pending texture");
    Require(SUCCEEDED(queue->Signal(fence.Get(), 3)), "signal the final upload slot from the GPU queue");
    WaitForUnload(path);
    Require(ReferenceCount(buffer.Get()) == bufferRefs, "release every pending resource after the final slot");

    auto unregistered = Microsoft::WRL::Make<UnregisteredFence>(fence.Get());
    Require(unregistered != nullptr, "allocate fence adapter");
    SubmitRetirements(path, buffer.Get(), unregistered.Get(), 4, 1);
    const ULONGLONG deadline = GetTickCount64() + 5000;
    while (unregistered->reads < 2 && GetTickCount64() < deadline)
        Sleep(10);
    Require(unregistered->reads >= 2 && ReferenceCount(buffer.Get()) == bufferRefs + 2,
        "failed event registration polls while preserving both references");
    Require(SUCCEEDED(queue->Signal(fence.Get(), 4)), "complete unregistered fence");
    WaitForUnload(path);
    Require(ReferenceCount(buffer.Get()) == bufferRefs, "release after event registration failure");

    ComPtr<ID3D12Device5> removalDevice;
    if (SUCCEEDED(device.As(&removalDevice)))
    {
        SubmitRetirements(path, buffer.Get(), fence.Get(), 5, 1);
        // This short-lived process owns the device. RemoveDevice exercises
        // D3D12's removal notification without resetting the adapter.
        removalDevice->RemoveDevice();
        WaitForUnload(path);
        Require(ReferenceCount(buffer.Get()) == bufferRefs, "release resources on device removal");
        std::puts("Passed retirement on device removal");
    }
    std::puts("Passed immediate release, delayed GPU completion, failed event registration and DLL unload");
    return EXIT_SUCCESS;
}

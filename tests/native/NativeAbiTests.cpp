#include "ImageBufferUtils.h"
#include "LottiePlugin.h"

#include <cstdint>
#include <cstdio>
#include <limits>

namespace
{
    int failures = 0;

    void Expect(bool condition, const char* message)
    {
        if (!condition)
        {
            std::fprintf(stderr, "FAILED: %s\n", message);
            ++failures;
        }
    }

    void TestCheckedImageSizing()
    {
        ImageBufferSize size{};
        Expect(TryGetImageBufferSize(3, 2, 16, size), "valid padded image layout");
        Expect(size.rowBytes == 12 && size.totalBytes == 32, "calculated image sizes");
        Expect(!TryGetImageBufferSize(0, 2, 16, size), "zero width rejected");
        Expect(!TryGetImageBufferSize(3, 0, 16, size), "zero height rejected");
        Expect(!TryGetImageBufferSize(3, 2, 11, size), "short stride rejected");
        Expect(!TryGetImageBufferSize(std::numeric_limits<uint32_t>::max(), 1,
                                      std::numeric_limits<uint32_t>::max(), size),
               "row-byte overflow rejected");
    }

    void TestBoundedRowCopy()
    {
        const uint8_t source[16] = {
            1, 2, 3, 4, 99, 99, 99, 99,
            5, 6, 7, 8, 88, 88, 88, 88};
        uint8_t destination[24] = {};
        Expect(CopyImageRows(destination, 12, source, 8, 1, 2), "valid row copy");
        Expect(destination[0] == 1 && destination[3] == 4 &&
               destination[12] == 5 && destination[15] == 8,
               "visible pixels copied");
        Expect(destination[4] == 0 && destination[16] == 0, "row padding untouched");
        Expect(!CopyImageRows(destination, 3, source, 8, 1, 2), "short destination row rejected");
        Expect(!CopyImageRows(destination, 12, source, 3, 1, 2), "short source row rejected");
    }

    void TestNullAbiArguments()
    {
        lottie_animation_wrapper* animation = reinterpret_cast<lottie_animation_wrapper*>(1);
        Expect(lottie_load_from_data(nullptr, nullptr, &animation) == -1 && animation == nullptr,
               "null JSON rejected and output cleared");

        static const char validJson[] =
            "{\"v\":\"5.7.4\",\"fr\":30,\"ip\":0,\"op\":1,\"w\":1,\"h\":1,\"layers\":[]}";
        Expect(lottie_load_from_data(validJson, nullptr, nullptr) == -1,
               "null animation output rejected");
        Expect(lottie_load_from_data("{", nullptr, &animation) == -1 && animation == nullptr,
               "malformed JSON with a null resource path is rejected safely");
        Expect(lottie_load_from_file(nullptr, &animation) == -1 && animation == nullptr,
               "null file path rejected and output cleared");
        Expect(lottie_allocate_render_data(nullptr) == -1, "null render-data output rejected");
        Expect(lottie_dispose_wrapper(nullptr) == 0, "null animation dispose is idempotent");
        Expect(lottie_dispose_render_data(nullptr) == 0, "null render-data dispose is idempotent");
        Expect(lottie_render_immediately(nullptr, nullptr, 0, false, false) == -1,
               "null immediate render rejected");
        Expect(lottie_render_create_future_async(nullptr, nullptr, 0, false, false) == -1,
               "null async render rejected");
        Expect(lottie_render_get_future_result(nullptr, nullptr) == -1,
               "null future result rejected");
        int32_t ready = 7;
        Expect(lottie_render_try_get_future_result(nullptr, nullptr, &ready) == -1 && ready == 7,
               "null future poll rejected without modifying output");
    }

    void TestRenderDataLifecycle()
    {
        lottie_render_data* renderData = nullptr;
        Expect(lottie_allocate_render_data(&renderData) == 0 && renderData != nullptr,
               "render data allocation succeeds");
        if (renderData != nullptr)
        {
            Expect(renderData->buffer == nullptr && renderData->width == 0 &&
                   renderData->height == 0 && renderData->bytesPerLine == 0,
                   "render data is value initialized");
        }
        Expect(lottie_dispose_render_data(&renderData) == 0 && renderData == nullptr,
               "render data dispose clears pointer");
        Expect(lottie_dispose_render_data(&renderData) == 0,
               "repeated render data dispose is idempotent");
    }

    void TestInvalidRenderLayouts()
    {
        static const char validJson[] =
            "{\"v\":\"5.7.4\",\"fr\":30,\"ip\":0,\"op\":1,\"w\":1,\"h\":1,\"layers\":[]}";
        lottie_animation_wrapper* animation = nullptr;
        Expect(lottie_load_from_data(validJson, nullptr, &animation) == 0 && animation != nullptr,
               "null resource path is treated as empty");
        if (animation == nullptr)
        {
            return;
        }

        uint32_t pixel = 0;
        lottie_render_data renderData{};
        renderData.buffer = &pixel;
        renderData.width = 1;
        renderData.height = 1;
        renderData.bytesPerLine = 3;
        Expect(lottie_render_immediately(animation, &renderData, 0, false, false) == -1,
               "short immediate-render stride rejected");
        Expect(lottie_render_create_future_async(animation, &renderData, 0, false, false) == -1,
               "short async-render stride rejected");

        renderData.bytesPerLine = 4;
        renderData.height = 0;
        Expect(lottie_render_immediately(animation, &renderData, 0, false, false) == -1,
               "zero-height render rejected");
        Expect(lottie_dispose_wrapper(&animation) == 0 && animation == nullptr,
               "loaded animation disposed");
    }
}

int main()
{
    TestCheckedImageSizing();
    TestBoundedRowCopy();
    TestNullAbiArguments();
    TestRenderDataLifecycle();
    TestInvalidRenderLayouts();
    if (failures == 0)
    {
        std::puts("Native ABI tests passed.");
    }
    return failures == 0 ? 0 : 1;
}

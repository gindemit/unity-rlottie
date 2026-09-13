#include "ImageBufferUtils.h"
#include "LottiePlugin.h"

#include <cstdint>
#include <cmath>
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
        Expect(lottie_set_fill_color(nullptr, "**.palette.base", 1, 0, 0) == -1,
               "fill override rejects a null animation");
        Expect(lottie_set_stroke_color(nullptr, "**.palette.ink", 1, 0, 0) == -1,
               "stroke override rejects a null animation");
        Expect(lottie_apply_color_overrides(nullptr, nullptr, 0) == -1,
               "batch override rejects a null animation");
    }

    uint32_t HashPixels(const uint32_t* pixels, size_t count)
    {
        uint32_t hash = 2166136261u;
        for (size_t index = 0; index < count; ++index)
        {
            hash = (hash ^ pixels[index]) * 16777619u;
        }
        return hash;
    }

    void TestSemanticColorKeypathsAndBatching()
    {
        // Semantic slots are nested Lottie names: group "palette" contains
        // paints "base" and "ink". Dots inside a single nm value are not a
        // keypath hierarchy.
        static const char paletteJson[] =
            "{\"v\":\"5.7.4\",\"fr\":30,\"ip\":0,\"op\":2,\"w\":16,\"h\":16,"
            "\"layers\":[{\"ty\":4,\"nm\":\"art\",\"ip\":0,\"op\":2,\"st\":0,"
            "\"ks\":{\"o\":{\"a\":0,\"k\":100},\"r\":{\"a\":0,\"k\":0},"
            "\"p\":{\"a\":0,\"k\":[8,8,0]},\"a\":{\"a\":0,\"k\":[0,0,0]},"
            "\"s\":{\"a\":0,\"k\":[100,100,100]}},\"shapes\":[{\"ty\":\"gr\","
            "\"nm\":\"palette\",\"it\":[{\"ty\":\"rc\",\"nm\":\"box\","
            "\"p\":{\"a\":0,\"k\":[0,0]},\"s\":{\"a\":0,\"k\":[12,12]},"
            "\"r\":{\"a\":0,\"k\":0}},{\"ty\":\"fl\",\"nm\":\"base\","
            "\"c\":{\"a\":0,\"k\":[0,0,1,1]},\"o\":{\"a\":0,\"k\":100},\"r\":1},"
            "{\"ty\":\"st\",\"nm\":\"ink\",\"c\":{\"a\":0,\"k\":[1,0,0,1]},"
            "\"o\":{\"a\":0,\"k\":100},\"w\":{\"a\":0,\"k\":2},\"lc\":1,\"lj\":1},"
            "{\"ty\":\"tr\",\"p\":{\"a\":0,\"k\":[0,0]},\"a\":{\"a\":0,\"k\":[0,0]},"
            "\"s\":{\"a\":0,\"k\":[100,100]},\"r\":{\"a\":0,\"k\":0},"
            "\"o\":{\"a\":0,\"k\":100},\"sk\":{\"a\":0,\"k\":0},"
            "\"sa\":{\"a\":0,\"k\":0}}]}]}]}";

        lottie_animation_wrapper* baseline = nullptr;
        lottie_animation_wrapper* sequential = nullptr;
        lottie_animation_wrapper* batched = nullptr;
        Expect(lottie_load_from_data(paletteJson, nullptr, &baseline) == 0 && baseline != nullptr,
               "baseline semantic palette animation loads");
        Expect(lottie_load_from_data(paletteJson, nullptr, &sequential) == 0 && sequential != nullptr,
               "semantic palette animation loads");
        Expect(lottie_load_from_data(paletteJson, nullptr, &batched) == 0 && batched != nullptr,
               "second semantic palette animation loads");
        if (baseline == nullptr || sequential == nullptr || batched == nullptr)
        {
            lottie_dispose_wrapper(&baseline);
            lottie_dispose_wrapper(&sequential);
            lottie_dispose_wrapper(&batched);
            return;
        }

        uint32_t baselinePixels[16 * 16] = {};
        uint32_t sequentialPixels[16 * 16] = {};
        uint32_t batchPixels[16 * 16] = {};
        lottie_render_data baselineSurface{baselinePixels, 16, 16, 16 * 4};
        lottie_render_data sequentialSurface{sequentialPixels, 16, 16, 16 * 4};
        lottie_render_data batchSurface{batchPixels, 16, 16, 16 * 4};
        Expect(lottie_render_immediately(baseline, &baselineSurface, 0, false, false) == 0,
               "baseline palette frame renders");
        const uint32_t baselineHash = HashPixels(baselinePixels, 16 * 16);
        Expect(lottie_set_fill_color(baseline, "**.palette.base", 0, 1, 0) == 0,
               "fill override applies after an initial render");
        Expect(lottie_render_immediately(baseline, &baselineSurface, 1, false, false) == 0 &&
                   HashPixels(baselinePixels, 16 * 16) != baselineHash,
               "post-render fill override changes the next source frame");

        Expect(lottie_set_fill_color(sequential, "**.palette.base", 0, 1, 0) == 0,
               "globstar semantic fill keypath applies");
        Expect(lottie_set_stroke_color(sequential, "**.palette.ink", 0, 0, 1) == 0,
               "globstar semantic stroke keypath applies");
        Expect(lottie_render_immediately(sequential, &sequentialSurface, 0, false, false) == 0,
               "sequentially overridden frame renders");
        const uint32_t sequentialHash = HashPixels(sequentialPixels, 16 * 16);
        Expect(sequentialHash != baselineHash,
               "semantic fill and stroke overrides change rendered pixels");

        const lottie_color_override overrides[] = {
            {"**.palette.base", LOTTIE_COLOR_PROPERTY_FILL, 0, 1, 0},
            {"**.palette.ink", LOTTIE_COLOR_PROPERTY_STROKE, 0, 0, 1}};
        Expect(lottie_apply_color_overrides(batched, overrides, 2) == 0,
               "fill and stroke overrides apply in one batch");
        Expect(lottie_render_immediately(batched, &batchSurface, 0, false, false) == 0,
               "batch-overridden frame renders");
        Expect(HashPixels(batchPixels, 16 * 16) == sequentialHash,
               "batched overrides match sequential fill and stroke output");
        Expect(batchPixels[8 * 16 + 8] == 0xff00ff00u,
               "semantic fill override renders exact opaque green at the center");
        bool containsBlueStroke = false;
        for (uint32_t pixel : batchPixels)
        {
            const uint32_t blue = pixel & 0xffu;
            const uint32_t green = (pixel >> 8) & 0xffu;
            const uint32_t red = (pixel >> 16) & 0xffu;
            const uint32_t alpha = pixel >> 24;
            containsBlueStroke |= alpha > 0 && blue > green && blue > red;
        }
        Expect(containsBlueStroke,
               "semantic stroke override renders blue-dominant edge pixels");
        const uint32_t beforeEmptyBatch = HashPixels(batchPixels, 16 * 16);
        Expect(lottie_apply_color_overrides(batched, nullptr, 0) == 0,
               "empty batch is valid for a loaded animation");
        Expect(lottie_render_immediately(batched, &batchSurface, 1, false, false) == 0 &&
                   HashPixels(batchPixels, 16 * 16) == beforeEmptyBatch,
               "empty batch leaves rendered colors unchanged");

        lottie_color_override invalid = {"**.palette.base", static_cast<LottieColorProperty>(99), 0, 0, 0};
        Expect(lottie_apply_color_overrides(batched, &invalid, 1) == -1,
               "batch rejects unsupported properties");
        Expect(lottie_set_fill_color(batched, "", 0, 0, 0) == -1,
               "override rejects empty keypaths");
        Expect(lottie_set_fill_color(batched, "**.palette.base",
                                     std::numeric_limits<float>::quiet_NaN(), 0, 0) == -1,
               "override rejects non-finite colors");
        Expect(lottie_dispose_wrapper(&baseline) == 0 && baseline == nullptr,
               "baseline palette animation disposed");
        Expect(lottie_dispose_wrapper(&sequential) == 0 && sequential == nullptr,
               "sequential palette animation disposed");
        Expect(lottie_dispose_wrapper(&batched) == 0 && batched == nullptr,
               "batched palette animation disposed");
    }

    void TestCompiledBackendCapabilities()
    {
#if defined(LOTTIE_EXPECT_NATIVE_VULKAN_BACKEND)
        Expect(lottie_is_native_vulkan_backend_compiled() == 1,
               "native Vulkan backend is compiled into the plugin");
#else
        Expect(lottie_is_native_vulkan_backend_compiled() == 0,
               "native Vulkan backend reports disabled when it is not compiled");
#endif
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
        renderData.buffer = nullptr;
        Expect(lottie_render_immediately(animation, &renderData, 0, false, false) == -1,
               "missing external render buffer rejected after slot acquisition");
        Expect(lottie_render_create_future_async(animation, &renderData, 0, false, false) == -1,
               "missing external async buffer rejected after slot acquisition");

        renderData.buffer = &pixel;
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
    TestSemanticColorKeypathsAndBatching();
    TestCompiledBackendCapabilities();
    TestRenderDataLifecycle();
    TestInvalidRenderLayouts();
    if (failures == 0)
    {
        std::puts("Native ABI tests passed.");
    }
    return failures == 0 ? 0 : 1;
}

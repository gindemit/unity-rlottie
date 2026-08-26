#include "WebGLBackend.h"

#if defined(__EMSCRIPTEN__)

#include <GLES2/gl2.h>
#include <algorithm>
#include <cstring>
#include <cstdint>
#include <limits>
#include <vector>

namespace
{
struct WebGLTextureState
{
    lottie_animation_wrapper* animation = nullptr;
    GLuint texture = 0;
    int width = 0;
    int height = 0;
    std::vector<uint8_t> uploadPixels;
    bool pending = false;
    bool available = false;
};

std::vector<WebGLTextureState> gTextures;
bool gRenderingPluginRegistered = false;

WebGLTextureState* FindState(lottie_animation_wrapper* animation)
{
    for (WebGLTextureState& state : gTextures)
    {
        if (state.animation == animation)
        {
            return &state;
        }
    }
    return nullptr;
}

void UNITY_INTERFACE_API OnWebGLRenderEvent(int /*eventID*/)
{
    WebGLTextureState* state = nullptr;
    for (WebGLTextureState& candidate : gTextures)
    {
        if (candidate.pending)
        {
            state = &candidate;
            break;
        }
    }

    if (state == nullptr)
    {
        return;
    }

    state->pending = false;
    if (!state->available || state->texture == 0 || state->uploadPixels.empty() ||
        glIsTexture(state->texture) == GL_FALSE)
    {
        state->available = false;
        return;
    }

    GLint previousTexture = 0;
    GLint previousUnpackAlignment = 4;
    glGetIntegerv(GL_TEXTURE_BINDING_2D, &previousTexture);
    glGetIntegerv(GL_UNPACK_ALIGNMENT, &previousUnpackAlignment);
    // Do not attribute an error left by Unity or another plug-in to this upload.
    while (glGetError() != GL_NO_ERROR)
    {
    }
    glBindTexture(GL_TEXTURE_2D, state->texture);
    glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
    glTexSubImage2D(
        GL_TEXTURE_2D,
        0,
        0,
        0,
        state->width,
        state->height,
        GL_RGBA,
        GL_UNSIGNED_BYTE,
        state->uploadPixels.data());
    const GLenum error = glGetError();
    glPixelStorei(GL_UNPACK_ALIGNMENT, previousUnpackAlignment);
    glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(previousTexture));
    state->available = error == GL_NO_ERROR;
}
}

extern "C"
{
typedef void (UNITY_INTERFACE_API *WebGLPluginLoadFunc)(void* unityInterfaces);
typedef void (UNITY_INTERFACE_API *WebGLPluginUnloadFunc)();
void UnityRegisterRenderingPlugin(WebGLPluginLoadFunc loadPlugin, WebGLPluginUnloadFunc unloadPlugin);

static void UNITY_INTERFACE_API OnWebGLPluginLoad(void* /*unityInterfaces*/)
{
}

static void UNITY_INTERFACE_API OnWebGLPluginUnload()
{
    gTextures.clear();
    gRenderingPluginRegistered = false;
}
}

void RegisterWebGLRenderingPlugin()
{
    if (!gRenderingPluginRegistered)
    {
        UnityRegisterRenderingPlugin(OnWebGLPluginLoad, OnWebGLPluginUnload);
        gRenderingPluginRegistered = true;
    }
}

bool RegisterUnityTextureWebGL(
    lottie_animation_wrapper* animation,
    void* nativeTexture,
    int width,
    int height)
{
    const uintptr_t textureName = reinterpret_cast<uintptr_t>(nativeTexture);
    if (!gRenderingPluginRegistered || animation == nullptr || textureName == 0 ||
        width <= 0 || height <= 0)
    {
        return false;
    }

    const size_t pixelCount = static_cast<size_t>(width) * static_cast<size_t>(height);
    if (pixelCount > std::numeric_limits<size_t>::max() / 4u)
    {
        return false;
    }

    WebGLTextureState* state = FindState(animation);
    if (state == nullptr)
    {
        gTextures.push_back({});
        state = &gTextures.back();
        state->animation = animation;
    }
    state->texture = static_cast<GLuint>(textureName);
    state->width = width;
    state->height = height;
    state->uploadPixels.resize(pixelCount * 4u);
    state->pending = false;
    state->available = true;
    return true;
}

void UnregisterUnityTextureWebGL(lottie_animation_wrapper* animation)
{
    gTextures.erase(
        std::remove_if(gTextures.begin(), gTextures.end(),
            [animation](const WebGLTextureState& state) { return state.animation == animation; }),
        gTextures.end());
}

bool RequestTextureUploadWebGL(
    lottie_animation_wrapper* animation,
    lottie_render_data* renderData)
{
    WebGLTextureState* state = FindState(animation);
    if (state == nullptr || !state->available || renderData == nullptr ||
        renderData->buffer == nullptr || renderData->width != static_cast<uint32_t>(state->width) ||
        renderData->height != static_cast<uint32_t>(state->height) ||
        renderData->bytesPerLine != renderData->width * 4u)
    {
        return false;
    }
    // GL.IssuePluginEvent may execute after managed code starts preparing a
    // later frame (notably in threaded WebGL builds). Snapshot the completed
    // pixels so the render event never observes a partially overwritten frame.
    std::memcpy(
        state->uploadPixels.data(),
        renderData->buffer,
        state->uploadPixels.size());
    state->pending = true;
    return true;
}

bool IsWebGLUploadAvailable(lottie_animation_wrapper* animation)
{
    WebGLTextureState* state = FindState(animation);
    return state != nullptr && state->available;
}

UnityRenderingEvent GetWebGLRenderEventFunc()
{
    return OnWebGLRenderEvent;
}

#endif

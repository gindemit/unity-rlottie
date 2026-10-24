#include "WebGLBackend.h"

#if defined(__EMSCRIPTEN__)

#include <GLES2/gl2.h>
#include <algorithm>
#include <cstring>
#include <cstdint>
#include <limits>
#include <vector>
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
#include <GLES3/gl3.h>
#include <emscripten/html5_webgl.h>
#include <emscripten/heap.h>
#include <cstddef>
#endif

namespace
{
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
static_assert(sizeof(LottieWebGLTraceHeader) == 128 && alignof(LottieWebGLTraceHeader) == 4, "trace header ABI");
static_assert(sizeof(LottieWebGLTraceRecord) == 192 && alignof(LottieWebGLTraceRecord) == 4, "trace record ABI");
static_assert(offsetof(LottieWebGLTraceHeader, flags) == 72 && offsetof(LottieWebGLTraceHeader, reserved7) == 124, "trace header offsets");
static_assert(offsetof(LottieWebGLTraceRecord, animationId) == 28 &&
    offsetof(LottieWebGLTraceRecord, checkedUploadError) == 144 &&
    offsetof(LottieWebGLTraceRecord, drainedErrorCount) == 160 &&
    offsetof(LottieWebGLTraceRecord, reserved6) == 188, "trace record offsets");

enum TraceFlag : uint32_t
{
    Armed = 1u, PreArmLost = 1u << 1, StorageExhausted = 1u << 2,
    CounterSaturated = 1u << 3, IdentityOverflow = 1u << 4,
    EventOverflow = 1u << 5, UnmatchedEvent = 1u << 6,
    UnsupportedContext = 1u << 7, InvalidCorrelation = 1u << 8,
    UnsupportedThread = 1u << 9, RegistrationLost = 1u << 10, RequestLost = 1u << 11
};
struct TraceRegistration
{
    uint32_t animationId = 0, generation = 0, textureToken = 0;
    uint32_t attempt = 0, frame = 0, origin = 0;
    bool valid = false;
};
struct TraceRequest
{
    uint32_t animationId = 0, generation = 0, sequence = 0, frame = 0;
    bool valid = false;
};
struct TraceEvent { uint32_t token = 0, frame = 0; };
constexpr LottieWebGLTraceHeader InitialTraceHeader()
{
    LottieWebGLTraceHeader header = {};
    header.magic = 0x4c575431u; header.abiVersion = 1; header.headerBytes = 128;
    header.recordBytes = 192; header.capacity = 64;
    return header;
}
LottieWebGLTraceHeader gTrace = InitialTraceHeader();
LottieWebGLTraceRecord gTraceRecords[64] = {};
TraceEvent gTraceEvents[64] = {};
uint32_t gLastTraceEvent = 1;

bool TraceArmed() { return (gTrace.flags & Armed) != 0; }
void TraceIncrement(uint32_t& value)
{
    if (value == UINT32_MAX) gTrace.flags |= CounterSaturated;
    else ++value;
}
void TraceIdentityLost()
{
    gTrace.flags |= IdentityOverflow | InvalidCorrelation;
    TraceIncrement(gTrace.identityOverflowTotal);
}
int32_t TraceReady()
{
#if defined(__EMSCRIPTEN_PTHREADS__)
    return -4;
#else
    return TraceArmed() ? 0 : -3;
#endif
}
LottieWebGLTraceRecord* TraceBeginCallback(int eventId)
{
    if (!TraceArmed()) return nullptr;
    TraceIncrement(gTrace.callbacksTotal);
    uint32_t frame = 0;
    bool matched = false;
    for (TraceEvent& event : gTraceEvents)
    {
        if (event.token != 0 && event.token == static_cast<uint32_t>(eventId))
        {
            frame = event.frame;
            event = {};
            matched = true;
            break;
        }
    }
    if (!matched)
    {
        gTrace.flags |= UnmatchedEvent | InvalidCorrelation;
        TraceIncrement(gTrace.unmatchedEventsTotal);
    }
    if (gTrace.storedRecords == 64)
    {
        gTrace.flags |= StorageExhausted;
        TraceIncrement(gTrace.callbacksDropped);
        return nullptr;
    }
    LottieWebGLTraceRecord* record = &gTraceRecords[gTrace.storedRecords++];
    *record = {};
    record->recordBytes = 192;
    record->abiVersion = 1;
    record->sequence = record->callbackSequence = gTrace.callbacksTotal;
    if (gTrace.storedRecords == 1) gTrace.firstSequence = record->sequence;
    gTrace.lastSequence = record->sequence;
    if (matched)
    {
        record->eventToken = static_cast<uint32_t>(eventId);
        record->eventUnityFrame = frame;
        record->validFlags |= 1u << 3;
    }
    else record->contextStatus = 6;
    return record;
}
GLboolean TraceIsTexture(GLuint texture, LottieWebGLTraceRecord* record)
{
    const GLboolean result = glIsTexture(texture); // The original call, not an added query.
    if (record != nullptr)
    {
        record->isTextureResult = result != GL_FALSE ? 1u : 0u;
        record->validFlags |= 1u << 6;
    }
    return result;
}
void TraceObserveContext(LottieWebGLTraceRecord* record, GLuint texture, GLint previousTexture, GLint alignment)
{
    if (record == nullptr) return;
    record->previousTextureRelation = previousTexture == 0 ? 0u :
        (static_cast<GLuint>(previousTexture) == texture ? 1u : 2u);
    record->previousUnpackAlignment = static_cast<uint32_t>(alignment);
    record->validFlags |= 1u << 7;
    if ((record->validFlags & 15u) != 15u)
    {
        record->contextStatus = 6;
        return;
    }
    record->validFlags |= 1u << 5;
    const EMSCRIPTEN_WEBGL_CONTEXT_HANDLE context = emscripten_webgl_get_current_context();
    if (context == 0) record->contextStatus = 2;
    else
    {
        EmscriptenWebGLContextAttributes attributes = {};
        if (emscripten_webgl_get_context_attributes(context, &attributes) != EMSCRIPTEN_RESULT_SUCCESS)
            record->contextStatus = 3;
        else
        {
            record->contextMajor = static_cast<uint32_t>(attributes.majorVersion);
            record->contextMinor = static_cast<uint32_t>(attributes.minorVersion);
            record->contextLost = emscripten_is_webgl_context_lost(context) ? 1u : 0u;
            record->contextStatus = record->contextLost ? 5u : (attributes.majorVersion == 2 ? 1u : 4u);
        }
    }
    if (record->contextStatus != 1)
    {
        gTrace.flags |= UnsupportedContext;
        return;
    }
    GLint value = 0;
    glGetIntegerv(GL_PIXEL_UNPACK_BUFFER_BINDING, &value);
    record->pixelUnpackBufferBound = value != 0 ? 1u : 0u;
    glGetIntegerv(GL_UNPACK_ROW_LENGTH, &value);
    record->unpackRowLength = static_cast<uint32_t>(value);
    glGetIntegerv(GL_UNPACK_SKIP_ROWS, &value);
    record->unpackSkipRows = static_cast<uint32_t>(value);
    glGetIntegerv(GL_UNPACK_SKIP_PIXELS, &value);
    record->unpackSkipPixels = static_cast<uint32_t>(value);
    record->validFlags |= 1u << 8;
}
#endif
struct WebGLTextureState
{
    lottie_animation_wrapper* animation = nullptr;
    GLuint texture = 0;
    int width = 0;
    int height = 0;
    std::vector<uint8_t> uploadPixels;
    bool pending = false;
    bool available = false;
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    TraceRegistration traceBinding, traceLatestRegistration;
    TraceRequest tracePending, traceLatestRequest;
    bool traceLatestRegistrationAccepted = false, traceLatestRequestAccepted = false;
#endif
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

void UNITY_INTERFACE_API OnWebGLRenderEvent(int eventID)
{
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    LottieWebGLTraceRecord* trace = TraceBeginCallback(eventID);
#else
    (void)eventID;
#endif
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
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
        if (trace != nullptr) trace->outcome = 1;
#endif
        return;
    }

#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    if (trace != nullptr)
    {
        trace->pendingBefore = state->pending ? 1u : 0u;
        trace->availableBefore = state->available ? 1u : 0u;
        trace->width = static_cast<uint32_t>(state->width);
        trace->height = static_cast<uint32_t>(state->height);
        trace->validFlags |= 1u << 4;
        if (state->traceBinding.valid)
        {
            const TraceRegistration& binding = state->traceBinding;
            trace->animationId = binding.animationId;
            trace->generation = binding.generation;
            trace->textureToken = binding.textureToken;
            trace->registrationAttempt = binding.attempt;
            trace->registrationUnityFrame = binding.frame;
            trace->registrationOrigin = binding.origin;
            trace->validFlags |= 3u;
        }
        else
        {
            gTrace.flags |= RegistrationLost | InvalidCorrelation;
            TraceIncrement(gTrace.registrationMetadataLossTotal);
        }
        if (state->tracePending.valid && state->traceBinding.valid &&
            state->tracePending.animationId == state->traceBinding.animationId &&
            state->tracePending.generation == state->traceBinding.generation)
        {
            trace->requestSequence = state->tracePending.sequence;
            trace->requestUnityFrame = state->tracePending.frame;
            trace->validFlags |= 1u << 2;
        }
        else
        {
            gTrace.flags |= RequestLost | InvalidCorrelation;
            TraceIncrement(gTrace.requestMetadataLossTotal);
        }
    }
#endif
    state->pending = false;
    if (!state->available || state->texture == 0 || state->uploadPixels.empty() ||
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
        TraceIsTexture(state->texture, trace) == GL_FALSE)
#else
        glIsTexture(state->texture) == GL_FALSE)
#endif
    {
        state->available = false;
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
        if (trace != nullptr) { trace->outcome = 2; trace->availableAfter = 0; }
#endif
        return;
    }

    GLint previousTexture = 0;
    GLint previousUnpackAlignment = 4;
    glGetIntegerv(GL_TEXTURE_BINDING_2D, &previousTexture);
    glGetIntegerv(GL_UNPACK_ALIGNMENT, &previousUnpackAlignment);
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    TraceObserveContext(trace, state->texture, previousTexture, previousUnpackAlignment);
#endif
    // Do not attribute an error left by Unity or another plug-in to this upload.
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    GLenum drainedError;
    while ((drainedError = glGetError()) != GL_NO_ERROR)
#else
    while (glGetError() != GL_NO_ERROR)
#endif
    {
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
        if (trace != nullptr)
        {
            if (trace->drainedErrorCount == 0) trace->firstDrainedError = drainedError;
            TraceIncrement(trace->drainedErrorCount);
        }
#endif
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
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    if (trace != nullptr)
    {
        trace->checkedUploadError = error;
        trace->checkedUploadErrorValid = trace->restorationCompleted = 1;
        trace->validFlags |= (1u << 9) | (1u << 10) | (1u << 11);
        trace->availableAfter = state->available ? 1u : 0u;
        trace->outcome = trace->contextStatus != 1 ? 5u : (error == GL_NO_ERROR ? 3u : 4u);
    }
#endif
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
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    // A new binding invalidates pixels accepted for the preceding binding.
    if (TraceArmed()) { state->tracePending = {}; state->traceBinding = {}; }
#endif
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
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    if (TraceArmed()) state->tracePending = {}; // A wrapper supplies the accepted request atomically below.
#endif
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

#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
int32_t WebGLTraceArm(uint32_t abi, uint32_t headerBytes, uint32_t recordBytes, uint32_t frame)
{
    if (abi != 1 || headerBytes != 128 || recordBytes != 192) return -1;
#if defined(__EMSCRIPTEN_PTHREADS__)
    return -4;
#else
    if (!TraceArmed())
    {
        gTrace.flags = Armed;
        gTrace.armUnityFrame = frame;
        if (gRenderingPluginRegistered || !gTextures.empty())
        {
            gTrace.preArmStateDetected = 1;
            gTrace.lateArmTotal = 1;
            gTrace.flags |= PreArmLost;
        }
    }
    return 0;
#endif
}

int32_t WebGLTraceRegister(lottie_animation_wrapper* animation, void* texture, int32_t width, int32_t height,
    uint32_t animationId, uint32_t generation, uint32_t textureToken, uint32_t attempt, uint32_t frame, uint32_t origin)
{
    const int32_t ready = TraceReady();
    if (ready != 0) return ready;
    TraceIncrement(gTrace.registrationsTotal);
    TraceRegistration context;
    context.animationId = animationId; context.generation = generation; context.textureToken = textureToken;
    context.attempt = attempt; context.frame = frame; context.origin = origin;
    context.valid = animationId != 0 && generation != 0 && textureToken != 0 && attempt != 0 && origin >= 1 && origin <= 3;
    if (!context.valid) TraceIdentityLost();
    WebGLTextureState* previous = FindState(animation);
    if (previous != nullptr && previous->traceBinding.valid &&
        (previous->traceBinding.animationId != animationId || generation < previous->traceBinding.generation ||
         (previous->traceLatestRegistration.attempt != 0 && attempt <= previous->traceLatestRegistration.attempt)))
    {
        context.valid = false;
        gTrace.flags |= InvalidCorrelation;
    }
    const bool accepted = RegisterUnityTextureWebGL(animation, texture, width, height);
    WebGLTextureState* state = FindState(animation); // Registration can reallocate the existing vector.
    if (state != nullptr)
    {
        state->traceLatestRegistration = context;
        state->traceLatestRegistrationAccepted = accepted;
        if (accepted) state->traceBinding = context;
    }
    if (!accepted) TraceIncrement(gTrace.registrationsRejected);
    if (state == nullptr || !context.valid)
    {
        gTrace.flags |= RegistrationLost;
        TraceIncrement(gTrace.registrationMetadataLossTotal);
    }
    return accepted ? 1 : 0;
}

int32_t WebGLTraceRequest(lottie_animation_wrapper* animation, lottie_render_data* data,
    uint32_t animationId, uint32_t generation, uint32_t sequence, uint32_t frame)
{
    const int32_t ready = TraceReady();
    if (ready != 0) return ready;
    TraceIncrement(gTrace.requestsTotal);
    TraceRequest context;
    context.animationId = animationId; context.generation = generation;
    context.sequence = sequence; context.frame = frame;
    context.valid = animationId != 0 && generation != 0 && sequence != 0;
    if (!context.valid) TraceIdentityLost();
    WebGLTextureState* state = FindState(animation);
    if (state != nullptr && (!state->traceBinding.valid || state->traceBinding.animationId != animationId ||
        state->traceBinding.generation != generation ||
        (state->traceLatestRequest.sequence != 0 && sequence <= state->traceLatestRequest.sequence)))
    {
        context.valid = false;
        gTrace.flags |= InvalidCorrelation;
    }
    const bool accepted = RequestTextureUploadWebGL(animation, data);
    if (state != nullptr)
    {
        state->traceLatestRequest = context;
        state->traceLatestRequestAccepted = accepted;
        if (accepted) state->tracePending = context;
    }
    if (!accepted) TraceIncrement(gTrace.requestsRejected);
    if (state == nullptr || !context.valid)
    {
        gTrace.flags |= RequestLost;
        TraceIncrement(gTrace.requestMetadataLossTotal);
    }
    return accepted ? 1 : 0;
}

int32_t WebGLTraceIssue(uint32_t token, uint32_t frame)
{
    const int32_t ready = TraceReady();
    if (ready != 0) return ready;
    if (token == 0 || token > static_cast<uint32_t>(INT32_MAX)) return -2;
    TraceIncrement(gTrace.eventsIssuedTotal);
    if (token == 1)
    {
        TraceIdentityLost(); // The managed sequence exhausted; still issue the baseline event.
        return 0;
    }
    if (token <= gLastTraceEvent)
    {
        // Poison a duplicate pending ID so it cannot masquerade as a valid earlier issue.
        for (TraceEvent& event : gTraceEvents) if (event.token == token) event = {};
        gTrace.flags |= InvalidCorrelation | UnmatchedEvent;
        return 0;
    }
    gLastTraceEvent = token;
    for (TraceEvent& event : gTraceEvents)
    {
        if (event.token == 0)
        {
            event.token = token;
            event.frame = frame;
            return 0;
        }
    }
    gTrace.flags |= EventOverflow | InvalidCorrelation;
    return 0; // A full trace table must not suppress the real GL.IssuePluginEvent.
}

namespace
{
bool TraceRange(const void* pointer, uint32_t bytes)
{
    const uintptr_t start = reinterpret_cast<uintptr_t>(pointer);
    const size_t heapBytes = emscripten_get_heap_size();
    return pointer != nullptr && (start & 3u) == 0 && start <= heapBytes && bytes <= heapBytes - start;
}
}

int32_t WebGLTraceCopy(uint32_t abi, uint32_t headerBytes, uint32_t recordBytes,
    void* header, uint32_t headerCapacityBytes, void* records, uint32_t capacity, uint32_t capacityBytes)
{
    if (abi != 1 || headerBytes != 128 || recordBytes != 192) return -1;
    if (headerCapacityBytes < 128 || capacity > 64 || capacityBytes < capacity * 192u ||
        !TraceRange(header, headerCapacityBytes) ||
        (records == nullptr && (capacity != 0 || capacityBytes != 0)) ||
        (records != nullptr && !TraceRange(records, capacityBytes))) return -2;
    if (records != nullptr)
    {
        const uintptr_t a = reinterpret_cast<uintptr_t>(header);
        const uintptr_t b = reinterpret_cast<uintptr_t>(records);
        if (a < b + capacityBytes && b < a + headerCapacityBytes) return -2;
    }
    const int32_t ready = TraceReady();
    if (ready != 0) return ready;
    LottieWebGLTraceHeader snapshot = gTrace;
    snapshot.returnedRecords = std::min(capacity, snapshot.storedRecords);
    std::memcpy(header, &snapshot, sizeof(snapshot));
    if (snapshot.returnedRecords != 0)
        std::memcpy(records, gTraceRecords, snapshot.returnedRecords * sizeof(LottieWebGLTraceRecord));
    return 0;
}
#endif
#endif

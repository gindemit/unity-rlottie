// Host-only contracts. native_contract.py supplies explicitly labelled GL/Unity/
// Emscripten stubs and copies the actual backend/header unchanged to external output.
// No real GPU, browser, Unity callback scheduler or Wasm memory is exercised.
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <new>
#include <limits>
#define __EMSCRIPTEN__ 1
#if !defined(LOTTIE_HOST_TRACE_OFF)
#define LOTTIE_WEBGL_NATIVE_STATE_TRACE 1
#endif
#include "WebGLBackend.cpp"
#include "native_exports.inc"
#include "native_offsets.inc"

namespace Host
{
char calls[4096] = {};
size_t callCount = 0, allocations = 0;
bool watchAllocations = false, isTexture = true, lost = false;
int context = 1, major = 2, attributesResult = 0, contextQueries = 0;
GLint binding = 11, alignment = 8;
GLenum errors[16] = {};
unsigned errorIndex = 0;
size_t heapLimit = std::numeric_limits<size_t>::max();
unsigned cases = 0;
void Call(char value) { if (callCount + 1 < sizeof(calls)) calls[callCount++] = value; calls[callCount] = 0; }
void Check(bool value, const char* message)
{
    if (!value) { std::fprintf(stderr, "FAILED: %s\n", message); std::exit(1); }
}
void Reset()
{
    gTextures.clear(); gRenderingPluginRegistered = false;
    callCount = 0; calls[0] = 0; allocations = 0; watchAllocations = false;
    isTexture = true; lost = false; context = 1; major = 2; attributesResult = contextQueries = 0;
    binding = 11; alignment = 8; errorIndex = 0; std::memset(errors, 0, sizeof(errors));
    heapLimit = std::numeric_limits<size_t>::max();
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    gTrace = InitialTraceHeader();
    std::memset(gTraceRecords, 0, sizeof(gTraceRecords));
    std::memset(gTraceEvents, 0, sizeof(gTraceEvents)); gLastTraceEvent = 1;
#endif
}
lottie_animation_wrapper animation;
uint32_t pixels[4] = {};
lottie_render_data data = { pixels, 2, 2, 8 };
void BaselineSetup()
{
    RegisterWebGLRenderingPlugin();
    Check(RegisterUnityTextureWebGL(&animation, reinterpret_cast<void*>(7), 2, 2), "baseline registration");
    Check(RequestTextureUploadWebGL(&animation, &data), "baseline request");
}
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
void Arm() { Check(WebGLTraceArm(1,128,192,10) == 0, "arm"); }
void Setup()
{
    Arm(); RegisterWebGLRenderingPlugin();
    Check(WebGLTraceRegister(&animation, reinterpret_cast<void*>(7), 2,2,1,1,1,1,11,1)==1, "trace register");
    Check(WebGLTraceRequest(&animation,&data,1,1,1,12)==1, "trace request");
}
void Callback(uint32_t token = 2, uint32_t frame = 13)
{
    Check(WebGLTraceIssue(token,frame)==0, "issue metadata");
    GetWebGLRenderEventFunc()(static_cast<int>(token));
}
void Copy(LottieWebGLTraceHeader& header, LottieWebGLTraceRecord* records, uint32_t capacity=64)
{
    Check(WebGLTraceCopy(1,128,192,&header,128,records,capacity,capacity*192)==0, "copy");
}
#endif
}

void* operator new(std::size_t count)
{
    if (Host::watchAllocations) ++Host::allocations;
    if (void* memory = std::malloc(count)) return memory;
    throw std::bad_alloc();
}
void operator delete(void* memory) noexcept { std::free(memory); }
void operator delete(void* memory, std::size_t) noexcept { std::free(memory); }

GLboolean glIsTexture(GLuint) { Host::Call('I'); return Host::isTexture ? GL_TRUE : GL_FALSE; }
void glGetIntegerv(GLenum name, GLint* result)
{
    switch(name)
    {
    case GL_TEXTURE_BINDING_2D: Host::Call('b'); *result=Host::binding; break;
    case GL_UNPACK_ALIGNMENT: Host::Call('a'); *result=Host::alignment; break;
    case GL_PIXEL_UNPACK_BUFFER_BINDING: Host::Call('p'); *result=9; break;
    case GL_UNPACK_ROW_LENGTH: Host::Call('r'); *result=17; break;
    case GL_UNPACK_SKIP_ROWS: Host::Call('s'); *result=3; break;
    case GL_UNPACK_SKIP_PIXELS: Host::Call('k'); *result=4; break;
    default: Host::Check(false,"unexpected GL selector");
    }
}
GLenum glGetError() { Host::Call('E'); return Host::errors[Host::errorIndex < 15 ? Host::errorIndex++ : 15]; }
void glBindTexture(GLenum,GLuint value) { Host::Call('B'); Host::binding=static_cast<GLint>(value); }
void glPixelStorei(GLenum,GLint value) { Host::Call('A'); Host::alignment=value; }
void glTexSubImage2D(GLenum,GLint,GLint,GLint,GLsizei,GLsizei,GLenum,GLenum,const void*) { Host::Call('U'); }
extern "C" void UnityRegisterRenderingPlugin(WebGLPluginLoadFunc,WebGLPluginUnloadFunc) {}
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
int emscripten_webgl_get_current_context() { ++Host::contextQueries; return Host::context; }
int emscripten_webgl_get_context_attributes(int,EmscriptenWebGLContextAttributes* value)
{ ++Host::contextQueries; value->majorVersion=Host::major; value->minorVersion=0; return Host::attributesResult; }
bool emscripten_is_webgl_context_lost(int) { ++Host::contextQueries; return Host::lost; }
size_t emscripten_get_heap_size() { return Host::heapLimit; }
#endif

int main()
{
    using namespace Host;
#if defined(__EMSCRIPTEN_PTHREADS__)
    Reset(); LottieWebGLTraceHeader unsupportedHeader = {};
    Check(lottie_webgl_trace_arm(1,128,192,1)==-4,"threaded trace arm explicitly unsupported");
    Check(lottie_webgl_trace_issue(2,1)==-4,"threaded issue explicitly unsupported");
    Check(lottie_webgl_trace_copy(1,128,192,&unsupportedHeader,128,nullptr,0,0)==-4,"threaded copy explicitly unsupported");
    std::puts("NativeWebGLTrace host contracts: 1 case PASS (unsupported threaded mode, stub-only)");
    return 0;
#endif
    Reset(); BaselineSetup();
    watchAllocations=true; GetWebGLRenderEventFunc()(1); watchAllocations=false;
    Check(std::strcmp(calls,"IbaEBAUEAB")==0,"ordinary/unselected exact GL order and two error reads");
    Check(allocations==0 && contextQueries==0,"unselected callback no diagnostic query/allocation");
    ++cases;
#if defined(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    Check(gTrace.callbacksTotal==0 && gTrace.storedRecords==0,"unselected callback no trace work");
    Arm(); Check(gTrace.preArmStateDetected==1 && gTrace.lateArmTotal==1,"late arm detected from existing state");
    Check(WebGLTraceArm(1,128,192,99)==0 && gTrace.armUnityFrame==10 && gTrace.lateArmTotal==1,"idempotent arm"); ++cases;

    Reset(); LottieWebGLTraceHeader header = {}; LottieWebGLTraceRecord records[64] = {};
    Check(WebGLTraceArm(2,128,192,0)==-1 && !TraceArmed(),"bad version no arm");
    Check(WebGLTraceArm(1,124,192,0)==-1 && WebGLTraceArm(1,128,188,0)==-1,"bad layout no arm");
    Check(WebGLTraceCopy(1,128,192,&header,128,records,64,sizeof(records))==-3,"copy before arm"); ++cases;

    Reset(); Setup();
    Check(gTrace.storedRecords==0,"control calls do not occupy callback slots");
    Callback(); Copy(header,records);
    Check(header.storedRecords==1 && header.callbacksTotal==1 && header.eventsIssuedTotal==1,"first callback counts");
    Check(records[0].animationId==1 && records[0].generation==1 && records[0].textureToken==1 &&
        records[0].registrationAttempt==1 && records[0].registrationUnityFrame==11 &&
        records[0].requestSequence==1 && records[0].requestUnityFrame==12 && records[0].eventToken==2 &&
        records[0].eventUnityFrame==13,"actual binding/request/issued-event identity");
    Check(records[0].outcome==3 && records[0].restorationCompleted==1 && records[0].checkedUploadErrorValid==1 &&
        records[0].validFlags==4095 && records[0].previousTextureRelation==2 &&
        records[0].pixelUnpackBufferBound==1 && records[0].unpackRowLength==17 &&
        records[0].unpackSkipRows==3 && records[0].unpackSkipPixels==4,"supported callback values/validity");
    Check(std::strcmp(calls,"IbaprskEBAUEAB")==0 && binding==11 && alignment==8,"four getters before original drain/restoration"); ++cases;

    const auto savedHeader=header; const auto savedRecord=records[0];
    Copy(header,records,1); Check(header.returnedRecords==1 && header.storedRecords==1,"prefix export");
    Check(WebGLTraceCopy(1,128,192,&header,128,nullptr,0,0)==0 && header.returnedRecords==0,"header-only export");
    Copy(header,records); Check(std::memcmp(&savedHeader,&header,sizeof(header))==0 &&
        std::memcmp(&savedRecord,&records[0],sizeof(savedRecord))==0,"exports never reset/advance history"); ++cases;
    Check(lottie_webgl_trace_copy(1,128,192,&header,128,records,64,sizeof(records))==0 &&
        std::memcmp(&savedHeader,&header,sizeof(header))==0,"actual C export forwards the frozen ABI"); ++cases;

    std::memset(&header,0x5a,sizeof(header)); const auto untouched=header;
    Check(WebGLTraceCopy(2,128,192,&header,128,records,64,sizeof(records))==-1,"copy version rejected");
    Check(WebGLTraceCopy(1,128,192,&header,127,records,64,sizeof(records))==-2,"header capacity rejected");
    Check(WebGLTraceCopy(1,128,192,&header,128,records,65,sizeof(records))==-2,"count bound before multiplication");
    Check(WebGLTraceCopy(1,128,192,&header,128,records,64,sizeof(records)-1)==-2,"record byte capacity rejected");
    Check(WebGLTraceCopy(1,128,192,&header,128,nullptr,1,192)==-2,"null record buffer rejected");
    Check(WebGLTraceCopy(1,128,192,&header,128,&header,1,192)==-2,"overlap rejected");
    Check(WebGLTraceCopy(1,128,192,reinterpret_cast<char*>(&header)+1,128,records,64,sizeof(records))==-2,"unaligned header rejected");
    heapLimit=16;
    Check(WebGLTraceCopy(1,128,192,&header,128,records,64,sizeof(records))==-2,"linear-memory range rejected");
    heapLimit=std::numeric_limits<size_t>::max();
    Check(std::memcmp(&untouched,&header,sizeof(header))==0,"all invalid copy paths leave caller bytes untouched"); ++cases;

    Reset(); Setup();
    Check(WebGLTraceRequest(&animation,&data,1,1,2,20)==1,"second accepted replaces pending");
    Check(WebGLTraceRequest(&animation,nullptr,1,1,3,21)==0,"rejected attempt");
    Callback(); Copy(header,records);
    Check(header.requestsTotal==3 && header.requestsRejected==1 && records[0].requestSequence==2 &&
        records[0].requestUnityFrame==20,"rejected attempt cannot relabel accepted pixels"); ++cases;

    Reset(); Setup();
    Check(WebGLTraceRegister(&animation,reinterpret_cast<void*>(8),2,2,1,2,2,2,22,2)==1,"replacement registration");
    Callback(); Copy(header,records); Check(records[0].outcome==1,"registration clears old pending request");
    Check(WebGLTraceRequest(&animation,&data,1,2,2,23)==1,"new generation request");
    Callback(3,24); Copy(header,records);
    Check(records[1].generation==2 && records[1].textureToken==2 && records[1].registrationOrigin==2,"new binding generation");
    UnregisterUnityTextureWebGL(&animation);
    Check(WebGLTraceRequest(&animation,&data,1,2,3,25)==0 && (gTrace.flags&RequestLost)!=0,"retired pointer cannot borrow metadata"); ++cases;

    Reset(); Arm();
    Check(WebGLTraceRegister(&animation,nullptr,2,2,1,1,1,1,0,1)==0 &&
        gTrace.registrationsRejected==1 && gTrace.registrationMetadataLossTotal==1 &&
        (gTrace.flags&RegistrationLost)!=0 && gTrace.storedRecords==0,"preregistration rejection explicit loss without callback consumption"); ++cases;

    Reset(); Setup(); isTexture=false; Callback(); Copy(header,records);
    Check(records[0].outcome==2 && records[0].isTextureResult==0 && (records[0].validFlags&(1u<<6))!=0 &&
        std::strcmp(calls,"I")==0 && contextQueries==0,"invalid texture preserves early return"); ++cases;

    for (int variant=0;variant<4;++variant)
    {
        Reset(); Setup();
        if(variant==0) context=0; if(variant==1) attributesResult=-1; if(variant==2) major=1; if(variant==3) lost=true;
        Callback(); Copy(header,records);
        Check(records[0].contextStatus==static_cast<uint32_t>(variant+2) && records[0].outcome==5 &&
            (gTrace.flags&UnsupportedContext)!=0 && std::strcmp(calls,"IbaEBAUEAB")==0,
            "unsupported context gets no added GL query, preserves baseline upload"); ++cases;
    }

    Reset(); Setup(); errors[0]=0x502; errors[1]=0x501; errors[2]=0; errors[3]=0x502;
    Callback(); Copy(header,records);
    Check(records[0].firstDrainedError==0x502 && records[0].drainedErrorCount==2 &&
        records[0].checkedUploadError==0x502 && records[0].outcome==4 && records[0].availableAfter==0 &&
        records[0].restorationCompleted==1 && std::strcmp(calls,"IbaprskEEEBAUEAB")==0,"existing errors observed without extra reads"); ++cases;

    Reset(); Setup();
    Check(WebGLTraceIssue(2,13)==0 && WebGLTraceIssue(2,14)==0,"duplicate issue continues caller event path");
    GetWebGLRenderEventFunc()(2); Copy(header,records);
    Check(records[0].eventToken==0 && records[0].contextStatus==6 && contextQueries==0 &&
        (gTrace.flags&UnmatchedEvent)!=0,"duplicate token poisoned rather than falsely matched"); ++cases;

    Reset(); Setup(); GetWebGLRenderEventFunc()(99); Copy(header,records);
    Check(records[0].eventToken==0 && (gTrace.flags&UnmatchedEvent)!=0 && contextQueries==0,"unknown callback identity"); ++cases;

    Reset(); Arm(); for(uint32_t token=2;token<66;++token) Check(WebGLTraceIssue(token,token)==0,"fill event table");
    Check(WebGLTraceIssue(66,66)==0 && (gTrace.flags&EventOverflow)!=0 && gTrace.storedRecords==0,"event table capacity does not consume callbacks");
    GetWebGLRenderEventFunc()(66); Check(gTraceRecords[0].eventToken==0,"overflow event remains unavailable"); ++cases;

    Reset(); Setup(); Check(WebGLTraceIssue(INT32_MAX,1)==0,"maximum positive event token accepted");
    GetWebGLRenderEventFunc()(INT32_MAX);
    Check(WebGLTraceIssue(1,2)==0 && (gTrace.flags&IdentityOverflow)!=0,"event token exhaustion continues baseline event");
    GetWebGLRenderEventFunc()(1); Check(gTraceRecords[1].eventToken==0,"exhausted token never fabricated"); ++cases;

    Reset(); Setup(); Check(WebGLTraceRequest(&animation,&data,0,0,0,0)==1,"identity overflow keeps baseline request");
    Callback(); Copy(header,records);
    Check((header.flags&IdentityOverflow)!=0 && records[0].requestSequence==0 && contextQueries==0,"zero identity explicit loss"); ++cases;

    Reset(); Setup();
    for(uint32_t sequence=2;sequence<102;++sequence)
        Check(WebGLTraceRequest(&animation,&data,1,1,sequence,sequence)==1,"many actual requests");
    Check(gTrace.storedRecords==0,"one hundred requests reserve all64 callback slots");
    for(uint32_t token=2;token<66;++token) Callback(token,token);
    Check(gTrace.storedRecords==64 && gTrace.callbacksDropped==0 && (gTrace.flags&StorageExhausted)==0,"all64 callbacks retained");
    callCount=0; calls[0]=0; contextQueries=0;
    Check(WebGLTraceRequest(&animation,&data,1,1,102,102)==1,"pending after full callback array");
    watchAllocations=true; Callback(66,66); watchAllocations=false;
    Check(gTrace.storedRecords==64 && gTrace.callbacksDropped==1 && (gTrace.flags&StorageExhausted)!=0 &&
        contextQueries==0 && allocations==0 && std::strcmp(calls,"IbaEBAUEAB")==0,"after64 no record construction/getters/allocations, baseline upload retained"); ++cases;

    gTrace.callbacksTotal=UINT32_MAX; gTrace.callbacksDropped=UINT32_MAX;
    Callback(67,67);
    Check(gTrace.callbacksTotal==UINT32_MAX && gTrace.callbacksDropped==UINT32_MAX &&
        (gTrace.flags&CounterSaturated)!=0,"counter saturation never wraps"); ++cases;
#endif
    std::printf("NativeWebGLTrace host contracts: %u cases PASS (stub-only; no Unity/WebGL qualification)\n",cases);
    return 0;
}

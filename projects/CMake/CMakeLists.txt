# CMakeList.txt : CMake project for LottiePlugin
cmake_minimum_required (VERSION 3.16)

# rlottie still declares compatibility with CMake 3.3. CMake 4 removed
# compatibility with policy versions older than 3.5, so provide the minimum
# policy version while the dependency is configured.
set(CMAKE_POLICY_VERSION_MINIMUM 3.5)

#set (MSVC_RUNTIME_LIBRARY MultiThreadedDebugDLL)
option(LOTTIE_WEBGL_NATIVE_STATE_TRACE "Build isolated WebGL startup diagnostics, never production archives" OFF)
set(LOTTIE_WEBGL_TRACE_OUTPUT_ROOT "" CACHE PATH "Fresh external diagnostic output directory")

if(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    if(NOT RLOTTIE_WEB_ASSEMBLY OR NOT RLOTTIE_WEBGL_WASM_EXCEPTIONS)
        message(FATAL_ERROR "WebGL trace requires WebAssembly and RLOTTIE_WEBGL_WASM_EXCEPTIONS=ON")
    endif()
    if(ANDROID OR RLOTTIE_ANDROID OR RLOTTIE_IOS OR RLOTTIE_OSX OR RLOTTIE_LINUX)
        message(FATAL_ERROR "WebGL trace cannot combine non-WebGL platform selections")
    endif()
    foreach(flag_set CMAKE_C_FLAGS CMAKE_CXX_FLAGS CMAKE_EXE_LINKER_FLAGS
        CMAKE_C_FLAGS_DEBUG CMAKE_C_FLAGS_RELEASE CMAKE_CXX_FLAGS_DEBUG CMAKE_CXX_FLAGS_RELEASE)
        if("${${flag_set}}" MATCHES "pthread|PTHREAD|USE_PTHREADS")
            message(FATAL_ERROR "WebGL trace v1 does not support threaded flags")
        endif()
    endforeach()
    if("$ENV{EMCC_CFLAGS}" MATCHES "pthread|PTHREAD|USE_PTHREADS")
        message(FATAL_ERROR "WebGL trace v1 does not support threaded EMCC_CFLAGS")
    endif()
    if(NOT IS_ABSOLUTE "${CMAKE_TOOLCHAIN_FILE}" OR NOT EXISTS "${CMAKE_TOOLCHAIN_FILE}")
        message(FATAL_ERROR "WebGL trace requires the absolute installed Unity Emscripten toolchain file")
    endif()
    file(REAL_PATH "${CMAKE_TOOLCHAIN_FILE}" trace_toolchain)
    if(NOT trace_toolchain MATCHES "/emscripten/cmake/Modules/Platform/Emscripten\\.cmake$")
        message(FATAL_ERROR "WebGL trace requires the reviewed installed Emscripten toolchain layout")
    endif()
    get_filename_component(trace_toolchain_directory "${trace_toolchain}" DIRECTORY)
    get_filename_component(trace_toolchain_root "${trace_toolchain_directory}/../../../.." ABSOLUTE)
    get_filename_component(trace_source_root "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
    get_filename_component(trace_cmake_bin "${CMAKE_COMMAND}" DIRECTORY)
    set(trace_protected_roots "${trace_source_root}" "${CMAKE_SOURCE_DIR}"
        "${trace_toolchain_root}" "${trace_cmake_bin}")
    if(IS_ABSOLUTE "${CMAKE_MAKE_PROGRAM}")
        get_filename_component(trace_ninja_bin "${CMAKE_MAKE_PROGRAM}" DIRECTORY)
        list(APPEND trace_protected_roots "${trace_ninja_bin}")
    endif()
    # Validate before project()/compiler detection can create generated native files.
    function(lottie_trace_external_path candidate)
        file(TO_CMAKE_PATH "${candidate}" path)
        if(NOT IS_ABSOLUTE "${path}" OR path MATCHES "(^|/)\\.\\.(/|$)" OR
            path MATCHES "^//" OR path MATCHES "[<>|?*]" OR path MATCHES ":.*:")
            message(FATAL_ERROR "Unsafe WebGL trace path: ${candidate}")
        endif()
        string(REGEX REPLACE "/+$" "" path "${path}")
        get_filename_component(parent "${path}" DIRECTORY)
        if(parent STREQUAL path OR parent STREQUAL "")
            message(FATAL_ERROR "WebGL trace cannot target a filesystem root")
        endif()
        set(check "${path}")
        while(NOT check STREQUAL "")
            if(IS_SYMLINK "${check}" OR EXISTS "${check}/.git")
                message(FATAL_ERROR "WebGL trace path crosses a link or Git worktree: ${check}")
            endif()
            if(EXISTS "${check}")
                file(REAL_PATH "${check}" resolved)
                string(TOLOWER "${check}" expected_lower)
                string(TOLOWER "${resolved}" resolved_lower)
                if(NOT expected_lower STREQUAL resolved_lower)
                    message(FATAL_ERROR "WebGL trace path resolves through a redirected ancestor: ${check}")
                endif()
            endif()
            get_filename_component(next "${check}" DIRECTORY)
            if(next STREQUAL check)
                break()
            endif()
            set(check "${next}")
        endwhile()
        string(TOLOWER "${path}" lower)
        foreach(protected IN LISTS trace_protected_roots)
            file(REAL_PATH "${protected}" protected_real)
            string(TOLOWER "${protected_real}/" protected_lower)
            string(FIND "${lower}/" "${protected_lower}" inside_input)
            string(FIND "${protected_lower}" "${lower}/" contains_input)
            if(inside_input EQUAL 0 OR contains_input EQUAL 0)
                message(FATAL_ERROR "WebGL trace path overlaps a source/toolchain/tool input: ${path}")
            endif()
        endforeach()
        if(lower MATCHES "/(assets|library|packages|packagecache|dependency|sysroot|cache)(/|$)")
            message(FATAL_ERROR "WebGL trace output overlaps a protected input directory")
        endif()
    endfunction()
    lottie_trace_external_path("${CMAKE_BINARY_DIR}")
    lottie_trace_external_path("${LOTTIE_WEBGL_TRACE_OUTPUT_ROOT}")
    if(EXISTS "${LOTTIE_WEBGL_TRACE_OUTPUT_ROOT}")
        message(FATAL_ERROR "WebGL trace output must be a fresh absent directory")
    endif()
    file(TO_CMAKE_PATH "${LOTTIE_WEBGL_TRACE_OUTPUT_ROOT}" trace_output)
    file(TO_CMAKE_PATH "${CMAKE_BINARY_DIR}" trace_build)
    string(TOLOWER "${trace_output}/" trace_output_lower)
    string(TOLOWER "${trace_build}/" trace_build_lower)
    string(FIND "${trace_output_lower}" "${trace_build_lower}" output_in_build)
    string(FIND "${trace_build_lower}" "${trace_output_lower}" build_in_output)
    if(output_in_build EQUAL 0 OR build_in_output EQUAL 0)
        message(FATAL_ERROR "WebGL trace build/output directories must not overlap")
    endif()
elseif(NOT LOTTIE_WEBGL_TRACE_OUTPUT_ROOT STREQUAL "")
    message(FATAL_ERROR "LOTTIE_WEBGL_TRACE_OUTPUT_ROOT requires trace ON")
endif()
project(LottiePlugin)

include(CTest)

option(RLOTTIE_ANDROID_PAGE_SIZE_16K "Build Android libraries with 16KB page size" OFF)
option(RLOTTIE_REQUIRE_NATIVE_VULKAN "Fail configuration when native Vulkan upload cannot be built" ${WIN32})
option(RLOTTIE_WEBGL_WASM_EXCEPTIONS "Build WebGL archives for Unity versions using Wasm exceptions" OFF)
set(UNITY_PLUGINAPI_DIR "${CMAKE_CURRENT_LIST_DIR}/../../dependency/unity-pluginapi" CACHE PATH "Path to Unity PluginAPI headers")

set(BUILD_SHARED_LIBS FALSE)
set(CMAKE_POSITION_INDEPENDENT_CODE TRUE)

# iOS (device and simulator, via RLOTTIE_IOS=1) ships static archives that are
# linked by the *consumer's* Xcode toolchain, not by us. LTO/IPO makes the
# compiler emit LLVM bitcode objects into those archives instead of machine
# code; a consumer's linker (using a different, possibly older, LLVM) then
# fails with "could not parse bitcode object file ... Invalid bitcode
# version". Every other platform here either produces a dynamic library
# (Windows/Linux/Android) or an app-local bundle (macOS) whose final link
# happens with the same toolchain that compiled it, so LTO is safe there.
# Keep IPO/LTO disabled specifically for the iOS static-lib build.
include(CheckIPOSupported)
check_ipo_supported(RESULT ipo_supported OUTPUT ipo_error)
if (ipo_supported AND NOT RLOTTIE_IOS)
    set(CMAKE_INTERPROCEDURAL_OPTIMIZATION_RELEASE ON)
endif()

if (MSVC)
    # Visual Studio can compile translation units concurrently within one
    # target; serialize writes to each target's program database.
    add_compile_options(/FS)
    add_compile_options("$<$<CONFIG:Release>:/O2>" "$<$<CONFIG:Release>:/GL>")
    add_link_options("$<$<CONFIG:Release>:/LTCG>")
else()
    add_compile_options("$<$<CONFIG:Release>:-O3>")
    if (NOT RLOTTIE_IOS)
        add_link_options("$<$<CONFIG:Release>:-flto>")
    endif()
endif()

set (RLOTTIE_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/../../dependency/rlottie)
set (PIXMAN_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/../../dependency/pixman)

# Disable the LOTTIE_MODULE in rlottie
set(LOTTIE_MODULE OFF CACHE BOOL "Disable LOTTIE MODULE SUPPORT" FORCE)

# Disable threading for WebGL (Emscripten doesn't support threading by default)
if (RLOTTIE_WEB_ASSEMBLY)
    set(LOTTIE_THREAD OFF CACHE BOOL "Disable LOTTIE THREAD SUPPORT for WebGL" FORCE)
endif()

if (RLOTTIE_IOS)
    set(CMAKE_XCODE_ATTRIBUTE_ENABLE_BITCODE NO)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${PROJECT_BINARY_DIR}/LottiePluginBuild)
elseif (RLOTTIE_OSX)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${PROJECT_BINARY_DIR}/LottiePluginBuild)
endif()

if (WIN32)
    set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /MT")
    set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} /MTd")
endif()

add_subdirectory(${RLOTTIE_ROOT} ${CMAKE_CURRENT_BINARY_DIR}/rlottie_build)
# Make rlottie compile as C++17 (doesn't affect your plugin target)
target_compile_features(rlottie PUBLIC cxx_std_17)
# Enable logging support only for non-WebGL builds (NanoLogger uses threads which are not available in WebGL)
if (NOT RLOTTIE_WEB_ASSEMBLY)
    target_compile_definitions(rlottie PRIVATE LOTTIE_LOGGING_SUPPORT)
endif()
if (MSVC)
    target_compile_definitions(rlottie PRIVATE _CRT_SECURE_NO_WARNINGS _CRT_NONSTDC_NO_DEPRECATE)
endif()

include_directories ($<BUILD_INTERFACE:${RLOTTIE_ROOT}/inc> ${RLOTTIE_ROOT}/src/vector/ ${RLOTTIE_ROOT}/vs2019/)
message("Lottie root directory: ${RLOTTIE_ROOT}")

set (RLOTTIE_PLUGIN_LIBRARY_SOURCES
    ../../src/ImageBufferUtils.cpp
    ../../src/LottieLogger.cpp
    ../../src/LottiePluginApi.cpp
    ../../src/RendererCommon.cpp
    ../../src/InstanceRegistry.cpp
    ../../src/UploadQueue.cpp
    ../../src/UploadPipeline.cpp
    ../../src/PixelFormatUtils.cpp
    ../../src/TextureBackend.cpp
    ../../src/VulkanBackend.cpp
    ../../src/UnityIntegration.cpp
    ../../src/UnityExports.cpp)

if (RLOTTIE_WEB_ASSEMBLY)
    list(APPEND RLOTTIE_PLUGIN_LIBRARY_SOURCES
        ../../src/WebGLBackend.cpp)
endif()

# Platform-specific backend sources
if (WIN32)
    list(APPEND RLOTTIE_PLUGIN_LIBRARY_SOURCES
        ../../src/D3D12Backend_win.cpp
        ../../src/D3D11Backend_win.cpp
        ../../src/OpenGLBackend.cpp)
elseif(RLOTTIE_OSX OR RLOTTIE_IOS)
    list(APPEND RLOTTIE_PLUGIN_LIBRARY_SOURCES
        ../../src/MetalBackend_apple.mm)
elseif(ANDROID)
    list(APPEND RLOTTIE_PLUGIN_LIBRARY_SOURCES
        ../../src/OpenGLBackend.cpp)
elseif(RLOTTIE_LINUX)
    list(APPEND RLOTTIE_PLUGIN_LIBRARY_SOURCES
        ../../src/OpenGLBackend.cpp)
endif()

if (RLOTTIE_OSX OR RLOTTIE_IOS)
    # Set Objective-C++ for files that need Metal
    set_source_files_properties(
        ../../src/MetalBackend_apple.mm
        ../../src/UnityIntegration.cpp
        ../../src/UnityExports.cpp
        ../../src/LottiePluginApi.cpp
        PROPERTIES LANGUAGE OBJCXX)
endif()

message("CMAKE_SYSTEM_PROCESSOR: ${CMAKE_SYSTEM_PROCESSOR}")

if (RLOTTIE_IOS OR RLOTTIE_WEB_ASSEMBLY)
  add_library (LottiePlugin STATIC ${RLOTTIE_PLUGIN_LIBRARY_SOURCES})
else()
  add_library (LottiePlugin SHARED ${RLOTTIE_PLUGIN_LIBRARY_SOURCES})
endif()

target_compile_definitions(LottiePlugin PRIVATE LOTTIE_PLUGIN_EXPORTS)
if(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    target_compile_definitions(LottiePlugin PRIVATE LOTTIE_WEBGL_NATIVE_STATE_TRACE=1)
endif()

if (RLOTTIE_WEB_ASSEMBLY AND RLOTTIE_WEBGL_WASM_EXCEPTIONS)
    # Unity 6000.5+ links WebGL players with Emscripten's Wasm exception and
    # longjmp ABI. setjmp/longjmp calls in rlottie's rasterizer must be lowered
    # with the same model or the final Unity link references emscripten_longjmp
    # from the obsolete JavaScript exception ABI.
    target_compile_options(rlottie PRIVATE
        -fwasm-exceptions
        "-sSUPPORT_LONGJMP=wasm"
        "-sWASM_LEGACY_EXCEPTIONS=1")
    target_compile_options(LottiePlugin PRIVATE
        -fwasm-exceptions
        "-sSUPPORT_LONGJMP=wasm"
        "-sWASM_LEGACY_EXCEPTIONS=1")
endif()

# Enable logging support only for non-WebGL builds
if (NOT RLOTTIE_WEB_ASSEMBLY)
    target_compile_definitions(LottiePlugin PRIVATE LOTTIE_LOGGING_SUPPORT)
endif()

# Add WebGL to platforms that need pixman-cpp shim:
if (RLOTTIE_IOS OR ANDROID OR RLOTTIE_OSX OR RLOTTIE_WEB_ASSEMBLY)
  message("Compile cpp source for iOS/Android/macOS/WebAssembly")
  target_sources(rlottie PRIVATE ${PIXMAN_ROOT}/pixman-cpp-standard.cpp)
endif()

# Build static lib for WebAssembly
if (RLOTTIE_WEB_ASSEMBLY)
  set(BUILD_SHARED_LIBS OFF)
endif()

if (WIN32)
  set(RLOTTIE_LIBRARY_NAME rlottie.lib)
elseif(RLOTTIE_OSX)
  set(RLOTTIE_LIBRARY_NAME Release/rlottie.a)
  add_compile_definitions(RLOTTIE_OSX)
  #set_target_properties(LottiePlugin PROPERTIES BUNDLE TRUE)
  #set_target_properties(LottiePlugin PROPERTIES SUFFIX ".bundle")
else()
  set(RLOTTIE_LIBRARY_NAME librlottie.a)
endif()

#declare common target compilation options
if(MSVC)
    target_compile_options(rlottie
                        PRIVATE
                            /std:c++17
                            /EHsc # enable exceptions
                            /Zc:__cplusplus # enable C++17 features
                            /permissive-) # standards compliant mode
    target_compile_options(LottiePlugin
                        PRIVATE
                            /std:c++17
                            /EHsc # enable exceptions
                            /Zc:__cplusplus # enable C++17 features
                            /permissive- # standards compliant mode
                            /W3 # warning level
                        )
else()
    target_compile_options(LottiePlugin
                        PRIVATE
                            -std=c++17
                            -Wall
                            -fvisibility=hidden
                            -Wnon-virtual-dtor
                            -Woverloaded-virtual
                            -Wno-unused-parameter
                        )
    if (RLOTTIE_WEB_ASSEMBLY OR ANDROID)
        target_compile_options(LottiePlugin
                            PRIVATE
                                -fno-exceptions
                                -fno-unwind-tables
                                -fno-asynchronous-unwind-tables
                                -fno-rtti)
    endif()
endif()

if (EXISTS "${UNITY_PLUGINAPI_DIR}/IUnityInterface.h" AND EXISTS "${UNITY_PLUGINAPI_DIR}/IUnityProfiler.h")
    target_include_directories(LottiePlugin PRIVATE "${UNITY_PLUGINAPI_DIR}")
else()
    message(FATAL_ERROR "No Unity headers found in '${UNITY_PLUGINAPI_DIR}'")
endif()

# IUnityGraphicsVulkan includes the platform Vulkan header. Android NDK
# toolchains provide it automatically; desktop builders can provide either a
# Vulkan SDK (VULKAN_SDK) or a system Vulkan-Headers installation. The plugin
# retains its managed Texture2D.Apply fallback when headers are unavailable.
if (ANDROID)
    target_compile_definitions(LottiePlugin PRIVATE LOTTIE_VULKAN_AVAILABLE=1)
else()
    find_path(RLOTTIE_VULKAN_INCLUDE_DIR
        NAMES vulkan/vulkan.h
        HINTS
            "$ENV{VULKAN_SDK}"
            "${CMAKE_CURRENT_LIST_DIR}/../../dependency/Vulkan-Headers"
        PATH_SUFFIXES Include include)
    if (RLOTTIE_VULKAN_INCLUDE_DIR)
        target_include_directories(LottiePlugin PRIVATE "${RLOTTIE_VULKAN_INCLUDE_DIR}")
        target_compile_definitions(LottiePlugin PRIVATE LOTTIE_VULKAN_AVAILABLE=1)
        message(STATUS "Vulkan native texture upload enabled (${RLOTTIE_VULKAN_INCLUDE_DIR})")
    else()
        if (RLOTTIE_REQUIRE_NATIVE_VULKAN)
            message(FATAL_ERROR
                "Vulkan headers not found; native Vulkan texture upload is required for this build. "
                "Install the Vulkan SDK, provide RLOTTIE_VULKAN_INCLUDE_DIR, or explicitly set "
                "RLOTTIE_REQUIRE_NATIVE_VULKAN=OFF to build with the managed upload fallback.")
        else()
            message(WARNING "Vulkan headers not found; native Vulkan texture upload disabled")
        endif()
    endif()
endif()

add_dependencies(LottiePlugin rlottie)
target_link_libraries(LottiePlugin rlottie)

if (BUILD_TESTING AND NOT CMAKE_CROSSCOMPILING)
    add_executable(LottiePluginNativeTests
        ../../tests/native/NativeAbiTests.cpp
        ../../src/ImageBufferUtils.cpp)
    target_compile_features(LottiePluginNativeTests PRIVATE cxx_std_17)
    target_include_directories(LottiePluginNativeTests PRIVATE
        ../../src
        "${RLOTTIE_ROOT}/inc"
        "${UNITY_PLUGINAPI_DIR}")
    if (ANDROID OR RLOTTIE_VULKAN_INCLUDE_DIR)
        target_compile_definitions(LottiePluginNativeTests PRIVATE LOTTIE_EXPECT_NATIVE_VULKAN_BACKEND=1)
    endif()
    target_link_libraries(LottiePluginNativeTests PRIVATE LottiePlugin)
    add_test(NAME LottiePlugin.NativeAbi COMMAND LottiePluginNativeTests)
    find_package(Threads REQUIRED)
    add_executable(LottieUploadQueueTests
        ../../tests/native/UploadQueueTests.cpp
        ../../src/UploadQueue.cpp)
    target_compile_features(LottieUploadQueueTests PRIVATE cxx_std_17)
    target_compile_definitions(LottieUploadQueueTests PRIVATE LOTTIE_PLUGIN_EXPORTS)
    target_link_libraries(LottieUploadQueueTests PRIVATE Threads::Threads)
    target_include_directories(LottieUploadQueueTests PRIVATE
        ../../src "${RLOTTIE_ROOT}/inc" "${UNITY_PLUGINAPI_DIR}")
    add_test(NAME LottiePlugin.UploadQueue COMMAND LottieUploadQueueTests)
endif()

# rlottie is built as a static library here, but its private ELF version-script
# linker item is propagated to consumers by CMake. That script only permits
# lottie_* and rlottie::* symbols, which hides UnityPluginLoad/Unload from the
# final Android and Linux plugin even though they have default visibility.
# Keep rlottie's normal link dependencies while dropping that archive-only
# export restriction from LottiePlugin's link interface.
if (NOT APPLE AND NOT WIN32)
    get_target_property(RLOTTIE_INTERFACE_LINK_LIBRARIES rlottie INTERFACE_LINK_LIBRARIES)
    if (RLOTTIE_INTERFACE_LINK_LIBRARIES)
        list(FILTER RLOTTIE_INTERFACE_LINK_LIBRARIES EXCLUDE REGEX "--version-script=")
        set_property(TARGET rlottie PROPERTY INTERFACE_LINK_LIBRARIES "${RLOTTIE_INTERFACE_LINK_LIBRARIES}")
    endif()
endif()

if (ipo_supported AND NOT RLOTTIE_IOS)
    set_property(TARGET rlottie PROPERTY INTERPROCEDURAL_OPTIMIZATION_RELEASE TRUE)
    set_property(TARGET LottiePlugin PROPERTY INTERPROCEDURAL_OPTIMIZATION_RELEASE TRUE)
endif()

if (WIN32)
    target_link_libraries(LottiePlugin d3d12 dxgi opengl32)
elseif(RLOTTIE_OSX OR RLOTTIE_IOS)
    find_library(METAL_FRAMEWORK Metal)
    find_library(FOUNDATION_FRAMEWORK Foundation)
    target_link_libraries(LottiePlugin ${METAL_FRAMEWORK} ${FOUNDATION_FRAMEWORK})
elseif(ANDROID)
    find_library(ANDROID_LOG log)
    find_library(ANDROID_LIB android)
    find_library(GLESV3_LIB GLESv3)
    if (NOT GLESV3_LIB)
        find_library(GLESV2_LIB GLESv2)
        set(GLES_LIBRARY ${GLESV2_LIB})
    else()
        set(GLES_LIBRARY ${GLESV3_LIB})
    endif()
    target_link_libraries(LottiePlugin ${GLES_LIBRARY} ${ANDROID_LOG} ${ANDROID_LIB})
elseif(RLOTTIE_LINUX)
    # Unity's Linux CI editor image provides the legacy libGL loader, but not
    # the separate GLVND libOpenGL loader used by newer Linux distributions.
    set(OpenGL_GL_PREFERENCE LEGACY)
    find_package(OpenGL REQUIRED)
    target_link_libraries(LottiePlugin OpenGL::GL)
endif()

if(ANDROID AND RLOTTIE_ANDROID_PAGE_SIZE_16K)
  target_link_options(LottiePlugin PRIVATE "-Wl,-z,max-page-size=16384" "-Wl,-z,common-page-size=16384")
  target_link_options(rlottie PRIVATE "-Wl,-z,max-page-size=16384" "-Wl,-z,common-page-size=16384")
endif()
message(${CMAKE_CURRENT_BINARY_DIR}/rlottie_build/${RLOTTIE_LIBRARY_NAME})

#Adding simple test program:
#add_executable(LottiePluginTest ../../src/PluginTest.c)
#target_link_libraries(LottiePluginTest LottiePlugin)
# if (RLOTTIE_IOS)
#   set_xcode_property(LottiePluginTest PRODUCT_BUNDLE_IDENTIFIER "com.soulside.app" All)
# endif()

if (WIN32)
if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set (RLOTTIE_PLUGIN_TARGET_ARCHITECTURE x86_64)
elseif(CMAKE_SIZEOF_VOID_P EQUAL 4)
    set (RLOTTIE_PLUGIN_TARGET_ARCHITECTURE x86)
endif()
elseif(ANDROID)
    set (RLOTTIE_PLUGIN_TARGET_ARCHITECTURE ${CMAKE_ANDROID_ARCH_ABI})
else()
    set (RLOTTIE_PLUGIN_TARGET_ARCHITECTURE ${CMAKE_SYSTEM_PROCESSOR})
endif()
if(LOTTIE_WEBGL_NATIVE_STATE_TRACE)
    # Never copy a freshly rebuilt rlottie under the immutable baseline name.
    # The owner runner verifies/copies the approved baseline archive separately.
    add_custom_command(TARGET LottiePlugin POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E make_directory "${LOTTIE_WEBGL_TRACE_OUTPUT_ROOT}/WasmExceptions"
        COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:LottiePlugin>"
            "${LOTTIE_WEBGL_TRACE_OUTPUT_ROOT}/WasmExceptions/libLottiePlugin.trace.a"
        VERBATIM)
else()
add_custom_command(
    TARGET LottiePlugin POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory
    "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}")

add_custom_command(
    TARGET LottiePlugin POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:LottiePlugin>"
        "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}/$<TARGET_FILE_NAME:LottiePlugin>"
    COMMENT "Copied final library to out directory")

add_custom_command(
    TARGET LottiePlugin POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:rlottie>"
        "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}/$<TARGET_FILE_NAME:rlottie>"
    COMMENT "Copied final library to out directory")

if (RLOTTIE_WEB_ASSEMBLY)
  add_custom_command(
      TARGET LottiePlugin POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E make_directory
          "${CMAKE_CURRENT_SOURCE_DIR}/../../out/Plugins/WebGL"
  )
  add_custom_command(
      TARGET LottiePlugin POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different
          "$<TARGET_FILE:LottiePlugin>"
          "${CMAKE_CURRENT_SOURCE_DIR}/../../out/Plugins/WebGL/libLottiePlugin.a"
      COMMENT "Copied libLottiePlugin.a to out/Plugins/WebGL"
  )
  add_custom_command(
      TARGET LottiePlugin POST_BUILD
      COMMAND ${CMAKE_COMMAND} -E copy_if_different
          "$<TARGET_FILE:rlottie>"
          "${CMAKE_CURRENT_SOURCE_DIR}/../../out/Plugins/WebGL/librlottie.a"
      COMMENT "Copied librlottie.a to out/Plugins/WebGL"
  )
endif()

endif() # trace-only external output versus unchanged production destinations

# Uncomment following lines if you want to copy the pdb files for Debug builds:
#if (WIN32)
# add_custom_command(
#     TARGET LottiePlugin POST_BUILD
#     COMMAND ${CMAKE_COMMAND} -E copy_if_different
#         "$<TARGET_PDB_FILE:LottiePlugin>"
#         "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}/$<TARGET_PDB_FILE_NAME:LottiePlugin>")
# endif()
# uncomment end
if (ANDROID)
  add_custom_command(
      TARGET LottiePlugin POST_BUILD
      COMMAND "${CMAKE_STRIP}" -g -S -d --strip-debug
              "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}/$<TARGET_FILE_NAME:LottiePlugin>"
      COMMENT "Strip debug symbols done on final binary.")
  add_custom_command(
      TARGET LottiePlugin POST_BUILD
      COMMAND "${CMAKE_STRIP}" -g -S -d --strip-debug
              "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}/$<TARGET_FILE_NAME:rlottie>"
      COMMENT "Strip debug symbols done on final binary.")
endif()

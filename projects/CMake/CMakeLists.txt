# CMakeList.txt : CMake project for LottiePlugin, include source and define
# project specific logic here.
#
cmake_minimum_required (VERSION 3.6)

#set (MSVC_RUNTIME_LIBRARY MultiThreadedDebugDLL)
project(LottiePlugin)

set (RLOTTIE_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/../../dependency/rlottie)
set (PIXMAN_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/../../dependency/pixman)
set(CMAKE_ASM_FLAGS "${CFLAGS} -x assembler-with-cpp")

if (RLOTTIE_IOS)
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${PROJECT_BINARY_DIR}/LottiePluginBuild)
elseif (RLOTTIE_OSX)
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${PROJECT_BINARY_DIR}/LottiePluginBuild)
endif()

if (WIN32)
    set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /MT")
    set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} /MTd")
endif()

add_subdirectory(${RLOTTIE_ROOT} ${CMAKE_CURRENT_BINARY_DIR}/rlottie_build)

include_directories ($<BUILD_INTERFACE:${RLOTTIE_ROOT}/inc>)

set (RLOTTIE_PLUGIN_LIBRARY_SOURCES 
    ../../src/LottiePlugin.cpp)

message("CMAKE_SYSTEM_PROCESSOR: ${CMAKE_SYSTEM_PROCESSOR}")

# if ("${CMAKE_SYSTEM_PROCESSOR}" STREQUAL "armv7-a" OR
#     "${CMAKE_SYSTEM_PROCESSOR}" STREQUAL "aarch64")
#     message("Building for arm architecture")
#     set(CMAKE_ASM_COMPILE_OBJECT "<CMAKE_ASM_COMPILER> <FLAGS> <DEFINES> -o <OBJECT> <SOURCE>")
#     set(CMAKE_ASM_FLAGS "${CMAKE_CXX_FLAGS} -x assembler-with-cpp")
#     set(ARM_ASM_SOURCE ${RLOTTIE_ROOT}/src/vector/pixman/pixman-arm-neon-asm.S)
#     set(RLOTTIE_PLUGIN_LIBRARY_SOURCES ${RLOTTIE_PLUGIN_LIBRARY_SOURCES} ${ARM_ASM_SOURCE})
# endif()

if (RLOTTIE_IOS)
  set(BUILD_SHARED_LIBS OFF)
  add_library (LottiePlugin STATIC ${RLOTTIE_PLUGIN_LIBRARY_SOURCES})
else()
  add_library (LottiePlugin SHARED ${RLOTTIE_PLUGIN_LIBRARY_SOURCES})
endif()

if ("${CMAKE_SYSTEM_PROCESSOR}" STREQUAL "armv7-a" OR "${CMAKE_SYSTEM_PROCESSOR}" STREQUAL "aarch64")
    enable_language(ASM)
endif()

if ("${CMAKE_SYSTEM_PROCESSOR}" STREQUAL "armv7-a")
    target_compile_options(rlottie PUBLIC -fno-integrated-as)
    target_compile_definitions(rlottie PUBLIC USE_ARM_NEON)
    target_sources(rlottie PRIVATE ${PIXMAN_ROOT}/pixman-arm-neon-asm.S)
elseif("${CMAKE_SYSTEM_PROCESSOR}" STREQUAL "aarch64")
    target_compile_options(rlottie PUBLIC -fno-integrated-as)
    target_compile_definitions(rlottie PUBLIC USE_ARM_NEON __ARM64_NEON__)
    target_sources(rlottie PRIVATE ${PIXMAN_ROOT}/pixman-arma64-neon-asm.S)
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
    target_compile_options(LottiePlugin
                        PUBLIC
                        PRIVATE
                            /std:c++14
                            /EHs-c- # disable exceptions
                            /GR- # disable RTTI
                            /W3
                        )
else()
    target_compile_options(LottiePlugin
                        PUBLIC
                        PRIVATE
                            -std=c++14
                            -fno-exceptions
                            -fno-unwind-tables
                            -fno-asynchronous-unwind-tables
                            -fno-rtti
                            -Wall
                            -fvisibility=hidden
                            -Wnon-virtual-dtor
                            -Woverloaded-virtual
                            -Wno-unused-parameter
                        )
endif()

add_dependencies(LottiePlugin rlottie)
#target_link_libraries(LottiePlugin ${CMAKE_CURRENT_BINARY_DIR}/rlottie_build/${RLOTTIE_LIBRARY_NAME})
target_link_libraries(LottiePlugin rlottie)
message(${CMAKE_CURRENT_BINARY_DIR}/rlottie_build/${RLOTTIE_LIBRARY_NAME})

#Adding simple test program:
#add_executable(LottiePluginTest ../../src/PluginTest.c)
#target_link_libraries(LottiePluginTest LottiePlugin)
if (RLOTTIE_IOS)
  set_xcode_property(LottiePluginTest PRODUCT_BUNDLE_IDENTIFIER "com.soulside.app" All)
endif()

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
    COMMAND "${ANDROID_TOOLCHAIN_PREFIX}strip" -g -S -d --strip-debug --verbose
            "${CMAKE_CURRENT_SOURCE_DIR}/../../out/$<CONFIG>/Plugins/$<PLATFORM_ID>/${RLOTTIE_PLUGIN_TARGET_ARCHITECTURE}/$<TARGET_FILE_NAME:LottiePlugin>"
    COMMENT "Strip debug symbols done on final binary.")
endif()

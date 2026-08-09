cmake_minimum_required(VERSION 3.22.1)

project(ai-chat VERSION 1.0.0 LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED true)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED true)

# Android 15 and newer can use devices with 16 KB memory pages. Apply the
# flexible-page-size linker setting to every shared library produced here,
# including the llama.cpp libraries added below.
add_link_options("-Wl,-z,max-page-size=16384")

if(NOT DEFINED ANDROID_ABI OR (NOT ANDROID_ABI STREQUAL "arm64-v8a" AND NOT ANDROID_ABI STREQUAL "x86_64"))
    message(FATAL_ERROR "This project packages the native runtime for arm64-v8a phones and x86_64 emulators.")
endif()

set(LLAMA_SRC "${CMAKE_CURRENT_LIST_DIR}/../build/llama-src" CACHE PATH
    "Path to the llama.cpp source checkout")
if(NOT EXISTS "${LLAMA_SRC}/CMakeLists.txt")
    message(FATAL_ERROR "The llama.cpp source checkout is missing.")
endif()

set(SHERPA_ANDROID_ROOT "${CMAKE_CURRENT_LIST_DIR}/../build/sherpa-android" CACHE PATH
    "Path to the prepared Sherpa-ONNX Android runtime")
set(SHERPA_ANDROID_LIB_DIR "${SHERPA_ANDROID_ROOT}/jniLibs/${ANDROID_ABI}")
set(SHERPA_ANDROID_INCLUDE_DIR "${SHERPA_ANDROID_ROOT}/include")
if(NOT EXISTS "${SHERPA_ANDROID_LIB_DIR}/libsherpa-onnx-c-api.so")
    message(FATAL_ERROR "The bundled Sherpa-ONNX Android runtime is missing.")
endif()

if(ANDROID_ABI STREQUAL "arm64-v8a")
    set(BUILD_SHARED_LIBS ON CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_COMMON ON CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
    set(LLAMA_BUILD_SERVER OFF CACHE BOOL "" FORCE)
    set(LLAMA_OPENSSL OFF CACHE BOOL "" FORCE)
    set(GGML_NATIVE OFF CACHE BOOL "" FORCE)
    set(GGML_BACKEND_DL ON CACHE BOOL "" FORCE)
    set(GGML_CPU_ALL_VARIANTS ON CACHE BOOL "" FORCE)
    set(GGML_LLAMAFILE OFF CACHE BOOL "" FORCE)
    set(GGML_SYSTEM_ARCH "ARM" CACHE STRING "" FORCE)
    set(GGML_CPU_KLEIDIAI ON CACHE BOOL "" FORCE)
    set(GGML_OPENMP ON CACHE BOOL "" FORCE)

    add_subdirectory("${LLAMA_SRC}" build-llama)

    add_library(ai-chat SHARED ai_chat.cpp)

    target_compile_definitions(ai-chat PRIVATE
        GGML_SYSTEM_ARCH=ARM
        GGML_CPU_KLEIDIAI=1
        GGML_OPENMP=1)

    target_include_directories(ai-chat PRIVATE
        "${LLAMA_SRC}"
        "${LLAMA_SRC}/common"
        "${LLAMA_SRC}/include"
        "${LLAMA_SRC}/ggml/include"
        "${LLAMA_SRC}/ggml/src")

    target_link_libraries(ai-chat PRIVATE
        llama
        llama-common
        android
        log)
endif()

add_library(sherpa-onnx-c-api SHARED IMPORTED)
set_target_properties(sherpa-onnx-c-api PROPERTIES
    IMPORTED_LOCATION "${SHERPA_ANDROID_LIB_DIR}/libsherpa-onnx-c-api.so")

add_library(tour-voice SHARED tour_voice.cpp)
target_include_directories(tour-voice PRIVATE "${SHERPA_ANDROID_INCLUDE_DIR}")
target_link_libraries(tour-voice PRIVATE
    sherpa-onnx-c-api
    android
    log)

cmake_minimum_required(VERSION 3.16)
project(apac_task_runner VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ─── library ─────────────────────────────────────────────────────────────

add_library(task_runner
    src/task_runner.cpp
)

target_include_directories(task_runner PUBLIC include)

target_compile_options(task_runner PRIVATE
    $<$<CXX_COMPILER_ID:MSVC>:/W4 /utf-8>
    $<$<CXX_COMPILER_ID:GNU>:-Wall -Wextra -Wpedantic>
    $<$<CXX_COMPILER_ID:Clang>:-Wall -Wextra -Wpedantic>
)

# ─── examples ────────────────────────────────────────────────────────────

add_executable(example examples/main.cpp)
target_link_libraries(example PRIVATE task_runner)

# ─── tests ───────────────────────────────────────────────────────────────

enable_testing()

add_executable(test_task_runner tests/test_task_runner.cpp)
target_link_libraries(test_task_runner PRIVATE task_runner)
add_test(NAME TaskRunner COMMAND test_task_runner)

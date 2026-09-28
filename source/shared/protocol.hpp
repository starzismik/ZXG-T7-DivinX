#pragma once
#include <cstdint>
namespace zxg {
inline constexpr std::uint32_t kProtocolVersion = 3;
inline constexpr wchar_t kPipePrefix[] = LR"(\\.\pipe\zxg_t7_guard_)";
inline constexpr char kHelloRequest[] = "HELLO|3\n";
inline constexpr char kVersion[] = "0.5.0";
}





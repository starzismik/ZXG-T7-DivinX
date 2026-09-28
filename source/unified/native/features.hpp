#pragma once
#include <windows.h>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
namespace zxg {
struct State {
 std::mutex mutex;
 std::string error, originalName, currentName;
 std::atomic<bool> engineReady{false}, menuReady{false}, menuOpen{false}, privateNetwork{false};
 std::atomic<unsigned> hookCount{0}, menuKey{VK_F5};
 std::atomic<bool> captureRequested{false};
 std::atomic<std::uint64_t> frames{0}, drawnFrames{0}, dropped{0};
};
State& state();
bool readable(const void*, std::size_t);
bool executable(const void*);
std::uintptr_t base();
void initialize_features();
void install_overlay();
std::string set_name(const std::string&);
std::string restore_name();
std::string set_password(const std::string&);
std::string command(const std::string&);
std::string status();
std::string hex(const std::string&);
bool unhex(const std::string&, std::string&);
}


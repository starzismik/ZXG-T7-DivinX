#include <windows.h>
#include <sddl.h>
#include <array>
#include <cstring>
#include <string>
#include <vector>
#include "shared/protocol.hpp"
#include "features.hpp"

namespace {
// Every pending operation is cancelled and joined before its stack buffer/event
// is released. A silent client cannot occupy the server indefinitely.
bool Finish(HANDLE pipe, OVERLAPPED& op, BOOL immediate, DWORD& count) {
  if (!immediate) {
    if (GetLastError() != ERROR_IO_PENDING) return false;
    if (WaitForSingleObject(op.hEvent, 2000) != WAIT_OBJECT_0) {
      CancelIoEx(pipe, &op);
      GetOverlappedResult(pipe, &op, &count, TRUE);
      return false;
    }
  }
  return GetOverlappedResult(pipe, &op, &count, FALSE) != FALSE;
}
bool Transfer(HANDLE pipe, void* data, DWORD size, bool writing, DWORD& count) {
  OVERLAPPED op{};
  op.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (!op.hEvent) return false;
  const BOOL started = writing ? WriteFile(pipe, data, size, nullptr, &op)
                               : ReadFile(pipe, data, size, nullptr, &op);
  const bool ok = Finish(pipe, op, started, count);
  CloseHandle(op.hEvent);
  return ok;
}
PSECURITY_DESCRIPTOR UserSecurity() {
  HANDLE token{};
  if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return nullptr;
  DWORD size{};
  GetTokenInformation(token, TokenUser, nullptr, 0, &size);
  std::vector<BYTE> bytes(size);
  if (!GetTokenInformation(token, TokenUser, bytes.data(), size, &size)) {
    CloseHandle(token); return nullptr;
  }
  CloseHandle(token);
  LPWSTR sid{};
  if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(bytes.data())->User.Sid, &sid)) return nullptr;
  const std::wstring sddl = L"D:P(A;;GA;;;SY)(A;;GA;;;" + std::wstring(sid) + L")";
  LocalFree(sid);
  PSECURITY_DESCRIPTOR descriptor{};
  if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) return nullptr;
  return descriptor;
}
DWORD WINAPI PipeThread(void* module) { 
  // The diagnostic runtime stays resident until the host exits. No unsafe hot unload.
  HMODULE pinned{};
  if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
      reinterpret_cast<LPCWSTR>(module), &pinned)) return 1;
  if (HANDLE features = CreateThread(nullptr,0,[](void*) -> DWORD {
    try { zxg::initialize_features(); }
    catch (const std::exception& e) { std::lock_guard lock(zxg::state().mutex); zxg::state().error = e.what(); }
    catch (...) { std::lock_guard lock(zxg::state().mutex); zxg::state().error = "Erreur initialisation moteur"; }
    return 0;
  },nullptr,0,nullptr)) CloseHandle(features);
  auto descriptor = UserSecurity();
  if (!descriptor) return 2;
  SECURITY_ATTRIBUTES sa{sizeof(sa), descriptor, FALSE};
  const auto name = std::wstring(zxg::kPipePrefix) + std::to_wstring(GetCurrentProcessId());
  HANDLE pipe = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE,
    PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
    1, 4096, 4096, 0, &sa);
  LocalFree(descriptor);
  if (pipe == INVALID_HANDLE_VALUE) return 3;
  for (;;) {
    OVERLAPPED op{};
    op.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!op.hEvent) { CloseHandle(pipe); return 4; }
    BOOL connected = ConnectNamedPipe(pipe, &op);
    DWORD count{};
    if (!connected) {
      const auto error = GetLastError();
      if (error == ERROR_PIPE_CONNECTED) connected = TRUE;
      else if (error == ERROR_IO_PENDING) {
        WaitForSingleObject(op.hEvent, INFINITE); // idle server, not a client operation
        connected = GetOverlappedResult(pipe, &op, &count, FALSE);
      }
    }
    CloseHandle(op.hEvent);
    if (connected) {
      std::array<char, 512> request{};
      if (Transfer(pipe, request.data(), static_cast<DWORD>(request.size()), false, count) &&
          count > 0 &&
          count <= request.size()) {
        std::string response = zxg::command(std::string(request.data(),count));
        SecureZeroMemory(request.data(),request.size());
        if (Transfer(pipe, const_cast<char*>(response.data()), static_cast<DWORD>(response.size()), true, count)) {
          // Wait for acknowledgement instead of an unbounded FlushFileBuffers.
          Transfer(pipe, request.data(), static_cast<DWORD>(request.size()), false, count);
        }
      }
    }
    DisconnectNamedPipe(pipe);
  }
}
}
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
  if (reason == DLL_PROCESS_ATTACH) { 
    DisableThreadLibraryCalls(module);
    HANDLE worker = CreateThread(nullptr, 0, PipeThread, module, 0, nullptr);
    if (!worker) return FALSE;
    CloseHandle(worker);
  }
  return TRUE;
}


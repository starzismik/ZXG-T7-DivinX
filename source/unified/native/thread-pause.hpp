#pragma once
#include <Windows.h>
#include <TlHelp32.h>
#include <vector>

// Allocate and open handles before pausing. No C++ allocation while peers stop.
class PausedPeers {
    struct Peer { HANDLE handle{}; bool stopped{}; };
    std::vector<Peer> peers;
public:
    bool prepare() {
        HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD,0);
        if(snapshot==INVALID_HANDLE_VALUE)return false;
        THREADENTRY32 item{sizeof(item)}; bool valid=true;
        for(BOOL more=Thread32First(snapshot,&item);more;more=Thread32Next(snapshot,&item)) {
            if(item.th32OwnerProcessID!=GetCurrentProcessId()||item.th32ThreadID==GetCurrentThreadId())continue;
            HANDLE h=OpenThread(THREAD_SUSPEND_RESUME|THREAD_GET_CONTEXT|THREAD_SET_CONTEXT|THREAD_QUERY_LIMITED_INFORMATION,FALSE,item.th32ThreadID);
            if(!h){valid=false;break;} peers.push_back({h,false});
        }
        CloseHandle(snapshot); return valid;
    }
    bool pause() {
        for(auto& peer:peers) {
            if(SuspendThread(peer.handle)==DWORD(-1)) { DWORD code{}; if(GetExitCodeThread(peer.handle,&code)&&code!=STILL_ACTIVE)continue;resume();return false; }
            peer.stopped=true;
        }
        return true;
    }
    template<class Patches> bool rewind_overwritten_instructions(const Patches& patches) {
        for(auto& peer:peers) {
            if(!peer.stopped)continue;
            CONTEXT context{};context.ContextFlags=CONTEXT_CONTROL;
            if(!GetThreadContext(peer.handle,&context))return false;
            for(const auto& patch:patches) {
                const auto start=reinterpret_cast<DWORD64>(patch.address);
                if(context.Rip>start && context.Rip<start+patch.size) {
                    context.Rip=start;
                    if(!SetThreadContext(peer.handle,&context))return false;
                    break;
                }
            }
        }
        return true;
    }
    void resume() { for(auto& peer:peers)if(peer.stopped){ResumeThread(peer.handle);peer.stopped=false;} }
    ~PausedPeers(){resume();for(auto& peer:peers)CloseHandle(peer.handle);}
};

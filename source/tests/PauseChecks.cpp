#include "thread-pause.hpp"
#include <atomic>
#include <thread>
#include <cstdio>
int main(){
 std::atomic<bool> stop=false;std::atomic<unsigned long> ticks=0;
 auto worker=[&]{while(!stop){++ticks;Sleep(1);}};
 std::thread first(worker),second(worker);
 while(ticks<10)Sleep(1);
 bool ok=true;
 {
  PausedPeers peers;
  ok=peers.prepare()&&peers.pause();
  auto before=ticks.load();Sleep(25);ok=ok&&ticks.load()==before;
  // Destructor must resume both peers, including on an early scope exit.
 }
 auto before=ticks.load();for(int i=0;i<100&&ticks==before;i++)Sleep(1);
 ok=ok&&ticks>before;stop=true;first.join();second.join();
 std::puts(ok?"PASS: peer threads paused consistently and resumed on scope exit":"FAIL: pause/resume");return ok?0:1;
}

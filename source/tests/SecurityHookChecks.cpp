#include "../unified/native/checked-hooks.hpp"
#include <array>
#include <cassert>
#include <iostream>
#include <set>
struct Backend {
 int createFailure=-1,queueFailure=-1; bool applyFailure=false;
 std::set<int> owned,enabled,queued;
 bool create(int h){if(h==createFailure)return false;owned.insert(h);return true;}
 bool queue(int h){if(h==queueFailure)return false;queued.insert(h);return true;}
 bool apply(){for(int h:queued){enabled.insert(h);if(applyFailure)return false;}return true;}
 void disable(int h){enabled.erase(h);queued.erase(h);}
 void remove(int h){owned.erase(h);queued.erase(h);}
};
int main(){
 const std::array<int,4> hooks{0,1,2,3};
 for(int i=0;i<4;++i){Backend b;b.createFailure=i;assert(!zxg::install_checked_hooks(hooks,b));assert(b.owned.empty()&&b.enabled.empty()&&b.queued.empty());}
 for(int i=0;i<4;++i){Backend b;b.queueFailure=i;assert(!zxg::install_checked_hooks(hooks,b));assert(b.owned.empty()&&b.enabled.empty()&&b.queued.empty());}
 Backend failed;failed.applyFailure=true;assert(!zxg::install_checked_hooks(hooks,failed));assert(failed.owned.empty()&&failed.enabled.empty()&&failed.queued.empty());
 Backend ok;assert(zxg::install_checked_hooks(hooks,ok));assert(ok.owned.size()==4&&ok.enabled.size()==4);
 std::cout<<"PASS: creation and queue failure at each position, partial activation rollback, successful installation\n";
}

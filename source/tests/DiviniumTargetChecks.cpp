#include "../unified/qol/divinium-target.hpp"
#include <cassert>
#include <iostream>
int main(){
 using divinium::TargetControl;
 assert(divinium::clampLowerTarget(15550,15154)==15153);
 assert(divinium::clampLowerTarget(15154,15154)==15153);
 assert(divinium::clampLowerTarget(1000,15154)==1000);
 assert(divinium::clampLowerTarget(-1,15154)==0);
 assert(divinium::clampLowerTarget(1000,0)==0);
 assert(divinium::clampLowerTarget(1000,1)==0);
 assert(!divinium::isLowerTarget(15154,15154));
 assert(!divinium::isLowerTarget(15550,15154));
 assert(!divinium::isLowerTarget(0,0));
 assert(divinium::isLowerTarget(0,1));
 assert(!divinium::isLowerTarget(1000,999)); // balance fell before Apply
 TargetControl c;
 assert(!c.start(14785,1000,false));assert(!c.start(10,-1,true));assert(!c.start(14685,1000,true));
 assert(c.start(14785,1000,true));int current=14785,n=0;
 for(unsigned long long time=1000;c.active;time+=1000){if(c.tick(current,true,time)){--current;--current;--current;++n;}}
 assert(current==1000&&n==4595&&!c.pending);
 TargetControl same;assert(same.start(1000,1000,true));assert(!same.tick(1000,true,1000));
 TargetControl zero;assert(zero.start(3,0,true));assert(zero.tick(3,true,1000));assert(!zero.tick(0,true,2000));assert(!zero.active);
 TargetControl delay;assert(delay.start(9,0,true));assert(delay.tick(9,true,1000));assert(!delay.tick(9,true,2000));assert(!delay.tick(9,true,16000));assert(delay.blocked);assert(!delay.start(9,0,true));
 TargetControl cancel;assert(cancel.start(9,0,true));assert(cancel.tick(9,true,1000));cancel.cancel();assert(!cancel.tick(6,true,2000));assert(!cancel.pending);
 TargetControl changed;assert(changed.start(9,0,true));assert(changed.tick(9,true,1000));assert(!changed.tick(5,true,2000));assert(changed.blocked);
 TargetControl lost;assert(lost.start(9,0,true));assert(!lost.tick(9,false,1000));assert(!lost.active);

 struct Mock final:divinium::IncreaseProvider {
  int calls=0,cancels=0;divinium::LiquidAmount delta=0;
  divinium::ProviderResult result=divinium::ProviderResult::Pending;
  void IncreaseLiquid(divinium::LiquidAmount amount)override{++calls;delta=amount;}
  divinium::ProviderResult Poll()override{return result;}
  void Cancel()override{++cancels;}
 } mock;
 TargetControl up(&mock);assert(up.start(10,20,true));assert(up.increase==TargetControl::IncreaseState::Required);
 assert(!up.tick(10,true,1000));assert(mock.calls==1&&mock.delta==10);
 assert(!up.start(10,30,true));assert(!up.tick(10,true,2000));assert(mock.calls==1);
 mock.result=divinium::ProviderResult::Completed;assert(!up.tick(10,true,3000));assert(up.active);
 assert(!up.tick(20,true,4000));assert(!up.active&&std::string(up.message)=="Solde actualisé");
 TargetControl mismatch(&mock);assert(mismatch.start(10,20,true));mismatch.tick(10,true,1000);mismatch.tick(10,true,2000);mismatch.tick(19,true,3000);assert(!mismatch.active&&std::string(mismatch.message).find("pas été atteinte")!=std::string::npos);
 TargetControl unavailable;assert(unavailable.start(10,20,true));unavailable.tick(10,true,1000);unavailable.tick(10,true,2000);assert(unavailable.active);unavailable.tick(10,true,3000);assert(!unavailable.active&&std::string(unavailable.message).find("non disponible")!=std::string::npos);
 mock.result=divinium::ProviderResult::Pending;TargetControl timeout(&mock);assert(timeout.start(10,20,true));timeout.tick(10,true,1000);timeout.tick(10,true,16000);assert(timeout.blocked&&mock.cancels==1);
 TargetControl abort(&mock);assert(abort.start(10,20,true));abort.tick(10,true,1000);abort.cancel();assert(!abort.active&&abort.blocked&&mock.cancels==2);
 TargetControl prereq(&mock);int calls=mock.calls;assert(prereq.start(10,20,true));prereq.tick(11,true,1000);assert(!prereq.active&&mock.calls==calls);
 TargetControl bounds;assert(!bounds.start(1,static_cast<divinium::LiquidAmount>(INT_MAX)+1,true));
 TargetControl unreadable(&mock);mock.result=divinium::ProviderResult::Completed;assert(unreadable.start(10,20,true));unreadable.tick(10,true,1000);unreadable.tick(10,true,2000);unreadable.tick(0,false,21000);assert(unreadable.blocked&&!unreadable.active);
 std::cout<<"PASS: absolute target, 14785->1000, unreachable targets, provider success, failure, mismatch, timeout, cancellation, limits, zero, no-op, delayed confirmation, timeout, cancel, external change, lost read\n";
}

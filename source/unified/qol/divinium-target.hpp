#pragma once
#include <cstdint>
#include <climits>
#include "divinium-increase-provider.hpp"
namespace divinium {
inline LiquidAmount clampLowerTarget(LiquidAmount desired, LiquidAmount balance) {
 if(balance<=0||desired<0)return 0;
 return desired>=balance?balance-1:desired;
}
inline bool isLowerTarget(LiquidAmount desired, LiquidAmount balance) {
 return desired>=0&&desired<balance;
}
struct TargetControl {
 enum class IncreaseState { None, Required, Waiting, Verify };
 IncreaseState increase=IncreaseState::None;
 UnavailableIncreaseProvider unavailable;
 IncreaseProvider* provider=&unavailable;
 bool providerFailed=false;
 explicit TargetControl(IncreaseProvider* implementation=nullptr){if(implementation)provider=implementation;}
 TargetControl(const TargetControl&)=delete;
 TargetControl& operator=(const TargetControl&)=delete;
 LiquidAmount target=0, before=0; bool active=false,pending=false,blocked=false;
 std::uint64_t sent=0; const char* message="Prêt";
 bool start(LiquidAmount current,LiquidAmount desired,bool valid){
  if(active){message="Une opération est déjà en cours.";return false;}
  if(blocked||pending){message="Une dépense précédente reste incertaine. Redémarrez BO3.";return false;}
  if(!valid){message="Solde indisponible.";return false;}
  if(desired<0){message="La cible doit être positive ou nulle.";return false;}
  if(current<0||desired>INT_MAX){message="Valeur hors limites du compteur du jeu.";return false;}
  increase=IncreaseState::None;providerFailed=false;
  if(desired>current){target=desired;before=current;active=true;increase=IncreaseState::Required;message="Prêt à appliquer";return true;}
  if((current-desired)%3){message="Cible inaccessible : dépenses par 3. Aucun arrondi appliqué.";return false;}
  target=desired;before=current;active=current!=desired;message=active?"Diminution en cours":"Solde déjà égal à la cible";return true;
 }
 void cancel(){
 if(increase!=IncreaseState::None){
  if(increase==IncreaseState::Waiting){provider->Cancel();blocked=true;}
  increase=IncreaseState::None;
 }
 active=false;message=pending?"Arrêt demandé ; dernière dépense en attente":"Arrêté";}
 // Returns true only when a single new spend may be submitted.
 bool tick(LiquidAmount current,bool valid,std::uint64_t now){
  if(increase!=IncreaseState::None){
   if(!active||blocked)return false;
   if(increase==IncreaseState::Required){
    if(!valid||current!=before){active=false;increase=IncreaseState::None;message="Solde changé ou indisponible : arrêté";return false;}
    provider->IncreaseLiquid(target-current);sent=now;increase=IncreaseState::Waiting;message="Opération en cours…";return false;
   }
   if(increase==IncreaseState::Waiting){
    auto result=provider->Poll();
    if(result==ProviderResult::Pending){
     if(now-sent>=15000){provider->Cancel();active=false;blocked=true;increase=IncreaseState::None;message="Délai dépassé : opération arrêtée";}
     return false;
    }
    providerFailed=result==ProviderResult::Failed;increase=IncreaseState::Verify;message="Actualisation du solde…";return false;
   }
   // This tick receives a new read, never the value sampled before provider completion.
   if(!valid){
    if(now-sent>=20000){active=false;blocked=true;increase=IncreaseState::None;message="Solde final illisible : résultat non confirmé";}
    return false;
   }
   active=false;increase=IncreaseState::None;
   message=current==target?"Solde actualisé":providerFailed?"Modification non disponible : solde inchangé":"La quantité souhaitée n’a pas été atteinte";
   return false;
  }
  if(pending){
   if(valid&&current==before-3){pending=false;before=current;message=active?"Dépense confirmée":"Dernière dépense confirmée";}
   else if(valid&&current!=before){pending=false;blocked=true;active=false;message="Variation inattendue : opération arrêtée";return false;}
   else if(now-sent>=15000){active=false;blocked=true;message="Dépense non confirmée : aucun nouvel envoi";return false;}
   else return false;
  }
  if(!active||blocked)return false;
  if(!valid){active=false;message="Lecture perdue : arrêté";return false;}
  if(current!=before){active=false;message="Solde modifié ailleurs : arrêté";return false;}
  if(current==target){active=false;message="Solde actualisé";return false;}
  if(current<target||current-target<3){active=false;message="Cible non atteignable : arrêté";return false;}
  if(now-sent<1000)return false;
  pending=true;sent=now;before=current;return true;
 }
 void failed(){active=false;blocked=true;message="Dépense refusée ou incertaine : arrêté";}
};
}

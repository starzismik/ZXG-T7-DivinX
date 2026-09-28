#pragma once
#include <wincodec.h>
#include <wrl/client.h>
#include "divinium-target.hpp"
#include "imgui/imgui_internal.h"
namespace divinium_ui {
using Microsoft::WRL::ComPtr;
struct Art { ComPtr<ID3D11ShaderResourceView> view; float w=1,h=1; };
Art illustration;
bool attempted=false;
divinium::TargetControl control;
int balance=0; divinium::LiquidAmount desired=0; bool valid=false,initialized=false,refresh=true;
ULONGLONG lastRead=0;
bool readBalance(int* result){
 __try {
  auto base=reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
  const unsigned char readPrefix[]={0x48,0x89,0x5c,0x24,0x08,0x57,0x48,0x83,0xec,0x20};
  const unsigned char readyPrefix[]={0x40,0x53,0x48,0x83,0xec,0x20};
  if(memcmp(reinterpret_cast<void*>(base+0x1e76100),readPrefix,sizeof(readPrefix)) || memcmp(reinterpret_cast<void*>(base+0x1e765a0),readyPrefix,sizeof(readyPrefix)))return false;
  if(!reinterpret_cast<bool(*)(int)>(base+0x1e765a0)(0))return false;
  int value=reinterpret_cast<int(*)(int,int)>(base+0x1e76100)(0,3);
  if(value<0)return false;
  *result=value;return true;
 } __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
bool spendThree(){
 __try {return Loot_SpendVials(0,3);} __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
bool SetLiquidTarget(long target){
 valid=readBalance(&balance);lastRead=GetTickCount64();refresh=true;
 if(!valid){control.message="Solde indisponible.";return false;}
 desired=divinium::clampLowerTarget(desired,balance);
 if(!divinium::isLowerTarget(target,balance)){
  control.message="La quantité souhaitée doit être inférieure au solde actuel.";return false;
 }
 bool accepted=control.start(balance,target,valid);
 if(accepted&&control.active){bDivinium=false;bDiviniumSpend=false;}
 return accepted;
}
void update(){
 const auto now=GetTickCount64();
 if(!open && control.active)control.cancel();
 if(refresh||now-lastRead>=500){valid=readBalance(&balance);lastRead=now;refresh=false;
  if(valid&&!initialized){desired=balance>=3?balance-3:0;initialized=true;}
  if(control.tick(balance,valid,now)){
   bDivinium=false;bDiviniumSpend=false;bSpoofBlackMarket=false;
   if(!spendThree())control.failed();
  }
 }
}
void load(Art& art,const wchar_t* name){
 HMODULE module{}; if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&load),&module))return;
 wchar_t path[MAX_PATH]{};GetModuleFileNameW(module,path,MAX_PATH);
 auto file=std::filesystem::path(path).parent_path()/L"divinium"/name;
 ComPtr<IWICImagingFactory> factory;ComPtr<IWICBitmapDecoder> decoder;ComPtr<IWICBitmapFrameDecode> frame;ComPtr<IWICFormatConverter> converter;
 if(FAILED(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory))))return;
 if(FAILED(factory->CreateDecoderFromFilename(file.c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnLoad,&decoder)))return;
 if(FAILED(decoder->GetFrame(0,&frame))||FAILED(factory->CreateFormatConverter(&converter)))return;
 if(FAILED(converter->Initialize(frame.Get(),GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom)))return;
 UINT w{},h{};if(FAILED(converter->GetSize(&w,&h))||!w||!h||w>4096||h>4096)return;
 std::vector<unsigned char> bytes(w*h*4);if(FAILED(converter->CopyPixels(nullptr,w*4,static_cast<UINT>(bytes.size()),bytes.data())))return;
 D3D11_TEXTURE2D_DESC d{};d.Width=w;d.Height=h;d.MipLevels=1;d.ArraySize=1;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_IMMUTABLE;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
 D3D11_SUBRESOURCE_DATA initial{bytes.data(),w*4,0};ComPtr<ID3D11Texture2D> texture;
 if(SUCCEEDED(pDevice->CreateTexture2D(&d,&initial,&texture))&&SUCCEEDED(pDevice->CreateShaderResourceView(texture.Get(),nullptr,&art.view))){art.w=float(w);art.h=float(h);}
}
void artwork(float width){
 const auto available=ImGui::GetContentRegionAvail();
 if(!illustration.view){ImGui::TextWrapped("Image Divinium indisponible.");return;}
 const float scale=(std::min)(width/illustration.w,available.y/illustration.h);
 const ImVec2 size(illustration.w*scale,illustration.h*scale);
 const auto cursor=ImGui::GetCursorPos();
 ImGui::SetCursorPos(ImVec2(cursor.x+(available.x-size.x)*0.5f,cursor.y+(available.y-size.y)*0.5f));
 ImGui::Image((ImTextureID)illustration.view.Get(),size);
}
}
void drawDivinium(){
 divinium_ui::update();
 if(!open)return;
 if(!divinium_ui::attempted&&pDevice){divinium_ui::attempted=true;HRESULT co=CoInitializeEx(nullptr,COINIT_MULTITHREADED);divinium_ui::load(divinium_ui::illustration,L"artwork.png");if(SUCCEEDED(co))CoUninitialize();}
 auto& st=ImGui::GetStyle();st.WindowRounding=4;st.FrameRounding=0;st.TabRounding=0;st.WindowBorderSize=1;st.FrameBorderSize=1;st.WindowPadding=ImVec2(14,12);st.ItemSpacing=ImVec2(9,10);st.WindowTitleAlign=ImVec2(.5f,.5f);
 st.Colors[ImGuiCol_WindowBg]=ImVec4(.025f,.05f,.075f,.98f);st.Colors[ImGuiCol_Border]=ImVec4(0,.55f,.75f,1);st.Colors[ImGuiCol_Text]=ImVec4(.9f,.95f,1,1);st.Colors[ImGuiCol_FrameBg]=ImVec4(.035f,.095f,.14f,1);st.Colors[ImGuiCol_Button]=ImVec4(.02f,.30f,.48f,1);st.Colors[ImGuiCol_ButtonHovered]=ImVec4(0,.48f,.7f,1);st.Colors[ImGuiCol_ButtonActive]=ImVec4(0,.6f,.8f,1);st.Colors[ImGuiCol_TitleBgActive]=ImVec4(.035f,.14f,.21f,1);st.Colors[ImGuiCol_Tab]=ImVec4(.04f,.15f,.22f,1);st.Colors[ImGuiCol_TabActive]=ImVec4(0,.40f,.65f,1);st.Colors[ImGuiCol_CheckMark]=ImVec4(0,.8f,1,1);
 ImGui::SetNextWindowSize(ImVec2(900,570),ImGuiCond_Once);ImGui::SetNextWindowPos(ImGui::GetMainViewport()->GetCenter(),ImGuiCond_Once,ImVec2(.5f,.5f));
 if(ImGui::Begin("[ZXG] T7 DivinX - Divinium",&open)){
 ImGui::TextColored(ImVec4(0,.8f,1,1),"par STARZISMIK");ImGui::SameLine();ImGui::TextDisabled(" | F5 : ouvrir / fermer");ImGui::Separator();
 if(ImGui::BeginTabBar("divinium-tabs")){
 if(ImGui::BeginTabItem("Divinium")){
 float left=ImGui::GetContentRegionAvail().x*.35f;
 ImGui::BeginChild("illustrations",ImVec2(left,0),true);divinium_ui::artwork(ImGui::GetContentRegionAvail().x);ImGui::EndChild();ImGui::SameLine();
 ImGui::BeginChild("settings",ImVec2(0,0),true);
 using namespace divinium_ui;
 ImGui::SeparatorText("Liquid Divinium");ImGui::TextUnformatted("Solde actuel");
 ImGui::BeginChild("balance-card",ImVec2(ImGui::GetContentRegionAvail().x-115,52),true);
 ImGui::SetWindowFontScale(1.55f);
 if(valid)ImGui::TextColored(ImVec4(0,.8f,1,1),"%d",balance);else ImGui::TextDisabled("—");
 ImGui::SetWindowFontScale(1.0f);ImGui::EndChild();ImGui::SameLine();
 if(ImGui::Button("Actualiser",ImVec2(106,52)))refresh=true;
 ImGui::Spacing();ImGui::SeparatorText("Quantité souhaitée");
 ImGui::BeginDisabled(control.active||control.pending||!valid||balance<=0);
 ImGui::SetNextItemWidth(ImGui::GetContentRegionAvail().x-160);
 // Reconcile the active text buffer as well as the stored number after balance changes.
 auto bounded=divinium::clampLowerTarget(desired,valid?balance:0);
 if(desired!=bounded){desired=bounded;if(ImGui::GetActiveID()==ImGui::GetID("##amount"))ImGui::ClearActiveID();}
 ImGui::InputScalar("##amount",ImGuiDataType_S64,&desired);
 bounded=divinium::clampLowerTarget(desired,valid?balance:0);
 if(desired!=bounded){desired=bounded;ImGui::ClearActiveID();}
 ImGui::SameLine();if(ImGui::Button("-",ImVec2(30,0))&&desired>0)--desired;
 ImGui::SameLine();
 if(ImGui::Button("Appliquer",ImVec2(110,0))){
  if(desired>LONG_MAX)control.message="Valeur hors limites du compteur du jeu.";
  else SetLiquidTarget(static_cast<long>(desired));
 }
 ImGui::EndDisabled();
 ImGui::Spacing();ImGui::TextWrapped("%s",control.message);
 ImGui::Spacing();ImGui::SeparatorText("Actions");
 // Same flags and hkPresent implementation as the full menu's Gobblegum section.
 // TargetControl owns outstanding target requests; do not overlap those operations.
 ImGui::BeginDisabled(control.active||control.pending);
 ImGui::Checkbox("Farm de Divinium",&bDivinium);
 ImGui::SameLine();
 ImGui::Checkbox("Dépenser le Divinium",&bDiviniumSpend);
 ImGui::EndDisabled();
 ImGui::TextDisabled(bDiviniumSpend?"Dépense en cours":bDivinium?"Farm en cours":control.active||control.pending?"En cours":"Inactif");
 if((bDivinium||bDiviniumSpend||control.active||control.pending)&&ImGui::Button("Arrêter",ImVec2(-1,30))){control.cancel();bDivinium=bDiviniumSpend=false;}

 ImGui::EndChild();ImGui::EndTabItem();}
 if(ImGui::BeginTabItem("Marche noir")){
 ImGui::SeparatorText("Cryptocles");ImGui::SliderInt("Quantite",&iCryptoAmt,0,48);ImGui::SliderInt("Delai (ms)",&iLootSpeed,50,500);
 if(ImGui::Checkbox("Farm de cryptocles",&bCrypto)&&bCrypto)bCryptoSpend=false;
 if(ImGui::Checkbox("Depenser les cryptocles",&bCryptoSpend)&&bCryptoSpend)bCrypto=false;
 if(ImGui::Button("Reparer les cryptocles"))resetCrypto();
 if(ImGui::Button("Arreter les actions")){bDivinium=bDiviniumSpend=bCrypto=bCryptoSpend=false;}
 ImGui::TextWrapped("Les actions restent actives quand le menu est ferme. Arretez-les ici avant de continuer.");ImGui::EndTabItem();}
 if(ImGui::BeginTabItem("Infos")){
 ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing,ImVec2(8,4));
 ImGui::BeginChild("##INFO",ImGui::GetContentRegionAvail());
            const ImVec2 available = ImGui::GetContentRegionAvail();
            const float cardWidth = (std::min)(600.0f, (std::max)(260.0f, available.x - 32.0f));
            const float cardHeight = 220.0f;
            const float cardX = (std::max)(0.0f, (available.x - cardWidth) * 0.5f);
            const float cardY = ImGui::GetCursorPosY() + (std::max)(12.0f, (available.y - cardHeight - 64.0f) * 0.5f);
            ImGui::SetCursorPos(ImVec2(cardX, cardY));
            ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, 0.0f);
            ImGui::PushStyleColor(ImGuiCol_Border, ImVec4(0.08f, 0.48f, 0.70f, 1.0f));
            ImGui::BeginChild("##DIVINX_CREDITS", ImVec2(cardWidth, cardHeight), true, ImGuiWindowFlags_NoScrollbar);
            const auto centeredLine = [](const char* text) {
                const float width = ImGui::CalcTextSize(text).x;
                ImGui::SetCursorPosX((std::max)(8.0f, (ImGui::GetWindowSize().x - width) * 0.5f));
                ImGui::TextUnformatted(text);
            };
            ImGui::Dummy(ImVec2(0, 14));
            ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0.12f, 0.70f, 1.0f, 1.0f));
            centeredLine("[ZXG] T7 DivinX");
            ImGui::PopStyleColor();
            centeredLine("Version 1.0.0");
            ImGui::Dummy(ImVec2(0, 9));
            centeredLine("Développée par STARZISMIK");
            ImGui::Dummy(ImVec2(0, 9));
            ImGui::Separator();
            ImGui::Dummy(ImVec2(0, 9));
            centeredLine("Base du menu : Scropts-QOL / Scroptss");
            centeredLine("Crédits : Serious, InsaneCallum et contributeurs amont");
            ImGui::EndChild();
            ImGui::PopStyleColor();
            ImGui::PopStyleVar();
 ImGui::EndChild();
 ImGui::PopStyleVar();
 ImGui::EndTabItem();
 }
 ImGui::EndTabBar();}}
 ImGui::End();
}

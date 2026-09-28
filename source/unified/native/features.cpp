// ZXG integration: reviewed mechanisms/offsets from Scroptss/T7Patch-src
// 46b268a6cda05ac5ee2f8ff88c6db9f6d549df71 and shiversoftdev's T7Patch.
// This is a bounded subset, not a claim of complete T7Patch protection.
#include "features.hpp"
#include "checked-hooks.hpp"
#include "MinHook.h"
#include "Arxan.h"
#include <array>
#include <algorithm>
#include <cstring>
#include <sstream>
#include <vector>
#include <bcrypt.h>

namespace zxg {
State& state() { static State value; return value; }
std::uintptr_t base() { return reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr)); }
bool readable(const void* p, std::size_t count) {
 MEMORY_BASIC_INFORMATION m{};
 if (!p || VirtualQuery(p,&m,sizeof(m)) != sizeof(m) || m.State != MEM_COMMIT || (m.Protect & (PAGE_NOACCESS|PAGE_GUARD))) return false;
 const auto begin=reinterpret_cast<std::uintptr_t>(p), region=reinterpret_cast<std::uintptr_t>(m.BaseAddress);
 return begin>=region && count<=m.RegionSize-(begin-region);
}
bool writable(const void* p, std::size_t count) {
 MEMORY_BASIC_INFORMATION m{};
 return readable(p,count) && VirtualQuery(p,&m,sizeof(m)) && (m.Protect & (PAGE_READWRITE|PAGE_WRITECOPY|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));
}
bool executable(const void* p) {
 MEMORY_BASIC_INFORMATION m{};
 return readable(p,16) && VirtualQuery(p,&m,sizeof(m)) && (m.Protect & (PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));
}
std::string hex(const std::string& s) {
 constexpr char digits[]="0123456789ABCDEF"; std::string out; out.reserve(s.size()*2);
 for(unsigned char c:s) {out+=digits[c>>4];out+=digits[c&15];} return out;
}
bool unhex(const std::string& s, std::string& out) {
 if(s.size()%2 || s.size()>256)return false;
 auto n=[](char c)->int {if(c>='0'&&c<='9')return c-'0'; if(c>='A'&&c<='F')return c-'A'+10; if(c>='a'&&c<='f')return c-'a'+10;return -1;};
 for(size_t i=0;i<s.size();i+=2) {int a=n(s[i]),b=n(s[i+1]);if(a<0||b<0)return false;out+=static_cast<char>((a<<4)|b);} return true;
}
namespace {
 bool verifiedFile(const wchar_t* path) {
  HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,0,nullptr);if(file==INVALID_HANDLE_VALUE)return false;
  LARGE_INTEGER size{};if(!GetFileSizeEx(file,&size)||size.QuadPart<=0||size.QuadPart>0xFFFFFFFF){CloseHandle(file);return false;}
  HANDLE mapping=CreateFileMappingW(file,nullptr,PAGE_READONLY,0,0,nullptr);void* data=mapping?MapViewOfFile(mapping,FILE_MAP_READ,0,0,0):nullptr;
  BCRYPT_ALG_HANDLE alg{};unsigned char digest[32]{};bool ok=false;
  if(data&&BCryptOpenAlgorithmProvider(&alg,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0)
   ok=BCryptHash(alg,nullptr,0,static_cast<PUCHAR>(data),static_cast<ULONG>(size.QuadPart),digest,32)>=0;
  if(alg)BCryptCloseAlgorithmProvider(alg,0);if(data)UnmapViewOfFile(data);if(mapping)CloseHandle(mapping);CloseHandle(file);
  return ok&&hex(std::string(reinterpret_cast<char*>(digest),32))=="51CA63BBC660E0826943C60DA67606F6BCB4B3B519528B5E0548C68C9423A323";
 }
 std::mutex configMutex;
 std::uint64_t password{},previousPassword{},passwordChanged{};
 std::string selectedName;
 constexpr std::array<std::uintptr_t,4> nameRvas{0x14F344B8,0x15E056C8,0x113A4970,0x113A48F0};
 std::array<std::array<char,16>,4> originalNames{}; bool nameOverridden{};
 struct Msg { bool overflowed,readOnly; short pad1; int pad2; char* data; char* split; unsigned maxsize,cursize,splitSize,readcount; int bit,lastEntity,flush,net; };
 using NameFn=bool(*)(int,char*,int); NameFn originalNameFn{};
 using VerifyFn=long long(*)(const char*,int); VerifyFn originalVerify{};
 using CopyFn=unsigned short(*)(char*,const char*,int); CopyFn originalCopy{};
 using PrepWriteFn=bool(*)(Msg*,char*,int,int); PrepWriteFn originalPrepWrite{};
 using PrepReadFn=bool(*)(Msg*); PrepReadFn originalPrepRead{};
 using PackageIntFn=bool(*)(void*,const char*,int*); PackageIntFn originalInt{};
 using PackageUIntFn=bool(*)(void*,const char*,unsigned*); PackageUIntFn originalUInt{};
 using PackageByteFn=bool(*)(void*,const char*,unsigned char*); PackageByteFn originalByte{};
 using PresenceFn=long long(*)(long long,long long); PresenceFn originalPresence{};
 using ConfigFn=const char*(*)(int); ConfigFn originalConfig{};
 auto checksum() {return reinterpret_cast<unsigned short(*)(const unsigned char*,int)>(base()+0x211ED70);}
 auto writeByte() {return reinterpret_cast<void(*)(Msg*,int)>(base()+0x20FED00);}
 auto readByte() {return reinterpret_cast<unsigned char(*)(Msg*)>(base()+0x20FC990);}
 auto modeName() {return reinterpret_cast<const char*(*)()>(base()+0x20EA640);}
 bool hkName(int controller,char* out,int length) {
  std::lock_guard lock(configMutex);
  if(selectedName.empty())return originalNameFn(controller,out,length);
  if(!out||length<=0)return false; strncpy_s(out,static_cast<size_t>(length),selectedName.c_str(),_TRUNCATE);return true;
 }
 long long hkVerify(const char* data,int length) {
  if(length<2||!readable(data,static_cast<size_t>(length))) {state().dropped++;return -1;}
  std::uint64_t current,old,changed;
  {std::lock_guard lock(configMutex);current=password;old=previousPassword;changed=passwordChanged;}
  const auto computed=checksum()(reinterpret_cast<const unsigned char*>(data),length-2);
  unsigned short received{};memcpy(&received,data+length-2,2);
  if(received==static_cast<unsigned short>(computed^current))return length-2;
  if(GetTickCount64()-changed<=1500 && received==static_cast<unsigned short>(computed^old))return length-2;
  state().dropped++;return -1;
 }
 unsigned short hkCopy(char* dest,const char* src,int length) {
  const auto value=originalCopy(dest,src,length);std::lock_guard lock(configMutex);return static_cast<unsigned short>(value^password);
 }
 bool hkPrepWrite(Msg* msg,char* data,int length,int type) {
  if(!originalPrepWrite(msg,data,length,type))return false;
  std::uint64_t pass;{std::lock_guard lock(configMutex);pass=password;}
  const auto prefix=static_cast<unsigned char>(pass>>16);
  if(prefix) {writeByte()(msg,prefix);writeByte()(msg,static_cast<unsigned char>(pass>>24));}
  return !msg->overflowed;
 }
 bool hkPrepRead(Msg* msg) {
  if(!originalPrepRead(msg))return false;
  std::uint64_t pass;{std::lock_guard lock(configMutex);pass=password;}
  const auto prefix=static_cast<unsigned char>(pass>>16);
  if(!prefix)return true;
  if(readByte()(msg)==prefix && readByte()(msg)==static_cast<unsigned char>(pass>>24) && !msg->overflowed)return true;
  state().dropped++;return false;
 }
 bool lobbyKey(const char* key) {return key && (!_stricmp(key,"lobbytype")||!_stricmp(key,"srclobbytype")||!_stricmp(key,"destlobbytype"));}
 bool hkInt(void* msg,const char* key,int* value) {
  bool ok=originalInt(msg,key,value); if(ok && lobbyKey(key) && (*value<0||*value>2)){state().dropped++;return false;}return ok;
 }
 bool hkUInt(void* msg,const char* key,unsigned* value) {
  bool ok=originalUInt(msg,key,value); if(ok && lobbyKey(key) && *value>2){state().dropped++;return false;}return ok;
 }
 bool hkByte(void* msg,const char* key,unsigned char* value) {
  bool ok=originalByte(msg,key,value);if(ok&&key&&!_stricmp(key,"nattype")&&*value>4){*value=4;state().dropped++;}return ok;
 }
 long long hkPresence(long long a,long long b) {
  if(!a||!b||!readable(reinterpret_cast<void*>(a+16),8))return 0;
  auto ptr=*reinterpret_cast<unsigned**>(a+16); if(!writable(ptr,4))return 0;
  if(((*ptr>>2)&31)>18){*ptr=(*ptr & ~(31u<<2))|(18u<<2);state().dropped++;}return originalPresence(a,b);
 }
 const char* hkConfig(int index) {
  const auto value=originalConfig(index);
  if(index!=3514&&index!=3627)return value;
  const auto mode=modeName()();if(mode&&!_stricmp(mode,"CP"))return value;
  if(value&&readable(value,9)&&!_strnicmp(value,"mspreload",9)){state().dropped++;return "";}return value;
 }
 using SystemInfoFn=bool(__fastcall*)(int,int,char*,int);
 SystemInfoFn originalSystemInfo{};
 constexpr char VersionLabel[]="^7[ZXG] ^5T7 DivinX^7 1.0.0 | par STARZISMIK";
 bool __fastcall hkSystemInfo(int controller,int type,char* output,int length){
  if(type!=0)return originalSystemInfo(controller,type,output,length);
  if(!output || length<=0 || !writable(output,static_cast<size_t>(length)))return false;
  if(static_cast<size_t>(length)<sizeof(VersionLabel)){output[0]=0;return false;}
  memcpy(output,VersionLabel,sizeof(VersionLabel));return true;
 }
 // Direct blocking behavior from Scroptss/T7Patch-src Hooks.cpp:
 // hkExecLuaCMD, hkUI_BrowserOpen, hkMods_SubscribeUGC.
 // No outbound traffic, subscription, browser launch or Lua command is performed.
 using ExecLuaFn=void(*)(); ExecLuaFn originalExecLua{};
 using BrowserFn=char(*)(long long); BrowserFn originalBrowser{};
 using SubscribeFn=bool(*)(long long); SubscribeFn originalSubscribe{};
 void hkExecLua(){state().dropped++;}
 char hkBrowser(long long){state().dropped++;return 0;}
 bool hkSubscribe(long long){state().dropped++;return false;}
 struct Hook {std::uintptr_t rva;const char* prefix;void* replacement;void** original;};
 std::vector<Hook> hooks{
  {0x1EF83A0,"40564883EC20E865292300488B48184863",reinterpret_cast<void*>(hkExecLua),reinterpret_cast<void**>(&originalExecLua)},
  {0x1EA4770,"40534883EC20803D1B01500F00488BD974",reinterpret_cast<void*>(hkBrowser),reinterpret_cast<void**>(&originalBrowser)},
  {0x20CAA00,"4883EC2833D2448D420AE8499CAF004885",reinterpret_cast<void*>(hkSubscribe),reinterpret_cast<void**>(&originalSubscribe)},
  {0x1E01000,"48895C24105556574154415541564157",reinterpret_cast<void*>(hkSystemInfo),reinterpret_cast<void**>(&originalSystemInfo)},
  {0x1EBAB40,"48895C24105556574883EC20",reinterpret_cast<void*>(hkName),reinterpret_cast<void**>(&originalNameFn)},
  {0x211EEE0,"48895C240848897424105748",reinterpret_cast<void*>(hkVerify),reinterpret_cast<void**>(&originalVerify)},
  {0x211EE40,"4883EC0833C04C8BD24C8BD9",reinterpret_cast<void*>(hkCopy),reinterpret_cast<void**>(&originalCopy)},
  {0x1EE9EA0,"48895C2408574883EC20418B",reinterpret_cast<void*>(hkPrepWrite),reinterpret_cast<void**>(&originalPrepWrite)},
  {0x1EEB210,"48895C2410574883EC60488B",reinterpret_cast<void*>(hkPrepRead),reinterpret_cast<void**>(&originalPrepRead)},
  {0x1EE9D20,"8B414085C07502F3C383F801",reinterpret_cast<void*>(hkInt),reinterpret_cast<void**>(&originalInt)},
  {0x1EE9DD0,"8B414085C07502F3C383F801",reinterpret_cast<void*>(hkUInt),reinterpret_cast<void**>(&originalUInt)},
  {0x1EE9D90,"8B414085C07502F3C383F801",reinterpret_cast<void*>(hkByte),reinterpret_cast<void**>(&originalByte)},
  {0x1E84D90,"48895C241048896C24185657",reinterpret_cast<void*>(hkPresence),reinterpret_cast<void**>(&originalPresence)},
  {0x1321130,"4863C1488D0DEE2638044863",reinterpret_cast<void*>(hkConfig),reinterpret_cast<void**>(&originalConfig)}
 };
 bool matches(const Hook& h) {std::string bytes;unhex(h.prefix,bytes);auto p=reinterpret_cast<void*>(base()+h.rva);return executable(p)&&!memcmp(p,bytes.data(),bytes.size());}
 void fail(const std::string& message) {std::lock_guard lock(state().mutex);state().error=message;}
}
std::string set_name(const std::string& name) {
 if(!state().engineReady)return "ERR|Moteur non initialise\n";
 if(name.empty()||name.size()>15||std::any_of(name.begin(),name.end(),[](unsigned char c){return c<32||c>126;}))return "ERR|Pseudo requis : 1 a 15 caracteres ASCII imprimables\n";
 std::lock_guard lock(configMutex);
 for(auto rva:nameRvas)if(!writable(reinterpret_cast<void*>(base()+rva),16))return "ERR|Buffer du pseudo inaccessible\n";
 if(!nameOverridden) {
  for(size_t i=0;i<nameRvas.size();i++)memcpy(originalNames[i].data(),reinterpret_cast<void*>(base()+nameRvas[i]),16);
  std::lock_guard stateLock(state().mutex);state().originalName=std::string(originalNames[0].data(),strnlen_s(originalNames[0].data(),16));
 }
 nameOverridden=true;selectedName=name;
 for(auto rva:nameRvas)strcpy_s(reinterpret_cast<char*>(base()+rva),16,name.c_str());
 {std::lock_guard stateLock(state().mutex);state().currentName=name;}
 return "OK\n";
}
std::string restore_name() {
 if(!state().engineReady)return "ERR|Moteur non initialise\n";
 std::lock_guard lock(configMutex);
 if(!nameOverridden)return "OK\n";
 for(auto rva:nameRvas)if(!writable(reinterpret_cast<void*>(base()+rva),16))return "ERR|Buffer du pseudo inaccessible\n";
 for(size_t i=0;i<nameRvas.size();i++)memcpy(reinterpret_cast<void*>(base()+nameRvas[i]),originalNames[i].data(),16);
 selectedName.clear();nameOverridden=false; {std::lock_guard stateLock(state().mutex);state().currentName=state().originalName;}
 return "OK\n";
}
std::string set_password(const std::string& value) {
 if(!state().engineReady)return "ERR|Moteur non initialise\n";
 if(value.size()>64||std::any_of(value.begin(),value.end(),[](unsigned char c){return c<32||c>126;}))return "ERR|Mot de passe : 64 caracteres ASCII maximum\n";
 std::uint64_t hash=14695981039346656037ULL;
 for(unsigned char c:value){if(c>='A'&&c<='Z')c+=32;hash=(hash^c)*1099511628211ULL;}
 hash=value.empty()?0:(hash&0x7FFFFFFFFFFFFFFFULL);
 {std::lock_guard lock(configMutex);previousPassword=password;password=hash;passwordChanged=GetTickCount64();}
 state().privateNetwork=!value.empty();return "OK\n";
}
void initialize_features() { 
 wchar_t path[MAX_PATH]{};GetModuleFileNameW(nullptr,path,MAX_PATH);
 auto filename=wcsrchr(path,L'\\');if(!filename||_wcsicmp(filename+1,L"BlackOps3.exe"))return;
 if(!verifiedFile(path)){fail("Empreinte SHA-256 moteur inconnue");return;}
 auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base());auto pe=reinterpret_cast<IMAGE_NT_HEADERS64*>(base()+dos->e_lfanew);
 if(pe->FileHeader.TimeDateStamp!=0x6A7B6355||pe->OptionalHeader.SizeOfImage!=0x1D75BC00){fail("Build moteur inconnu");return;}
 // Allow the game's own initialization to complete, without touching its input.
 bool ready=false;
 for(int i=0;i<120;i++) {
  auto signal=reinterpret_cast<std::uintptr_t*>(base()+0x1686E948);
  if(readable(signal,8)&&*signal&&std::all_of(hooks.begin(),hooks.end(),matches)){ready=true;break;}Sleep(500);
 }
 if(!ready){fail("Signatures moteur non reconnues apres 60 s");return;}
 for(size_t i=0;i<nameRvas.size();i++) {
  auto source=reinterpret_cast<const char*>(base()+nameRvas[i]);if(!readable(source,16)){fail("Buffers du pseudo indisponibles");return;}memcpy(originalNames[i].data(),source,16);
 }
 {std::lock_guard lock(state().mutex);state().originalName=std::string(originalNames[0].data(),strnlen_s(originalNames[0].data(),16));state().currentName=state().originalName;}
 const std::array<std::pair<std::uintptr_t,const char*>,4> helpers{{
  {0x211ED70,"48895C24084533DB8BC2448B"},{0x20FED00,"4863411C3B41187D0F4C8BC0"},
  {0x20FC990,"4C8BC14863491C418B40204D"},{0x20EA640,"8B0D2E427814C1E11CC1F91C"}}};
 for(const auto& helper:helpers) {std::string bytes;unhex(helper.second,bytes);auto p=reinterpret_cast<void*>(base()+helper.first);if(!executable(p)||memcmp(p,bytes.data(),bytes.size())){fail("Signature auxiliaire absente : "+std::to_string(helper.first));return;}}
  std::string adaptation;
 if(!arxan_bypass::install(adaptation)){fail("Adaptation du moteur refusee : "+adaptation);return;}
  const auto init=MH_Initialize();if(init!=MH_OK){fail("MinHook deja utilise ou indisponible");return;}
 struct Backend {
  bool create(const Hook& h){return MH_CreateHook(reinterpret_cast<void*>(base()+h.rva),h.replacement,h.original)==MH_OK;}
  bool queue(const Hook& h){return MH_QueueEnableHook(reinterpret_cast<void*>(base()+h.rva))==MH_OK;}
  bool apply(){return MH_ApplyQueued()==MH_OK;}
  void disable(const Hook& h){MH_DisableHook(reinterpret_cast<void*>(base()+h.rva));}
  void remove(const Hook& h){MH_RemoveHook(reinterpret_cast<void*>(base()+h.rva));}
 } backend;
 if(!install_checked_hooks(hooks,backend)){fail("Installation des hooks refusee ; annulation effectuee");return;}
 arxan_bypass::maintain();state().hookCount=static_cast<unsigned>(hooks.size());state().engineReady=true;
  install_overlay(); 
}
std::string status() {
 if(state().engineReady) {
  std::lock_guard configLock(configMutex);
  if(!nameOverridden) {
   auto current=reinterpret_cast<const char*>(base()+nameRvas[0]);
   if(readable(current,16)){std::lock_guard stateLock(state().mutex);state().currentName=std::string(current,strnlen_s(current,16));state().originalName=state().currentName;}
  }
 }
 auto& s=state();std::lock_guard lock(s.mutex);
 return "ZXG|3|"+std::to_string(GetCurrentProcessId())+"|0.5.0|"+(s.engineReady?"PARTIAL":"NONE")+"|"+(s.menuReady?"RENDERED":"WAITING")+"|"+std::to_string(s.frames)+"|"+(s.menuOpen?"1":"0")+"|"+hex(s.currentName)+"|"+(s.privateNetwork?"1":"0")+"|"+std::to_string(s.hookCount)+"|"+hex(s.error)+"|"+std::to_string(s.drawnFrames)+"\n";
}
std::string command(const std::string& request) {
 if(request=="HELLO|3\n")return status();
 if(request=="RESTORE\n")return restore_name();
 if(request=="MENU|OPEN\n"){if(!state().menuReady)return "ERR|Menu indisponible\n";state().menuOpen=true;return "OK\n";}
 if(request=="MENU|CLOSE\n"){state().menuOpen=false;return "OK\n";}
 if(request=="CAPTURE\n"){if(!state().menuReady)return "ERR|Rendu indisponible\n";state().captureRequested=true;return "OK\n";}
 if(request=="VERSION_TEST\n"){
  if(!state().engineReady)return "ERR|Moteur indisponible\n";
  char buffer[128]{};
  auto live=reinterpret_cast<SystemInfoFn>(base()+0x1E01000);
  if(!live(0,0,buffer,sizeof(buffer)) || strcmp(buffer,VersionLabel))return "ERR|Texte de version non applique\n";
  char tiny[2]={'x','z'};if(hkSystemInfo(0,0,tiny,1)||tiny[0]!=0||tiny[1]!='z')return "ERR|Limite du buffer invalide\n";
  return "OK\n";
 }
 if(request=="NETWORK_TEST\n"){
  if(!state().engineReady)return "ERR|Moteur indisponible\n";
  const char payload[16]="ZXG_LOCAL_TEST";char packet[18]{};
  auto sum=hkCopy(packet,payload,16);memcpy(packet+16,&sum,2);
  if(hkVerify(packet,18)!=16)return "ERR|Test checksum aller-retour echoue\n";
  packet[0]^=0x40;if(hkVerify(packet,18)!=-1)return "ERR|Paquet modifie non refuse\n";
  return "OK\n";
 }
 if(request.starts_with("KEY|")) {try{if(!request.ends_with('\n'))return "ERR|Touche invalide\n";auto value=request.substr(4,request.size()-5);if(value.empty()||value.find_first_not_of("0123456789")!=std::string::npos)return "ERR|Touche invalide\n";auto key=std::stoul(value);if(key!=VK_INSERT&&!(key>=VK_F1&&key<=VK_F12))return "ERR|Touche invalide\n";state().menuKey=key;return "OK\n";}catch(...){return "ERR|Touche invalide\n";}}
 for(auto prefix:{std::string("NAME|"),std::string("PASSWORD|")}) {
  if(request.starts_with(prefix)&&request.ends_with('\n')){std::string value;if(!unhex(request.substr(prefix.size(),request.size()-prefix.size()-1),value))return "ERR|Encodage invalide\n";auto answer=prefix=="NAME|"?set_name(value):set_password(value);SecureZeroMemory(value.data(),value.size());return answer;}
 }
 return "ERR|Commande invalide\n";
}
}

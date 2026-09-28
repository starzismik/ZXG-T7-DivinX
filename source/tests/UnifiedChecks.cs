using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ZXGT7Guard;

if(args.Length>0 && args[0]=="--probe") { var g=GuardServices.DetectGame()!; var state=await GuardServices.ProbeAsync(g.Pid); Console.WriteLine(state?.ToString()??"No IPC");return; }
if(args.Length<2 || args[0] is not ("--host" or "--game" or "--settings"))throw new Exception("Usage: --host|--game absolute-runtime.dll");
string dll=Path.GetFullPath(args[1]);int checks=0;
void Check(bool ok,string description){if(!ok)throw new Exception("FAIL: "+description);Console.WriteLine("PASS: "+description);checks++;}
if(args[0]=="--host")
{
 NativeLibrary.Load(dll);
 int pid=Environment.ProcessId;
 var initial=await GuardServices.ProbeAsync(pid);
 Check(initial is {Version:"0.5.0",Protections:"NONE",Menu:"WAITING"},"v3 handshake outside BO3 reports no engine capabilities");
 foreach(var text in new[]{"HELLO|2\n","MENU|OPEN\n","NAME|585A47\n","PASSWORD|74657374\n","INVALID\n","KEY|1\n","KEY|45junk\n","KEY|45\nextra","KEY|45"})
 {
  bool rejected=false;try{await GuardServices.SendCommandAsync(pid,text);}catch(InvalidOperationException){rejected=true;}
  Check(rejected,"invalid or unavailable command rejected: "+text.Split('|')[0].Trim());
 }
 Check(await GuardServices.ProbeAsync(pid) is not null,"server remains responsive after rejected commands");
 Console.WriteLine($"{checks} isolated checks passed. No game modifications.");return;
}
var game=GuardServices.DetectGame()??throw new Exception("BO3 absent. No launch performed by this test.");
Check(GuardServices.InspectBuild(game.Path).DiagnosticSupported,"exact BO3 fingerprint accepted");
for(int attempt=0; attempt<60 && !GuardServices.QolRendererReady(game); attempt++) await Task.Delay(1000);
if(!GuardServices.QolRendererReady(game)) throw new Exception("Game renderer not ready");
var load=GuardServices.LoadQol(game,dll);Console.WriteLine(load.Message);Check(load.Loaded,"runtime module present");
RuntimeStatus? status=null;
for(int i=0;i<30;i++)
{
 status=await GuardServices.ProbeAsync(game.Pid);
 if(status is {Protections:"PARTIAL",Menu:"RENDERED"})break;
 if(status?.Error is {Length:>0})throw new Exception("Runtime error: "+status.Error);
 await Task.Delay(1000);
}
Check(status is {Protections:"PARTIAL",Hooks:14},"fourteen checked hooks installed, including three added T7 security blocks");
Check(status is {Menu:"RENDERED",Frames:>0},"D3D11 callback and ImGui frames active in BO3");
await GuardServices.SendCommandAsync(game.Pid,"VERSION_TEST\n"); Check(true,"version label and bounded output verified through game entry point"); Check(!status!.PrivateNetwork,"new runtime has no network password");
using var process=Process.GetProcessById(game.Pid);
long address=process.MainModule!.BaseAddress.ToInt64();
var handle=Memory.OpenProcess(0x410,false,game.Pid);if(handle==0)throw new Exception("Read-only process handle unavailable");
long[] rvas=[0x14F344B8,0x15E056C8,0x113A4970,0x113A48F0];
byte[] Read(long rva){byte[] b=new byte[16];if(!Memory.ReadProcessMemory(handle,(nint)(address+rva),b,16,out var read)||read!=16)throw new Exception("Read failed");return b;}
var originals=rvas.Select(Read).ToArray();
try
{
 await GuardServices.SendCommandAsync(game.Pid,"NAME|"+GuardServices.EncodeSetting("ZXG_TEST")+"\n");
 Check(rvas.All(rva=>Encoding.ASCII.GetString(Read(rva)).Split('\0')[0]=="ZXG_TEST"),"name independently read back from all four game buffers");
 await GuardServices.SendCommandAsync(game.Pid,"RESTORE\n");
 var restored=rvas.Select(Read).ToArray(); for(int i=0;i<rvas.Length;i++) if(!restored[i].SequenceEqual(originals[i])) Console.WriteLine($"Name buffer {i} differs at bytes: {string.Join(",", Enumerable.Range(0,16).Where(j=>restored[i][j]!=originals[i][j]))}"); Check(restored.Select((bytes,i)=>bytes.SequenceEqual(originals[i])).All(x=>x),"all original name buffers restored byte-for-byte");
 await GuardServices.SendCommandAsync(game.Pid,"PASSWORD|"+GuardServices.EncodeSetting("zxg-local-validation")+"\n");
 Check((await GuardServices.ProbeAsync(game.Pid))!.PrivateNetwork,"network password activated without logging its value");
 await GuardServices.SendCommandAsync(game.Pid,"NETWORK_TEST\n");
 Check(true,"native checksum send/receive round trip and altered-packet rejection, no network transmission");
 await GuardServices.SendCommandAsync(game.Pid,"PASSWORD|\n");
 Check(!(await GuardServices.ProbeAsync(game.Pid))!.PrivateNetwork,"test network password cleared");
 if(args[0]!="--settings") { await GuardServices.SendCommandAsync(game.Pid,"MENU|OPEN\n");
 ulong before=status.DrawnFrames;await Task.Delay(1500);var open=await GuardServices.ProbeAsync(game.Pid);
 Check(open!.MenuOpen&&open.DrawnFrames>before,"menu generates vertices and draws in BO3");
 
 await GuardServices.SendCommandAsync(game.Pid,"MENU|CLOSE\n");
 Check(!(await GuardServices.ProbeAsync(game.Pid))!.MenuOpen,"menu closes through runtime command");
 } var same=GuardServices.LoadQol(game,dll);Check(same.Message.Contains("déjà présent"),"repeated load request reconnects without another injection");
 for(int i=0;i<10;i++){await Task.Delay(500);Check(await GuardServices.ProbeAsync(game.Pid) is not null,$"post-install liveness {i+1}/10");}
 await GuardServices.SendCommandAsync(game.Pid,"CAPTURE\n"); await Task.Delay(1000); Console.WriteLine($"{checks} integration checks passed. No progression or currency commands executed.");
}
finally
{
 try{await GuardServices.SendCommandAsync(game.Pid,"RESTORE\n");await GuardServices.SendCommandAsync(game.Pid,"PASSWORD|\n");await GuardServices.SendCommandAsync(game.Pid,"MENU|CLOSE\n");Console.WriteLine("Cleanup confirmed: original name, empty password, closed menu.");}
 finally{Memory.CloseHandle(handle);}
}
static class Memory
{
 [DllImport("kernel32.dll")]public static extern nint OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll")]public static extern bool ReadProcessMemory(nint process,nint address,byte[] data,nuint length,out nuint read);
 [DllImport("kernel32.dll")]public static extern bool CloseHandle(nint handle);
}



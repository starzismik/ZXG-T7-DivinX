using System.Diagnostics;
using ZXGT7Guard;

if(args.Length!=2)throw new Exception("Usage: --isolated|--conflict|--loaded DivinX.dll");
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);checks++;}
GuardServices.VerifyQolFile(args[1]);Check(true,"localized DLL matches pinned DivinX SHA-256");
string invalid=Path.Combine(AppContext.BaseDirectory,"not-the-official-file.test");
try{File.WriteAllText(invalid,"invalid test input");bool rejected=false;try{GuardServices.VerifyQolFile(invalid);}catch(InvalidDataException){rejected=true;}Check(rejected,"different content rejected before loading");}finally{File.Delete(invalid);}
if(args[0]!="--isolated")
{
 var game=GuardServices.DetectGame()??throw new Exception("BO3 absent");
 Check(GuardServices.InspectBuild(game.Path).DiagnosticSupported,"BO3 fingerprint accepted");
 if(args[0]=="--conflict")
 {
  Check(GuardServices.HasRuntime(game),"ZXG native runtime present for conflict test");
  bool rejected=false;try{GuardServices.LoadQol(game,args[1]);}catch(InvalidOperationException ex){rejected=ex.Message.Contains("déjà chargé");}
  Check(rejected,"official DLL loading refused while ZXG hooks are active");
  Check(!GuardServices.HasQol(game),"official DLL remains absent after refusal");
 }
 else if(args[0]=="--loaded")
 {
  Check(GuardServices.HasDivinX(game),"DivinX DLL detected in BO3");
  Check(!GuardServices.HasRuntime(game),"native ZXG runtime absent (exclusive mode)");
  var repeat=GuardServices.LoadQol(game,args[1]);Check(repeat.Loaded&&repeat.Message.Contains("déjà présente"),"repeated request does not load a second copy");
  bool blocked=false;try{GuardServices.LoadRuntime(game,"not-used.dll");}catch(InvalidOperationException){blocked=true;}
  Check(blocked,"native loader refuses coexistence in reverse direction");
  using var process=Process.GetProcessById(game.Pid);
  var mods=process.Modules.Cast<ProcessModule>().Where(m=>m.ModuleName==GuardServices.QolName).ToArray();
  Check(mods.Length==1,"exactly one renamed module loaded");
  Check(Path.GetFullPath(mods[0].FileName).Equals(Path.GetFullPath(args[1]),StringComparison.OrdinalIgnoreCase),"loaded module comes from the application plugin folder");
  await Task.Delay(5000);Check(GuardServices.IsSameProcess(game),"BO3 stays alive after load");
 }
 else throw new Exception("Unknown mode");
}
Console.WriteLine($"{checks} checks passed. No input, currency or progression actions sent.");

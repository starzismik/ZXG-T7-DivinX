using ZXGT7Guard;
var game=GuardServices.DetectGame()??throw new Exception("BO3 absent");
var dll=Path.GetFullPath(args[0]);
if(!GuardServices.InspectBuild(game.Path).DiagnosticSupported)throw new Exception("Unrecognized game fingerprint");
GuardServices.VerifyQolFile(dll);
for(int i=0;i<60&&!GuardServices.QolRendererReady(game);i++)await Task.Delay(1000);
if(!GuardServices.QolRendererReady(game))throw new Exception("Renderer not ready");
Console.WriteLine(GuardServices.LoadQol(game,dll).Message);
RuntimeStatus? state=null;
for(int i=0;i<75;i++){
 state=await GuardServices.ProbeAsync(game.Pid);
 if(state is {Protections:"PARTIAL",Hooks:14,Frames:>0})break;
 if(state?.Error is {Length:>0})throw new Exception(state.Error);
 await Task.Delay(1000);
}
if(state is not {Protections:"PARTIAL",Hooks:14,Frames:>0})throw new Exception("Fourteen hooks not confirmed: "+state);
Console.WriteLine("PASS: game fingerprint, DLL fingerprint, 14 hooks, renderer active");
var before=state.DrawnFrames;
await GuardServices.SendCommandAsync(game.Pid,"MENU|OPEN\n");
await Task.Delay(1500);
state=await GuardServices.ProbeAsync(game.Pid);
if(state is not {MenuOpen:true} || state.DrawnFrames<=before)throw new Exception("Menu did not draw");
await GuardServices.SendCommandAsync(game.Pid,"MENU|CLOSE\n");
for(int i=0;i<15;i++){
 await Task.Delay(1000);state=await GuardServices.ProbeAsync(game.Pid);
 if(state is not {Protections:"PARTIAL",Hooks:14})throw new Exception("Runtime liveness lost");
}
Console.WriteLine("PASS: menu open/close and 15-second runtime liveness; no gameplay or currency actions invoked");
Console.WriteLine(state);

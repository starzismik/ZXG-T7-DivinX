using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXGT7Guard;

internal static class UiChecks
{
 const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
 static int checks;
 static MainWindow window=null!;
 static Type services=typeof(MainWindow).Assembly.GetType("ZXGT7Guard.GuardServices")!;
 static void Check(bool value,string message){if(!value)throw new Exception(message+"; UI: "+Control<TextBlock>("SettingsMessage").Text);Console.WriteLine("PASS: "+message);checks++;}
 static T Control<T>(string name) where T:FrameworkElement => (T)window.FindName(name);
 static object? Field(string name)=>typeof(MainWindow).GetField(name,Hidden)!.GetValue(window);
 static void Field(string name,object? value)=>typeof(MainWindow).GetField(name,Hidden)!.SetValue(window,value);
 static object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Hidden)!.Invoke(window,args);
 static async Task<object?> Probe(int pid)
 {
  var task=(Task)services.GetMethod("ProbeAsync",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[pid,CancellationToken.None])!;
  await task;return task.GetType().GetProperty("Result")!.GetValue(task);
 }
 static async Task Action(string name)
 {
  Call(name,window,new RoutedEventArgs());
  for(int i=0;i<100&&(bool)Field("_settingsBusy")!;i++)await Task.Delay(50);
  Check(!(bool)Field("_settingsBusy")!,name+" completed");
 }
 static void Capture(string path,double scale)
 {
  var root=(FrameworkElement)window.Content;
  root.Measure(new Size(1240,860));root.Arrange(new Rect(0,0,1240,860));root.UpdateLayout();
  var bitmap=new RenderTargetBitmap((int)(1240*scale),(int)(860*scale),96*scale,96*scale,PixelFormats.Pbgra32);
  bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
  using var file=File.Create(path);encoder.Save(file);
 }
 [STAThread] public static int Main(string[] args)
 {
  var app=new Application(); var source=System.Xml.Linq.XDocument.Load(Path.Combine(Environment.CurrentDirectory,"desktop","App.xaml")); System.Xml.Linq.XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"; var resources=new System.Xml.Linq.XElement(ns+"ResourceDictionary",new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),source.Root!.Element(ns+"Application.Resources")!.Elements()); app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resources.ToString()); app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  window=new MainWindow();Control<CheckBox>("AutoLoad").IsChecked=false;
  app.Dispatcher.BeginInvoke(new Action(async ()=>
  {
   int pid=int.Parse(args[0]);var folder=args[1];Directory.CreateDirectory(folder);
   try
   {
        if(args.Length>2 && args[2]=="--qol")
    {
     var flags=BindingFlags.Static|BindingFlags.NonPublic;
     var game=services.GetMethod("DetectGame",flags)!.Invoke(null,null)??throw new Exception("BO3 absent");
     Check((int)game.GetType().GetProperty("Pid")!.GetValue(game)! == pid,"WPF observes expected game PID");
     Check((bool)services.GetMethod("HasDivinX",flags)!.Invoke(null,[game])!,"WPF confirms DivinX module presence");
     Field("_game",game);Field("_qolLoaded",true);
     Field("_build",services.GetMethod("InspectBuild",flags)!.Invoke(null,[game.GetType().GetProperty("Path")!.GetValue(game)]));
     await (Task)Call("DiscoverAsync")!; await (Task)Call("MonitorAsync")!;
     Check(Control<FrameworkElement>("SessionSettings").Visibility==Visibility.Collapsed,"native-only controls hidden in official mode");
     Check(Control<TextBlock>("IpcValue").Text=="Non fourni","official DLL not misrepresented as native IPC");
     Check(Control<TextBlock>("MenuDetail").Text.Contains("F5"),"official F5 shortcut displayed");
     Check(Control<TextBlock>("MenuValue").Text=="DLL chargée","module status displayed without claiming all options tested");
     Capture(Path.Combine(folder,"application-divinx.png"),1); Call("Modules_Click",window,new RoutedEventArgs()); Check(Control<ScrollViewer>("ModulesPage").Visibility==Visibility.Visible,"credits navigation works"); Capture(Path.Combine(folder,"application-divinx-credits.png"),1); Call("Logs_Click",window,new RoutedEventArgs()); Check(Control<Grid>("LogsPage").Visibility==Visibility.Visible,"log navigation works"); Call("Overview_Click",window,new RoutedEventArgs());
     Console.WriteLine($"{checks} official-mode WPF checks passed without global input.");return;
    }
    Check(Control<ComboBox>("RuntimeMode").Items.Count==1,"only one menu offered");
    Control<TextBox>("NameInput").Text="ZXG_PRELAUNCH";
    Control<CheckBox>("PrivateSession").IsChecked=true;
    Check(!(bool)Call("QueueLaunchSettings")!,"private session requires a password");
    Control<PasswordBox>("NetworkPassword").Password="prelaunch-test";
    Check((bool)Call("QueueLaunchSettings")!,"prelaunch settings accepted without runtime");
    Check((string?)Field("_pendingName")=="ZXG_PRELAUNCH","nickname queued before runtime");
    Check((string?)Field("_pendingPassword")=="prelaunch-test","password queued in memory");
    Check(Control<PasswordBox>("NetworkPassword").Password.Length==0,"password field cleared after queuing");
    Field("_pendingName",null);Field("_pendingPassword",null);Field("_pendingKey",null);
    await Action("RestoreName_Click");
    Check(Control<TextBox>("NameInput").Text.Length==0,"prelaunch nickname reset clears pending input");
    await (Task)Call("DiscoverAsync")!; await (Task)Call("MonitorAsync")!; Check(Control<FrameworkElement>("SessionSettings").Visibility==Visibility.Visible,"session settings visible in unified mode"); Check(Control<TextBlock>("IpcValue").Text=="Connectée","unified mode confirms IPC"); var status=await Probe(pid);Check(status!=null,"live runtime reachable by WPF services");Field("_runtime",status);Call("RefreshFeatureStatus");
    Check(Control<TextBlock>("ProtectionValue").Text=="Partielles","desktop distinguishes partial protection");
    string original=Control<TextBlock>("CurrentNameValue").Text;
    Control<TextBox>("NameInput").Text="ZXG_UI_TEST";
    Control<PasswordBox>("NetworkPassword").Password="queued-ui-test";
    Check((bool)Call("QueueLaunchSettings")!,"launch preparation accepts nickname and private password");
    await (Task)Call("ApplyPendingSettingsAsync")!;
    Check(Control<TextBlock>("NetworkValue").Text=="Mot de passe réseau actif","queued password applied to live unified DLL");
    await Action("ClearPassword_Click");
    Check(Control<TextBlock>("CurrentNameValue").Text.EndsWith("ZXG_UI_TEST"),"pseudo command confirmed through actual WPF handler and runtime");
    var task=(Task)Call("RunMenuCommand","RESTORE\n")!;await task;
    Console.WriteLine("Before restore: "+original+"; after: "+Control<TextBlock>("CurrentNameValue").Text); Check(Control<TextBlock>("CurrentNameValue").Text==original,"original pseudo restored through WPF");
    Control<PasswordBox>("NetworkPassword").Password="zxg-ui-test";await Action("ApplyPassword_Click");
    Check(Control<PasswordBox>("NetworkPassword").Password.Length==0,"masked password input cleared after submission");
    Check(Control<TextBlock>("NetworkValue").Text=="Mot de passe réseau actif","network activation confirmed through WPF");
    await Action("ClearPassword_Click");Check(Control<TextBlock>("NetworkValue").Text.Contains("public"),"network password cleared through WPF");
    Check(!Control<ListBox>("ActivityList").Items.Cast<object>().Any(x=>x.ToString()!.Contains("zxg-ui-test")),"password absent from desktop journal");
    Capture(Path.Combine(folder,"application-zxg-100.png"),1);
    Control<ScrollViewer>("OverviewPage").ScrollToEnd();Capture(Path.Combine(folder,"application-zxg-reglages.png"),1);
    Capture(Path.Combine(folder,"application-zxg-150.png"),1.5);
    Capture(Path.Combine(folder,"application-zxg-200.png"),2);
    Check(true,"offscreen WPF rendering at three pixel densities without desktop input");
    Console.WriteLine($"{checks} WPF checks passed. Window never shown; no global input used.");
   }
   catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
   finally
   {
    try {Field("_pendingPassword","");Field("_pendingName",null);await (Task)Call("ApplyPendingSettingsAsync")!;await (Task)Call("RunMenuCommand","RESTORE\n")!;}catch(Exception e){Console.Error.WriteLine("Cleanup: "+e.Message);Environment.ExitCode=1;}
    app.Shutdown(Environment.ExitCode);
   }
  }));
  return app.Run();
 }
}

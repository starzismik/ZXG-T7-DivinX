using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: AssemblyProduct("[ZXG] T7 DivinX")]
[assembly: AssemblyCompany("STARZISMIK")]
internal static class StandaloneLauncher {
 [STAThread] static void Main() {
  bool owns=false;
  using(var mutex=new Mutex(false,@"Local\ZXG_T7DivinX_Standalone")) {
   try {
    try { owns=mutex.WaitOne(0); } catch(AbandonedMutexException) { owns=true; }
    if(!owns) { MessageBox.Show("T7 DivinX est déjà ouvert.","T7 DivinX"); return; }
    string parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZXG","T7DivinX");
    string folder=Path.Combine(parent,"1.0.2-68a1dea184a55ca0");
    string exe=Path.Combine(folder,"ZXG T7 DivinX.exe");
    if(!File.Exists(Path.Combine(folder,"package.complete"))) {
     Directory.CreateDirectory(parent);
     string temp=Path.Combine(parent,"preparation-"+Guid.NewGuid().ToString("N"));
     Directory.CreateDirectory(temp);
     using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
     using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
      foreach(var entry in zip.Entries) {
       string path=Path.GetFullPath(Path.Combine(temp,entry.FullName));
       if(!path.StartsWith(temp+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Chemin de paquet invalide.");
       Directory.CreateDirectory(Path.GetDirectoryName(path));
       entry.ExtractToFile(path);
      }
     }
     File.WriteAllText(Path.Combine(temp,"package.complete"),"1.0.2");
     Directory.Move(temp,folder);
    }
    using(var child=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=folder,UseShellExecute=false})) child.WaitForExit();
   } catch(Exception e) { MessageBox.Show(e.Message,"T7 DivinX",MessageBoxButtons.OK,MessageBoxIcon.Error); }
   finally { if(owns) mutex.ReleaseMutex(); }
  }
 }
}

using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ZXGT7Guard;

internal sealed record GameInstance(int Pid, long Started, string Path);
internal sealed record BuildInfo(uint Timestamp, uint ImageSize, string Hash, bool DiagnosticSupported);
internal sealed record RuntimeStatus(int Pid, string Version, string Protections, string Menu, ulong Frames, bool MenuOpen, string PlayerName, bool PrivateNetwork, int Hooks, string Error, ulong DrawnFrames);
internal sealed record LoadResult(bool Loaded, string Message);
internal enum LoadedMenuKind { None, Selected, OtherDivinX, LegacyRuntime, Foreign, Multiple }
internal sealed record LoadedMenuInfo(LoadedMenuKind Kind,string Name,string Path="");

internal static class GuardServices
{
    internal const string DiviniumName = "ZXG Divinium.dll";
    internal const string DiviniumHash = "25A487DC52505739D30AE1924BDD899075A347E0E43E7651DC35210676BDBA1B";
    internal const string InGameName = "ZXG InGame.dll";
    internal const string InGameHash = "B8A9F1A2265809235CC52718146527EDCB2A0398ACBDBC84E35005D0A4D164D6";
    internal static bool HasSelectedModule(GameInstance game,string module) => HasModule(game,module);
    internal const string QolName = "ZXG DivinX.dll";
    internal const string QolHash = "8CB326B47B3BBC3A977A930A6D6F03A850716F5B301EA4D8A6FD39507BF3965C";
    internal const string RuntimeName = "ZXG T7 Guard Runtime.dll";
    internal const string ExpectedHash = "51CA63BBC660E0826943C60DA67606F6BCB4B3B519528B5E0548C68C9423A323";

    internal static void InstallGscBridge(string gameExe)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "bridge", "zxg_studio");
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("Pont GSC DivinX absent du paquet de l'application.");

        string gameRoot = Path.GetDirectoryName(Path.GetFullPath(gameExe))
            ?? throw new InvalidDataException("Chemin de BO3 invalide.");
        string destination = Path.Combine(gameRoot, "mods", "zxg_studio");
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Chemin du pont GSC invalide.");
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
        string zone = Path.Combine(destination, "zone", "zm_mod.ff");
        if (!File.Exists(zone) || new FileInfo(zone).Length == 0)
            throw new InvalidDataException("Installation du pont GSC incomplète.");
    }

    internal static string? FindGame()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string root = key?.GetValue("SteamPath") as string ?? @"C:\Program Files (x86)\Steam";
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        foreach (string library in libraries)
        {
            string manifest = Path.Combine(library, "steamapps", "appmanifest_311210.acf");
            if (!File.Exists(manifest)) continue;
            var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"");
            if (!match.Success) continue;
            string common = Path.GetFullPath(Path.Combine(library, "steamapps", "common")) + Path.DirectorySeparatorChar;
            string exe = Path.GetFullPath(Path.Combine(common, match.Groups[1].Value, "BlackOps3.exe"));
            if (exe.StartsWith(common, StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) return exe;
        }
        return null;
    }

    internal static GameInstance? DetectGame()
    {
        var processes = Process.GetProcessesByName("BlackOps3");
        try
        {
            if (processes.Length > 1) throw new InvalidOperationException("Plusieurs processus BO3 : fermez les instances supplémentaires.");
            if (processes.Length == 0) return null;
            var p = processes[0];
            return new(p.Id, p.StartTime.ToUniversalTime().Ticks, p.MainModule?.FileName
                ?? throw new InvalidOperationException("Chemin du processus inaccessible."));
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    internal static bool IsSameProcess(GameInstance game)
    {
        try { using var p = Process.GetProcessById(game.Pid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == game.Started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal static BuildInfo InspectBuild(string path)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(file, Encoding.UTF8, true);
        if (file.Length < 64 || reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("En-tête DOS invalide.");
        file.Position = 60;
        int pe = reader.ReadInt32();
        if (pe < 64 || pe > file.Length - 88) throw new InvalidDataException("En-tête PE hors limites.");
        file.Position = pe;
        if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("BO3 doit être un exécutable PE x64.");
        file.Position = pe + 8; uint timestamp = reader.ReadUInt32();
        file.Position = pe + 24;
        if (reader.ReadUInt16() != 0x20B) throw new InvalidDataException("Format PE32+ requis.");
        file.Position = pe + 80; uint size = reader.ReadUInt32();
        file.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(file));
        return new(timestamp, size, hash, timestamp == 0x6A7B6355 && size == 0x1D75BC00 && hash == ExpectedHash);
    }

    internal static RuntimeStatus ParseStatus(string message, int pid)
    {
        var f = message.EndsWith('\n') ? message[..^1].Split('|') : Array.Empty<string>();
        if (!message.EndsWith('\n') || message[..^1].Contains('\n') || message.Contains('\r') || f.Length != 13 || f[0] != "ZXG" || f[1] != "3" ||
            f[2] != pid.ToString(System.Globalization.CultureInfo.InvariantCulture) || f[3] != "0.5.0" ||
            f[4] is not ("NONE" or "PARTIAL") || f[5] is not ("WAITING" or "RENDERED") ||
            !ulong.TryParse(f[6], out var frames) || f[7] is not ("0" or "1") || f[9] is not ("0" or "1") ||
            !int.TryParse(f[10], out var hooks) || hooks is < 0 or > 100 || !ulong.TryParse(f[12], out var drawn))
            throw new InvalidDataException("Réponse IPC incompatible. Fermez BO3 puis relancez avec la version courante.");
        try { return new(pid,f[3],f[4],f[5],frames,f[7]=="1",Encoding.UTF8.GetString(Convert.FromHexString(f[8])),f[9]=="1",hooks,Encoding.UTF8.GetString(Convert.FromHexString(f[11])),drawn); }
        catch (FormatException) { throw new InvalidDataException("Réponse IPC mal encodée."); }
    }

    internal static async Task<string> SendCommandAsync(int pid, string command, CancellationToken cancellation = default)
    {
        var response = await ExchangeAsync(pid,command,cancellation);
        if(response != "OK\n") throw new InvalidOperationException(response.StartsWith("ERR|") ? response[4..].Trim() : "Commande non confirmée par le runtime.");
        return response;
    }

    internal static string EncodeSetting(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    internal static async Task<RuntimeStatus?> ProbeAsync(int pid, CancellationToken cancellation = default)
    {
        try { return ParseStatus(await ExchangeAsync(pid,"HELLO|3\n",cancellation),pid); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static async Task<string> ExchangeAsync(int pid,string message,CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(2500);
        using var pipe = new NamedPipeClientStream(".", $"zxg_t7_guard_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != pid)
            throw new InvalidDataException("Le serveur IPC n’appartient pas au processus attendu.");
        await pipe.WriteAsync(Encoding.ASCII.GetBytes(message), timeout.Token);
        byte[] response = new byte[4096]; int length = 0;
        while (length < response.Length)
        {
            int count = await pipe.ReadAsync(response.AsMemory(length), timeout.Token);
            if (count == 0) throw new IOException("Connexion IPC fermée.");
            length += count;
            if (response[length - 1] != (byte)'\n') continue;
            await pipe.WriteAsync(Encoding.ASCII.GetBytes("ACK\n"),timeout.Token);
            return Encoding.ASCII.GetString(response,0,length);
        }
        throw new InvalidDataException("Réponse IPC trop longue.");
    }
    internal static bool HasRuntime(GameInstance game) => HasModule(game, RuntimeName);
    internal static bool HasDivinX(GameInstance game) => HasModule(game, QolName);
    internal static bool HasQol(GameInstance game) => GetLoadedMenu(game,QolName).Kind is LoadedMenuKind.Selected or LoadedMenuKind.OtherDivinX or LoadedMenuKind.Foreign or LoadedMenuKind.Multiple;
    internal static LoadedMenuInfo ClassifyLoadedMenus(IEnumerable<string> moduleNames,string selectedModule)
    {
        bool Known(string name) => name.Equals(QolName,StringComparison.OrdinalIgnoreCase) ||
            name.Equals(DiviniumName,StringComparison.OrdinalIgnoreCase) ||
            name.Equals(InGameName,StringComparison.OrdinalIgnoreCase) ||
            name.Equals(RuntimeName,StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ZXG Menu.dll",StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("Scropts",StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("Somnolence",StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("Somnolance",StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("T7Patch",StringComparison.OrdinalIgnoreCase));
        var known=moduleNames.Select(name=>System.IO.Path.GetFileName(name)).Where(Known)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name=>name,StringComparer.OrdinalIgnoreCase).ToArray();
        if(known.Length==0)return new(LoadedMenuKind.None,"");
        if(known.Length>1)return new(LoadedMenuKind.Multiple,string.Join(", ",known));
        var module=known[0];
        if(module.Equals(RuntimeName,StringComparison.OrdinalIgnoreCase))return new(LoadedMenuKind.LegacyRuntime,module);
        if(module.Equals(QolName,StringComparison.OrdinalIgnoreCase) || module.Equals(DiviniumName,StringComparison.OrdinalIgnoreCase) || module.Equals(InGameName,StringComparison.OrdinalIgnoreCase))
            return new(module.Equals(selectedModule,StringComparison.OrdinalIgnoreCase)?LoadedMenuKind.Selected:LoadedMenuKind.OtherDivinX,module);
        return new(LoadedMenuKind.Foreign,module);
    }
    internal static LoadedMenuInfo GetLoadedMenu(GameInstance game,string selectedModule)
    {
        if(!IsSameProcess(game))throw new InvalidOperationException("BO3 a changé de session pendant la détection.");
        using var process=Process.GetProcessById(game.Pid);
        var modules=process.Modules.Cast<ProcessModule>().ToArray();
        var result=ClassifyLoadedMenus(modules.Select(module=>module.ModuleName),selectedModule);
        return result with { Path=modules.FirstOrDefault(module=>module.ModuleName.Equals(result.Name,StringComparison.OrdinalIgnoreCase))?.FileName??"" };
    }
    private static bool HasModule(GameInstance game, string moduleName)
    {
        using var p = Process.GetProcessById(game.Pid);
        return p.Modules.Cast<ProcessModule>().Any(m => string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
    }

    internal static LoadResult LoadRuntime(GameInstance game, string dll)
    {
        if(HasQol(game))throw new InvalidOperationException("Scropts-QOL est déjà chargé. Redémarrez BO3 avant de changer de mode.");
        return LoadModule(game,dll,RuntimeName);
    }
    internal static void VerifyQolFile(string dll)
    {
        using var file=File.OpenRead(dll);
        string name=Path.GetFileName(dll);
        string expected=name==DiviniumName?DiviniumHash:name==InGameName?InGameHash:name==QolName?QolHash:"";
        if(expected.Length!=64 || Convert.ToHexString(SHA256.HashData(file))!=expected)throw new InvalidDataException("La DLL ne correspond pas à la version DivinX française validée.");
    }
    internal const int GameWindowDelayMilliseconds = 20000;
    private static readonly object GameWindowGate = new();
    private static (int Pid,long Started,long Since,bool Seen) _gameWindowObservation;
    internal static int ObserveGameWindow(GameInstance? game,bool visible,long now)
    {
        lock(GameWindowGate)
        {
            if(game is null){_gameWindowObservation=default;return GameWindowDelayMilliseconds / 1000;}
            if(_gameWindowObservation.Pid!=game.Pid || _gameWindowObservation.Started!=game.Started)
                _gameWindowObservation=(game.Pid,game.Started,0,false);
            if(!_gameWindowObservation.Seen && visible)
                _gameWindowObservation=(game.Pid,game.Started,now,true);
            if(!_gameWindowObservation.Seen)return GameWindowDelayMilliseconds / 1000;
            long remaining=GameWindowDelayMilliseconds-Math.Max(0,now-_gameWindowObservation.Since);
            return (int)Math.Max(0,(remaining+999)/1000);
        }
    }
    internal static int GameWindowDelayRemaining(GameInstance game)
    {
        if(!IsSameProcess(game)){ObserveGameWindow(null,false,Environment.TickCount64);return GameWindowDelayMilliseconds / 1000;}
        using var process=Process.GetProcessById(game.Pid);
        var window=process.MainWindowHandle;
        return ObserveGameWindow(game,window!=0 && IsWindowVisible(window),Environment.TickCount64);
    }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    internal const int RendererStabilityMilliseconds = 3000;
    private static readonly object RendererGate = new();
    private static (int Pid, long Started, ulong Chain, ulong Table, ulong Friends, ulong Apps, ulong Network, long Since) _rendererObservation;
    internal static bool QolRendererReady(GameInstance game)
    {
        if(!IsSameProcess(game))return false;
        int windowDelay=GameWindowDelayRemaining(game);
        using var p=Process.GetProcessById(game.Pid);
        long image=p.MainModule!.BaseAddress.ToInt64();
        nint handle=OpenProcess(0x410,false,game.Pid);
        if(handle==0)return false;
        try
        {
            ulong Read(long address){byte[] b=new byte[8];return ReadProcessMemory(handle,(nint)address,b,8,out var n)&&n==8?BitConverter.ToUInt64(b):0;}
            if(Read(image+0x1686E948)==0){lock(RendererGate) _rendererObservation=default;return false;}
            ulong chain=Read(image+0xF4378D8);if(chain==0){lock(RendererGate) _rendererObservation=default;return false;}
            ulong table=Read((long)chain);
            bool Method(ulong instance,int index) { ulong vtable=Read((long)instance);return vtable!=0 && Read((long)vtable+index*8)!=0; }
            ulong friends=Read(image+0x10B3DC20), apps=Read(image+0x10B3DC40), network=Read(image+0x10B3DC50);
            if(table==0||Read((long)table+8*8)==0||Read((long)table+13*8)==0 ||
               !Method(friends,0)||!Method(apps,6)||!Method(apps,7)||!Method(network,2))
            {
                lock(RendererGate) _rendererObservation=default;
                return false;
            }
            // A non-null swap chain can appear while BO3 is still starting.
            // Require a stable renderer observation and a responsive game window.
            bool stable=ObserveRenderer(game,chain,table,friends,apps,network,p.MainWindowHandle!=0 && p.Responding,Environment.TickCount64);
            return stable && windowDelay==0;
        }
        finally{CloseHandle(handle);}
    }
    internal static bool ObserveRenderer(GameInstance game,ulong chain,ulong table,ulong friends,ulong apps,ulong network,bool responsive,long now)
    {
        lock(RendererGate)
        {
            if(!responsive || chain==0 || table==0 || friends==0 || apps==0 || network==0)
            { _rendererObservation=default;return false; }
            if(_rendererObservation.Pid!=game.Pid || _rendererObservation.Started!=game.Started ||
               _rendererObservation.Chain!=chain || _rendererObservation.Table!=table ||
               _rendererObservation.Friends!=friends || _rendererObservation.Apps!=apps || _rendererObservation.Network!=network)
                _rendererObservation=(game.Pid,game.Started,chain,table,friends,apps,network,now);
            return now-_rendererObservation.Since>=RendererStabilityMilliseconds;
        }
    }
    internal static string CenterQolLayout(string ini, int width, int height)
    {
        if (width < 850 || height < 500) throw new InvalidOperationException("Fenêtre BO3 trop petite pour centrer le menu 850 × 500.");
        string section = $"[Window][[ZXG] T7 DivinX]\nPos={(width-850)/2},{(height-500)/2}\nSize=850,500\nCollapsed=0\n\n";
        const string pattern = @"(?m)^\[Window\]\[\[ZXG\] T7 DivinX\]\r?\n[\s\S]*?(?=^\[|\z)";
        return Regex.IsMatch(ini, pattern) ? Regex.Replace(ini, pattern, _ => section) : ini.TrimEnd() + "\n\n" + section;
    }

    [StructLayout(LayoutKind.Sequential)] private struct ClientRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out ClientRect rect);

    internal static LoadResult LoadQol(GameInstance game,string dll)
    {
        if(!InspectBuild(game.Path).DiagnosticSupported)throw new InvalidDataException("Build BO3 inconnu : chargement refusé.");
        VerifyQolFile(dll);
        var loaded=GetLoadedMenu(game,Path.GetFileName(dll));
        if(loaded.Kind is LoadedMenuKind.LegacyRuntime or LoadedMenuKind.Foreign or LoadedMenuKind.Multiple)
            throw new InvalidOperationException($"Module déjà chargé : {loaded.Name}. Redémarrez BO3 avant une autre injection.");
        if(HasRuntime(game))throw new InvalidOperationException("Runtime ZXG déjà chargé : redémarrez BO3. Les deux DLL ne doivent pas être chargées ensemble.");
        var moduleName=Path.GetFileName(dll);
        if(moduleName!=QolName && moduleName!=DiviniumName && moduleName!=InGameName)throw new InvalidDataException("Module inconnu.");
        if(HasModule(game,moduleName))return new(true,"DLL DivinX déjà présente ; aucun second chargement.");
        if(HasQol(game))throw new InvalidOperationException("Ancien menu chargé : redémarrez BO3 pour afficher le titre DivinX.");
        if(!QolRendererReady(game))return new(false,"BO3 initialise encore son rendu. Attendez son écran de titre puis actualisez.");
        return LoadModule(game,dll,moduleName);
    }
    private static LoadResult LoadModule(GameInstance game, string dll, string moduleName)
    {
        if (!Environment.Is64BitProcess) throw new InvalidOperationException("Chargeur x64 requis.");
        // Serialize separate desktop instances and retain an uncertainty marker on timeout.
        using var gate = new Mutex(false, $@"Local\ZXG_Load_{game.Pid}_{game.Started}");
        bool acquired;
        try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return new(false, "Un chargement est déjà en cours dans une autre instance.");
        try
        {
            if (!IsSameProcess(game)) return new(false, "Le processus a changé ; chargement annulé.");
            if (moduleName == RuntimeName && HasQol(game) || (moduleName == QolName || moduleName == DiviniumName) && (HasRuntime(game) || HasQol(game) && !HasModule(game,moduleName))) throw new InvalidOperationException("Un autre mode est déjà chargé. Redémarrez BO3.");
            if (HasModule(game,moduleName)) return new(true, "Runtime déjà présent : reconnexion, sans rechargement.");
            // UI layout is managed in memory by the overlay.
            if (!File.Exists(dll)) throw new FileNotFoundException("Runtime intégré introuvable.", dll);
            string marker = Path.Combine(AppContext.BaseDirectory, $"load-{game.Pid}-{game.Started}.pending");
            if (File.Exists(marker)) return new(false, "Une tentative précédente reste incertaine. Redémarrez BO3 avant de réessayer.");
            File.WriteAllText(marker, "Chargement en cours. Ne pas supprimer avant fermeture du processus.");
            nint process = 0, remote = 0, thread = 0;
            bool remoteFinished = false, threadStarted = false;
            try
            {
                process = OpenProcess(0x043A, false, game.Pid);
                if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                // Resolve the exported address relative to its actual owner (forwarded exports included).
                nint local = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                if (local == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                using var self = Process.GetCurrentProcess();
                var owner = self.Modules.Cast<ProcessModule>().Single(m => local.ToInt64() >= m.BaseAddress.ToInt64()
                    && local.ToInt64() < m.BaseAddress.ToInt64() + m.ModuleMemorySize);
                using var target = Process.GetProcessById(game.Pid);
                var targetOwner = target.Modules.Cast<ProcessModule>().Single(m => string.Equals(m.FileName, owner.FileName, StringComparison.OrdinalIgnoreCase));
                nint entry = targetOwner.BaseAddress + checked((int)(local.ToInt64() - owner.BaseAddress.ToInt64()));
                byte[] bytes = Encoding.Unicode.GetBytes(Path.GetFullPath(dll) + '\0');
                remote = VirtualAllocEx(process, 0, (nuint)bytes.Length, 0x3000, 4);
                if (remote == 0 || !WriteProcessMemory(process, remote, bytes, (nuint)bytes.Length, out nuint written) || written != (nuint)bytes.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                thread = CreateRemoteThread(process, 0, 0, entry, remote, 0, out _);
                if (thread == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                threadStarted = true;
                uint wait = WaitForSingleObject(thread, 10000);
                if (wait != 0) return new(false, $"Chargement non confirmé (attente 0x{wait:X}). Redémarrage de BO3 requis ; aucune seconde tentative.");
                remoteFinished = true;
                // An HMODULE is 64-bit; GetExitCodeThread cannot be used as its value.
                return HasModule(game,moduleName) ? new(true, moduleName == QolName ? "[ZXG] T7 DivinX chargé. F5 ouvre le menu." : "Module chargé ; vérification IPC en cours.") : new(false, "LoadLibrary terminé, mais module absent. Vérifiez les dépendances du runtime.");
            }
            finally
            {
                if (remote != 0 && (!threadStarted || remoteFinished)) VirtualFreeEx(process, remote, 0, 0x8000);
                if (thread != 0) CloseHandle(thread);
                if (process != 0) CloseHandle(process);
                if (!threadStarted || remoteFinished) File.Delete(marker);
                // On timeout the small argument buffer is deliberately retained until process exit.
            }
        }
        finally { gate.ReleaseMutex(); }
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(nint process,nint address,byte[] data,nuint size,out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint VirtualAllocEx(nint process, nint address, nuint size, uint allocationType, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(nint process, nint address, nuint size, uint freeType);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(nint process, nint address, byte[] bytes, nuint size, out nuint written);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateRemoteThread(nint process, nint attributes, nuint stackSize, nint start, nint parameter, uint flags, out uint id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}


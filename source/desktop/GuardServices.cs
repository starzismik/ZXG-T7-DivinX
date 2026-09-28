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

internal static class GuardServices
{
    internal const string DiviniumName = "ZXG Divinium.dll";
    internal const string DiviniumHash = "A8C80EC8EDF66A5366B8D8D3114E4F0B3257179BC03A90A049A9875061B86ED0";
    internal static bool HasSelectedModule(GameInstance game,string module) => HasModule(game,module);
    internal const string QolName = "ZXG DivinX.dll";
    internal const string QolHash = "6EC15569828564FBC7C57EF27F9F23FC166E499B19A9F6526730712963C71E43";
    internal const string RuntimeName = "ZXG T7 Guard Runtime.dll";
    internal const string ExpectedHash = "51CA63BBC660E0826943C60DA67606F6BCB4B3B519528B5E0548C68C9423A323";

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
    internal static bool HasQol(GameInstance game) => HasModule(game, DiviniumName) || HasModule(game, QolName) || HasModule(game,"Scropts.QOL.v3.2.5.dll") || HasModule(game,"ZXG Menu.dll");
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
        if(Convert.ToHexString(SHA256.HashData(file))!=(Path.GetFileName(dll)==DiviniumName?DiviniumHash:QolHash))throw new InvalidDataException("La DLL ne correspond pas à la version DivinX française validée.");
    }
    private static readonly object RendererGate = new();
    private static (int Pid, long Started, ulong Chain, ulong Table, long Since) _rendererObservation;
    internal static bool QolRendererReady(GameInstance game)
    {
        if(!IsSameProcess(game))return false;
        using var p=Process.GetProcessById(game.Pid);
        long image=p.MainModule!.BaseAddress.ToInt64();
        nint handle=OpenProcess(0x410,false,game.Pid);
        if(handle==0)return false;
        try
        {
            ulong Read(long address){byte[] b=new byte[8];return ReadProcessMemory(handle,(nint)address,b,8,out var n)&&n==8?BitConverter.ToUInt64(b):0;}
            if(Read(image+0x1686E948)==0)return false;
            ulong chain=Read(image+0xF4378D8);if(chain==0)return false;
            ulong table=Read((long)chain);
            if(table==0||Read((long)table+8*8)==0||Read((long)table+13*8)==0)return false;
            // A non-null swap chain can appear while BO3 is still starting.
            // Require a stable renderer observation and a responsive game window.
            lock(RendererGate)
            {
                if(_rendererObservation.Pid!=game.Pid || _rendererObservation.Started!=game.Started || _rendererObservation.Chain!=chain || _rendererObservation.Table!=table)
                    _rendererObservation=(game.Pid,game.Started,chain,table,Environment.TickCount64);
                return Environment.TickCount64-_rendererObservation.Since>=10000 && DateTime.UtcNow-p.StartTime.ToUniversalTime()>=TimeSpan.FromSeconds(45) && p.MainWindowHandle!=0 && p.Responding;
            }
        }
        finally{CloseHandle(handle);}
    }
    internal static string CenterQolLayout(string ini, int width, int height)
    {
        if (width < 850 || height < 500) throw new InvalidOperationException("Fenêtre BO3 trop petite pour centrer le menu 850 × 500.");
        string section = $"[Window][[ZXG] T7 DivinX]\nPos={(width-850)/2},{(height-500)/2}\nSize=850,500\nCollapsed=0\n\n";
        const string pattern = @"(?m)^\[Window\]\[\[ZXG\] T7 DivinX\]\r?\n[\s\S]*?(?=^\[|\z)";
        return Regex.IsMatch(ini, pattern) ? Regex.Replace(ini, pattern, _ => section) : ini.TrimEnd() + "\n\n" + section;
    }

    private static void PrepareQolLayout(GameInstance game)
    {
        using var process = Process.GetProcessById(game.Pid);
        if (process.MainWindowHandle == 0 || !GetClientRect(process.MainWindowHandle, out var rect))
            throw new InvalidOperationException("Dimensions de la fenêtre BO3 indisponibles ; réessayez sur son écran de titre.");
        string path = Path.Combine(Path.GetDirectoryName(game.Path)!, "imgui.ini");
        string original = File.Exists(path) ? File.ReadAllText(path) : "";
        string centered = CenterQolLayout(original, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (original == centered) return;
        if (File.Exists(path)) File.Copy(path, path + ".divinx-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff"));
        File.WriteAllText(path, centered, new UTF8Encoding(false));
    }

    [StructLayout(LayoutKind.Sequential)] private struct ClientRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out ClientRect rect);

    internal static LoadResult LoadQol(GameInstance game,string dll)
    {
        if(!InspectBuild(game.Path).DiagnosticSupported)throw new InvalidDataException("Build BO3 inconnu : chargement refusé.");
        VerifyQolFile(dll);
        if(HasRuntime(game))throw new InvalidOperationException("Runtime ZXG déjà chargé : redémarrez BO3. Les deux DLL ne doivent pas être chargées ensemble.");
        var moduleName=Path.GetFileName(dll);
        if(moduleName!=QolName && moduleName!=DiviniumName)throw new InvalidDataException("Module inconnu.");
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
            if (moduleName == QolName || moduleName == DiviniumName) PrepareQolLayout(game);
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


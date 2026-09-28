using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using ZXGT7Guard;

if (args.Length > 0 && args[0] == "--probe-game")
{
    var liveGame = GuardServices.DetectGame();
    if (liveGame is null)
    {
        Console.WriteLine("SKIP: BO3 absent ; aucune relance, injection ou interaction effectuée.");
        Environment.ExitCode = 2;
        return;
    }
    using var process = Process.GetProcessById(liveGame.Pid);
    int modules = process.Modules.Cast<ProcessModule>().Count(m => m.ModuleName == GuardServices.RuntimeName);
    Console.WriteLine($"BO3 PID={liveGame.Pid}; runtime modules={modules}");
    if (modules != 1) throw new Exception("Un module runtime attendu ; aucun chargement tenté.");
    for (int i = 0; i < 10; i++)
    {
        if (!GuardServices.IsSameProcess(liveGame)) throw new Exception("Le jeu a changé de processus.");
        var status = await GuardServices.ProbeAsync(liveGame.Pid) ?? throw new Exception("IPC sans réponse.");
        Console.WriteLine($"PASS live probe {i + 1}/10: PID={status.Pid}, version={status.Version}, protections={status.Protections}, menu={status.Menu}");
        await Task.Delay(500);
    }
    Console.WriteLine("Read-only game checks passed. No loading, hooks, input or profile changes.");
    return;
}

int count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); count++;
}
void Reject(Action action, string name)
{
    try { action(); } catch (InvalidDataException) { Check(true, name); return; }
    throw new Exception("FAIL (accepted): " + name);
}
int pid = Environment.ProcessId;
Check(GuardServices.ParseStatus($"ZXG|3|{pid}|0.5.0|NONE|WAITING|0|0||0|0||0\n", pid).Protections == "NONE", "valid response reports no protections");
Reject(() => GuardServices.ParseStatus($"ZXG|2|{pid}|0.5.0|NONE|WAITING|0|0||0|0||0\n", pid), "old protocol rejected");
Reject(() => GuardServices.ParseStatus($"ZXG|3|{pid + 1}|0.5.0|NONE|WAITING|0|0||0|0||0\n", pid), "wrong PID rejected");
Reject(() => GuardServices.ParseStatus($"ZXG|3|{pid}|0.5.0|READY|WAITING|0|0||0|0||0\n", pid), "false capabilities rejected");
Reject(() => GuardServices.ParseStatus($"ZXG|3|{pid}|0.5.0|NONE|WAITING|0|0||0|0||0", pid), "truncated frame rejected");
Reject(() => GuardServices.ParseStatus($"ZXG|3|{pid}|0.5.0|NONE|WAITING|0|0||0|0||0\nextra", pid), "trailing data rejected");

string malformed = Path.Combine(AppContext.BaseDirectory, "invalid-pe.test");
try
{
    File.WriteAllBytes(malformed, new byte[256]);
    Reject(() => GuardServices.InspectBuild(malformed), "invalid PE rejected");
    byte[] fake = new byte[512];
    BitConverter.GetBytes((ushort)0x5A4D).CopyTo(fake, 0);
    BitConverter.GetBytes(128).CopyTo(fake, 60);
    BitConverter.GetBytes(0x4550).CopyTo(fake, 128);
    BitConverter.GetBytes((ushort)0x8664).CopyTo(fake, 132);
    BitConverter.GetBytes(0x6A7B6355u).CopyTo(fake, 136);
    BitConverter.GetBytes((ushort)0x20B).CopyTo(fake, 152);
    BitConverter.GetBytes(0x1D75BC00u).CopyTo(fake, 208);
    File.WriteAllBytes(malformed, fake);
    Check(!GuardServices.InspectBuild(malformed).DiagnosticSupported, "matching timestamp and size with wrong hash is blocked");
}
finally { File.Delete(malformed); }

var watch = Stopwatch.StartNew();
Check(await GuardServices.ProbeAsync(pid) is null && watch.Elapsed < TimeSpan.FromSeconds(4), "absent pipe times out");
using (var server = new NamedPipeServerStream($"zxg_t7_guard_{pid}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
{
    var probe = GuardServices.ProbeAsync(pid);
    await server.WaitForConnectionAsync();
    byte[] request = new byte[32]; await server.ReadAsync(request);
    watch.Restart();
    Check(await probe is null && watch.Elapsed < TimeSpan.FromSeconds(4), "connected but silent server times out");
}
using (var server = new NamedPipeServerStream("zxg_t7_guard_999999", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
{
    var probe = GuardServices.ProbeAsync(999999);
    await server.WaitForConnectionAsync();
    bool rejected = false;
    try { await probe; } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "server PID is verified by Windows, not only message contents");
}

string dll = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new Exception("Pass the built runtime path.");
NativeLibrary.Load(dll); // Isolated test process. No game or profile access.
Check(await GuardServices.ProbeAsync(pid) is not null, "real C++ DLL handshake");
Check(await GuardServices.ProbeAsync(pid) is not null, "second connection to same runtime");
using (var self = Process.GetCurrentProcess())
{
    var instance = new GameInstance(pid, self.StartTime.ToUniversalTime().Ticks, self.MainModule!.FileName);
    Check(GuardServices.LoadRuntime(instance, dll).Message.Contains("déjà présent"), "loader reconnects to existing module without another LoadLibrary call");
}
using (var client = new NamedPipeClientStream(".", $"zxg_t7_guard_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous))
{
    using var timeout = new CancellationTokenSource(4000);
    await client.ConnectAsync(timeout.Token);
    await client.WriteAsync(Encoding.ASCII.GetBytes("HELLO_WRONG\n"), timeout.Token);
    bool rejected;
    try { byte[] reply = new byte[128]; int size = await client.ReadAsync(reply, timeout.Token); rejected = size > 0 && Encoding.ASCII.GetString(reply,0,size) == "ERR|Commande invalide\n"; await client.WriteAsync(Encoding.ASCII.GetBytes("ACK\n"),timeout.Token); }
    catch (IOException) { rejected = true; }
    Check(rejected, "runtime rejects malformed request");
}
using (var client = new NamedPipeClientStream(".", $"zxg_t7_guard_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous))
{
    using var timeout = new CancellationTokenSource(4000);
    await client.ConnectAsync(timeout.Token);
    await Task.Delay(2400);
}
Check(await GuardServices.ProbeAsync(pid) is not null, "server recovers from a silent client");
string? game = GuardServices.FindGame();
Check(game is not null, "Steam installation found via manifest");
Check(GuardServices.InspectBuild(game!).DiagnosticSupported, "installed BO3 exact hash and PE recognized for diagnostics");
Console.WriteLine($"{count} checks passed. No game profile modification.");

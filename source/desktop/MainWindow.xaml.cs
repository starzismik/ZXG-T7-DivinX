using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ZXGT7Guard;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _monitor = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _logFile = Path.Combine(AppContext.BaseDirectory, "logs", $"diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    private GameInstance? _game;
    private BuildInfo? _build;
    private RuntimeStatus? _runtime;
    private string? _installation;
    private string? _lastError;
    private bool _busy, _attempted;
    private DateTime _launchUntil;

    public MainWindow()
    {
        InitializeComponent();

        try { if(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"selected-module.txt")).Trim()=="1")RuntimeMode.SelectedIndex=1; } catch(IOException) { }
        LogPathValue.Text = _logFile;
        Log("[ZXG] T7 DivinX 1.0.0 : application initialisée.");
        _monitor.Tick += async (_, _) => await MonitorAsync();
        Loaded += async (_, _) => await StartAfterUpdateCheckAsync();
        Closed += (_, _) => { _monitor.Stop(); _shutdown.Cancel(); };
    }

    private void Log(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        ActivityList.Items.Add(line);
        if (ActivityList.Items.Count > 300) ActivityList.Items.RemoveAt(0);
        ActivityList.ScrollIntoView(ActivityList.Items[^1]);
        try { Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!); File.AppendAllText(_logFile, line + Environment.NewLine); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { LogPathValue.Text = "Journal en mémoire seulement : écriture du fichier impossible."; }
    }

    private async Task DiscoverAsync()
    {
        try
        {
            _installation = await Task.Run(GuardServices.FindGame);
            PathValue.Text = _installation ?? "BO3 introuvable dans les bibliothèques Steam.";
            Log(_installation is null ? "Installation Steam introuvable." : $"Installation Steam : {_installation}");
        }
        catch (Exception e) { ShowError(e); }
    }

    private void ShowError(Exception e)
    {
        OperationValue.Text = "Erreur : " + e.Message;
        if (_lastError != e.Message) Log($"ERREUR : {e.Message}");
        _lastError = e.Message;
    }

    private async Task MonitorAsync()
    {
        if (_updateRequired) return;
        if(IsQolMode){await MonitorQolAsync();return;}
        if (_busy || _shutdown.IsCancellationRequested) return;
        _busy = true;
        bool auto = false;
        try
        {
            var current = await Task.Run(GuardServices.DetectGame);
            if (current != _game)
            {
                _game = current; _build = null; _runtime = null; _attempted = false;
                if (current is null) { Log("BO3 fermé ou absent ; états invalidés."); OperationValue.Text = "En attente du jeu."; }
                else
                {
                    _launchUntil = default;
                    Log($"BO3 détecté : PID {current.Pid}.");
                    OperationValue.Text = "Vérification de l’empreinte SHA-256…";
                    BusyBar.Visibility = Visibility.Visible;
                }
            }
            if (current is not null)
            {
                if (_build is null)
                {
                    _build = await Task.Run(() => GuardServices.InspectBuild(current.Path));
                    Log($"Empreinte : timestamp 0x{_build.Timestamp:X8}, image 0x{_build.ImageSize:X8}, SHA-256 {_build.Hash}.");
                    Log(_build.DiagnosticSupported ? "Empreinte reconnue ; la DLL vérifiera chaque signature moteur." : "Build inconnu : chargement bloqué.");
                    OperationValue.Text = _build.DiagnosticSupported ? "Runtime ZXG prêt à être chargé." : "Build inconnu : aucun chargement autorisé.";
                }
                var status = await GuardServices.ProbeAsync(current.Pid, _shutdown.Token);
                if (_runtime is not null && status is null)
                {
                    Log("Connexion IPC perdue. Aucune réinjection automatique.");
                    OperationValue.Text = "Connexion IPC perdue. Le runtime doit être vérifié avant toute reconnexion.";
                }
                if (_runtime is null && status is not null)
                {
                    Log($"IPC confirmé par le PID {status.Pid}, runtime {status.Version}.");
                    OperationValue.Text = "Connecté au runtime ZXG.";
                }
                _runtime = status;
                await ApplyPendingSettingsAsync();
                auto = AutoLoad.IsChecked == true && _build.DiagnosticSupported && !_attempted && status is null;
                if (status is not null) _attempted = true;
            }
            _lastError = null;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception e) { _runtime = null; _build = null; ShowError(e); }
        finally { _busy = false; Render(); }
        if (auto && !_shutdown.IsCancellationRequested) await ConnectAsync();
    }

    private void Render()
    {
        if(IsQolMode){RenderQol();return;}
        SessionSettings.Visibility=Visibility.Visible; QolHelp.Visibility=Visibility.Collapsed; RuntimeMode.IsEnabled=!_busy;
        if (_shutdown.IsCancellationRequested) return;
        bool detected = _game is not null;
        GameState.Text = detected ? "Black Ops III détecté" : _launchUntil > DateTime.UtcNow ? "Lancement via Steam…" : "Black Ops III est fermé";
        ProcessValue.Text = detected ? $"BlackOps3.exe · PID {_game!.Pid}" : "Aucun processus détecté";
        BuildValue.Text = _build is null ? "Empreinte non vérifiée" : _build.DiagnosticSupported ? "Build reconnu · signatures contrôlées par la DLL" : "Build inconnu · chargement bloqué";
        IpcValue.Text = _runtime is null ? "Déconnectée" : "Connectée";
        RuntimeVersion.Text = _runtime is null ? "Runtime non confirmé" : $"Runtime {_runtime.Version} · PID {_runtime.Pid}";
        BusyBar.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        PrimaryActionLabel.Text = !detected ? "Lancer BO3" : _busy ? "Vérification…" : _runtime is null ? "Charger le runtime ZXG" : "IPC connecté";
        PrimaryAction.IsEnabled = !_busy && _runtime is null && (detected ? _build?.DiagnosticSupported == true : _launchUntil <= DateTime.UtcNow);
        StopAction.IsEnabled = detected && !_busy;
        RefreshFeatureStatus();
    }

    private async Task ConnectAsync()
    {
        if(IsQolMode){await ConnectQolAsync();return;}
        if (_busy || _game is null || _shutdown.IsCancellationRequested) return;
        var game = _game;
        _busy = true; _attempted = true; Render();
        try
        {
            // Revalidate the on-disk executable immediately before any loading attempt.
            var build = await Task.Run(() => GuardServices.InspectBuild(game.Path));
            _build = build;
            if (!build.DiagnosticSupported) throw new InvalidOperationException("Build inconnu : chargement refusé.");
            _runtime = await GuardServices.ProbeAsync(game.Pid, _shutdown.Token);
            if (_runtime is null)
            {
                OperationValue.Text = "Chargement du runtime ZXG…";
                var result = await Task.Run(() => GuardServices.LoadRuntime(game, Path.Combine(AppContext.BaseDirectory, GuardServices.RuntimeName)));
                Log(result.Message); OperationValue.Text = result.Message;
                if (!result.Loaded) return;
                _runtime = await GuardServices.ProbeAsync(game.Pid, _shutdown.Token);
            }
            if (!GuardServices.IsSameProcess(game)) { _runtime = null; throw new InvalidOperationException("BO3 s’est fermé pendant la connexion."); }
            if (_runtime is null) throw new InvalidOperationException("Module présent mais IPC absent ou incompatible. Redémarrez BO3 ; aucun second chargement effectué.");
            Log($"IPC confirmé par le PID {game.Pid}, runtime {_runtime.Version}.");
            OperationValue.Text = "Connecté au runtime ZXG ; initialisation du moteur en cours.";
            await ApplyPendingSettingsAsync();
            if (MinimizeAfterLoad.IsChecked == true) WindowState = WindowState.Minimized;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception e) { _runtime = null; ShowError(e); }
        finally { _busy = false; Render(); }
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (!QueueLaunchSettings()) return;
        if (_game is not null) { await ConnectAsync(); return; }
        try
        {
            await DiscoverAsync();
            if (_installation is null) throw new FileNotFoundException("Installez BO3 dans Steam puis actualisez la détection.");
            if (GuardServices.DetectGame() is not null) { await MonitorAsync(); return; }
            Process.Start(new ProcessStartInfo("steam://run/311210") { UseShellExecute = true });
            _launchUntil = DateTime.UtcNow.AddSeconds(60);
            OperationValue.Text = "Demande envoyée à Steam. Si le jeu ne démarre pas, consultez la fenêtre Steam.";
            Log("Lancement demandé via Steam, AppID 311210."); Render();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_game is null || !GuardServices.IsSameProcess(_game)) return;
            using var process = Process.GetProcessById(_game.Pid);
            string message = process.CloseMainWindow() ? "Demande de fermeture propre envoyée à BO3." : "Fermez BO3 depuis son menu ; aucune fermeture forcée.";
            Log(message); OperationValue.Text = message;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) { await DiscoverAsync(); await MonitorAsync(); }
    private void Page(UIElement page)
    {
        OverviewPage.Visibility = ModulesPage.Visibility = InfoPage.Visibility = LogsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        InfoNav.Tag=page==InfoPage?"active":null;
        OverviewNav.Tag=page==OverviewPage?"active":null; ModulesNav.Tag=page==ModulesPage?"active":null; LogsNav.Tag=page==LogsPage?"active":null;
    }
    private void Overview_Click(object sender, RoutedEventArgs e) => Page(OverviewPage);
    private void Modules_Click(object sender, RoutedEventArgs e) => Page(ModulesPage);
    private void Logs_Click(object sender, RoutedEventArgs e) => Page(LogsPage);
}

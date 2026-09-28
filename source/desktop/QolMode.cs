using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ZXGT7Guard;
public partial class MainWindow
{
    private bool _qolLoaded;
    private bool _qolReady;
    private string SelectedModule => RuntimeMode.SelectedIndex == 1 ? GuardServices.DiviniumName : GuardServices.QolName;
    private bool IsQolMode => RuntimeMode?.SelectedIndex is 0 or 1;
    private async void RuntimeMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _busy) return;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"selected-module.txt"),RuntimeMode.SelectedIndex.ToString());
        _runtime = null; _qolLoaded = false; _qolReady = false; _build = null; _attempted = false;
        _pendingName = _pendingPassword = null; _pendingKey = null;
        NetworkPassword.Clear();
        Log(IsQolMode ? "Mode T7 DivinX sélectionné (base Scropts-QOL)." : "Mode runtime ZXG sélectionné.");
        await MonitorAsync();
    }
    private async Task MonitorQolAsync()
    {
        if (_busy || _shutdown.IsCancellationRequested) return;
        _qolReady=false; _busy=true; bool load=false; string selectedModule=SelectedModule;
        try
        {
            var current=await Task.Run(GuardServices.DetectGame);
            if(current!=_game)
            {
                _game=current;_build=null;_runtime=null;_qolLoaded=false;_attempted=false;
                Log(current is null ? "BO3 fermé ; état du menu invalidé." : $"BO3 détecté : PID {current.Pid}.");
            }
            if(current is not null)
            {
                _launchUntil=default;
                _build ??= await Task.Run(()=>GuardServices.InspectBuild(current.Path));
                bool native=await Task.Run(()=>GuardServices.HasRuntime(current));
                _qolLoaded=await Task.Run(()=>GuardServices.HasSelectedModule(current,selectedModule));
                _runtime = _qolLoaded ? await GuardServices.ProbeAsync(current.Pid,_shutdown.Token) : null;
                if(native) OperationValue.Text="Le runtime ZXG est déjà chargé. Redémarrez BO3 pour utiliser le menu T7 DivinX seul.";
                else if(_qolLoaded) OperationValue.Text="T7 DivinX chargé. Pseudo et session appliqués après confirmation du moteur.";
                else if(GuardServices.HasQol(current)) OperationValue.Text="Un autre module est chargé : redémarrez BO3 pour changer de menu.";
                else if(!_build.DiagnosticSupported) OperationValue.Text="Build inconnu : chargement bloqué.";
                else if(!(_qolReady=await Task.Run(()=>GuardServices.QolRendererReady(current)))) OperationValue.Text="Stabilisation de BO3 avant chargement (au moins 45 s après démarrage)…";
                else load=AutoLoad.IsChecked==true&&!_attempted;
            }
            else OperationValue.Text="En attente du jeu.";
            _lastError=null;
        }
        catch(Exception ex){_qolLoaded=false;_runtime=null;ShowError(ex);}
        finally{_busy=false;Render();}
        await ApplyPendingSettingsAsync();
        if(load&&!_shutdown.IsCancellationRequested && QueueLaunchSettings())await ConnectQolAsync();
    }
    private async Task ConnectQolAsync()
    {
        if(_busy||_game is null||!_qolReady||_shutdown.IsCancellationRequested)return;
        var game=_game;var selectedModule=SelectedModule;_busy=true;_attempted=true;Render();
        try
        {
            var moduleDirectory = Path.GetDirectoryName(typeof(App).Assembly.Location)!;
            var result=await Task.Run(()=>GuardServices.LoadQol(game,Path.Combine(moduleDirectory,"plugins",selectedModule)));
            _qolLoaded=result.Loaded;Log(result.Message);OperationValue.Text=result.Message;
            if(result.Loaded&&MinimizeAfterLoad.IsChecked==true)WindowState=WindowState.Minimized;
        }
        catch(Exception ex){_qolLoaded=false;_runtime=null;ShowError(ex);}
        finally{_busy=false;Render();}
    }
    private void RenderQol()
    {
        SessionSettings.Visibility=Visibility.Visible;
        QolHelp.Visibility=Visibility.Visible;
        RuntimeMode.IsEnabled=!_busy;
        bool detected=_game is not null;
        GameState.Text=detected?"Black Ops III détecté":_launchUntil>DateTime.UtcNow?"Lancement via Steam…":"Black Ops III est fermé";
        ProcessValue.Text=detected?$"BlackOps3.exe · PID {_game!.Pid}":"Aucun processus détecté";
        BuildValue.Text=_build is null?"Empreinte non vérifiée":_build.DiagnosticSupported?"Build BO3 reconnu":"Build inconnu · chargement bloqué";
        IpcValue.Text=_runtime is null?"En attente":"Connectée";RuntimeVersion.Text="Réglages transmis au menu unifié";
        RefreshFeatureStatus();
        if (!_qolLoaded) MenuValue.Text="Non chargé";
        if (_runtime is null) MenuDetail.Text="T7 DivinX · F5 par défaut";
        PrimaryActionLabel.Text=!detected?"Lancer BO3":_busy?"Vérification…":_qolLoaded?(_runtime?.Menu=="RENDERED"?"Menu prêt":"Initialisation du menu…"):!_qolReady?"Préparation de BO3…":"Injecter T7 DivinX";
        PrimaryAction.IsEnabled=!_busy&&!_qolLoaded&&(detected?_qolReady&&_build?.DiagnosticSupported==true:_launchUntil<=DateTime.UtcNow);
        StopAction.IsEnabled=detected&&!_busy;
        BusyBar.Visibility=_busy?Visibility.Visible:Visibility.Collapsed;
    }
}
